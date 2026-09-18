using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;
using Vintagestory.Server;
using Vintagestory.Server.Network;
using Zaldaryon.Pharos.Platform;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Hosts an embedded, in-process Vintage Story server with loopback networking.
/// </summary>
/// <remarks>
/// <para>
/// This host replaces <see cref="AtlasServerHost"/> with native Pharos types, removing
/// the dependence on Pixnop.Atlas.WorldOptions.
/// </para>
/// <para>
/// The host provides lifecycle guards for known engine race conditions:
/// <list type="bullet">
/// <item>Exit state: <c>server.exitState</c> is assigned before <c>PreLaunch()</c> to prevent NRE in packet parser threads.</item>
/// <item>Asset drain: Shutdown waits for background <c>BuildServerAssetsPacket</c> tasks to complete.</item>
/// <item>Exception containment: Known NREs in <c>ServerSystemMonitor.Dispose()</c> are caught and logged.</item>
/// </list>
/// </para>
/// <para>
/// The host provides deterministic tick control:
/// <list type="bullet">
/// <item><see cref="Tick"/>: Advances the server by exactly one simulation tick.</item>
/// <item><see cref="Ticks"/>: Advances the server by N consecutive ticks.</item>
/// <item><see cref="TickUntilAsync(Func{bool}, int, CancellationToken)"/>: Ticks until a condition is met.</item>
/// <item><see cref="RunOnGameThreadAsync(Action)"/>: Queues work to execute on the game thread during tick processing.</item>
/// </list>
/// </para>
/// </remarks>
public sealed class EmbeddedServerHost : IDisposable, IAsyncDisposable
{
    private bool _disposed;
    private readonly bool _ownsDataPath;
    private readonly MethodInfo? _waitOnBuildServerAssetsPacket;
    private int _tickCount;
    private Exception? _serverException;
    private readonly ConcurrentQueue<(Action action, TaskCompletionSource tcs)> _gameThreadQueue = new();
    private readonly ConcurrentQueue<(Func<object?> func, TaskCompletionSource<object?> tcs)> _gameThreadFuncQueue = new();

    /// <summary>The underlying Vintage Story server instance.</summary>
    public ServerMain Server { get; }

    /// <summary>The dummy TCP transport layer used for in-process connections.</summary>
    public DummyNetwork TcpNetwork { get; }

    /// <summary>The dummy UDP transport layer used for in-process connections.</summary>
    public DummyNetwork UdpNetwork { get; }

    /// <summary>The world configuration options used to create this server.</summary>
    public ServerWorldOptions Options { get; }

    /// <summary>The data directory path used for server storage.</summary>
    public string DataPath { get; }

    /// <summary>The sandbox instance used for this server, if any.</summary>
    public ServerSandbox? Sandbox { get; }

    /// <summary>Returns true if the server is running and not disposed.</summary>
    public bool IsRunning => !_disposed && !Server.stopped;

    /// <summary>Returns the current number of connected clients.</summary>
    public int ConnectedClientCount => Server.Clients?.Count ?? 0;

    /// <summary>
    /// Gets the number of simulation ticks that have been processed.
    /// </summary>
    public int TickCount => _tickCount;

    private EmbeddedServerHost(
        ServerMain server,
        DummyNetwork tcpNetwork,
        DummyNetwork udpNetwork,
        ServerWorldOptions options,
        string dataPath,
        bool ownsDataPath,
        ServerSandbox? sandbox = null)
    {
        Server = server;
        TcpNetwork = tcpNetwork;
        UdpNetwork = udpNetwork;
        Options = options;
        DataPath = dataPath;
        _ownsDataPath = ownsDataPath;
        Sandbox = sandbox;

        _waitOnBuildServerAssetsPacket = typeof(ServerMain).GetMethod(
            "WaitOnBuildServerAssetsPacket",
            BindingFlags.Instance | BindingFlags.NonPublic);
    }

