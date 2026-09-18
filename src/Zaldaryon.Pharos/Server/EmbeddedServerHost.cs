using System;
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
/// </remarks>
public sealed class EmbeddedServerHost : IDisposable, IAsyncDisposable
{
    private bool _disposed;
    private readonly bool _ownsDataPath;
    private readonly MethodInfo? _waitOnBuildServerAssetsPacket;

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

    /// <summary>Returns true if the server is running and not disposed.</summary>
    public bool IsRunning => !_disposed && !Server.stopped;

    /// <summary>Returns the current number of connected clients.</summary>
    public int ConnectedClientCount => Server.Clients?.Count ?? 0;

    private EmbeddedServerHost(
        ServerMain server,
        DummyNetwork tcpNetwork,
        DummyNetwork udpNetwork,
        ServerWorldOptions options,
        string dataPath,
        bool ownsDataPath)
    {
        Server = server;
        TcpNetwork = tcpNetwork;
        UdpNetwork = udpNetwork;
        Options = options;
        DataPath = dataPath;
        _ownsDataPath = ownsDataPath;

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

        return new EmbeddedServerHost(server, tcpNetwork, udpNetwork, options, dataPath, ownsDataPath);
    }

    /// <summary>
    /// Advances the server by one tick.
    /// </summary>
    public void Tick()
    {
        if (_disposed) return;
        Server.Process();
    }

    /// <summary>
    /// Advances the server by N ticks.
    /// </summary>
    public void Ticks(int count)
    {
        if (_disposed) return;
        for (int i = 0; i < count; i++)
        {
            Server.Process();
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
