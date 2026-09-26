using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.Network;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Fixtures;
using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Memory;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Input;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.UI;
using Zaldaryon.Pharos.World;

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
    public ChunkTesselatorManager? ChunkTesselatorManager { get; private set; }
    public bool IsDisposed => _disposed;

    /// <summary>
    /// OpenGL command proxy for recording draw and buffer operations per frame.
    /// Recording is disabled by default. Call <see cref="GlCommandProxy.Enable"/> before a frame
    /// and <see cref="GlCommandProxy.Snapshot"/> after to capture counts.
    /// </summary>
    public GlCommandProxy GL { get; } = new();

    /// <summary>
    /// Frustum and culling state inspector. Call <see cref="CullingInspector.Snapshot"/> after a
    /// frame step to capture which chunks were visible, culled, or occlusion-culled.
    /// </summary>
    public CullingInspector Culling { get; }

    /// <summary>
    /// Mesh pool and allocation metrics inspector. <see cref="MemoryInspector.PoolSnapshot"/> is
    /// always available. Call <see cref="MemoryInspector.Enable"/> to activate hit/miss and
    /// per-type allocation tracking. Use <see cref="MemoryInspector.MeasureAllocations"/> for
    /// inline heap measurement without patching.
    /// </summary>
    public MemoryInspector Memory { get; } = new();

    /// <summary>
    /// Shader program state inspector. Captures the active program, uniform values, bound textures,
    /// and redundant upload counts per frame. Recording is disabled by default; call
    /// <see cref="ShaderInspector.Enable"/> before a frame and <see cref="ShaderInspector.Snapshot"/>
    /// after to capture shader state.
    /// </summary>
    public ShaderInspector Shaders { get; } = new();

    /// <summary>
    /// Indirect draw inspector for GPU multi-draw verification. Captures indirect dispatch counts,
    /// command totals, buffer bindings, and direct draw fallback calls per frame. Recording is
    /// disabled by default; call <see cref="IndirectDrawInspector.Enable"/> before a frame and
    /// <see cref="IndirectDrawInspector.Snapshot"/> after to capture indirect draw statistics.
    /// </summary>
    public IndirectDrawInspector IndirectDraw { get; } = new();
    /// <summary>
    /// Virtual input controller for deterministic mouse and keyboard simulation.
    /// Supports synthetic input injection, state queries, and thread-safe snapshots.
    /// </summary>
    public VirtualInputController Input { get; } = new();
    /// <summary>
    /// GUI inspector for headless dialog and widget inspection.
    /// Supports enumerating open dialogs, HUD elements, and simulating button clicks.
    /// </summary>
    public GuiInspector Gui { get; }

    /// <summary>
    /// Inventory automation controller for slot operations, drag-and-drop, and crafting grid manipulation.
    /// Supports deterministic inventory interactions without a live server using mock state.
    /// </summary>
    public InventoryAutomation Inventory { get; }

    /// <summary>
    /// Block interaction simulator for deterministic placement, breaking, and tool usage.
    /// Supports mock mode for pure-logic testing without a live server.
    /// </summary>
    public BlockInteractionSimulator BlockInteraction { get; } = new();

    /// <summary>
    /// Packet recorder for capturing network packets during live or loopback sessions.
    /// Supports deterministic recording, JSON serialization, and offline replay.
    /// </summary>
    public PacketRecorder PacketRecorder { get; } = new();

    /// <summary>
    /// Network degradation simulator for testing client behavior under adverse network conditions.
    /// Supports configurable latency, packet drop, jitter, and corruption with deterministic seeding.
    /// </summary>
    public NetworkDegradationSimulator NetworkDegradation { get; } = new();

    /// <summary>
    /// Disconnect simulator for testing client resilience to server disconnects and crashes.
    /// Supports various disconnect reasons, reconnect attempt tracking, and crash containment.
    /// </summary>
    public DisconnectSimulator DisconnectSimulator { get; } = new();

    /// <summary>
    /// Managed object leak tracker for detecting unreturned mesh parts and unrecycled MeshData.
    /// Call <see cref="ManagedLeakTracker.StartBaseline"/> before a scenario and
    /// <see cref="ManagedLeakTracker.GetLeakReport"/> after to detect leaks.
    /// </summary>
    public ManagedLeakTracker ManagedLeaks { get; } = new();

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
        Gui = new GuiInspector(screenManager);
        TestPlayer = new ClientTestPlayer(client);
        Culling = new CullingInspector(client);
        Inventory = new InventoryAutomation(TestPlayer.Inventory);
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

    public Task<bool> WaitForChunkMeshedAsync(ChunkPos chunkPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForChunkMeshedAsync(chunkPos, maxFrames, dt, ct);
    }

    public Task<bool> WaitForChunkMeshed(int chunkX, int chunkY, int chunkZ, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForChunkMeshedAsync(chunkX, chunkY, chunkZ, maxFrames, dt, ct);
    }

    public Task<bool> WaitForChunkMeshedAsync(int chunkX, int chunkY, int chunkZ, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForChunkMeshedAsync(chunkX, chunkY, chunkZ, maxFrames, dt, ct);
    }

    public Task<bool> WaitForChunkMeshed(Vec3i chunkPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForChunkMeshedAsync(chunkPos, maxFrames, dt, ct);
    }

    public Task<bool> WaitForChunkMeshedAsync(Vec3i chunkPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForChunkMeshedAsync(chunkPos, maxFrames, dt, ct);
    }

    public Task<bool> WaitForChunkMeshed(BlockPos blockPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForChunkMeshedAsync(blockPos, maxFrames, dt, ct);
    }

    public Task<bool> WaitForChunkMeshedAsync(BlockPos blockPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForChunkMeshedAsync(blockPos, maxFrames, dt, ct);
    }

    /// <summary>
    /// Advances frames until the specified chunk radius around the player is loaded and meshed.
    /// </summary>
    public Task<bool> WaitForWorldReady(int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForWorldReadyAsync(radius, maxFrames, dt, ct);
    }

    public Task<bool> WaitForWorldReadyAsync(int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForWorldReadyAsync(radius, maxFrames, dt, ct);
    }

    public Task<bool> WaitForWorldReady(ChunkPos center, int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForWorldReadyAsync(center, radius, maxFrames, dt, ct);
    }

    public Task<bool> WaitForWorldReadyAsync(ChunkPos center, int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForWorldReadyAsync(center, radius, maxFrames, dt, ct);
    }

    public Task<bool> WaitForWorldReady(BlockPos center, int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForWorldReadyAsync(center, radius, maxFrames, dt, ct);
    }

    public Task<bool> WaitForWorldReadyAsync(BlockPos center, int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForWorldReadyAsync(center, radius, maxFrames, dt, ct);
    }

    /// <summary>
    /// Advances frames until all background chunk tessellation and upload queues have drained.
    /// </summary>
    public Task<bool> WaitForAllMeshesReady(int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForAllMeshesReadyAsync(maxFrames, dt, ct);
    }

    public Task<bool> WaitForAllMeshesReadyAsync(int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        return FrameController.WaitForAllMeshesReadyAsync(maxFrames, dt, ct);
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
    /// Prepares the engine startup state the vanilla render pass needs, so that
    /// <c>GuiScreenRunningGame.RenderToPrimary</c> can run in this fixture-mode client.
    /// </summary>
    /// <remarks>
    /// Opt-in. The default boot path leaves this state unset so chunk fixtures keep
    /// running unchanged. See <see cref="ClientEngineStartup"/> for what is filled in and
    /// what is still missing.
    /// </remarks>
    public string? PrepareRenderPass(Vec3d? playerPosition = null)
    {
        return ClientEngineStartup.Prepare(this, playerPosition);
    }

    /// <summary>
    /// Whether the vanilla render pass would get past its player null check right now.
    /// </summary>
    public bool IsRenderGateOpen => ClientEngineStartup.IsRenderGateOpen(this);

    /// <summary>
    /// Connects the headless client to an in-process embedded Atlas server instance using engine singleplayer loopback.
    /// </summary>
    /// <remarks>
    /// This overload uses the legacy <see cref="AtlasServerHost"/>. For new code, use
    /// <see cref="ConnectLoopback(EmbeddedServerHost, string)"/> instead.
    /// </remarks>
    [Obsolete("Use ConnectLoopback(EmbeddedServerHost, string) instead. AtlasServerHost will be removed in a future version.")]
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

#pragma warning disable CS0618 // Internal use of obsolete constructor
        return new ClientServerLoopbackSession(this, server);
#pragma warning restore CS0618
    }

    /// <summary>
    /// Connects the headless client to an in-process embedded server instance using native loopback networking.
    /// </summary>
    /// <param name="server">The embedded server host to connect to.</param>
    /// <param name="playerName">The player name for this client connection.</param>
    /// <returns>A loopback session for coordinating client-server interaction.</returns>
    /// <remarks>
    /// <para>
    /// This method sets up loopback networking between the client and server using the engine's
    /// singleplayer dummy network transport. The handshake follows the correct two-step sequence:
    /// </para>
    /// <list type="number">
    /// <item>Packet 33 (LoginTokenQuery) - Client requests a login token</item>
    /// <item>Packet 1 (ClientIdentification) - Client sends identification with the received token</item>
    /// </list>
    /// <para>
    /// Socket slot 0 is reserved by the engine; this connection uses slot 1+.
    /// </para>
    /// </remarks>
    public ClientServerLoopbackSession ConnectLoopback(EmbeddedServerHost server, string playerName = "PharosTest")
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

    /// <summary>
    /// Initializes a standalone mock world within ClientWorldMap without running an embedded or remote server.
    /// Configures world dimensions, lighting models, chunk data pools, and initializes terrain mesher state.
    /// </summary>
    public void InitializeMockWorld(Vec3i? mapSize = null, int defaultSunlight = 31)
    {
        // Vanilla defaults for the values Packet_LevelInitialize would have carried.
        const int DefaultChunkSize = 32;
        const int DefaultRegionSize = 512;
        const int DefaultMaxViewDistance = 8;

        Vec3i size = mapSize ?? new Vec3i(1024, 256, 1024);
        Client.WorldMap.OnMapSizeReceived(size, new Vec3i(32, 256, 32));

        // OnMapSizeReceived only fills mapsize, chunks, chunkMapSizeY, the index multipliers and
        // the region counts. The chunk and region SIZES arrive separately in
        // Packet_LevelInitialize, which ClientSystemStartup.HandleLevelInitialize applies, and a
        // mock world never sees a server. Left at 0 they make
        // ClientWorldMap.MapRegionSizeInChunks (RegionSize / ServerChunkSize) zero, and
        // ChunkTesselator.BeginProcessChunk divides by it through
        // LoadOrCreateLerpedClimateMapOffthread the moment a chunk has any visible face, which is
        // why tessellation only survived on builds where the fixture tesselated to nothing.
        // ClientEngineStartup fills these too, but that is the opt-in render path; tessellation
        // reaches them without it. Only fill what is unset so a real login still wins.
        if (Client.WorldMap.ClientChunkSize <= 0) Client.WorldMap.ClientChunkSize = DefaultChunkSize;
        if (Client.WorldMap.ServerChunkSize <= 0) Client.WorldMap.ServerChunkSize = DefaultChunkSize;
        if (Client.WorldMap.MapChunkSize <= 0) Client.WorldMap.MapChunkSize = DefaultChunkSize;
        if (Client.WorldMap.regionSize <= 0) Client.WorldMap.regionSize = DefaultRegionSize;
        if (Client.WorldMap.MaxViewDistance <= 0) Client.WorldMap.MaxViewDistance = DefaultMaxViewDistance;

        Client.WorldMap.SunBrightness = Math.Clamp(defaultSunlight, 0, 31);
        Client.WorldMap.BlockLightLevels = new float[32];
        Client.WorldMap.SunLightLevels = new float[32];
        Client.WorldMap.BlockLightLevelsByte = new byte[32];
        Client.WorldMap.SunLightLevelsByte = new byte[32];
        Client.WorldMap.hueLevels = new byte[32];
        Client.WorldMap.satLevels = new byte[32];
        for (int i = 0; i < 32; i++)
        {
            float lvl = i / 31f;
            Client.WorldMap.BlockLightLevels[i] = lvl;
            Client.WorldMap.SunLightLevels[i] = lvl;
            Client.WorldMap.BlockLightLevelsByte[i] = (byte)(255 * lvl);
            Client.WorldMap.SunLightLevelsByte[i] = (byte)(255 * lvl);
        }

        Client.WorldMap.OnBlocksAndLightLevelsReceived();

        // Ensure frustum culler is initialized for distance and culling checks
        FieldInfo? frustumField = typeof(ClientMain).GetField("frustumCuller", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (frustumField?.GetValue(Client) == null)
        {
            FrustumCulling culler = new();
            culler.UpdateViewDistance(ClientSettings.ViewDistance);
            frustumField?.SetValue(Client, culler);
        }

        // Enable terrain tessellation and initialize ChunkTesselator
        Client.ShouldTesselateTerrain = true;

        if (ChunkTesselatorManager == null)
        {
            ChunkTesselatorManager = new ChunkTesselatorManager(Client);
            FrameController.ChunkTesselatorManager = ChunkTesselatorManager;
        }

        // Optimum's manager owns its tesselator pool and gates its frame pass on
        // PrimaryTesselator.started, so configure and start that instance when the
        // build exposes it. Otherwise fall back to a standalone tesselator.
        ChunkTesselator? primary = ChunkTesselatorManager
            .GetType()
            .GetProperty("PrimaryTesselator", BindingFlags.Instance | BindingFlags.Public)
            ?.GetValue(ChunkTesselatorManager) as ChunkTesselator;

        if (primary != null)
        {
            Client.TerrainChunkTesselator = primary;
        }
        else if (Client.TerrainChunkTesselator == null)
        {
            Client.TerrainChunkTesselator = new ChunkTesselator(Client);
        }

        ChunkTesselator tct = Client.TerrainChunkTesselator;
        FieldInfo? lightsGoField = typeof(ChunkTesselator).GetField("lightsGo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        FieldInfo? texturesGoField = typeof(ChunkTesselator).GetField("blockTexturesGo", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        lightsGoField?.SetValue(tct, true);
        texturesGoField?.SetValue(tct, true);

        try
        {
            tct.Start();
        }
        catch
        {
            FieldInfo? startedField = typeof(ChunkTesselator).GetField("started", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            startedField?.SetValue(tct, true);
        }

        // Ensure chunk renderer is wired if atlas textures are available
        FieldInfo? rendererField = typeof(ClientMain).GetField("chunkRenderer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (rendererField?.GetValue(Client) == null)
        {
            int[] textureIds = (Client.BlockAtlasManager?.AtlasTextures != null && Client.BlockAtlasManager.AtlasTextures.Count > 0)
                ? Client.BlockAtlasManager.AtlasTextures.Select(t => t.TextureId).ToArray()
                : new int[] { 1 };
            ChunkRenderer renderer = new(textureIds, Client);
            rendererField?.SetValue(Client, renderer);
        }
    }

    /// <summary>
    /// Injects a synthetic ChunkFixture directly into ClientWorldMap chunk cache.
    /// </summary>
    public ClientChunk InjectChunk(ChunkFixture fixture, bool triggerTesselation = true)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        if (Client.WorldMap.MapSizeY <= 0)
        {
            InitializeMockWorld();
        }

        FieldInfo? poolField = typeof(ClientWorldMap).GetField("chunkDataPool", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        ClientChunkDataPool pool = (ClientChunkDataPool)poolField?.GetValue(Client.WorldMap)!
            ?? new ClientChunkDataPool(32, Client);

        ClientChunk clientChunk = ClientChunk.CreateNew(pool);

        // Mark loaded from server so ChunkTesselatorManager does not requeue indefinitely
        FieldInfo? loadedField = typeof(ClientChunk).GetField("loadedFromServer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        loadedField?.SetValue(clientChunk, true);

        ushort sun = (ushort)Math.Clamp(fixture.DefaultSunlight, (byte)0, (byte)31);
        clientChunk.Lighting.FillWithSunlight(sun);

        bool hasNonAirBlocks = false;

        foreach (var (index3d, blockCode) in fixture.BlockCodes)
        {
            int blockId = ResolveBlockId(blockCode);
            clientChunk.Data[index3d] = blockId;
            if (blockId != 0) hasNonAirBlocks = true;
        }

        foreach (var (index3d, blockId) in fixture.BlockIds)
        {
            clientChunk.Data[index3d] = blockId;
            if (blockId != 0) hasNonAirBlocks = true;
        }

        // Placeholder blocks from BlockList.getNoBlock draw as Empty, which makes
        // ChunkTesselator.TesselateBlock return before emitting any geometry. Register the ids
        // this fixture uses as drawable cubes so the chunk actually meshes into the render pool.
        if (hasNonAirBlocks)
        {
            List<int> usedIds = [.. fixture.BlockCodes.Values.Select(code => ResolveBlockId(code)),
                                 .. fixture.BlockIds.Values];
            FixtureBlockRegistry.EnsureDrawableBlocks(Client, usedIds);
        }

        foreach (var (index3d, sunLevel) in fixture.CustomSunlight)
        {
            clientChunk.Lighting.SetSunlight(index3d, sunLevel);
        }

        foreach (var (index3d, blockLight) in fixture.CustomBlocklight)
        {
            clientChunk.Lighting.SetBlocklight(index3d, blockLight);
        }

        foreach (Entity entity in fixture.Entities)
        {
            clientChunk.AddEntity(entity);
        }

        foreach (var (pos, blockEntity) in fixture.BlockEntities)
        {
            clientChunk.BlockEntities[pos] = blockEntity;
        }

        clientChunk.LightPositions = new HashSet<int>();
        clientChunk.Empty = !hasNonAirBlocks;

        // Insert into ClientWorldMap.chunks dictionary
        FieldInfo? chunksField = typeof(ClientWorldMap).GetField("chunks", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        FieldInfo? lockField = typeof(ClientWorldMap).GetField("chunksLock", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        object chunksLock = lockField?.GetValue(Client.WorldMap) ?? new object();
        var chunksDict = (Dictionary<long, ClientChunk>)chunksField?.GetValue(Client.WorldMap)!;

        long key = MapUtil.Index3dL(fixture.Position.X, fixture.Position.Y, fixture.Position.Z, Client.WorldMap.index3dMulX, Client.WorldMap.index3dMulZ);
        lock (chunksLock)
        {
            if (chunksDict.TryGetValue(key, out ClientChunk? existingChunk) && existingChunk != null)
            {
                FieldInfo? rendererField = typeof(ClientMain).GetField("chunkRenderer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (rendererField?.GetValue(Client) is object renderer)
                {
                    MethodInfo? removeMethod = typeof(ClientChunk).GetMethod("RemoveDataPoolLocations", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    removeMethod?.Invoke(existingChunk, new object[] { renderer });
                }
            }
            chunksDict[key] = clientChunk;
        }

        if (triggerTesselation && !clientChunk.Empty)
        {
            FieldInfo? redrawField = typeof(ClientChunk).GetField("enquedForRedraw", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            redrawField?.SetValue(clientChunk, true);

            FieldInfo? dirtyPriorityField = typeof(ClientMain).GetField("dirtyChunksPriority", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            FieldInfo? dirtyPriorityLockField = typeof(ClientMain).GetField("dirtyChunksPriorityLock", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            object lockObj = dirtyPriorityLockField?.GetValue(Client) ?? new object();
            object? queueObj = dirtyPriorityField?.GetValue(Client);

            if (queueObj != null)
            {
                lock (lockObj)
                {
                    MethodInfo? enqueueMethod = queueObj.GetType().GetMethod("Enqueue", new[] { typeof(long) });
                    enqueueMethod?.Invoke(queueObj, new object[] { key });

                    // Edge-dirty adjacent chunks if loaded
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dz = -1; dz <= 1; dz++)
                            {
                                if (dx == 0 && dy == 0 && dz == 0) continue;
                                long neighborKey = MapUtil.Index3dL(
                                    fixture.Position.X + dx,
                                    fixture.Position.Y + dy,
                                    fixture.Position.Z + dz,
                                    Client.WorldMap.index3dMulX,
                                    Client.WorldMap.index3dMulZ);

                                lock (chunksLock)
                                {
                                    if (chunksDict.TryGetValue(neighborKey, out ClientChunk? neighbor) && neighbor != null && !neighbor.Empty)
                                    {
                                        enqueueMethod?.Invoke(queueObj, new object[] { neighborKey | long.MinValue });
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        return clientChunk;
    }

    /// <summary>
    /// Injects multiple chunk fixtures into ClientWorldMap.
    /// </summary>
    public List<ClientChunk> InjectChunks(IEnumerable<ChunkFixture> fixtures, bool triggerTesselation = true)
    {
        ArgumentNullException.ThrowIfNull(fixtures);
        List<ClientChunk> injected = new();
        foreach (ChunkFixture fixture in fixtures)
        {
            injected.Add(InjectChunk(fixture, triggerTesselation));
        }
        return injected;
    }

    private int ResolveBlockId(string blockCode)
    {
        if (string.Equals(blockCode, "air", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(blockCode, "game:air", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (int.TryParse(blockCode, out int numericId))
        {
            return numericId;
        }

        if (Client.BlocksByCode != null && Client.BlocksByCode.TryGetValue(new AssetLocation(blockCode), out Block? block))
        {
            return block.BlockId;
        }

        return 1;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            GL.Disable();
        }
        catch
        {
            // Ignore GL proxy teardown errors
        }

        try
        {
            Memory.Disable();
        }
        catch
        {
            // Ignore memory inspector teardown errors
        }

        try
        {
            Shaders.Disable();
        }
        catch
        {
            // Ignore shader inspector teardown errors
        }

        try
        {
            IndirectDraw.Disable();
        }
        catch
        {
            // Ignore indirect draw inspector teardown errors
        }

        try
        {
            ManagedLeaks.Dispose();
        }
        catch
        {
            // Ignore managed leak tracker teardown errors
        }

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
