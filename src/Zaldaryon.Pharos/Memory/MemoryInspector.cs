using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;

namespace Zaldaryon.Pharos.Memory;

/// <summary>
/// Inspects mesh pool state and tracks per-type allocation counts for the headless client.
/// Pool state is always readable via <see cref="PoolSnapshot"/>. Hit/miss and allocation
/// counters require <see cref="Enable"/> to install Harmony patches.
/// Chunk render pool state is readable via <see cref="ChunkRenderPoolSnapshot"/>.
/// <see cref="MeasureAllocations"/> is a standalone static helper that requires no patching.
/// </summary>
public sealed class MemoryInspector
{
    /// <summary>The client this inspector belongs to, for what it reads on the client.</summary>
    internal Core.HeadlessClient? Owner { get; set; }

    // --- Reflected ChunkRenderer fields ---
    private static readonly Type? s_chunkRendererType;
    private static readonly FieldInfo? s_masterPool;
    private static readonly FieldInfo? s_poolsByRenderPass;
    private static readonly Type? s_meshDataPoolManagerType;
    private static readonly FieldInfo? s_poolsField;
    private static readonly Type? s_meshDataPoolMasterManagerType;
    private static readonly FieldInfo? s_modelPoolsField;
    private static readonly FieldInfo? s_removalQueueField;
    private static readonly Type? s_meshDataPoolType;
    private static readonly FieldInfo? s_poolLocationsField;

    // EnumChunkRenderPass names for labeling
    private static readonly string[] s_renderPassNames = ["Opaque", "OpaqueNoCull", "BlendNoCull", "Meta", "Liquid", "TopSoil", "Transparent"];