    /// <summary>
    /// Boots an embedded server instance with the specified world options and isolated scratch storage.
    /// </summary>
    /// <param name="options">World configuration options. When null, uses default superflat creative settings.</param>
    /// <param name="customDataPath">Optional data directory. When null, creates a temporary scratch directory.</param>
    /// <returns>A running embedded server host.</returns>
    public static EmbeddedServerHost Boot(ServerWorldOptions? options = null, string? customDataPath = null)
    {
        HeadlessPlatformResolver.Initialize();
        HeadlessPlatformResolver.EnsureAssetsPath();

        options ??= new ServerWorldOptions();

        bool ownsDataPath = string.IsNullOrEmpty(customDataPath);
        string dataPath = customDataPath ?? Path.Combine(Path.GetTempPath(), "pharos-embedded-" + Guid.NewGuid().ToString("N")[..8]);

        GamePaths.DataPath = dataPath;
        GamePaths.EnsurePathsExist();

        ServerProgramArgs progArgs = new()
        {
            DataPath = dataPath
        };
        ServerMain.Logger = (Logger)new ServerLogger(progArgs);
        Lang.PreLoad((ILogger)(object)ServerMain.Logger, GamePaths.AssetsPath, "en");

        DummyNetwork tcpNetwork = new();
        tcpNetwork.Start();

        DummyNetwork udpNetwork = new();
        udpNetwork.Start();

        DummyTcpNetServer dummyTcpServer = new();
        dummyTcpServer.SetNetwork(tcpNetwork);

        DummyUdpNetServer dummyUdpServer = new();
        dummyUdpServer.SetNetwork(udpNetwork);

        string saveLocation = options.SaveFileLocation ?? Path.Combine(dataPath, "Saves", options.WorldName + ".vcdbs");

        StartServerArgs startArgs = new()
        {
            Seed = options.Seed,
            WorldName = options.WorldName,
            SaveFileLocation = saveLocation,
            AllowCreativeMode = true,
            PlayStyle = options.PlayStyle,
            PlayStyleLangCode = options.PlayStyle,
            WorldType = options.WorldType,
            WorldConfiguration = JsonObject.FromJson(options.WorldConfigurationJson),
            Language = "en",
            IsNew = true
        };

        ServerMain server = new(startArgs, new[] { "--dataPath", dataPath }, progArgs, isDedicatedServer: false);

        // CRITICAL: Assign exitState BEFORE PreLaunch() to prevent NRE in packet parser threads
        server.exitState = new GameExitState();

        server.MainSockets[0] = dummyTcpServer;
        server.UdpSockets[0] = dummyUdpServer;

        server.PreLaunch();
        server.Launch();

        // Wait for background asset build tasks to settle
        try
        {
            MethodInfo? waitMethod = typeof(ServerMain).GetMethod("WaitOnBuildServerAssetsPacket", BindingFlags.Instance | BindingFlags.NonPublic);
            waitMethod?.Invoke(server, null);
        }
        catch
        {
            // Best effort wait
        }

        return new EmbeddedServerHost(server, tcpNetwork, udpNetwork, options, dataPath, ownsDataPath, sandbox: null);
    }

    /// <summary>
    /// Boots an embedded server instance using a pre-configured sandbox for filesystem isolation.
    /// </summary>
    /// <param name="sandbox">The server sandbox to use for storage paths.</param>
    /// <param name="options">World configuration options. When null, uses default superflat creative settings.</param>
    /// <returns>A running embedded server host.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="sandbox"/> is null.</exception>
    public static EmbeddedServerHost Boot(ServerSandbox sandbox, ServerWorldOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(sandbox);

        HeadlessPlatformResolver.Initialize();
        HeadlessPlatformResolver.EnsureAssetsPath();

        options ??= new ServerWorldOptions();

        string dataPath = sandbox.RootPath;

        GamePaths.DataPath = dataPath;
        GamePaths.EnsurePathsExist();

        ServerProgramArgs progArgs = new()
        {
            DataPath = dataPath
        };
        ServerMain.Logger = (Logger)new ServerLogger(progArgs);
        Lang.PreLoad((ILogger)(object)ServerMain.Logger, GamePaths.AssetsPath, "en");

        DummyNetwork tcpNetwork = new();
        tcpNetwork.Start();

        DummyNetwork udpNetwork = new();
        udpNetwork.Start();

        DummyTcpNetServer dummyTcpServer = new();
        dummyTcpServer.SetNetwork(tcpNetwork);

        DummyUdpNetServer dummyUdpServer = new();
        dummyUdpServer.SetNetwork(udpNetwork);

        string saveLocation = options.SaveFileLocation ?? Path.Combine(sandbox.SavesPath, options.WorldName + ".vcdbs");

        StartServerArgs startArgs = new()
        {
            Seed = options.Seed,
            WorldName = options.WorldName,
            SaveFileLocation = saveLocation,
            AllowCreativeMode = true,
            PlayStyle = options.PlayStyle,
            PlayStyleLangCode = options.PlayStyle,
            WorldType = options.WorldType,
            WorldConfiguration = JsonObject.FromJson(options.WorldConfigurationJson),
            Language = "en",
            IsNew = true
        };

        ServerMain server = new(startArgs, new[] { "--dataPath", dataPath }, progArgs, isDedicatedServer: false);

        // CRITICAL: Assign exitState BEFORE PreLaunch() to prevent NRE in packet parser threads
        server.exitState = new GameExitState();

        server.MainSockets[0] = dummyTcpServer;
        server.UdpSockets[0] = dummyUdpServer;

        server.PreLaunch();
        server.Launch();

        // Wait for background asset build tasks to settle
        try
        {
            MethodInfo? waitMethod = typeof(ServerMain).GetMethod("WaitOnBuildServerAssetsPacket", BindingFlags.Instance | BindingFlags.NonPublic);
            waitMethod?.Invoke(server, null);
        }
        catch
        {
            // Best effort wait
        }

        return new EmbeddedServerHost(server, tcpNetwork, udpNetwork, options, dataPath, ownsDataPath: false, sandbox);
    }

