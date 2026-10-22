using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;
using Vintagestory.Server.Network;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.Mods;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.World;

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
public sealed partial class EmbeddedServerHost : IDisposable, IAsyncDisposable
{
    private bool _disposed;
    private int _sessionActivityRegistered;
    private readonly bool _ownsDataPath;
    private readonly MethodInfo? _waitOnBuildServerAssetsPacket;
    private int _tickCount;
    private ServerSimulation? _simulation;
    private ServerReplay? _replay;
    private StateHashRecorder? _stateHash;

    /// <summary>The soak run this test made most recently, for a failure's soak.csv; cleared when the test ends.</summary>
    internal SoakReport? LastSoakReport { get; set; }
    private Exception? _serverException;
    private readonly ConcurrentQueue<(Action action, TaskCompletionSource tcs)> _gameThreadQueue = new();
    private readonly ConcurrentQueue<(Func<object?> func, TaskCompletionSource<object?> tcs)> _gameThreadFuncQueue = new();

    /// <summary>The underlying Vintage Story server instance.</summary>
    public ServerMain Server { get; }

    /// <summary>The server-side API for game interactions.</summary>
    public ICoreServerAPI Api => (ICoreServerAPI)Server.Api;

    /// <summary>The mod event manager of the server.</summary>
    public ServerEventManager ModEventManager => Server.ModEventManager;

    /// <summary>The server event manager.</summary>
    public ServerEventManager EventManager => Server.EventManager;

    /// <summary>All currently connected players on the server.</summary>
    public IPlayer[] AllOnlinePlayers => Server.AllOnlinePlayers;

    /// <summary>Connected clients by client ID.</summary>
    public CachingConcurrentDictionary<int, ConnectedClient> Clients => Server.Clients;

    /// <summary>Elapsed simulation milliseconds on the server.</summary>
    public long ElapsedMilliseconds => Server.ElapsedMilliseconds;

    /// <summary>The server's default player spawn position.</summary>
    public EntityPos DefaultSpawnPosition => Server.DefaultSpawnPosition;

    /// <summary>Gets a connected client by player name, or null if not found.</summary>
    public ConnectedClient? GetClientByPlayername(string name) => Server.GetClientByPlayername(name);

    /// <summary>The server configuration.</summary>
    public ServerConfig Config => Server.Config;

    /// <summary>Changes the world's saved configuration on the game thread.</summary>
    public WorldConfigDriver WorldConfig => _worldConfig ??= new WorldConfigDriver(this);

    private WorldConfigDriver? _worldConfig;

    /// <summary>Edits the live server config and marks it for saving.</summary>
    public ServerConfigDriver ServerConfig => _serverConfig ??= new ServerConfigDriver(this);

    private ServerConfigDriver? _serverConfig;

    /// <summary>Writes JSON files into the server's isolated <c>ModConfig</c> folder.</summary>
    public ModConfigDriver ModConfig => _modConfig ??= new ModConfigDriver(this);

    private ModConfigDriver? _modConfig;

    /// <summary>The server's commands: their tree, their parsers, and the callers that may run them.</summary>
    public CommandDriver Commands => _commands ??= new CommandDriver(this);

    private CommandDriver? _commands;

    /// <summary>The server's chat handlers, and what they did with the messages.</summary>
    public ChatDriver Chat => _chat ??= new ChatDriver(this);

    private ChatDriver? _chat;

    /// <summary>The server's items: how long they take to mine with, their tier and their durability.</summary>
    public Zaldaryon.Pharos.Items.ItemDriver Items => _items ??= new Zaldaryon.Pharos.Items.ItemDriver(this);

    private Zaldaryon.Pharos.Items.ItemDriver? _items;

    /// <summary>The animals of the server: what they breed, how they grow and what a hen box holds.</summary>
    public AnimalDriver Animals => _animals ??= new AnimalDriver(this);

    private AnimalDriver? _animals;

    /// <summary>The random block ticks of the server: the blocks that spread, melt and grow.</summary>
    public BlockTickDriver BlockTicks => _blockTicks ??= new BlockTickDriver(this);

    private BlockTickDriver? _blockTicks;

    /// <summary>The snow of the server: how much lies on a block and how it settles.</summary>
    public SnowDriver Snow => _snow ??= new SnowDriver(this);

    private SnowDriver? _snow;

    /// <summary>The block outcomes of the server: the drops, the explosions and the hooks a block calls.</summary>
    public BlockOutcomeDriver Blocks => _blocks ??= new BlockOutcomeDriver(this);

    private BlockOutcomeDriver? _blocks;

