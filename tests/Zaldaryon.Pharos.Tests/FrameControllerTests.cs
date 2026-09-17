using System;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests;

[Collection("Sequential")]
public class FrameControllerTests
{
    static FrameControllerTests()
    {
        HeadlessPlatformResolver.Initialize();
    }

    [Fact]
    public async Task Frame_Should_AdvanceSingleFrameAndAccumulateDeltaTime()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        Assert.Equal(0, client.FrameController.TotalFrames);
        Assert.Equal(0.0, client.FrameController.TotalElapsedSeconds);

        float dt = 1f / 60f;
        await client.Frame(dt);

        Assert.Equal(1, client.FrameController.TotalFrames);
        Assert.InRange(client.FrameController.TotalElapsedSeconds, (double)dt - 0.0001, (double)dt + 0.0001);
        Assert.Equal(dt, client.FrameController.LastDeltaTime);
    }

    [Fact]
    public async Task Frames_Should_AdvanceMultipleFramesDeterministically()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        float dt = 0.02f; // 50 FPS
        int frameCount = 10;
        await client.Frames(frameCount, dt);

        Assert.Equal(10, client.FrameController.TotalFrames);
        Assert.InRange(client.FrameController.TotalElapsedSeconds, 0.2 - 0.0001, 0.2 + 0.0001);
    }

    [Fact]
    public void Step_Should_ExecuteScreenManagerMainThreadTasks()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        bool taskExecuted = false;
        ScreenManager.EnqueueMainThreadTask(() => taskExecuted = true);

        Assert.False(taskExecuted, "Task must not execute before stepping a frame");

        client.Step(1f / 60f);

        Assert.True(taskExecuted, "Task must execute during frame step");
        Assert.Equal(1, client.FrameController.TotalFrames);
    }

    [Fact]
    public async Task WaitFor_Should_AdvanceFramesUntilConditionMet()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        int targetFrame = 7;
        bool result = await client.WaitFor(
            () => client.FrameController.TotalFrames >= targetFrame,
            maxFrames: 50,
            dt: 1f / 60f);

        Assert.True(result);
        Assert.True(client.FrameController.TotalFrames >= targetFrame);
    }

    [Fact]
    public async Task WaitFor_Should_ReturnFalseWhenConditionNeverMetWithinBudget()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        bool result = await client.WaitFor(
            () => false,
            maxFrames: 4,
            dt: 1f / 60f);

        Assert.False(result);
        Assert.Equal(4, client.FrameController.TotalFrames);
    }

    [Fact]
    public async Task WaitForChunkMeshed_Should_ThrowTimeoutWhenChunkNotPresent()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        // In standalone offline test mode without server, an unmeshed chunk position should timeout
        ChunkPos nonExistentChunk = new(100, 200, 300);

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await client.WaitForChunkMeshed(nonExistentChunk, maxFrames: 3, dt: 1f / 60f);
        });

        Assert.Equal(3, client.FrameController.TotalFrames);
    }

    [Fact]
    public void ChunkPos_Should_ConstructAndConvertAccurately()
    {
        ChunkPos cp1 = new(1, 2, 3);
        Assert.Equal(1, cp1.X);
        Assert.Equal(2, cp1.Y);
        Assert.Equal(3, cp1.Z);

        Vec3i vec = cp1.ToVec3i();
        Assert.Equal(1, vec.X);
        Assert.Equal(2, vec.Y);
        Assert.Equal(3, vec.Z);

        ChunkPos cp2 = new(vec);
        Assert.Equal(cp1, cp2);

        BlockPos blockPos = new(64, 96, 128);
        ChunkPos fromBlock = ChunkPos.FromBlockPos(blockPos);
        Assert.Equal(2, fromBlock.X);
        Assert.Equal(3, fromBlock.Y);
        Assert.Equal(4, fromBlock.Z);

        ChunkPos fromCoords = ChunkPos.FromBlockCoordinates(32, 64, 96);
        Assert.Equal(1, fromCoords.X);
        Assert.Equal(2, fromCoords.Y);
        Assert.Equal(3, fromCoords.Z);

        // Negative coordinates floor towards negative infinity (>> 5)
        ChunkPos negCoords = ChunkPos.FromBlockCoordinates(-1, -32, -33);
        Assert.Equal(-1, negCoords.X);
        Assert.Equal(-1, negCoords.Y);
        Assert.Equal(-2, negCoords.Z);

        BlockPos negBlock = new(-64, -1, -65);
        ChunkPos negFromBlock = ChunkPos.FromBlockPos(negBlock);
        Assert.Equal(-2, negFromBlock.X);
        Assert.Equal(-1, negFromBlock.Y);
        Assert.Equal(-3, negFromBlock.Z);
    }

    [Fact]
    public async Task FrameController_Should_FireOnFrameCompletedEvent()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        int eventCount = 0;
        long lastFrame = 0;
        float lastDt = 0;

        client.FrameController.OnFrameCompleted += (sender, args) =>
        {
            eventCount++;
            lastFrame = args.FrameNumber;
            lastDt = args.DeltaTime;
        };

        await client.Frame(0.02f);

        Assert.Equal(1, eventCount);
        Assert.Equal(1, lastFrame);
        Assert.Equal(0.02f, lastDt);
    }

    [Fact]
    public async Task FrameAsync_Should_RespectCancellationToken()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.Frame(ct: cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await client.Frames(5, ct: cts.Token);
        });
    }

    [Fact]
    public void Step_Should_ThrowOnInvalidArguments()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        Assert.Throws<ArgumentOutOfRangeException>(() => client.Step(-0.01f));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.Step(0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => client.StepFrames(-1));
    }
}