    /// <summary>
    /// Captures a snapshot of the current world state for later restoration.
    /// </summary>
    /// <returns>A snapshot containing the current world state.</returns>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    /// <remarks>
    /// The snapshot captures chunk data and server options. Restoration typically completes
    /// in under 100ms, much faster than restarting the server.
    /// </remarks>
    public WorldSnapshot TakeSnapshot()
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("Cannot take snapshot from a stopped server.");
        }

        return WorldSnapshot.CreateFrom(this);
    }

    /// <summary>
    /// Restores a previously captured snapshot to this server.
    /// </summary>
    /// <param name="snapshot">The snapshot to restore.</param>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    /// <remarks>
    /// After restoration, the server state matches the state at the time the snapshot was captured.
    /// This allows tests to rapidly reset to a known state without restarting the server.
    /// </remarks>
    public void RestoreSnapshot(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!IsRunning)
        {
            throw new InvalidOperationException("Cannot restore snapshot to a stopped server.");
        }

        snapshot.Restore(this);
    }

    /// <summary>
    /// Advances the server by exactly one simulation tick.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method processes one server tick via <c>ServerMain.Process()</c> and drains
    /// any pending game thread dispatch queue items before and after the tick.
    /// </para>
    /// <para>
    /// If the server has crashed, this method throws <see cref="ServerCrashedException"/>
    /// wrapping the original exception.
    /// </para>
    /// </remarks>
    /// <exception cref="ServerCrashedException">The server previously crashed during tick processing.</exception>
    public void Tick()
    {
        if (_disposed) return;

        // Check for prior crash
        if (_serverException is not null)
        {
            throw ServerCrashedException.FromServerException(_serverException);
        }

        // Drain pending game thread actions before the tick
        DrainGameThreadQueue();

        try
        {
            Server.Process();
            _tickCount++;
        }
        catch (Exception ex)
        {
            _serverException = ex;
            FaultPendingWaiters(ex);
            throw ServerCrashedException.FromServerException(ex);
        }

        // Drain any actions queued during the tick
        DrainGameThreadQueue();
    }

    /// <summary>
    /// Advances the server by the specified number of consecutive ticks.
    /// </summary>
    /// <param name="count">The number of ticks to process.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ServerCrashedException">The server crashed during tick processing.</exception>
    public void Ticks(int count)
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Tick count must be non-negative.");

        if (_disposed) return;

        for (int i = 0; i < count; i++)
        {
            Tick();
        }
    }

    /// <summary>
    /// Advances the server until the specified predicate returns true or the maximum tick count is reached.
    /// </summary>
    /// <param name="predicate">A function that returns true when the wait condition is satisfied.</param>
    /// <param name="maxTicks">The maximum number of ticks to wait. Default is 600 (10 seconds at 60 TPS).</param>
    /// <param name="ct">A cancellation token to cancel the wait.</param>
    /// <returns>True if the predicate was satisfied; false if maxTicks was reached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTicks"/> is less than or equal to zero.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="ServerCrashedException">The server crashed during tick processing.</exception>
    public Task<bool> TickUntilAsync(Func<bool> predicate, int maxTicks = 600, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (maxTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTicks), maxTicks, "Maximum tick count must be positive.");

        return TickUntilAsyncCore(predicate, maxTicks, ct);
    }

    /// <summary>
    /// Advances the server until the specified async predicate returns true or the maximum tick count is reached.
    /// </summary>
    /// <param name="asyncPredicate">An async function that returns true when the wait condition is satisfied.</param>
    /// <param name="maxTicks">The maximum number of ticks to wait. Default is 600 (10 seconds at 60 TPS).</param>
    /// <param name="ct">A cancellation token to cancel the wait.</param>
    /// <returns>True if the predicate was satisfied; false if maxTicks was reached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="asyncPredicate"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxTicks"/> is less than or equal to zero.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    /// <exception cref="ServerCrashedException">The server crashed during tick processing.</exception>
    public Task<bool> TickUntilAsync(Func<Task<bool>> asyncPredicate, int maxTicks = 600, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(asyncPredicate);

        if (maxTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTicks), maxTicks, "Maximum tick count must be positive.");

        return TickUntilAsyncCore(asyncPredicate, maxTicks, ct);
    }

    private async Task<bool> TickUntilAsyncCore(Func<bool> predicate, int maxTicks, CancellationToken ct)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Check for prior crash before ticking
            if (_serverException is not null)
            {
                throw ServerCrashedException.FromServerException(_serverException);
            }

            if (predicate())
            {
                return true;
            }

            Tick();
        }

        return false;
    }

    private async Task<bool> TickUntilAsyncCore(Func<Task<bool>> asyncPredicate, int maxTicks, CancellationToken ct)
    {
        for (int i = 0; i < maxTicks; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Check for prior crash before ticking
            if (_serverException is not null)
            {
                throw ServerCrashedException.FromServerException(_serverException);
            }

            if (await asyncPredicate().ConfigureAwait(false))
            {
                return true;
            }

            Tick();
        }

        return false;
    }

    /// <summary>
    /// Queues an action to execute on the game thread during the next tick.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <returns>A task that completes when the action has executed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    /// <exception cref="ServerCrashedException">The server crashed before the action could execute.</exception>
    public Task RunOnGameThreadAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        // Check for prior crash
        if (_serverException is not null)
        {
            return Task.FromException(ServerCrashedException.FromServerException(_serverException));
        }

        TaskCompletionSource tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameThreadQueue.Enqueue((action, tcs));
        return tcs.Task;
    }

    /// <summary>
    /// Queues a function to execute on the game thread during the next tick.
    /// </summary>
    /// <typeparam name="T">The return type of the function.</typeparam>
    /// <param name="func">The function to execute.</param>
    /// <returns>A task that completes with the function's result when the function has executed.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="func"/> is null.</exception>
    /// <exception cref="ServerCrashedException">The server crashed before the function could execute.</exception>
    public Task<T> RunOnGameThreadAsync<T>(Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);

        // Check for prior crash
        if (_serverException is not null)
        {
            return Task.FromException<T>(ServerCrashedException.FromServerException(_serverException));
        }

        TaskCompletionSource<object?> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _gameThreadFuncQueue.Enqueue((() => func(), tcs));
        return tcs.Task.ContinueWith(t => (T)t.Result!, TaskContinuationOptions.ExecuteSynchronously);
    }

    private void DrainGameThreadQueue()
    {
        // Drain action queue
        while (_gameThreadQueue.TryDequeue(out var item))
        {
            try
            {
                item.action();
                item.tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                item.tcs.TrySetException(ex);
            }
        }

        // Drain func queue
        while (_gameThreadFuncQueue.TryDequeue(out var item))
        {
            try
            {
                object? result = item.func();
                item.tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                item.tcs.TrySetException(ex);
            }
        }
    }

    private void FaultPendingWaiters(Exception serverException)
    {
        ServerCrashedException crashEx = ServerCrashedException.FromServerException(serverException);

        // Fault all pending action queue items
        while (_gameThreadQueue.TryDequeue(out var item))
        {
            item.tcs.TrySetException(crashEx);
        }

        // Fault all pending func queue items
        while (_gameThreadFuncQueue.TryDequeue(out var item))
        {
            item.tcs.TrySetException(crashEx);
        }
    }

    /// <summary>
    /// Gracefully stops the server and cleans up resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        DrainAndDisposeCore();
    }

    /// <summary>
    /// Asynchronously stops the server and cleans up resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await Task.Run(DrainAndDisposeCore).ConfigureAwait(false);
    }

    private void DrainAndDisposeCore()
    {
        // Wait for background BuildServerAssetsPacket tasks to settle
        try
        {
            _waitOnBuildServerAssetsPacket?.Invoke(Server, null);
        }
        catch
        {
            // Best effort drain
        }

        // Stop the server
        try
        {
            if (!Server.stopped)
            {
                Server.Stop("Pharos host teardown", EnumExitMode.SoftExit);
            }
        }
        catch
        {
            // Ignore server stop failures during teardown
        }

        // Dispose server with exception containment for known ServerSystemMonitor NRE
        try
        {
            Server.Dispose();
        }
        catch (NullReferenceException nre)
        {
            // Known NRE in ServerSystemMonitor.Dispose() during teardown
            try
            {
                ServerMain.Logger?.Warning("ServerSystemMonitor.Dispose() NRE during teardown (expected): {0}", nre.Message);
            }
            catch
            {
                // Ignore logging failures
            }
        }
        catch
        {
            // Ignore other server dispose failures during teardown
        }

        // Cleanup scratch directory if we own it
        if (_ownsDataPath && Directory.Exists(DataPath))
        {
            try
            {
                Directory.Delete(DataPath, recursive: true);
            }
            catch
            {
                // Ignore scratch cleanup failures during teardown
            }
        }
    }
}