    /// <summary>The server's projectiles: how they fly, what they hit and what happened to their stack.</summary>
    public ProjectileDriver Projectiles => _projectiles ??= new ProjectileDriver(this);

    private ProjectileDriver? _projectiles;

    /// <summary>The server's liquid containers, and the pouring between them.</summary>
    public Zaldaryon.Pharos.Items.LiquidDriver Liquids => _liquids ??= new Zaldaryon.Pharos.Items.LiquidDriver(this);

    private Zaldaryon.Pharos.Items.LiquidDriver? _liquids;

    /// <summary>The server's inventories: the containers, the items on the ground and what moves between them.</summary>
    public InventoryDriver Inventories => _inventories ??= new InventoryDriver(this);

    private InventoryDriver? _inventories;

    /// <summary>The held-item hooks of the game, and what they did.</summary>
    public Zaldaryon.Pharos.Items.HeldDriver Held => _held ??= new Zaldaryon.Pharos.Items.HeldDriver();

    private Zaldaryon.Pharos.Items.HeldDriver? _held;

    /// <summary>
    /// The TCP and UDP port the server listens on for network clients, or null when it accepts
    /// only in-memory connections. See <see cref="ServerWorldOptions.ListenPort"/>.
    /// </summary>
    public int? Port { get; private init; }

    /// <summary>What the server has logged since it booted. See <see cref="LogCapture"/>.</summary>
    public LogCapture Logs { get; private init; } = new(EnumAppSide.Server);

    /// <summary>
    /// The warnings, errors and fatal errors the server logged while it booted, until
    /// <see cref="Boot(ServerWorldOptions?, string?)"/> returned. See <c>docs/boot-diagnostics.md</c>.
    /// </summary>
    public BootDiagnostics BootDiagnostics => Logs.BootDiagnostics;

    /// <summary>
    /// The world's calendar: time of day, date, season, and whether time passes. See
    /// <see cref="CalendarDriver"/>.
    /// </summary>
    public CalendarDriver Calendar => _calendar ??= new CalendarDriver(this);

    /// <summary>Plays the server forward in game days, for soak tests.</summary>
    public ServerSimulation Simulate => _simulation ??= new ServerSimulation(this);

    /// <summary>Replays recorded client traffic into a player's connection.</summary>
    public ServerReplay Replay => _replay ??= new ServerReplay(this);

    /// <summary>
    /// The wall-clock time each tick's server processing took, in stopwatch ticks, while a soak run
    /// records them. Null otherwise. Written on the game thread.
    /// </summary>
    internal List<long>? TickTimes { get; set; }

    /// <summary>Raised on the game thread after each tick's processing, for the state hash recordings.</summary>
    internal event Action? TickProcessed;

    /// <summary>The state hash recording this test stopped most recently, for a failure's run.json; cleared when the test ends.</summary>
    internal StateHashRecording? LastStateHashes { get; set; }

    /// <summary>The virtual clock's tick, or 0 when the wall clock runs the server, which makes its hashes non-repeatable.</summary>
    internal int TickMsForHashes => VirtualClock?.TickMs ?? 0;

    /// <summary>Hashes the server's state after each tick, for comparing two runs.</summary>
    public StateHashRecorder StateHash => _stateHash ??= new StateHashRecorder(this);

    /// <summary>
    /// Optimum's counters on an Optimum build. They belong to the whole process, so the client's
    /// and the server's are the same. See <see cref="OptimumDiagnostics"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tests run on vanilla Vintage Story.</exception>
    public OptimumDiagnostics Optimum => OptimumDiagnostics.Loaded
        ?? throw new InvalidOperationException(OptimumDriver.NotOptimum(Environment.GetEnvironmentVariable("VINTAGE_STORY")));

    private CalendarDriver? _calendar;

    /// <summary>
    /// The world's weather: cloud patterns, wind, precipitation, and whether it changes on its own.
    /// See <see cref="WeatherDriver"/>.
    /// </summary>
    public WeatherDriver Weather => _weather ??= new WeatherDriver(this);

    private WeatherDriver? _weather;

    /// <summary>
    /// The server's game tick listeners and delayed callbacks, and a fast-forward for the ones at
    /// one block position. See <see cref="ServerTickListenerInspector"/>.
    /// </summary>
    public ServerTickListenerInspector TickListeners => _tickListeners ??= new ServerTickListenerInspector(this);

    private ServerTickListenerInspector? _tickListeners;

    /// <summary>
    /// Profiles the server's systems, mods, block entities, entities, AI tasks and traffic for a
    /// stretch of ticks. See <see cref="ServerProfileDriver"/>.
    /// </summary>
    public ServerProfileDriver Profile => _profile ??= new ServerProfileDriver(this);