    static MemoryInspector()
    {
        // Resolve ChunkRenderer type (internal, in VintagestoryLib)
        s_chunkRendererType = Type.GetType("Vintagestory.Client.NoObf.ChunkRenderer, VintagestoryLib");
        if (s_chunkRendererType != null)
        {
            s_masterPool = s_chunkRendererType.GetField("masterPool", BindingFlags.Instance | BindingFlags.NonPublic);
            s_poolsByRenderPass = s_chunkRendererType.GetField("poolsByRenderPass", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        }

        // MeshDataPoolManager.pools
        s_meshDataPoolManagerType = typeof(MeshDataPoolManager);
        s_poolsField = s_meshDataPoolManagerType.GetField("pools", BindingFlags.Instance | BindingFlags.NonPublic);

        // MeshDataPoolMasterManager.modelPools and removalQueue
        s_meshDataPoolMasterManagerType = typeof(MeshDataPoolMasterManager);
        s_modelPoolsField = s_meshDataPoolMasterManagerType.GetField("modelPools", BindingFlags.Instance | BindingFlags.NonPublic);
        s_removalQueueField = s_meshDataPoolMasterManagerType.GetField("removalQueue", BindingFlags.Instance | BindingFlags.NonPublic);

        // MeshDataPool.poolLocations
        s_meshDataPoolType = typeof(MeshDataPool);
        s_poolLocationsField = s_meshDataPoolType.GetField("poolLocations", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    }

    /// <summary>
    /// Tracks every OpenGL buffer, vertex array, texture, framebuffer and renderbuffer the client
    /// creates and deletes until the scope is disposed; its <see cref="GlResourceScope.Report"/>
    /// lists what was created and not deleted. Assert on it with
    /// <c>PharosAssert.NoGlLeaks(scope, kinds)</c>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The inspector has no client, another client's GL objects are tracked, or OpenGL object creation could not be hooked.</exception>
    public GlResourceScope TrackGlResources(GlResourceTrackingOptions? options = null)
    {
        Core.HeadlessClient owner = Owner ?? throw new InvalidOperationException("This memory inspector belongs to no client.");
        try
        {
            Graphics.GlResourceHooks.Install();
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException("OpenGL objects cannot be tracked: " + ex.Message, ex);
        }

        return new GlResourceScope(owner, options ?? new GlResourceTrackingOptions());
    }

    private const string HarmonyId = "zaldaryon.pharos.memory";

    private readonly Harmony _harmony = new(HarmonyId);
    private readonly object _lock = new();
    private bool _enabled;

    private long _hits;
    private long _misses;
    private long _itemRenderInfoAllocations;
    private long _meshDataAllocations;

    [ThreadStatic]
    private static bool _inGetOrCreateMesh;

    // Single active instance receiving increments from static patch delegates.
    private static MemoryInspector? _active;
    private static readonly object _activeLock = new();

    // --- Reflected pool fields (MeshDataRecycler) ---
    private static readonly FieldInfo? s_smallSizes =
        typeof(MeshDataRecycler).GetField("smallSizes", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_mediumSizes =
        typeof(MeshDataRecycler).GetField("mediumSizes", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_largeSizes =
        typeof(MeshDataRecycler).GetField("largeSizes", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_forRecycling =
        typeof(MeshDataRecycler).GetField("forRecycling", BindingFlags.Instance | BindingFlags.NonPublic);

    // --- Harmony patch targets ---
    private static readonly MethodInfo? s_getRecycled =
        typeof(MeshDataRecycler).GetMethod("GetRecycled", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? s_getOrCreateMesh =
        typeof(MeshDataRecycler).GetMethod("GetOrCreateMesh", BindingFlags.Instance | BindingFlags.Public);
    private static readonly ConstructorInfo? s_itemRenderInfoCtor =
        typeof(ItemRenderInfo).GetConstructor(Type.EmptyTypes);
    private static readonly ConstructorInfo? s_meshDataCtorDefault =
        typeof(MeshData).GetConstructor([typeof(bool)]);
    private static readonly ConstructorInfo? s_meshDataCtorCapacity =
        typeof(MeshData).GetConstructor([typeof(int)]);
    private static readonly ConstructorInfo? s_meshDataCtorFull =
        typeof(MeshData).GetConstructor([typeof(int), typeof(int), typeof(bool), typeof(bool)]);

    /// <summary>Whether allocation and hit/miss tracking patches are currently installed.</summary>
    public bool IsEnabled
    {
        get { lock (_lock) { return _enabled; } }
    }

    /// <summary>
    /// Installs Harmony patches to track mesh pool hit/miss and type allocation counts.
    /// Pool size is always readable via <see cref="PoolSnapshot"/> regardless of this state.
    /// </summary>
    public void Enable()
    {
        lock (_activeLock)
        {
            lock (_lock)
            {
                if (_enabled) return;
                _active = this;
                ApplyPatches();
                _enabled = true;
            }
        }
    }

    /// <summary>Removes Harmony patches and stops tracking.</summary>
    public void Disable()
    {
        lock (_activeLock)
        {
            lock (_lock)
            {
                if (!_enabled) return;
                _harmony.UnpatchAll(HarmonyId);
                if (ReferenceEquals(_active, this)) _active = null;
                _enabled = false;
            }
        }
    }

    /// <summary>Resets all hit/miss and allocation counters to zero without changing enabled state.</summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _hits, 0);
        Interlocked.Exchange(ref _misses, 0);
        Interlocked.Exchange(ref _itemRenderInfoAllocations, 0);
        Interlocked.Exchange(ref _meshDataAllocations, 0);
    }

    /// <summary>
    /// Returns an immutable snapshot of current mesh pool sizes and hit/miss counts.
    /// Safe to call at any time, with or without <see cref="Enable"/>.
    /// </summary>
    public MeshPoolSnapshot PoolSnapshot()
    {
        MeshDataRecycler? recycler = MeshData.Recycler;
        if (recycler == null) return MeshPoolSnapshot.Empty;

        var small = s_smallSizes?.GetValue(recycler) as SortedList<float, MeshData>;
        var medium = s_mediumSizes?.GetValue(recycler) as SortedList<float, MeshData>;
        var large = s_largeSizes?.GetValue(recycler) as SortedList<float, MeshData>;
        var pending = s_forRecycling?.GetValue(recycler) as ConcurrentQueue<MeshData>;

        return new MeshPoolSnapshot
        {
            SmallPoolSize = small?.Count ?? 0,
            MediumPoolSize = medium?.Count ?? 0,
            LargePoolSize = large?.Count ?? 0,
            PendingRecycleCount = pending?.Count ?? 0,
            Hits = _hits,
            Misses = _misses,
        };
    }

    /// <summary>
    /// Returns an immutable snapshot of allocation counts recorded since <see cref="Enable"/>
    /// or the last <see cref="Reset"/>.
    /// </summary>
    public AllocationSnapshot AllocationSnapshot() => new()
    {
        ItemRenderInfoAllocations = _itemRenderInfoAllocations,
        MeshDataAllocations = _meshDataAllocations,
    };

    /// <summary>
    /// Returns an immutable snapshot of the chunk render mesh pools used for terrain rendering.
    /// This is the pool that holds actual rendered chunk mesh data (vertices, indices).
    /// Unlike <see cref="PoolSnapshot"/> which reads the MeshDataRecycler (temporary tessellation buffers),
    /// this reads the ChunkRenderer's master pool where rendered chunks live.
    /// </summary>
    /// <returns>
    /// A snapshot with pool statistics, or <see cref="ChunkRenderPoolSnapshot.Empty"/> if the
    /// ChunkRenderer is not accessible.
    /// </returns>
    public ChunkRenderPoolSnapshot ChunkRenderPoolSnapshot()
    {
        Core.HeadlessClient? owner = Owner;
        if (owner == null)
        {
            return Memory.ChunkRenderPoolSnapshot.Empty with
            {
                DiagnosticMessage = "MemoryInspector has no owner client"
            };
        }

        // Get ClientMain from HeadlessClient
        object? clientMain = owner.GetType().GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(owner);
        if (clientMain == null)
        {
            return Memory.ChunkRenderPoolSnapshot.Empty with
            {
                DiagnosticMessage = "ClientMain not accessible from HeadlessClient"
            };
        }

        // Get chunkRenderer field from ClientMain
        FieldInfo? chunkRendererField = clientMain.GetType().GetField("chunkRenderer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        object? chunkRenderer = chunkRendererField?.GetValue(clientMain);
        if (chunkRenderer == null)
        {
            return Memory.ChunkRenderPoolSnapshot.Empty with
            {
                DiagnosticMessage = "ChunkRenderer not initialized or not accessible"
            };
        }

        return ReadChunkRendererPools(chunkRenderer);
    }

    private ChunkRenderPoolSnapshot ReadChunkRendererPools(object chunkRenderer)
    {
        try
        {
            // Get masterPool (MeshDataPoolMasterManager)
            object? masterPool = s_masterPool?.GetValue(chunkRenderer);
            // Get poolsByRenderPass (MeshDataPoolManager[][])
            object? poolsByRenderPass = s_poolsByRenderPass?.GetValue(chunkRenderer);

            if (masterPool == null || poolsByRenderPass == null)
            {
                return Memory.ChunkRenderPoolSnapshot.Empty with
                {
                    DiagnosticMessage = "masterPool or poolsByRenderPass field not accessible"
                };
            }

            // Read master pool stats
            var modelPools = s_modelPoolsField?.GetValue(masterPool) as System.Collections.IList;
            int totalPoolCount = modelPools?.Count ?? 0;

            // Count pending removals
            int pendingRemovalCount = 0;
            if (s_removalQueueField?.GetValue(masterPool) is Array removalQueues)
            {
                foreach (var queue in removalQueues)
                {
                    if (queue is System.Collections.ICollection coll)
                    {
                        pendingRemovalCount += coll.Count;
                    }
                }
            }

            // Aggregate stats from all pools
            long totalVerticesAllocated = 0;
            long totalVerticesUsed = 0;
            long totalIndicesAllocated = 0;
            long totalRenderedTriangles = 0;
            long totalAllocatedTriangles = 0;
            double fragmentationSum = 0;
            int fragmentationCount = 0;

            var byRenderPass = new Dictionary<string, RenderPassPoolStats>();

            // Read poolsByRenderPass as MeshDataPoolManager[][]
            if (poolsByRenderPass is Array outerArray)
            {
                for (int passIndex = 0; passIndex < outerArray.Length && passIndex < s_renderPassNames.Length; passIndex++)
                {
                    string passName = s_renderPassNames[passIndex];
                    var inner = outerArray.GetValue(passIndex) as Array;
                    if (inner == null) continue;

                    long passVerticesAllocated = 0;
                    long passVerticesUsed = 0;
                    long passIndicesAllocated = 0;
                    long passRenderedTriangles = 0;
                    long passAllocatedTriangles = 0;
                    int passPoolCount = 0;
                    int passChunkPartCount = 0;

                    for (int atlasIndex = 0; atlasIndex < inner.Length; atlasIndex++)
                    {
                        object? poolManager = inner.GetValue(atlasIndex);
                        if (poolManager == null) continue;

                        // Get pools from MeshDataPoolManager
                        var pools = s_poolsField?.GetValue(poolManager) as System.Collections.IList;
                        if (pools == null) continue;

                        passPoolCount += pools.Count;

                        foreach (MeshDataPool pool in pools)
                        {
                            passVerticesAllocated += pool.VerticesPoolSize;
                            passVerticesUsed += pool.UsedVertices;
                            passIndicesAllocated += pool.IndicesPoolSize;
                            passRenderedTriangles += pool.RenderedTriangles;
                            passAllocatedTriangles += pool.AllocatedTris;

                            fragmentationSum += pool.CurrentFragmentation;
                            fragmentationCount++;

                            // Count chunk parts (locations)
                            var locations = s_poolLocationsField?.GetValue(pool) as System.Collections.IList;
                            if (locations != null)
                            {
                                passChunkPartCount += locations.Count;
                            }
                        }
                    }

                    totalVerticesAllocated += passVerticesAllocated;
                    totalVerticesUsed += passVerticesUsed;
                    totalIndicesAllocated += passIndicesAllocated;
                    totalRenderedTriangles += passRenderedTriangles;
                    totalAllocatedTriangles += passAllocatedTriangles;

                    byRenderPass[passName] = new RenderPassPoolStats
                    {
                        RenderPass = passName,
                        PoolCount = passPoolCount,
                        VerticesAllocated = passVerticesAllocated,
                        VerticesUsed = passVerticesUsed,
                        IndicesAllocated = passIndicesAllocated,
                        RenderedTriangles = passRenderedTriangles,
                        AllocatedTriangles = passAllocatedTriangles,
                        ChunkPartCount = passChunkPartCount
                    };
                }
            }

            // Estimate video memory: vertices use ~28 bytes each (pos, normal, uv, color, flags), indices 4 bytes
            long estimatedMemory = totalVerticesAllocated * 28 + totalIndicesAllocated * 4;

            return new ChunkRenderPoolSnapshot
            {
                IsValid = true,
                TotalPoolCount = totalPoolCount,
                TotalVerticesAllocated = totalVerticesAllocated,
                TotalVerticesUsed = totalVerticesUsed,
                TotalIndicesAllocated = totalIndicesAllocated,
                TotalRenderedTriangles = totalRenderedTriangles,
                TotalAllocatedTriangles = totalAllocatedTriangles,
                EstimatedVideoMemoryBytes = estimatedMemory,
                AverageFragmentation = fragmentationCount > 0 ? fragmentationSum / fragmentationCount : 0,
                ByRenderPass = byRenderPass,
                PendingRemovalCount = pendingRemovalCount
            };
        }
        catch (Exception ex)
        {
            return Memory.ChunkRenderPoolSnapshot.Empty with
            {
                DiagnosticMessage = $"Exception reading ChunkRenderer pools: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Measures managed heap bytes allocated on the current thread during execution of
    /// <paramref name="action"/>. No patches needed; uses
    /// <see cref="GC.GetAllocatedBytesForCurrentThread"/> for thread-accurate accounting.
    /// </summary>
    public static long MeasureAllocations(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        long after = GC.GetAllocatedBytesForCurrentThread();
        return after - before;
    }

    // --- Internal counter increments called by static patch methods ---
    internal static void OnHit()
    {
        MemoryInspector? active = _active;
        if (active != null) Interlocked.Increment(ref active._hits);
    }

    internal static void OnMiss()
    {
        MemoryInspector? active = _active;
        if (active != null) Interlocked.Increment(ref active._misses);
    }

    internal static void OnItemRenderInfoAllocated()
    {
        MemoryInspector? active = _active;
        if (active != null) Interlocked.Increment(ref active._itemRenderInfoAllocations);
    }

    internal static void OnMeshDataAllocated()
    {
        MemoryInspector? active = _active;
        if (active != null) Interlocked.Increment(ref active._meshDataAllocations);
    }

    private void ApplyPatches()
    {
        // 1. GetRecycled postfix: null result = miss, non-null = hit
        if (s_getRecycled != null)
        {
            _harmony.Patch(s_getRecycled,
                postfix: new HarmonyMethod(typeof(MemoryInspector), nameof(Postfix_GetRecycled)));
        }

        // 2. GetOrCreateMesh prefix/postfix: guard flag so MeshData ctor patches skip recycler-internal allocs
        if (s_getOrCreateMesh != null)
        {
            _harmony.Patch(s_getOrCreateMesh,
                prefix: new HarmonyMethod(typeof(MemoryInspector), nameof(Prefix_GetOrCreateMesh)),
                finalizer: new HarmonyMethod(typeof(MemoryInspector), nameof(Finalizer_GetOrCreateMesh)));
        }

        // 3. ItemRenderInfo default constructor postfix
        if (s_itemRenderInfoCtor != null)
        {
            _harmony.Patch(s_itemRenderInfoCtor,
                postfix: new HarmonyMethod(typeof(MemoryInspector), nameof(Postfix_ItemRenderInfoCtor)));
        }

        // 4. MeshData constructors (all three overloads)
        HarmonyMethod meshCtorPostfix = new(typeof(MemoryInspector), nameof(Postfix_MeshDataCtor));
        if (s_meshDataCtorDefault != null)
        {
            _harmony.Patch(s_meshDataCtorDefault, postfix: meshCtorPostfix);
        }
        if (s_meshDataCtorCapacity != null)
        {
            _harmony.Patch(s_meshDataCtorCapacity, postfix: meshCtorPostfix);
        }
        if (s_meshDataCtorFull != null)
        {
            _harmony.Patch(s_meshDataCtorFull, postfix: meshCtorPostfix);
        }
    }

    // --- Static Harmony patch methods ---

    private static void Postfix_GetRecycled(MeshData? __result)
    {
        if (_active == null) return;
        if (__result != null)
            OnHit();
        else
            OnMiss();
    }

    private static void Prefix_GetOrCreateMesh()
    {
        _inGetOrCreateMesh = true;
    }

    private static void Finalizer_GetOrCreateMesh()
    {
        _inGetOrCreateMesh = false;
    }

    private static void Postfix_ItemRenderInfoCtor()
    {
        if (_active != null) OnItemRenderInfoAllocated();
    }

    private static void Postfix_MeshDataCtor()
    {
        if (_active != null && !_inGetOrCreateMesh) OnMeshDataAllocated();
    }
}
