using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Inspection;

/// <summary>A mod's renderers, tick listeners, particles and highlights, seen from a real engine-mode client.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharosrendermod")]
public partial class LiveClientInspectionTests : ClientServerScenarioBase
{
    private const string Mod = "pharosrendermod";

    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task AModsRenderers_AreListedAtTheirStages_AndCounted()
    {
        IReadOnlyList<RendererInfo> mine = Client!.Renderers.Of(Mod);

        RendererInfo opaque = Assert.Single(mine, r => r.Stage == EnumRenderStage.Opaque);
        Assert.Equal(0.42, opaque.RenderOrder);
        Assert.Equal(24, opaque.RenderRange);
        Assert.Equal("pharosrendermod", opaque.ProfilingName);
        Assert.Contains("CountingRenderer", opaque.TypeName);
        RendererInfo bare = Assert.Single(mine, r => r.Stage == EnumRenderStage.Ortho);
        Assert.Equal(0.97, bare.RenderOrder);
        Assert.Contains("PharosRenderModSystem", bare.TypeName);
        Assert.Equal(2, mine.Count);
        Assert.Contains(Client.Renderers.At(EnumRenderStage.Opaque), r => r.Mod == null);
        Assert.Equal(Client.Renderers.At(EnumRenderStage.Opaque).Select(r => r.RenderOrder).Order(), Client.Renderers.At(EnumRenderStage.Opaque).Select(r => r.RenderOrder));

        Counts before = await CountsAsync();
        IReadOnlyDictionary<RendererInfo, int> calls = await Client.Renderers.CountCallsAsync(30);
        Counts after = await CountsAsync();

        Assert.Equal(after.Renders - before.Renders, calls[opaque]);
        Assert.Equal(30, calls[opaque]);
        Assert.Equal(after.Bare - before.Bare, calls[bare]);
        Assert.Equal(10, await Client.Renderers.CountCallsAsync(opaque, 10));
    }

    [ClientServerScenario]
    public async Task AModsTickListener_IsListed_AndCounted()
    {
        TickListenerInfo listener = Assert.Single(Client!.TickListeners.Of(Mod));
        Assert.Equal(50, listener.IntervalMs);
        Assert.Equal(ListenerKind.World, listener.Kind);
        Assert.Contains("PharosRenderModSystem", listener.Handler);
        Assert.Contains(Client.TickListeners.All(), l => l.Mod == null);

        CallbackInfo callback = Assert.Single(Client.TickListeners.Callbacks(), c => c.Mod == Mod);
        Assert.InRange(callback.DueInMs, 1, 600_000);

        // The client's clock follows real time: count against what the mod counted itself.
        Counts before = await CountsAsync();
        int calls = await Client.TickListeners.CountCallsAsync(listener, 120);
        Counts after = await CountsAsync();

        Assert.True(calls > 0, "the listener never ran in 120 frames");
        Assert.Equal(after.Ticks - before.Ticks, calls);
        Assert.Equal(listener.Handler, Assert.Single(Client.TickListeners.Of(Mod)).Handler);
    }

    [ClientServerScenario]
    public async Task SpawnedParticles_AreCaptured_AndAlive()
    {
        Vec3d at = Client!.RunOnClientThread(() => Client.Client.EntityPlayer.Pos.XYZ.AddCopy(0, 1, 0));
        SpawnedParticles mine;
        using (ParticleCapture capture = Client.Particles.Capture(includeOffThread: false))
        {
            await Client.Commands.ExecuteSuccessAsync(".pharosfx particles");
            await StepFramesAsync(2);
            mine = Assert.Single(capture.Spawned, p => p.ProviderType == nameof(SimpleParticleProperties) && p.Color == ColorUtil.ToRgba(255, 200, 40, 40));
        }

        Assert.Equal(EnumParticleModel.Quad, mine.Model);
        Assert.Equal(7, mine.Quantity);
        // The pool scales the quantity with the speed of time.
        Assert.InRange(mine.Spawned, 1, 7);
        Assert.Equal(30, mine.LifeLength);
        Assert.True(mine.Position!.DistanceTo(at) < 1.5, $"spawned at {mine.Position}, the player is at {at}");
        Assert.False(mine.OffThread);
        Assert.True(Client.Particles.Alive(EnumParticleModel.Quad) >= mine.Spawned);
        Assert.True(Client.Particles.Alive() >= Client.Particles.Alive(EnumParticleModel.Quad));
    }

    [ClientServerScenario]
    public async Task Highlights_FromTheClientAndTheServer_AreSeen()
    {
        BlockPos player = Client!.RunOnClientThread(() => Client.Client.EntityPlayer.Pos.AsBlockPos);

        await Client.Commands.ExecuteSuccessAsync(".pharosfx highlight");
        ClientHighlight own = Client.Highlights(9)!;
        Assert.Equal([player.AddCopy(1, 0, 0), player.AddCopy(2, 0, 0)], own.Positions);
        Assert.Equal([ColorUtil.ToRgba(128, 255, 0, 0), ColorUtil.ToRgba(128, 0, 255, 0)], own.Colors);
        Assert.Contains(9, Client.HighlightSlots());

        // The client's own clear leaves the slot, empty.
        await Client.Commands.ExecuteSuccessAsync(".pharosfx clear");
        Assert.Empty(Client.Highlights(9)!.Positions);

        BlockPos marked = player.AddCopy(0, -1, 0);
        ServerHost!.RunOnGameThread(() =>
        {
            ICoreServerAPI sapi = (ICoreServerAPI)Server!.Api;
            sapi.World.HighlightBlocks(sapi.World.AllOnlinePlayers[0], 10, [marked], [ColorUtil.WhiteArgb]);
        });
        Assert.True(await Session!.StepUntilAsync(() => Client.Highlights(10) != null, maxFrames: 300), "the server's highlight never arrived");
        Assert.Equal([marked], Client.Highlights(10)!.Positions);

        // So does the server's: an empty highlight still carries its count of blocks.
        ServerHost.RunOnGameThread(() =>
        {
            ICoreServerAPI sapi = (ICoreServerAPI)Server!.Api;
            sapi.World.HighlightBlocks(sapi.World.AllOnlinePlayers[0], 10, []);
        });
        Assert.True(await Session.StepUntilAsync(() => Client.Highlights(10)?.Positions.Count == 0, maxFrames: 300), "the server's clear never arrived");
        Assert.Null(Client.Highlights(77));
    }

    private Task StepFramesAsync(int frames) => Session!.StepFramesAsync(frames);

    private async Task<Counts> CountsAsync()
    {
        ClientCommandResult result = await Client!.Commands.ExecuteSuccessAsync(".pharosfx counts");
        Match match = CountsPattern().Match(result.Message ?? "");
        Assert.True(match.Success, result.ToString());
        return new Counts(Int(match, 1), Int(match, 2), Int(match, 3));
    }

    private static int Int(Match match, int group) => int.Parse(match.Groups[group].Value, System.Globalization.CultureInfo.InvariantCulture);

    private sealed record Counts(int Renders, int Bare, int Ticks);

    [GeneratedRegex(@"renders=(\d+) bare=(\d+) ticks=(\d+)")]
    private static partial Regex CountsPattern();
}
