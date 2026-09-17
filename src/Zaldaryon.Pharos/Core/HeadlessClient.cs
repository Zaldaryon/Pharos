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
        Culling = new CullingInspector(client);
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

    /// <summary>
    /// Initializes a standalone mock world within ClientWorldMap without running an embedded or remote server.
    /// Configures world dimensions, lighting models, chunk data pools, and initializes terrain mesher state.
    /// </summary>
    public void InitializeMockWorld(Vec3i? mapSize = null, int defaultSunlight = 31)
    {
        Vec3i size = mapSize ?? new Vec3i(1024, 256, 1024);
        Client.WorldMap.OnMapSizeReceived(size, new Vec3i(32, 256, 32));

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

        if (Client.TerrainChunkTesselator == null)
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

        if (ChunkTesselatorManager == null)
        {
            ChunkTesselatorManager = new ChunkTesselatorManager(Client);
            FrameController.ChunkTesselatorManager = ChunkTesselatorManager;
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
