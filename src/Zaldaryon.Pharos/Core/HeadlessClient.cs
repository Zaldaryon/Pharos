using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Core;

/// <summary>
/// Headless Vintage Story client instance running offscreen in-process.
/// </summary>
public sealed class HeadlessClient : IDisposable
{
    private bool _disposed;
    private readonly string? _tempDataPath;

    public ClientMain Client { get; }
    public ClientPlatformWindows Platform { get; }
    public ScreenManager ScreenManager { get; }
    public GuiScreenRunningGame RunningGameScreen { get; }
    public HeadlessWindow Window { get; }
    public HeadlessFramebuffer Framebuffer => Window.Framebuffer;
    public GlRendererInfo? RendererInfo => Window.RendererInfo;
    public HeadlessClientOptions Options { get; }
    public DeterministicFrameController FrameController { get; }
    public bool IsDisposed => _disposed;

    internal HeadlessClient(
        ClientMain client,
        ClientPlatformWindows platform,
        ScreenManager screenManager,
        GuiScreenRunningGame runningGameScreen,
        HeadlessWindow window,
        HeadlessClientOptions options,
        string? tempDataPath)
    {
        Client = client;
        Platform = platform;
        ScreenManager = screenManager;
        RunningGameScreen = runningGameScreen;
        Window = window;
        Options = options;
        _tempDataPath = tempDataPath;
        FrameController = new DeterministicFrameController(client, platform, screenManager, runningGameScreen, window);
    }

    /// <summary>
    /// Advances the client by one deterministic frame.
    /// </summary>
    public Task Frame(float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.FrameAsync(dt, ct);
    }

    /// <summary>
    /// Advances the client by N deterministic frames.
    /// </summary>
    public Task Frames(int count, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.FramesAsync(count, dt, ct);
    }

    /// <summary>
    /// Advances frames until the specified predicate returns true or maximum frames reached.
    /// </summary>
    public Task<bool> WaitFor(Func<bool> predicate, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForAsync(predicate, maxFrames, dt, ct);
    }

    /// <summary>
    /// Advances frames until the specified chunk is meshed or confirmed empty.
    /// </summary>
    public Task<bool> WaitForChunkMeshed(ChunkPos chunkPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForChunkMeshedAsync(chunkPos, maxFrames, dt, ct);
    }

    public Task<bool> WaitForChunkMeshed(int chunkX, int chunkY, int chunkZ, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForChunkMeshedAsync(chunkX, chunkY, chunkZ, maxFrames, dt, ct);
    }

    public Task<bool> WaitForChunkMeshed(Vec3i chunkPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForChunkMeshedAsync(chunkPos, maxFrames, dt, ct);
    }

    public Task<bool> WaitForChunkMeshed(BlockPos blockPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForChunkMeshedAsync(blockPos, maxFrames, dt, ct);
    }

    /// <summary>
    /// Advances the client by one deterministic frame synchronously.
    /// </summary>
    public void Step(float dt = 1f / 60f)
    {
        FrameController.Step(dt);
    }

    /// <summary>
    /// Advances the client by N deterministic frames synchronously.
    /// </summary>
    public void StepFrames(int count, float dt = 1f / 60f)
    {
        FrameController.StepFrames(count, dt);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            Client.Dispose();
        }
        catch
        {
            // Ignore client shutdown errors
        }

        try
        {
            if (Platform.Logger is IDisposable disposableLogger)
            {
                disposableLogger.Dispose();
            }
        }
        catch
        {
            // Ignore logger disposal errors
        }

        try
        {
            Window.Dispose();
        }
        catch
        {
            // Ignore teardown errors during test shutdown
        }

        try
        {
            lock (ScreenManager.MainThreadTasks)
            {
                ScreenManager.MainThreadTasks.Clear();
            }
        }
        catch
        {
            // Ignore queue clearing errors
        }

        if (!string.IsNullOrEmpty(_tempDataPath) && Directory.Exists(_tempDataPath))
        {
            try
            {
                Directory.Delete(_tempDataPath, recursive: true);
            }
            catch
            {
                // Best effort temporary cleanup
            }
        }
    }
}