    private ServerProfileDriver? _profile;

    /// <summary>
    /// Generates a fresh chunk area and reports what each world generator cost, by pass, mod and
    /// handler. See <see cref="ServerWorldgenDriver"/>.
    /// </summary>
    public ServerWorldgenDriver Worldgen => _worldgen ??= new ServerWorldgenDriver(this);

    private ServerWorldgenDriver? _worldgen;

    /// <summary>
    /// Runs one save and reports the time and bytes of each category and the mod data it carries.
    /// See <see cref="ServerSavesDriver"/>.
    /// </summary>
    public ServerSavesDriver Saves => _saves ??= new ServerSavesDriver(this);

    private ServerSavesDriver? _saves;

    /// <summary>
    /// How the game loaded the staged mods: their states, systems, phases, patches and compatibility
    /// assets. See <see cref="ModLoadReport"/> and <c>docs/mod-loading.md</c>.
    /// </summary>
    public ModLoadReport Mods => ModsProfile ?? ModLoadReport.Empty(Server.Api.Side);

    /// <summary>Which mod supplied an asset, and whether a patch changed it.</summary>
    public AssetInspector Assets => AssetInspector ?? new AssetInspector(Server.Api, Mods);

    internal ModLoadReport? ModsProfile { get; init; }

    internal AssetInspector? AssetInspector { get; init; }

    /// <summary>
    /// A snapshot of the loaded world: chunks, map chunks, map regions, entities and block
    /// entities. See <see cref="WorldStats"/>.
    /// </summary>
    public WorldStats WorldStats => WorldStats.Capture(this);

    /// <summary>Controls and inspects entities in the server world.</summary>
    public ServerEntityDriver Entities => _entities ??= new ServerEntityDriver(this);

    private ServerEntityDriver? _entities;

    /// <summary>Waits for and reads the server's world physics: light, liquids, falling blocks, relighting, rooms and fire.</summary>
    public WorldPhysicsDriver World => _world ??= new WorldPhysicsDriver(this);

    private WorldPhysicsDriver? _world;

    /// <summary>Land claims on the server: adds, reads and removes them, and restores them with the world.</summary>
    public LandClaimDriver LandClaims => _landClaims ??= new LandClaimDriver(this);

    private LandClaimDriver? _landClaims;

    /// <summary>Whether players can damage each other on the server, restored with the world.</summary>
    public PvpDriver Pvp => _pvp ??= new PvpDriver(this);

    private PvpDriver? _pvp;

    /// <summary>The server's player roles, and assigning them to test players.</summary>
    public RoleDriver Roles => _roles ??= new RoleDriver(this);

    private RoleDriver? _roles;

    /// <summary>Block reinforcement through the survival mod. Placed reinforcements are cleared by rollback.</summary>
    public ReinforcementDriver Reinforcement => _reinforcement ??= new ReinforcementDriver(this);

    private ReinforcementDriver? _reinforcement;

    /// <summary>Player groups: creating them, adding members, and finding them. Groups a test creates are removed by rollback.</summary>
    public GroupDriver Groups => _groups ??= new GroupDriver(this);

    private GroupDriver? _groups;

    /// <summary>Climate at a position, and climate overrides for a region, restored by rollback.</summary>
    public ClimateDriver Climate => _climate ??= new ClimateDriver(this);

    private ClimateDriver? _climate;

    /// <summary>Farmland and crops: planting, reading the soil and crop, and growing them through the calendar.</summary>
    public FarmingDriver Farming => _farming ??= new FarmingDriver(this);

    private FarmingDriver? _farming;

    /// <summary>Firepits and barrels: filling them, reading their state, and running their processing for a span of game time.</summary>
    public ProcessingDriver Processing => _processing ??= new ProcessingDriver(this);

    private ProcessingDriver? _processing;

    /// <summary>Matches crafting grids against the game's recipes, and explains a grid that matches none.</summary>
    public RecipeDriver Recipes => _recipes ??= new RecipeDriver(this);

    /// <summary>Disconnects the player that matches recipes without one, so a test leaves no player behind.</summary>
    internal void ReleaseRecipePlayer() => _recipes?.ReleaseRecipePlayer();

    private RecipeDriver? _recipes;

    /// <summary>
    /// Records the server's game events, such as blocks placed and entities dying, to assert on.
    /// See <see cref="Zaldaryon.Pharos.Events.ServerEventRecorder"/>.
    /// </summary>
    public global::Zaldaryon.Pharos.Events.ServerEventRecorder Events => _events ??= new global::Zaldaryon.Pharos.Events.ServerEventRecorder(this);

