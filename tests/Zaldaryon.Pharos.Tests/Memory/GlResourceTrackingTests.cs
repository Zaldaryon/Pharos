using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Memory;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Memory;

/// <summary>The ledger's bookkeeping, fed by hand: no GL needed.</summary>
[Collection("Sequential")]
public class GlResourceLedgerTests
{
    private const int Thread = 7;

    private static GlResourceLedger Open(bool stacks = false) => GlResourceLedger.Open(Thread, () => 3, stacks);

    [Fact]
    public void ObjectsCreatedAndNotDeleted_AreLive_ByKind()
    {
        GlResourceLedger ledger = Open();
        long start = ledger.Generation;
        try
        {
            ledger.OnCreated(GlResourceKind.Texture, 5, Thread);
            ledger.OnCreated(GlResourceKind.Texture, 6, Thread);
            ledger.OnCreated(GlResourceKind.VertexArray, 5, Thread);
            ledger.OnDeleted(GlResourceKind.Texture, 6, Thread);

            Assert.Equal([(GlResourceKind.Texture, 5u), (GlResourceKind.VertexArray, 5u)], ledger.LiveSince(start, GlResourceKind.All).Select(e => (e.Kind, e.Id)));
            Assert.Single(ledger.LiveSince(start, GlResourceKind.VertexArray));
            Assert.Equal(3, ledger.LiveSince(start, GlResourceKind.All)[0].Frame);
        }
        finally
        {
            GlResourceLedger.Close(ledger, false);
        }
    }

    [Fact]
    public void ZeroIds_UnknownDeletions_AndOtherThreads_AreCountedNotTracked()
    {
        GlResourceLedger ledger = Open();
        long start = ledger.Generation;
        try
        {
            ledger.OnCreated(GlResourceKind.Buffer, 0, Thread);
            ledger.OnDeleted(GlResourceKind.Buffer, 0, Thread);
            ledger.OnDeleted(GlResourceKind.Buffer, 99, Thread);
            ledger.OnCreated(GlResourceKind.Buffer, 4, Thread + 1);

            Assert.Empty(ledger.LiveSince(start, GlResourceKind.All));
            Assert.Equal((1, 1, 1), (ledger.ZeroIds, ledger.PreexistingDeleted, ledger.OtherThreadCalls));
        }
        finally
        {
            GlResourceLedger.Close(ledger, false);
        }
    }

    [Fact]
    public void AReusedId_IsANewObject_SoAnInnerScopeSeesOnlyItsOwn()
    {
        GlResourceLedger outer = Open();
        try
        {
            outer.OnCreated(GlResourceKind.Texture, 9, Thread);
            outer.OnDeleted(GlResourceKind.Texture, 9, Thread);

            GlResourceLedger inner = Open();
            Assert.Same(outer, inner);
            long innerStart = inner.Generation;
            inner.OnCreated(GlResourceKind.Texture, 9, Thread);
            Assert.Single(inner.LiveSince(innerStart, GlResourceKind.All));
            GlResourceLedger.Close(inner, false);

            Assert.Same(outer, GlResourceLedger.Live);
        }
        finally
        {
            GlResourceLedger.Close(outer, false);
        }

        Assert.Null(GlResourceLedger.Live);
    }

    [Fact]
    public void Stacks_AreKept_OnlyWhenAScopeAsks()
    {
        GlResourceLedger ledger = Open();
        long start = ledger.Generation;
        try
        {
            ledger.OnCreated(GlResourceKind.Texture, 1, Thread);
            GlResourceLedger.Open(Thread, () => 0, captureStacks: true);
            ledger.OnCreated(GlResourceKind.Texture, 2, Thread);
            GlResourceLedger.Close(ledger, true);

            IReadOnlyList<GlResourceLedger.Entry> live = ledger.LiveSince(start, GlResourceKind.All);
            Assert.Null(live[0].Stack);
            Assert.NotNull(live[1].Stack);
        }
        finally
        {
            GlResourceLedger.Close(ledger, false);
        }
    }

    [Fact]
    public void AnotherClientsThread_CannotOpenAScopeMeanwhile()
    {
        GlResourceLedger ledger = Open();
        try
        {
            Assert.Throws<InvalidOperationException>(() => GlResourceLedger.Open(Thread + 1, () => 0, false));
        }
        finally
        {
            GlResourceLedger.Close(ledger, false);
        }
    }

    [Fact]
    public void TheCounterDetector_NowSeesTexturesVertexArraysAndFramebuffers()
    {
        GlResourceLeakDetector detector = new();
        detector.StartBaseline(new Zaldaryon.Pharos.Graphics.GlCommandRecord(), 0);
        GlLeakReport report = detector.GetLeakReport(new Zaldaryon.Pharos.Graphics.GlCommandRecord
        {
            VertexArrayAllocations = 3, VertexArrayDeletions = 1,
            TextureAllocations = 2,
            FramebufferAllocations = 1, FramebufferDeletions = 1,
            RenderbufferAllocations = 1,
        }, 0);

        Assert.Equal((2, 2, 0, 1), (report.VAOLeaks, report.TextureLeaks, report.FramebufferLeaks, report.RenderbufferLeaks));
        Assert.Equal(5, report.TotalResourceLeaks);
    }
}

