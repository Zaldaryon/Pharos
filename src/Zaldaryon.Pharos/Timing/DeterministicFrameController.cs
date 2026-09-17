using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using OpenTK.Graphics.OpenGL;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Timing;

/// <summary>
/// Event arguments delivered when a deterministic frame completes execution.
/// </summary>
public sealed class FrameCompletedEventArgs : EventArgs
{
    public long FrameNumber { get; }
    public float DeltaTime { get; }
    public double TotalElapsedSeconds { get; }

    public FrameCompletedEventArgs(long frameNumber, float deltaTime, double totalElapsedSeconds)
    {
        FrameNumber = frameNumber;
        DeltaTime = deltaTime;
        TotalElapsedSeconds = totalElapsedSeconds;
    }
}

/// <summary>
/// Controls frame stepping and timing deterministically for a headless Vintage Story client.
/// </summary>
public sealed class DeterministicFrameController
{
    private static readonly FieldInfo? s_currentScreenField = typeof(ScreenManager).GetField("CurrentScreen", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? s_lastTessField = typeof(ClientChunk).GetField("lastTesselationMs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? s_quantityDrawnField = typeof(ClientChunk).GetField("quantityDrawn", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? s_centerPoolField = typeof(ClientChunk).GetField("centerModelPoolLocations", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    private readonly ClientMain _client;
    private readonly ClientPlatformWindows _platform;
    private readonly ScreenManager _screenManager;
    private readonly GuiScreenRunningGame _runningGameScreen;
    private readonly HeadlessWindow _window;
    private readonly object _stepLock = new();

    private long _totalFrames;
    private double _totalElapsedSeconds;
    private float _lastDt;
    private bool _isStepping;

    public DeterministicFrameController(
        ClientMain client,
        ClientPlatformWindows platform,
        ScreenManager screenManager,
        GuiScreenRunningGame runningGameScreen,
        HeadlessWindow window)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _screenManager = screenManager ?? throw new ArgumentNullException(nameof(screenManager));
        _runningGameScreen = runningGameScreen ?? throw new ArgumentNullException(nameof(runningGameScreen));
        _window = window ?? throw new ArgumentNullException(nameof(window));
    }

    /// <summary>
    /// Total count of frames executed deterministically.
    /// </summary>
    public long TotalFrames => _totalFrames;

    /// <summary>
    /// Accumulated simulated seconds across all executed frames.
    /// </summary>
    public double TotalElapsedSeconds => _totalElapsedSeconds;

    /// <summary>
    /// Delta time of the most recently executed frame.
    /// </summary>
    public float LastDeltaTime => _lastDt;

    /// <summary>
    /// Whether a frame step is currently in progress.
    /// </summary>
    public bool IsStepping => _isStepping;

    /// <summary>
    /// Raised immediately after each deterministic frame completes.
    /// </summary>
    public event EventHandler<FrameCompletedEventArgs>? OnFrameCompleted;

    /// <summary>
    /// Advances the client by a single deterministic frame.
    /// </summary>
    public void Step(float dt = 1f / 60f)
    {
        if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt))
        {
            throw new ArgumentOutOfRangeException(nameof(dt), "Delta time must be positive and finite.");
        }

