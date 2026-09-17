using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.Network;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Server;
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
    public IClientTestPlayer TestPlayer { get; }
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
        TestPlayer = new ClientTestPlayer(client);
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

    /// <summary>
    /// Connects the headless client to an in-process embedded Atlas server instance using engine singleplayer loopback.
    /// </summary>
    public ClientServerLoopbackSession ConnectLoopback(AtlasServerHost server, string playerName = "PharosTest")
    {
        ArgumentNullException.ThrowIfNull(server);

        ClientSettings.PlayerName = playerName;
        ClientSettings.PlayerUID = "pharos-" + playerName.ToLowerInvariant();

        Client.IsSingleplayer = true;
        Client.Connectdata = new ServerConnectData
        {
            Host = "localhost",
            Port = 42424
        };

        FieldInfo? serverInfoField = typeof(ClientMain).GetField("ServerInfo", BindingFlags.NonPublic | BindingFlags.Instance);
        if (serverInfoField != null)
        {
            object? serverInfo = serverInfoField.GetValue(Client);
            if (serverInfo == null)
            {
                serverInfo = Activator.CreateInstance(serverInfoField.FieldType);
                serverInfoField.SetValue(Client, serverInfo);
            }
            serverInfo?.GetType().GetField("connectdata")?.SetValue(serverInfo, Client.Connectdata);
        }

        Platform.singlePlayerServerDummyNetwork = new DummyNetwork[2]
        {
            server.TcpNetwork,
            server.UdpNetwork
        };

        DummyTcpNetClient dummyTcp = new();
        dummyTcp.SetNetwork(server.TcpNetwork);
        Client.MainNetClient = dummyTcp;

        DummyUdpNetClient dummyUdp = new();
        dummyUdp.SetNetwork(server.UdpNetwork);
        Client.UdpNetClient = dummyUdp;

        Client.Connect();

        return new ClientServerLoopbackSession(this, server);
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
