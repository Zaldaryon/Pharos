using System;
using System.IO;
using Atlas.Api;
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
/// Hosts an embedded, in-process Vintage Story server configured with Pixnop.Atlas world options and loopback networking.
/// </summary>
public sealed class AtlasServerHost : IDisposable
{
    private bool _disposed;
    private readonly bool _ownsDataPath;

    public ServerMain Server { get; }
    public DummyNetwork TcpNetwork { get; }
    public DummyNetwork UdpNetwork { get; }
    public WorldOptions Options { get; }
    public string DataPath { get; }

    public bool IsRunning => !_disposed && !Server.stopped;
    public int ConnectedClientCount => Server.Clients?.Count ?? 0;

    private AtlasServerHost(
        ServerMain server,
        DummyNetwork tcpNetwork,
        DummyNetwork udpNetwork,
        WorldOptions options,
        string dataPath,
        bool ownsDataPath)
    {
        Server = server;
        TcpNetwork = tcpNetwork;
        UdpNetwork = udpNetwork;
        Options = options;
        DataPath = dataPath;
        _ownsDataPath = ownsDataPath;
    }

    /// <summary>
    /// Boots an embedded Atlas server instance with the specified world options and isolated scratch storage.
    /// </summary>
    public static AtlasServerHost Boot(WorldOptions? options = null, string? customDataPath = null)
    {
        HeadlessPlatformResolver.Initialize();
        HeadlessPlatformResolver.EnsureAssetsPath();

        options ??= new WorldOptions
        {
            Seed = "424242",
            WorldName = "PharosAtlasWorld",
            PlayStyle = "creativebuilding",
            WorldType = "superflat"
        };

        bool ownsDataPath = string.IsNullOrEmpty(customDataPath);
        string dataPath = customDataPath ?? Path.Combine(Path.GetTempPath(), "pharos-atlas-" + Guid.NewGuid().ToString("N")[..8]);

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

        string saveLocation = options.SaveFile ?? Path.Combine(dataPath, "Saves", options.WorldName + ".vcdbs");

        StartServerArgs startArgs = new()
        {
            Seed = options.Seed,
            WorldName = options.WorldName,
            SaveFileLocation = saveLocation,
            AllowCreativeMode = true,
            PlayStyle = options.PlayStyle,
            PlayStyleLangCode = options.PlayStyle,
            WorldType = options.WorldType,
            WorldConfiguration = JsonObject.FromJson("{}"),
            Language = "en",
            IsNew = true
        };

        ServerMain server = new(startArgs, new[] { "--dataPath", dataPath }, progArgs, isDedicatedServer: false);
        server.exitState = new GameExitState();
        server.MainSockets[0] = dummyTcpServer;
        server.UdpSockets[0] = dummyUdpServer;

        server.PreLaunch();
        server.Launch();

        return new AtlasServerHost(server, tcpNetwork, udpNetwork, options, dataPath, ownsDataPath);
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

        try
        {
            Server.Dispose();
        }
        catch
        {
            // Ignore server dispose failures during teardown
        }

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