    private global::Zaldaryon.Pharos.Events.ServerEventRecorder? _events;

    /// <summary>
    /// Steps one frame of the <see cref="ClientServerLoopbackSession"/> the server is in, so a wait
    /// on the server keeps the client going too; null outside a session. With several sessions on
    /// one server, the last one made wins. Once its client is disposed it ticks only the server.
    /// </summary>
    internal System.Func<CancellationToken, Task>? SessionStepper { get; set; }

    /// <summary>Whether the calling thread is the server's game thread.</summary>
    internal bool IsOnGameThread => _gameThread.IsCurrent;

/// <summary>
    /// The server's random generators and the process-wide static ones: seeds them so a test's
    /// draws can be replayed. See <see cref="GameRandom"/> and <see cref="XUnit.SeededAttribute"/>.
    /// </summary>
    public GameRandom Random => _random ??= new GameRandom(new ServerRandomSide(this));

    private GameRandom? _random;

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

    // The server's game thread: boot, ticks, snapshots and teardown all run on it.
    private readonly EngineThread _gameThread;

    private readonly HeadlessPlayers _players;

    /// <summary>
    /// The headless players joined with <see cref="JoinPlayerAsync"/> that are still connected.
    /// </summary>
    public IReadOnlyList<ServerTestPlayer> TestPlayers => _players.Joined;

    internal Task<ServerTestPlayer> RejoinPlayerAsync(string playerName, ServerTestPlayer player, int maxTicks, CancellationToken ct) =>
        _players.JoinAsync(playerName, maxTicks, ct, player);

    /// <summary>
    /// Joins a headless player: a real multiplayer client as far as the server can tell, with no
    /// rendering client behind it. It goes through the full join sequence, packet by packet, and
    /// is a playing player with a spawned entity and inventories when the task completes. The
    /// server is ticked while waiting.
    /// </summary>
    /// <param name="playerName">The player name. Must not belong to a connected player.</param>
    /// <param name="maxTicks">How many server ticks each join step may take.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">A player with that name is already connected.</exception>
    public Task<ServerTestPlayer> JoinPlayerAsync(string playerName, int maxTicks = 2000, CancellationToken ct = default) =>
        _players.JoinAsync(playerName, maxTicks, ct);

    /// <summary>
    /// Joins a headless test player to the server. Alias for <see cref="JoinPlayerAsync"/>.
    /// </summary>
    public Task<ServerTestPlayer> CreateTestPlayer(string playerName = "TestPlayer", int maxTicks = 2000, CancellationToken ct = default) =>
        JoinPlayerAsync(playerName, maxTicks, ct);

    /// <summary>
    /// Joins a headless test player to the server. Alias for <see cref="JoinPlayerAsync"/>.
    /// </summary>
    public Task<ServerTestPlayer> CreateTestPlayerAsync(string playerName = "TestPlayer", int maxTicks = 2000, CancellationToken ct = default) =>
        JoinPlayerAsync(playerName, maxTicks, ct);

    /// <summary>
    /// The packets of message type <typeparamref name="T"/> that mods on this server sent
    /// <paramref name="player"/> on <paramref name="channel"/>, deserialized. The message id is
    /// looked up from the channel's server-side registration.
    /// </summary>
    /// <exception cref="InvalidOperationException">The channel or the message type is not registered.</exception>
    public IReadOnlyList<T> ModPackets<T>(ServerTestPlayer player, string channel)
    {
        ArgumentNullException.ThrowIfNull(player);
        int messageId = RunOnGameThread(() =>
        {
            object serverChannel = ((Vintagestory.API.Server.ICoreServerAPI)Server.Api).Network.GetChannel(channel)
                ?? throw new InvalidOperationException($"No network channel '{channel}' is registered on the server.");
            FieldInfo? typesField = typeof(NetworkChannelBase).GetField("messageTypes", BindingFlags.Instance | BindingFlags.NonPublic);
            if (typesField?.GetValue(serverChannel) is not Dictionary<Type, int> types || !types.TryGetValue(typeof(T), out int id))
            {
                throw new InvalidOperationException($"Message type {typeof(T).Name} is not registered on channel '{channel}'.");
            }

            return id;
        });

        return player.Received.ModPacketsOf<T>(channel, messageId);
    }

    /// <summary>
    /// Marks the host as having a real client attached over its loopback socket. That client reads
    /// the UDP queue the headless players share, so the host stops emptying it.
    /// </summary>
    internal void MarkLoopbackClientAttached() => _players.LoopbackClientAttached = true;

    /// <summary>Whether a real client reads this server's loopback socket, which a soak run cannot step.</summary>
    internal bool LoopbackClientAttached => _players.LoopbackClientAttached;

