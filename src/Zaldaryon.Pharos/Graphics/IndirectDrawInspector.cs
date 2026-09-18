using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL4;

namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Intercepts indirect draw buffer bindings and multi-draw indirect dispatches
/// to expose batching statistics for headless client test scenarios.
/// Uses Harmony prefix patches on GL static methods.
/// Zero overhead when disabled: patches are not installed until <see cref="Enable"/> is called.
/// </summary>
public sealed class IndirectDrawInspector
{
    private const string HarmonyId = "zaldaryon.pharos.indirectdraw";
    private const int DrawIndirectBufferTarget = 0x8F3F; // GL_DRAW_INDIRECT_BUFFER

    private readonly Harmony _harmony = new(HarmonyId);
    private readonly object _lock = new();
    private bool _enabled;

    // Counters incremented by Harmony prefix patches
    private int _indirectDispatchCount;
    private int _totalCommandCount;
    private int _totalInstanceCount;
    private long _bufferBytesTotal;
    private int _directDrawCalls;

    // Buffer binding history
    private readonly List<IndirectCommandBufferSnapshot> _bufferBindings = new();

    // Single active instance receiving increments from static patch delegates.
    private static IndirectDrawInspector? _active;
    private static readonly object _activeLock = new();

    /// <summary>Whether indirect draw intercepting patches are currently installed.</summary>
    public bool IsEnabled
    {
        get { lock (_lock) { return _enabled; } }
    }

    /// <summary>
    /// Activates indirect draw intercepting by installing Harmony patches.
    /// No-op if already enabled.
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

    /// <summary>Deactivates intercepting and removes Harmony patches.</summary>
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

    /// <summary>
    /// Resets all counters and buffer binding history without changing enabled state.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            Interlocked.Exchange(ref _indirectDispatchCount, 0);
            Interlocked.Exchange(ref _totalCommandCount, 0);
            Interlocked.Exchange(ref _totalInstanceCount, 0);
            Interlocked.Exchange(ref _bufferBytesTotal, 0);
            Interlocked.Exchange(ref _directDrawCalls, 0);
            _bufferBindings.Clear();
        }
    }

    /// <summary>
    /// Returns an immutable snapshot of current indirect draw statistics.
    /// </summary>
    public IndirectDrawStats Snapshot()
    {
        lock (_lock)
        {
            return new IndirectDrawStats
            {
                IndirectDispatchCount = _indirectDispatchCount,
                TotalCommandCount = _totalCommandCount,
                TotalInstanceCount = _totalInstanceCount,
                BufferBytesTotal = _bufferBytesTotal,
                DirectDrawCalls = _directDrawCalls,
            };
        }
    }

    /// <summary>
    /// Returns a copy of all buffer binding snapshots recorded since the last reset.
    /// </summary>
    public IReadOnlyList<IndirectCommandBufferSnapshot> GetBufferBindings()
    {
        lock (_lock)
        {
            return _bufferBindings.ToList();
        }
    }

    // -------------------------------------------------------------------------
    // Internal counter helpers called by static Harmony prefix methods
    // -------------------------------------------------------------------------

    internal static void OnIndirectDispatch(int drawcount)
    {
        IndirectDrawInspector? active = _active;
        if (active == null) return;

        Interlocked.Increment(ref active._indirectDispatchCount);
        Interlocked.Add(ref active._totalCommandCount, drawcount);
    }

    internal static void OnIndirectDispatchWithInstances(int drawcount, int instanceCount)
    {
        IndirectDrawInspector? active = _active;
        if (active == null) return;

        Interlocked.Increment(ref active._indirectDispatchCount);
        Interlocked.Add(ref active._totalCommandCount, drawcount);
        Interlocked.Add(ref active._totalInstanceCount, instanceCount);
    }

    internal static void OnDirectDrawCall()
    {
        IndirectDrawInspector? active = _active;
        if (active == null) return;

        Interlocked.Increment(ref active._directDrawCalls);
    }

    internal static void OnBufferBind(int bufferId, long sizeBytes)
    {
        IndirectDrawInspector? active = _active;
        if (active == null) return;

        lock (active._lock)
        {
            Interlocked.Add(ref active._bufferBytesTotal, sizeBytes);
            active._bufferBindings.Add(new IndirectCommandBufferSnapshot
            {
                BufferId = bufferId,
                BufferSizeBytes = sizeBytes,
                TimestampTicks = Environment.TickCount64,
            });
        }
    }

    // -------------------------------------------------------------------------
    // Harmony patch application
    // -------------------------------------------------------------------------

    private void ApplyPatches()
    {
        Type gl = typeof(GL);
        BindingFlags pub = BindingFlags.Public | BindingFlags.Static;

        // Patch indirect draw calls
        PatchAllOverloads(gl, "MultiDrawArraysIndirect", pub, nameof(Prefix_MultiDrawIndirect));
        PatchAllOverloads(gl, "MultiDrawElementsIndirect", pub, nameof(Prefix_MultiDrawIndirect));

        // Patch BindBuffer to intercept GL_DRAW_INDIRECT_BUFFER bindings
        PatchAllOverloads(gl, "BindBuffer", pub, nameof(Prefix_BindBuffer));

        // Patch direct draw calls for fallback tracking
        PatchAllOverloads(gl, "DrawArrays", pub, nameof(Prefix_DirectDraw));
        PatchAllOverloads(gl, "DrawElements", pub, nameof(Prefix_DirectDraw));
        PatchAllOverloads(gl, "DrawArraysInstanced", pub, nameof(Prefix_DirectDraw));
        PatchAllOverloads(gl, "DrawElementsInstanced", pub, nameof(Prefix_DirectDraw));
        PatchAllOverloads(gl, "DrawRangeElements", pub, nameof(Prefix_DirectDraw));
    }

    private void PatchAllOverloads(Type type, string methodName, BindingFlags flags, string prefixName)
    {
        foreach (MethodInfo method in type.GetMethods(flags)
            .Where(m => m.Name == methodName && !m.IsGenericMethodDefinition))
        {
            _harmony.Patch(method, prefix: new HarmonyMethod(typeof(IndirectDrawInspector), prefixName));
        }
    }

    // -------------------------------------------------------------------------
    // Static Harmony prefix methods
    // -------------------------------------------------------------------------

    private static void Prefix_MultiDrawIndirect(int drawcount)
    {
        if (_active != null) OnIndirectDispatch(drawcount);
    }

    private static void Prefix_BindBuffer(BufferTarget target, int buffer)
    {
        if (_active == null) return;
        if ((int)target != DrawIndirectBufferTarget) return;

        // Attempt to query buffer size if buffer is non-zero
        long sizeBytes = 0;
        if (buffer != 0)
        {
            try
            {
                GL.GetBufferParameter(target, BufferParameterName.BufferSize, out int size);
                sizeBytes = size;
            }
            catch
            {
                // GL context may not be available in unit tests; ignore errors
            }
        }

        OnBufferBind(buffer, sizeBytes);
    }

    private static void Prefix_DirectDraw()
    {
        if (_active != null) OnDirectDrawCall();
    }
}