/// <summary>
/// An engine-mode client creating GL objects on purpose: some left for the scope to find, the
/// rest deleted. Everything happens in one call on the client thread, with no frame between, so
/// only the test's own objects are counted.
/// </summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveGlResourceTrackingTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    private ICoreClientAPI Capi => Client!.Client.api as ICoreClientAPI ?? throw new InvalidOperationException("No client API.");

    [ClientServerScenario]
    public void MeshesAndTexturesLeftUndeleted_AreReported_WithWhereTheyWereMade()
    {
        using GlResourceScope scope = Client!.Memory.TrackGlResources(new GlResourceTrackingOptions { CaptureStacks = true });
        List<MeshRef> meshes = [];
        List<LoadedTexture> textures = [];
        Client.RunOnClientThread(() =>
        {
            for (int i = 0; i < 5; i++)
            {
                meshes.Add(Capi.Render.UploadMesh(CubeMeshUtil.GetCube()));
                LoadedTexture texture = new(Capi);
                Capi.Render.LoadOrUpdateTextureFromBgra(new int[16 * 16], false, 0, ref texture);
                textures.Add(texture);
            }
        });

        GlResourceReport report = scope.Report();
        try
        {
            Assert.Equal(5, report.Count(GlResourceKind.VertexArray));
            Assert.Equal(5, report.Count(GlResourceKind.Texture));
            Assert.True(report.Count(GlResourceKind.Buffer) >= 5);
            Assert.Contains(report.Leaks, l => l.Stack?.Contains(nameof(MeshesAndTexturesLeftUndeleted_AreReported_WithWhereTheyWereMade)) == true);
            PharosAssertException error = Assert.Throws<PharosAssertException>(() => PharosAssert.NoGlLeaks(scope, GlResourceKind.VertexArray | GlResourceKind.Texture));
            Assert.Contains("VertexArray #", error.Message);
        }
        finally
        {
            Client.RunOnClientThread(() =>
            {
                foreach (MeshRef mesh in meshes) Capi.Render.DeleteMesh(mesh);
                foreach (LoadedTexture texture in textures) texture.Dispose();
            });
        }

        Assert.False(scope.Report().HasLeaks);
    }

    [ClientServerScenario]
    public void ObjectsDeletedAgain_LeaveNothing_AndAReusedIdCountsOnce()
    {
        using GlResourceScope scope = Client!.Memory.TrackGlResources();
        Client.RunOnClientThread(() =>
        {
            MeshRef mesh = Capi.Render.UploadMesh(CubeMeshUtil.GetCube());
            Capi.Render.DeleteMesh(mesh);
            LoadedTexture texture = new(Capi);
            Capi.Render.LoadOrUpdateTextureFromBgra(new int[8 * 8], false, 0, ref texture);
            texture.Dispose();
        });
        PharosAssert.NoGlLeaks(scope);

        int first = Client.RunOnClientThread(() => GL.GenTexture());
        Client.RunOnClientThread(() => GL.DeleteTexture(first));
        int second = Client.RunOnClientThread(() => GL.GenTexture());
        try
        {
            Assert.Single(scope.Report(GlResourceKind.Texture).Leaks);
        }
        finally
        {
            Client.RunOnClientThread(() => GL.DeleteTexture(second));
        }
    }

    [ClientServerScenario]
    public void EveryKind_AndTheTrackedShapes_AreSeen()
    {
        using GlResourceScope scope = Client!.Memory.TrackGlResources();
        int texture = 0, buffer = 0, vertexArray = 0, framebuffer = 0, renderbuffer = 0;
        uint unsignedBuffer = 0;
        Client.RunOnClientThread(() =>
        {
            texture = GL.GenTexture();
            buffer = GL.GenBuffer();
            GL.GenBuffers(1, out unsignedBuffer);
            GL.GenVertexArrays(1, out vertexArray);
            framebuffer = GL.GenFramebuffer();
            renderbuffer = GL.GenRenderbuffer();
        });

        GlResourceReport report = scope.Report();
        Assert.Equal((1, 2, 1, 1, 1), (report.Count(GlResourceKind.Texture), report.Count(GlResourceKind.Buffer), report.Count(GlResourceKind.VertexArray), report.Count(GlResourceKind.Framebuffer), report.Count(GlResourceKind.Renderbuffer)));

        // Deletions go through every form: arrays, references and single ids.
        Client.RunOnClientThread(() =>
        {
            GL.DeleteTextures(1, [texture]);
            GL.DeleteBuffers(1, ref buffer);
            GL.DeleteBuffers(1, [unsignedBuffer]);
            GL.DeleteVertexArray(vertexArray);
            GL.DeleteFramebuffer(framebuffer);
            GL.DeleteRenderbuffer(renderbuffer);
        });
        PharosAssert.NoGlLeaks(scope);
    }
}