    private EmbeddedServerHost(
        ServerMain server,
        DummyNetwork tcpNetwork,
        DummyNetwork udpNetwork,
        ServerWorldOptions options,
        string dataPath,
        bool ownsDataPath,
        ServerSandbox? sandbox,
        EngineThread gameThread)
    {
        _gameThread = gameThread;
        _players = new HeadlessPlayers(this);
        Server = server;
        TcpNetwork = tcpNetwork;
        UdpNetwork = udpNetwork;
        Options = options;
        DataPath = dataPath;
        _ownsDataPath = ownsDataPath;
        GameSessionActivity.Started();
        _sessionActivityRegistered = 1;
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
        CheckSaveFile(options);
        return BootOnGameThread(gameThread => BootCore(options, customDataPath, gameThread));
    }

    /// <summary>
    /// Applies the network options once the server has loaded its config: player verification,
    /// and the real TCP and UDP sockets next to the in-memory ones, as the game's own
    /// <c>/allowlan</c> command opens them. Returns the port, or null.
    /// </summary>
    private static int? ConfigureNetwork(ServerMain server, ServerWorldOptions options)
    {
        server.Config.VerifyPlayerAuth = options.VerifyPlayerAuth;
        if (options.ListenPort is not { } requested) return null;

        int port = requested == 0 ? FreePorts.TakeTcpUdp(options.ListenAddress) : requested;

        server.MainSockets[1] = new TcpNetServer();
        server.MainSockets[1].SetIpAndPort(options.ListenAddress, port);
        server.MainSockets[1].Start();

        server.UdpSockets[1] = new UdpNetServer(server.Clients);
        server.UdpSockets[1].SetIpAndPort(options.ListenAddress, port);
        server.UdpSockets[1].Start();

        ServerMain.Logger.Notification("Pharos: listening for network clients on {0}:{1}", options.ListenAddress, port);
        return port;
    }

    private static EmbeddedServerHost BootOnGameThread(System.Func<EngineThread, EmbeddedServerHost> boot)
    {
        // A server a scenario kept for its next test shares the game's static server state. Shut
        // down later, while this one runs, it would tear that state out from under it. Whoever
        // boots a server now no longer needs the kept one.
        ScenarioHostPool.Clear();

        // The server is built on the thread it will tick on, so the per-thread state it creates
        // while booting is there for every later tick.
        EngineThread gameThread = new("Pharos server game thread");
        try
        {
            EmbeddedServerHost host = gameThread.Invoke(() => boot(gameThread));

            // Before the world ticks: what the game makes from here on can be seeded.
            host.Random.HookAtBoot();
            return host;
        }
        catch
        {
            gameThread.Dispose();
            throw;
        }
    }

    private static EmbeddedServerHost BootCore(ServerWorldOptions? options, string? customDataPath, EngineThread gameThread)
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
        // Refresh the singleton logger for every host; a prior scenario may have disposed its logger.
        TyronThreadPool.Inst.Logger = ServerMain.Logger;
        LogCapture logs = new(EnumAppSide.Server);
        logs.Attach(ServerMain.Logger);
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
        StageSaveFile(options, saveLocation, mayReplace: ownsDataPath);

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

        ModSafetyCheck.Disable();
        SideDataPaths.Patch();
        Performance.MeasurementHooks.InstallServer();
        SideDataPaths.UseServerDataPath(dataPath);
        ServerMain server = new(startArgs, new[] { "--dataPath", dataPath }, progArgs, isDedicatedServer: false);

        // CRITICAL: Assign exitState BEFORE PreLaunch() to prevent NRE in packet parser threads
        server.exitState = new GameExitState();

        server.MainSockets[0] = dummyTcpServer;
        server.UdpSockets[0] = dummyUdpServer;

        ModLoadObserver.Install();
        CommandRegistry.Install();
        server.PreLaunch();
        server.Launch();

        int? port = ConfigureNetwork(server, options);

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