        lock (_stepLock)
        {
            _isStepping = true;
            try
            {
                // 0. Ensure GLFW context is current
                _window.NativeWindow.MakeCurrent();

                // 1. Process pending GLFW window events
                _window.NativeWindow.ProcessEvents(0.0);

                // 2. Increment frame counters
                _totalFrames++;
                _totalElapsedSeconds += dt;
                _lastDt = dt;

                // 3. Process main thread tasks queued in ScreenManager (snapshot to avoid deadlocks)
                Action[] pendingTasks;
                lock (ScreenManager.MainThreadTasks)
                {
                    if (ScreenManager.MainThreadTasks.Count > 0)
                    {
                        pendingTasks = ScreenManager.MainThreadTasks.ToArray();
                        ScreenManager.MainThreadTasks.Clear();
                    }
                    else
                    {
                        pendingTasks = Array.Empty<Action>();
                    }
                }

                foreach (Action task in pendingTasks)
                {
                    task.Invoke();
                }

                // 4. Ensure offscreen FBO is bound for rendering
                _window.Framebuffer.Bind();

                // 5. Execute screen and game rendering pipeline
                if (_client.Player?.Entity?.Pos != null)
                {
                    _runningGameScreen.RenderToPrimary(dt);
                    _runningGameScreen.RenderAfterPostProcessing(dt);
                    _runningGameScreen.RenderAfterFinalComposition(dt);
                    _runningGameScreen.RenderAfterBlit(dt);
                    _runningGameScreen.RenderToDefaultFramebuffer(dt);
                }
                else
                {
                    _client.ExecuteMainThreadTasks(dt);

                    if (_client.eventManager != null && !_client.IsPaused)
                    {
                        long deterministicElapsedMs = (long)(_totalElapsedSeconds * 1000.0);
                        _client.eventManager.TriggerGameTick(deterministicElapsedMs, _client);
                    }

                    GuiScreen? currentScreen = s_currentScreenField?.GetValue(_screenManager) as GuiScreen;
                    if (currentScreen != null && currentScreen != _runningGameScreen)
                    {
                        currentScreen.RenderToPrimary(dt);
                        currentScreen.RenderToDefaultFramebuffer(dt);
                    }
                }

                // 6. Flush rendering pipeline
                GL.Flush();

                // 7. Fire notification
                OnFrameCompleted?.Invoke(this, new FrameCompletedEventArgs(_totalFrames, dt, _totalElapsedSeconds));
            }
            finally
            {
                _isStepping = false;
            }
        }
    }

    /// <summary>
    /// Advances the client by multiple deterministic frames synchronously.
    /// </summary>
    public void StepFrames(int count, float dt = 1f / 60f)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Frame count must be non-negative.");
        }

        for (int i = 0; i < count; i++)
        {
            Step(dt);
        }
    }

    /// <summary>
    /// Asynchronously advances the client by one deterministic frame.
    /// </summary>
    public Task FrameAsync(float dt = 1f / 60f, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Step(dt);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asynchronously advances the client by N deterministic frames.
    /// </summary>
    public Task FramesAsync(int count, float dt = 1f / 60f, CancellationToken ct = default)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Frame count must be non-negative.");
        }

        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            Step(dt);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Advances frames until the specified predicate returns true, or the maximum frame budget is reached.
    /// </summary>
    public async Task<bool> WaitForAsync(Func<bool> predicate, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        for (int i = 0; i < maxFrames; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (predicate())
            {
                return true;
            }

            await FrameAsync(dt, ct);

            if (predicate())
            {
                return true;
            }
        }

        return predicate();
    }

    /// <summary>
    /// Determines whether the chunk at the specified position has been meshed or is empty.
    /// </summary>
    public bool IsChunkMeshed(ChunkPos chunkPos)
    {
        if (_client.WorldMap == null)
        {
            return false;
        }

        IWorldChunk? chunk = _client.WorldMap.GetChunk(chunkPos.X, chunkPos.Y, chunkPos.Z);
        if (chunk == null)
        {
            return false;
        }

        if (chunk.Empty)
        {
            return true;
        }

        if (_client.WorldMap.IsChunkRendered(chunkPos.X, chunkPos.Y, chunkPos.Z))
        {
            return true;
        }

        if (chunk is ClientChunk clientChunk)
        {
            long lastTess = (long)(s_lastTessField?.GetValue(clientChunk) ?? 0L);
            if (lastTess > 0)
            {
                return true;
            }

            int quantityDrawn = (int)(s_quantityDrawnField?.GetValue(clientChunk) ?? 0);
            if (quantityDrawn > 0)
            {
                return true;
            }

            if (s_centerPoolField?.GetValue(clientChunk) != null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Advances frames until the chunk at the given coordinate is meshed or confirmed empty.
    /// Throws TimeoutException if the chunk is not meshed within the specified frame budget.
    /// </summary>
    public async Task<bool> WaitForChunkMeshedAsync(ChunkPos chunkPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        bool meshed = await WaitForAsync(() => IsChunkMeshed(chunkPos), maxFrames, dt, ct);
        if (!meshed)
        {
            throw new TimeoutException($"Chunk at {chunkPos} was not meshed within {maxFrames} frames.");
        }
        return true;
    }

    public Task<bool> WaitForChunkMeshedAsync(int chunkX, int chunkY, int chunkZ, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return WaitForChunkMeshedAsync(new ChunkPos(chunkX, chunkY, chunkZ), maxFrames, dt, ct);
    }

    public Task<bool> WaitForChunkMeshedAsync(Vec3i chunkPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return WaitForChunkMeshedAsync(new ChunkPos(chunkPos), maxFrames, dt, ct);
    }

    public Task<bool> WaitForChunkMeshedAsync(BlockPos blockPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return WaitForChunkMeshedAsync(ChunkPos.FromBlockPos(blockPos), maxFrames, dt, ct);
    }
}
