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
/// <see cref="MeasureAllocations"/> is a standalone static helper that requires no patching.
/// </summary>
public sealed class MemoryInspector
{
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
