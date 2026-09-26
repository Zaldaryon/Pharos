using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Client;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Fixtures;
using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests;

/// <summary>
/// Covers the engine startup shim that opens the vanilla client render pass.
/// </summary>
/// <remarks>
/// The headless bootstrap never calls <c>ScreenManager.Start</c> or
/// <c>GuiScreenRunningGame.Start</c>, so the render pass starts out gated. These tests pin
/// the gate closed, open it, and prove the engine actually reaches render stage dispatch.
/// <para>
/// Opening the gate also switches <c>DeterministicFrameController.Step</c> onto the render
/// branch, so a frame after <c>PrepareRenderPass</c> no longer only ticks. It dispatches render
/// stages and then throws inside <c>AmbientManager.UpdateAmbient</c>, because
/// <c>ClientMain.ColorMaps</c> is only populated when a mod's
/// <c>config/colormaps.json</c> is registered. Fixture-mode tessellation is unaffected, because
/// the shim is opt-in and the default boot path still ticks without rendering.
/// </para>
/// </remarks>
[Collection("Sequential")]
public sealed class RenderPassTests
{
    static RenderPassTests()
    {
        HeadlessPlatformResolver.Initialize();
    }

    private static HeadlessClient Boot()
    {
        return HeadlessClientBootstrap.Boot(new HeadlessClientOptions
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        });
    }

    [Fact]
    public void RenderGate_IsClosedOnTheDefaultBootPath()
    {
        using HeadlessClient client = Boot();
        client.InitializeMockWorld();

        Assert.Null(client.Client.EntityPlayer);
        Assert.False(client.IsRenderGateOpen);
    }

    [Fact]
    public void PrepareRenderPass_OpensTheRenderGate()
    {
        using HeadlessClient client = Boot();
        client.InitializeMockWorld();

        Assert.Null(client.PrepareRenderPass());

        Assert.NotNull(client.Client.EntityPlayer);
        Assert.NotNull(client.Client.EntityPlayer!.Pos);
        Assert.True(client.IsRenderGateOpen);
    }

    [Fact]
    public void PrepareRenderPass_IsIdempotent()
    {
        using HeadlessClient client = Boot();
        client.InitializeMockWorld();

        Assert.Null(client.PrepareRenderPass());
        object? first = client.Client.EntityPlayer;

        Assert.Null(client.PrepareRenderPass());

        Assert.Same(first, client.Client.EntityPlayer);
    }

    [Fact]
    public void PrepareRenderPass_FillsTheStateMainRenderLoopDereferences()
    {
        using HeadlessClient client = Boot();
        client.InitializeMockWorld();

        client.PrepareRenderPass();

        // UpdateFreeMouse reads this hotkey.
        Assert.True(ScreenManager.hotkeyManager.HotKeys.ContainsKey("togglemousecontrol"));

        // MapRegionSizeInChunks divides by ServerChunkSize, and DefaultShaderUniforms.Update
        // reads the climate map through it.
        Assert.True(client.Client.WorldMap.ServerChunkSize > 0);
        Assert.True(client.Client.WorldMap.regionSize > 0);

        // The render loop writes GameWorldCalendar.Timelapse and the sky and terrain shaders
        // read its sun and daylight values.
        Assert.NotNull(client.Client.Calendar);
    }

    [Fact]
    public void RenderStageDispatch_IsReachedOnceTheGateIsOpen()
    {
        using HeadlessClient client = Boot();
        client.InitializeMockWorld();

        client.PrepareRenderPass();

        RenderStageProbe probe = new();
        client.Client.eventManager!.RegisterRenderer(probe, EnumRenderStage.Before, "pharos-probe");

        client.GL.Enable();
        try
        {
            // The remaining engine startup gaps can still throw from a later render stage
            // handler. The claim under test is narrower: dispatch itself is reached.
            try
            {
                client.Step(1f / 60f);
            }
            catch (Exception)
            {
                // Recorded, asserted on below.
            }
        }
        finally
        {
            client.GL.Disable();
        }

        Assert.True(probe.WasDispatched, "ClientMain.MainRenderLoop never dispatched EnumRenderStage.Before.");
    }

    [Fact]
    public void RenderPass_FrameCompletesAndRecordsDrawCalls()
    {
        using HeadlessClient client = Boot();
        client.InitializeMockWorld();

        client.PrepareRenderPass();

        client.GL.Enable();
        Exception? thrown = null;
        try
        {
            client.Step(1f / 60f);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        client.GL.Disable();

        Assert.True(
            thrown is null,
            $"MainRenderLoop threw {thrown?.GetType().Name}: {thrown?.Message}\n{thrown?.StackTrace}");

        client.GL.Enable();
        client.Step(1f / 60f);
        client.GL.Disable();

        GlCommandRecord record = client.GL.Snapshot();
        Assert.True(
            record.DrawCalls > 0,
            $"No draw calls recorded. Multi={record.MultiDrawCalls} Indirect={record.IndirectDrawCalls}");
    }

    [Fact]
    public void PrepareRenderPass_AllocatesColorMapRectsSoTerrainShadersDoNotFault()
    {
        // ShaderProgramBase.Use calls Uniforms4("colorMapRects", 40, shUniforms.ColorMapRects4)
        // for any shader including colormap.vsh. A null array there is marshalled into
        // glUniform4fv as a null pointer with count 40 and the process takes a 0xC0000005 access
        // violation, so this asserts the array exists rather than letting the crash speak.
        using HeadlessClient client = Boot();
        client.InitializeMockWorld();

        client.PrepareRenderPass();

        float[] rects = client.Client.shUniforms.ColorMapRects4;

        Assert.NotNull(rects);
        Assert.Equal(160, rects.Length);

        // The object that matters is the one ShaderProgramBase.Use reads, which is
        // ClientPlatformWindows.ShaderUniforms. It starts as its own instance and is only
        // replaced by ClientMain.shUniforms in ClientMain.Start.
        Assert.Same(client.Client.shUniforms, client.Platform.ShaderUniforms);
        Assert.NotNull(client.Platform.ShaderUniforms.ColorMapRects4);
    }

    [Fact]
    public void RenderPass_TerrainStageRunsWithoutFaultingTheProcess()
    {
        // This is the regression guard for the 0xC0000005 access violation. ChunkRenderer.
        // RenderOpaque calls chunkopaque.Use, and ShaderProgramBase.Use feeds
        // Uniforms4("colorMapRects", 40, ColorMapRects4) to OpenTK. A null ColorMapRects4 is
        // marshalled into glUniform4fv as a null pointer with a count of 40 and the driver
        // dereferences it, killing the test host outright rather than throwing.
        //
        // A crashed process cannot be caught, so the assertion is that the whole frame completes.
        using HeadlessClient client = Boot();
        client.InitializeMockWorld();

        ChunkPos target = new(10, 2, 10);
        // Two different block ids side by side on purpose. A uniform slab culls every interior
        // face, and a chunk with no visible face produces a TesselatedChunkPart whose MeshData
        // entries are all null, so no MeshDataPool is ever created.
        ClientChunk injectedChunk = client.InjectChunk(
            new ChunkFixtureBuilder()
                .At(target)
                .WithDefaultSunlight(31)
                .Fill(0, 0, 0, 15, 1, 31, 1)
                .Fill(16, 0, 0, 31, 1, 31, 2)
                .Build(),
            triggerTesselation: false);
        Assert.Equal(1, injectedChunk.Data[ChunkFixture.ToIndex(2, 0, 2)]);

        client.PrepareRenderPass(new Vintagestory.API.MathTools.Vec3d(
            target.X * 32 + 16, target.Y * 32 + 2, target.Z * 32 + 16));
        int tessellatedVertices = TessellateAndPoolFixtureChunk(client, target, injectedChunk);
        Assert.True(tessellatedVertices > 0, $"fixture produced no vertices: {PoolSummary(client, target)}");

        client.GL.Enable();
        client.GL.Reset();
        Exception? thrown = null;
        try
        {
            client.Step(1f / 60f);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }
        finally
        {
            client.GL.Disable();
        }

        Assert.True(
            thrown is null,
            $"Terrain render stage threw {thrown?.GetType().Name}: {thrown?.Message}\n{thrown?.StackTrace}");

        GlCommandRecord record = client.GL.Snapshot();
        Assert.True(record.DrawCalls > 0, "no draw calls at all; " + PoolSummary(client, target));

        Assert.True(
            record.MultiDrawCalls > 0,
            $"terrain drew no multi-draw calls. Draw={record.DrawCalls} Multi={record.MultiDrawCalls} " +
            $"Indirect={record.IndirectDrawCalls} {PoolSummary(client, target)}");
    }

    private static int TessellateAndPoolFixtureChunk(HeadlessClient client, ChunkPos target, ClientChunk chunk)
    {
        object? tesselator = typeof(ClientMain)
            .GetField("TerrainChunkTesselator", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?.GetValue(client.Client);
        if (tesselator == null) return 0;

        Type? type = typeof(ClientMain).Assembly.GetType("Vintagestory.Client.NoObf.TesselatedChunk");
        object? tessChunk = type == null ? null : Activator.CreateInstance(type);
        if (type == null || tessChunk == null) return 0;
        SetTessField(type, tessChunk, "chunk", chunk);
        SetTessField(type, tessChunk, "CullVisible", chunk.CullVisible);
        SetTessField(type, tessChunk, "positionX", target.X * 32);
        SetTessField(type, tessChunk, "positionYAndDimension", target.Y * 32);
        SetTessField(type, tessChunk, "positionZ", target.Z * 32);

        MethodInfo? process = tesselator.GetType().GetMethod(
            "NowProcessChunk", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        int vertices = (int)(process?.Invoke(tesselator, [target.X, target.Y, target.Z, tessChunk, false]) ?? 0);

        object? renderer = typeof(ClientMain)
            .GetField("chunkRenderer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?.GetValue(client.Client);
        MethodInfo? add = renderer?.GetType().GetMethod(
            "AddTesselatedChunk", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        add?.Invoke(renderer, [tessChunk, chunk]);
        return vertices;
    }

    private static void SetTessField(Type type, object target, string name, object? value)
    {
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.SetValue(target, value);
    }

    private static string PoolSummary(HeadlessClient client, ChunkPos target)
    {
        // MeshDataPoolManager.Render emits a multi-draw only for pools with a non-zero
        // indicesGroupsCount after frustum culling, so an empty summary means fixture chunks
        // tessellate to no poolable geometry.
        StringBuilder extra = new();

        FieldInfo? subidsField = typeof(ClientMain).GetField(
            "FastBlockTextureSubidsByBlockAndFace", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        extra.Append($" subids={(subidsField?.GetValue(client.Client) is int[][] s ? s.Length : -1)}");
        object? tesselator = typeof(ClientMain)
            .GetField("TerrainChunkTesselator", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?.GetValue(client.Client);
        extra.Append($" atlasses={tesselator?.GetType().GetField("quantityAtlasses", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(tesselator)}");
        extra.Append($" blocksCount={client.Client.Blocks?.Count} blocksLen={(client.Client.Blocks is BlockList bl2 ? bl2.BlocksFast.Length : -1)}");
        IWorldChunk? expectedChunk = client.Client.WorldMap.GetChunk(target.X, target.Y, target.Z);
        object? nearby = tesselator?.GetType().GetField("chunksNearby", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(tesselator);
        object? dataNearby = tesselator?.GetType().GetField("chunkdatasNearby", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(tesselator);
        object? centerChunk = nearby is Array nearbyArray && nearbyArray.Length > 13 ? nearbyArray.GetValue(13) : null;
        object? centerData = dataNearby is Array dataArray && dataArray.Length > 13 ? dataArray.GetValue(13) : null;
        extra.Append($" centerChunkMatches={ReferenceEquals(centerChunk, expectedChunk)} centerDataMatches={ReferenceEquals(centerData, expectedChunk?.Data)}");
        if (expectedChunk is ClientChunk expectedClientChunk)
        {
            int sampleIndex = ChunkFixture.ToIndex(2, 0, 2);
            extra.Append($" chunkRead66={expectedClientChunk.Data[sampleIndex]}");
            object? dataLayer = expectedClientChunk.Data.GetType().GetField("blocksLayer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(expectedClientChunk.Data);
            MethodInfo? getUnsafe = dataLayer?.GetType().GetMethod("GetUnsafe", BindingFlags.Instance | BindingFlags.Public);
            extra.Append($" layerRead66={getUnsafe?.Invoke(dataLayer, [sampleIndex])}");
        }
        object? extValue = tesselator?.GetType().GetField("currentChunkBlocksExt", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(tesselator);
        if (extValue is Array extBlocks)
        {
            int nonAir = extBlocks.Cast<object?>().Count(value => value is Block block && block.Id != 0);
            extra.Append($" extNonAir={nonAir}");
        }
        if (centerData != null)
        {
            object? layer = centerData.GetType().GetField("blocksLayer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(centerData);
            extra.Append($" centerPalette={layer?.GetType().GetField("paletteCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(layer)} centerBits={layer?.GetType().GetField("bitsize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(layer)}");
        }
        extra.Append($" mapChunks={tesselator?.GetType().GetField("mapsizeChunksx", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(tesselator)}" +
            $"/{tesselator?.GetType().GetField("mapsizeChunksy", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(tesselator)}" +
            $"/{tesselator?.GetType().GetField("mapsizeChunksz", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(tesselator)}");
        for (int id = 1; id <= 2; id++)
        {
            Block block = client.Client.Blocks[id];
            extra.Append($" block[{id}]/drawType={block.DrawType}");
        }
        FieldInfo? rendererField = typeof(ClientMain).GetField(
            "chunkRenderer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        object? renderer = rendererField?.GetValue(client.Client);
        if (renderer == null) return "chunkRenderer=null";

        FieldInfo? poolsField = renderer.GetType().GetField(
            "poolsByRenderPass", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (poolsField?.GetValue(renderer) is not Array passes) return "poolsByRenderPass=null";

        System.Collections.Generic.List<string> parts = [];
        for (int pass = 0; pass < Math.Min(passes.Length, 2); pass++)
        {
            if (passes.GetValue(pass) is not Array byAtlas) continue;
            for (int atlas = 0; atlas < byAtlas.Length; atlas++)
            {
                if (byAtlas.GetValue(atlas) is not MeshDataPoolManager manager) continue;
                int poolCount = 0;
                int groupCount = 0;
                FieldInfo? pools = manager.GetType().GetField(
                    "pools", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (pools?.GetValue(manager) is System.Collections.IEnumerable list)
                {
                    foreach (object? pool in list)
                    {
                        poolCount++;
                        groupCount += (int)(pool?.GetType()
                            .GetField("indicesGroupsCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                            ?.GetValue(pool) ?? 0);
                        if (pool?.GetType().GetField("poolLocations", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(pool) is System.Collections.IList locs)
                        {
                            foreach (object? loc in locs)
                            {
                                if (loc is ModelDataPoolLocation ml)
                                {
                                    extra.Append($" [loc: hide={ml.Hide} cullVis={ml.CullVisible[ModelDataPoolLocation.VisibleBufIndex]} sphere=({ml.FrustumCullSphere.x},{ml.FrustumCullSphere.y},{ml.FrustumCullSphere.z},r={ml.FrustumCullSphere.radius}) ind={ml.IndicesEnd - ml.IndicesStart}]");
                                }
                            }
                        }
                    }
                }

                parts.Add($"pass{pass}[{atlas}] pools={poolCount} groups={groupCount}");
            }
        }

        extra.Append($" camEye=({client.Client.MainCamera.CameraEyePos.X:F1},{client.Client.MainCamera.CameraEyePos.Y:F1},{client.Client.MainCamera.CameraEyePos.Z:F1})");
        extra.Append($" playerCam=({client.Client.EntityPlayer.CameraPos.X:F1},{client.Client.EntityPlayer.CameraPos.Y:F1},{client.Client.EntityPlayer.CameraPos.Z:F1})");
        extra.Append($" viewDist={ClientSettings.ViewDistance} vdSq={client.Client.frustumCuller.ViewDistanceSq} lod0Sq={client.Client.frustumCuller.lod0BiasSq}");

        return string.Join(" ", parts) + extra +
            $" awaitingTess={RuntimeStats.chunksAwaitingTesselation}" +
            $" awaitingPooling={RuntimeStats.chunksAwaitingPooling}";
    }

    [Fact]
    public void RenderPass_RegistersTheTerrainRenderSystem()
    {
        // SystemRenderTerrain and SystemRenderSkyColor are internal to Vintagestory.Client.NoObf,
        // so they are compared by name rather than by type.
        using HeadlessClient client = Boot();
        client.InitializeMockWorld();

        client.PrepareRenderPass();

        string[] registered = client.Client.clientSystems
            .Select(s => s.GetType().Name)
            .ToArray();

        Assert.Contains("SystemRenderTerrain", registered);
        Assert.Contains("SystemRenderSkyColor", registered);
    }

    [Fact]
    public void RenderPass_PostFramebufferAndGuiStagesRunWithoutThrowing()
    {
        // DeterministicFrameController.Step calls RenderToPrimary and then
        // RenderAfterPostProcessing, RenderAfterFinalComposition, RenderAfterBlit and
        // RenderToDefaultFramebuffer. Those four are the GUI and post-processing path.
        using HeadlessClient client = Boot();
        client.InitializeMockWorld();

        client.PrepareRenderPass();

        Exception? thrown = null;
        try
        {
            client.StepFrames(5, 1f / 60f);
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        Assert.True(
            thrown is null,
            $"GUI and post-framebuffer stages threw {thrown?.GetType().Name}: {thrown?.Message}\n{thrown?.StackTrace}");

        Assert.Equal(5, client.FrameController.TotalFrames);
    }

    [Fact]
    public void GlCommandProxy_PatchesBothOpenGlBindingGenerations()
    {
        // ClientPlatformWindows issues its draws through OpenTK.Graphics.OpenGL, not
        // OpenTK.Graphics.OpenGL4. A proxy that only patches the OpenGL4 class silently records
        // nothing, so every draw-call assertion would pass for the wrong reason.
        using HeadlessClient client = Boot();

        client.GL.Enable();
        try
        {
            IReadOnlyList<Type> targets = client.GL.PatchedTypes;

            Assert.Contains(typeof(OpenTK.Graphics.OpenGL.GL), targets);
            Assert.Contains(typeof(OpenTK.Graphics.OpenGL4.GL), targets);
        }
        finally
        {
            client.GL.Disable();
        }
    }

    private sealed class RenderStageProbe : IRenderer
    {
        public bool WasDispatched { get; private set; }

        public double RenderOrder => -1000.0;

        public int RenderRange => 0;

        public void OnRenderFrame(float deltaTime, EnumRenderStage stage) => WasDispatched = true;

        public void Dispose()
        {
        }
    }
}
