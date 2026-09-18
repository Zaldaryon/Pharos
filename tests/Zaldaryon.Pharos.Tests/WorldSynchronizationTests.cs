using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests;

[Collection("Sequential")]
public class WorldSynchronizationTests
{
    static WorldSynchronizationTests()
    {
        HeadlessPlatformResolver.Initialize();
    }

    [Fact]
    public async Task WaitForAllMeshesReady_WhenNoQueuedChunks_CompletesImmediately()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        // Queues are empty on boot, so AreAllMeshesReady should return true
        Assert.True(client.FrameController.AreAllMeshesReady());

        bool ready = await client.WaitForAllMeshesReady(maxFrames: 10);
        Assert.True(ready);
    }

    [Fact]
    public async Task WaitForChunkMeshed_WhenChunkMissing_ThrowsDescriptiveTimeoutException()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        ChunkPos targetChunk = new(42, 1, 99);
        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await client.WaitForChunkMeshed(targetChunk, maxFrames: 3, dt: 1f / 60f);
        });

        Assert.Contains(targetChunk.ToString(), ex.Message);
        Assert.Contains("within 3 frames", ex.Message);
        Assert.Contains("Chunk status:", ex.Message);
        Assert.Contains("awaitingTesselation=", ex.Message);
    }

    [Fact]
    public async Task WaitForWorldReady_WhenWorldNotLoaded_ThrowsDescriptiveTimeoutException()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await client.WaitForWorldReady(radius: 1, maxFrames: 3, dt: 1f / 60f);
        });

        Assert.Contains("World was not ready within 3 frames", ex.Message);
        Assert.Contains("radius: 1", ex.Message);
        Assert.Contains("Unmeshed chunks remaining", ex.Message);
        Assert.Contains("awaitingTesselation=", ex.Message);
    }

    [Fact]
    public void WaitForWorldReady_NegativeRadius_ThrowsArgumentOutOfRangeException()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            client.FrameController.IsWorldReady(-1, out _);
        });
    }

    [Fact]
    public async Task WaitForWorldReady_CenterOverloads_ExecuteCorrectly()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        ChunkPos chunkCenter = new(10, 2, 10);
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await client.WaitForWorldReady(chunkCenter, radius: 0, maxFrames: 2);
        });

        BlockPos blockCenter = new(320, 64, 320);
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await client.WaitForWorldReady(blockCenter, radius: 0, maxFrames: 2);
        });
    }

    [Fact]
    public async Task ClientServerLoopbackSession_ExposesWorldSynchronizationPrimitives()
    {
        HeadlessClientOptions clientOptions = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using EmbeddedServerHost server = EmbeddedServerHost.Boot();
        using HeadlessClient client = HeadlessClientBootstrap.Boot(clientOptions);
        using ClientServerLoopbackSession session = client.ConnectLoopback(server, "SyncPilot");

        // Wait for all meshes ready in session (empty/initial queues)
        bool meshesReady = await session.WaitForAllMeshesReady(maxFrames: 5);
        Assert.True(meshesReady);

        // Timeout on an unreachable distant chunk
        ChunkPos distantChunk = new(9999, 0, 9999);
        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await session.WaitForChunkMeshed(distantChunk, maxFrames: 2);
        });
    }

    [Fact]
    public void IsWorldReady_WithRadiusZero_ChecksSingleChunkCoordinate()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        ChunkPos center = new(5, 3, 7);
        bool ready = client.FrameController.IsWorldReady(0, out List<ChunkPos> unmeshed, center);
        Assert.False(ready);
        // Radius 0 must check strictly 1 chunk coordinate (the center chunk)
        Assert.Single(unmeshed);
        Assert.Equal(center, unmeshed[0]);
    }

    [Fact]
    public void IsWorldReady_WithRadiusOne_ChecksTwentySevenChunks()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        ChunkPos center = new(5, 3, 7);
        bool ready = client.FrameController.IsWorldReady(1, out List<ChunkPos> unmeshed, center);
        Assert.False(ready);
        // Radius 1: 3x3 horizontal columns, 3 vertical chunks (y=2, 3, 4) = 27 chunks total
        Assert.Equal(27, unmeshed.Count);
        Assert.Contains(center, unmeshed);
    }

    [Fact]
    public async Task IsChunkMeshed_WhenChunkIsEmptyOrRendered_ReturnsTrueAndAwaitsSuccessfully()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        FieldInfo? chunksField = typeof(ClientWorldMap).GetField("chunks", BindingFlags.Instance | BindingFlags.NonPublic);
        object? chunksDictObj = chunksField?.GetValue(client.Client.WorldMap);
        if (chunksDictObj is Dictionary<long, ClientChunk> chunksDict)
        {
            ClientChunk emptyChunk = (ClientChunk)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(ClientChunk));
            emptyChunk.Empty = true;

            long key = MapUtil.Index3dL(10, 2, 10, client.Client.WorldMap.index3dMulX, client.Client.WorldMap.index3dMulZ);
            lock (chunksDict)
            {
                chunksDict[key] = emptyChunk;
            }

            ChunkPos pos = new(10, 2, 10);
            Assert.True(client.FrameController.IsChunkMeshed(pos));

            bool meshed = await client.WaitForChunkMeshed(pos, maxFrames: 2);
            Assert.True(meshed);

            // Mark as enquedForRedraw, which should make IsChunkMeshed false
            FieldInfo? redrawField = typeof(ClientChunk).GetField("enquedForRedraw", BindingFlags.Instance | BindingFlags.NonPublic);
            redrawField?.SetValue(emptyChunk, true);

            Assert.False(client.FrameController.IsChunkMeshed(pos));
        }
    }

    [Fact]
    public async Task ClientServerLoopbackSession_TimeoutException_ContainsActionableDiagnostics()
    {
        HeadlessClientOptions clientOptions = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using EmbeddedServerHost server = EmbeddedServerHost.Boot();
        using HeadlessClient client = HeadlessClientBootstrap.Boot(clientOptions);
        using ClientServerLoopbackSession session = client.ConnectLoopback(server, "DiagPilot");

        ChunkPos unreachable = new(8888, 1, 8888);
        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await session.WaitForChunkMeshed(unreachable, maxFrames: 2);
        });

        Assert.Contains("Chunk at ChunkPos(8888, 1, 8888)", ex.Message);
        Assert.Contains("Chunk status:", ex.Message);
        Assert.Contains("awaitingTesselation=", ex.Message);
        Assert.Contains("dirtyPriority=", ex.Message);

        TimeoutException worldEx = await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await session.WaitForWorldReady(radius: 1, maxFrames: 2);
        });

        Assert.Contains("World was not ready in loopback session within 2 frames", worldEx.Message);
        Assert.Contains("radius: 1", worldEx.Message);
        Assert.Contains("awaitingTesselation=", worldEx.Message);
        Assert.Contains("dirtyPriority=", worldEx.Message);
    }
}