        // The server has booted: what it logs from here on is not a boot diagnostic.
        logs.CompleteBoot();
        ModLoadReport mods = ModLoadReport.Capture(server.Api);
        AssetInspector assets = new(server.Api, mods);
        return new EmbeddedServerHost(server, tcpNetwork, udpNetwork, options, dataPath, ownsDataPath, sandbox: null, gameThread)
        {
            Logs = logs,
            Port = port,
            ModsProfile = mods,
            AssetInspector = assets
        };
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
        CheckSaveFile(options);
        return BootOnGameThread(gameThread => BootCore(sandbox, options, gameThread));
    }

    private static EmbeddedServerHost BootCore(ServerSandbox sandbox, ServerWorldOptions? options, EngineThread gameThread)
    {
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
        // Refresh the singleton logger for every host; a prior scenario may have disposed its logger.
        TyronThreadPool.Inst.Logger = ServerMain.Logger;
        LogCapture logs = new(EnumAppSide.Server);
        logs.Attach(ServerMain.Logger);
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
        StageSaveFile(options, saveLocation, mayReplace: true);

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

        ModSafetyCheck.Disable();
        SideDataPaths.Patch();
        Performance.MeasurementHooks.InstallServer();
        SideDataPaths.UseServerDataPath(dataPath);
        ServerMain server = new(startArgs, new[] { "--dataPath", dataPath }, progArgs, isDedicatedServer: false);

        // CRITICAL: Assign exitState BEFORE PreLaunch() to prevent NRE in packet parser threads
        server.exitState = new GameExitState();

        server.MainSockets[0] = dummyTcpServer;
        server.UdpSockets[0] = dummyUdpServer;

        ModLoadObserver.Install();
        CommandRegistry.Install();
        server.PreLaunch();
        server.Launch();

        int? port = ConfigureNetwork(server, options);

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

        // The server has booted: what it logs from here on is not a boot diagnostic.
        logs.CompleteBoot();
        ModLoadReport mods = ModLoadReport.Capture(server.Api);
        AssetInspector assets = new(server.Api, mods);
        return new EmbeddedServerHost(server, tcpNetwork, udpNetwork, options, dataPath, ownsDataPath: false, sandbox, gameThread)
        {
            Logs = logs,
            Port = port,
            ModsProfile = mods,
            AssetInspector = assets
        };
    }

    /// <summary>
    /// Captures the world as it is now, for <see cref="RestoreSnapshot"/>. See
    /// <see cref="WorldSnapshot"/> for what it holds.
    /// </summary>
    /// <returns>A snapshot of the loaded chunks, the players' data and the connected players.</returns>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    public WorldSnapshot TakeSnapshot()
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("Cannot take snapshot from a stopped server.");
        }

        return _gameThread.Invoke(() => WorldSnapshot.CreateFrom(this));
    }

    /// <summary>
    /// Puts the world back as it was when <paramref name="snapshot"/> was taken, in place, on the
    /// game thread. The server keeps running and connected clients stay joined; they are sent the
    /// restored chunks again.
    /// </summary>
    /// <param name="snapshot">The snapshot to restore.</param>
    /// <returns>How many chunks had changed and were restored.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    public int RestoreSnapshot(WorldSnapshot snapshot) => RestoreSnapshot(snapshot, WorldRestoreFilter.All).ChunksRestored;

    /// <summary>
    /// Puts the world back as it was when <paramref name="snapshot"/> was taken, in place, on the
    /// game thread, leaving the keys <paramref name="filter"/> refuses as they are. The server keeps
    /// running and connected clients stay joined; they are sent the restored chunks, map regions,
    /// entity state and player data again.
    /// </summary>
    /// <param name="snapshot">The snapshot to restore.</param>
    /// <param name="filter">Which mod data and player attribute keys to put back.</param>
    /// <returns>What was put back.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="snapshot"/> or <paramref name="filter"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    public WorldRestoreResult RestoreSnapshot(WorldSnapshot snapshot, WorldRestoreFilter filter)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(filter);

        if (!IsRunning)
        {
            throw new InvalidOperationException("Cannot restore snapshot to a stopped server.");
        }

        return _gameThread.Invoke(() => snapshot.RestoreCore(this, filter));
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

        if (!_gameThread.IsCurrent)
        {
            _gameThread.Invoke(Tick);
            return;
        }

        // Check for prior crash
        if (_serverException is not null)
        {
            throw ServerCrashedException.FromServerException(_serverException);
        }

        // Drain pending game thread actions before the tick
        DrainGameThreadQueue();

        try
        {
            _faults?.BeforeTick();
            long processStarted = Stopwatch.GetTimestamp();
            Server.Process();
            TickTimes?.Add(Stopwatch.GetTimestamp() - processStarted);
            _tickCount++;
            TickProcessed?.Invoke();
        }
        catch (Exception ex)
        {
            _serverException = ex;
            FaultPendingWaiters(ex);
            throw ServerCrashedException.FromServerException(ex);
        }

        _players.AfterTick();

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
    /// <summary>
    /// Advances the server until the specified predicate returns true or the maximum tick count is reached.
    /// Returns false if maxTicks was reached without the condition being met.
    /// </summary>
    public Task<bool> TryTickUntilAsync(Func<bool> predicate, int maxTicks = 600, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        if (maxTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTicks), maxTicks, "Maximum tick count must be positive.");

        return TickUntilAsyncCore(predicate, maxTicks, ct);
    }

    /// <summary>
    /// Advances the server until the specified async predicate returns true or the maximum tick count is reached.
    /// Returns false if maxTicks was reached without the condition being met.
    /// </summary>
    public Task<bool> TryTickUntilAsync(Func<Task<bool>> asyncPredicate, int maxTicks = 600, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(asyncPredicate);

        if (maxTicks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTicks), maxTicks, "Maximum tick count must be positive.");

        return TickUntilAsyncCore(asyncPredicate, maxTicks, ct);
    }

    /// <summary>
    /// Advances the server until the specified predicate returns true or throws <see cref="PharosTimeoutException"/>.
    /// </summary>
    public async Task WaitTickUntilAsync(Func<bool> predicate, int maxTicks = 600, string? conditionDescription = null, CancellationToken ct = default)
    {
        bool ok = await TryTickUntilAsync(predicate, maxTicks, ct).ConfigureAwait(false);
        if (!ok)
        {
            throw new PharosTimeoutException($"Server condition {(conditionDescription != null ? $"'{conditionDescription}' " : "")}was not satisfied within {maxTicks} ticks.");
        }
    }

    /// <summary>
    /// Advances the server until the specified async predicate returns true or throws <see cref="PharosTimeoutException"/>.
    /// </summary>
    public async Task WaitTickUntilAsync(Func<Task<bool>> asyncPredicate, int maxTicks = 600, string? conditionDescription = null, CancellationToken ct = default)
    {
        bool ok = await TryTickUntilAsync(asyncPredicate, maxTicks, ct).ConfigureAwait(false);
        if (!ok)
        {
            throw new PharosTimeoutException($"Server condition {(conditionDescription != null ? $"'{conditionDescription}' " : "")}was not satisfied within {maxTicks} ticks.");
        }
    }

    /// <summary>Legacy tick waiter.</summary>
    [Obsolete("Use TryTickUntilAsync instead. This method will be removed in 0.8.")]
    public Task<bool> TickUntilAsync(Func<bool> predicate, int maxTicks = 600, CancellationToken ct = default)
        => TryTickUntilAsync(predicate, maxTicks, ct);

    /// <summary>Legacy async tick waiter.</summary>
    [Obsolete("Use TryTickUntilAsync instead. This method will be removed in 0.8.")]
    public Task<bool> TickUntilAsync(Func<Task<bool>> asyncPredicate, int maxTicks = 600, CancellationToken ct = default)
        => TryTickUntilAsync(asyncPredicate, maxTicks, ct);

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
    /// Runs <paramref name="action"/> on the server's game thread now and waits for it. Use it for
    /// anything that touches live server state from test code: the engine keeps per-thread state
    /// on its game thread and expects to be called there.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="action"/> is null.</exception>
    public void RunOnGameThread(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        _gameThread.Invoke(action);
    }

    /// <summary>
    /// Runs <paramref name="func"/> on the server's game thread now and returns its result. See
    /// <see cref="RunOnGameThread(Action)"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="func"/> is null.</exception>
    public T RunOnGameThread<T>(System.Func<T> func)
    {
        ArgumentNullException.ThrowIfNull(func);
        return _gameThread.Invoke(func);
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
        _random?.ReleaseOnDispose();
        _disposed = true;
        try { DisposeOnGameThread(); }
        finally { ReleaseSessionActivity(); }
    }

    /// <summary>
    /// Asynchronously stops the server and cleans up resources.
    /// </summary>
    /// <remarks>Uses a dedicated long-running task because teardown waits for thread-pool callbacks.</remarks>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _random?.ReleaseOnDispose();
        _disposed = true;

        Task disposal = Task.Factory.StartNew(
            () =>
            {
                try { DisposeOnGameThread(); }
                finally { ReleaseSessionActivity(); }
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);
        return new ValueTask(disposal);
    }

    private void ReleaseSessionActivity()
    {
        GameSessionActivity.Release(ref _sessionActivityRegistered);
    }

    private void DisposeOnGameThread()
    {
        EndVirtualClock();

        // A later server answers its own time reads; this one's offset goes with it.
        if (_clockState != null && ReferenceEquals(ServerClockPatches.Current, _clockState)) ServerClockPatches.Current = null;
        try
        {
            _gameThread.Invoke(DrainAndDisposeCore);
        }
        finally
        {
            _gameThread.Dispose();
        }
    }

    /// <summary>
    /// Waits for the server's separate threads to finish their own shutdown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>ServerMain.Stop</c> only sets the static <c>ServerThread.shouldExit</c> flag. The thread
    /// observes it at the bottom of <c>ServerThread.Process</c> and calls <c>ShutDown</c>, which is
    /// what invokes <c>ServerSystem.OnSeperateThreadShutDown</c> on every registered system.
    /// </para>
    /// <para>
    /// That hook is where the world database is released:
    /// <c>ServerSystemLoadAndSaveGame.OnSeperateThreadShutDown</c> disposes
    /// <c>chunkthread.gameDatabase</c> and, on Optimum builds, <c>chunkthread.optimumReadPool</c>,
    /// a pool of SQLite connections. Without this wait the teardown raced those threads, the
    /// scratch data path could not be deleted, and the surviving connection kept writing
    /// <c>Saves/World.vcdbs-wal</c> long after the host was gone.
    /// </para>
    /// <para>
    /// Bounded, because a wedged thread must not hang the test host. A timeout is reported and the
    /// teardown continues, so the leftover directory surfaces as a failed assertion rather than a
    /// stuck run.
    /// </para>
    /// </remarks>
    private void WaitForServerThreadsToShutDown()
    {
        FieldInfo? loopsField = typeof(ServerMain).GetField(
            "ServerThreadLoops",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (loopsField?.GetValue(Server) is not System.Collections.IEnumerable loops) return;

        PropertyInfo? aliveProperty = typeof(ServerThread).GetProperty(
            "Alive", BindingFlags.Instance | BindingFlags.Public);
        if (aliveProperty is null) return;

        const int TimeoutMs = 5000;
        const int PollMs = 10;
        int waited = 0;

        while (waited < TimeoutMs)
        {
            bool anyAlive = false;
            foreach (object? loop in loops)
            {
                if (loop is null) continue;
                if (aliveProperty.GetValue(loop) is true)
                {
                    anyAlive = true;
                    break;
                }
            }

            if (!anyAlive) return;

            Thread.Sleep(PollMs);
            waited += PollMs;
        }

        try
        {
            ServerMain.Logger?.Warning(
                "Server threads still alive {0}ms after Stop; their OnSeperateThreadShutDown " +
                "hooks did not run, so the world database may still be open.", TimeoutMs);
        }
        catch
        {
            // Ignore logging failures
        }
    }

    private void DrainAndDisposeCore()
    {
        var logger = ServerMain.Logger;

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

        // ServerMain.Stop clears its logger watchers before returning. Reattach before waiting
        // for any server threads that did not stop cleanly, and keep capture active through the
        // final server disposal and scratch cleanup.
        if (logger is not null) Logs.Reattach(logger);
        WaitForServerThreadsToShutDown();
        ServerThreadPoolDrain stopThreadPoolDrain = new(Server, logger);
        if (!stopThreadPoolDrain.WaitForQuietPeriod()) stopThreadPoolDrain.LogTimeout(logger, "after Stop");

        ServerThreadPoolDrain.TryInstallBeforeThreadPoolDisposal(Server, logger);

        try
        {
            try
            {
                Server.Dispose();
            }
            catch (NullReferenceException nre)
            {
                // Known NRE in ServerSystemMonitor.Dispose() during teardown
                try
                {
                    logger?.Warning("ServerSystemMonitor.Dispose() NRE during teardown (expected): {0}", nre.Message);
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

            // ServerMain.Dispose tears down systems before it disposes TyronThreadPool and the
            // logger. Those system disposals can queue callbacks after the first quiet period.
            // Reattach after Server.Dispose too, then give those callbacks their own bounded drain.
            if (logger is not null) Logs.Reattach(logger);
            ServerThreadPoolDrain disposeThreadPoolDrain = new(Server, logger);
            if (!disposeThreadPoolDrain.WaitForQuietPeriod()) disposeThreadPoolDrain.LogTimeout(logger, "after Server.Dispose");

            // Cleanup scratch directory if we own it
            if (_ownsDataPath && Directory.Exists(DataPath))
            {
                try
                {
                    Directory.Delete(DataPath, recursive: true);
                }
                catch (Exception ex)
                {
                    // A scratch directory that outlives the host is a real leak, so name the locked
                    // file instead of dropping the failure on the floor. On an Optimum build this
                    // is how a world database still held open at teardown announces itself.
                    try
                    {
                        logger?.Warning("Scratch data path {0} survived teardown: {1}", DataPath, ex.Message);
                    }
                    catch
                    {
                        // Ignore logging failures
                    }
                }
            }
        }
        finally
        {
            logger?.ClearWatchers();
        }
    }
}
