using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL4;

namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Records OpenGL draw and buffer commands executed during a frame.
/// Uses Harmony prefix patches on <see cref="GL"/> static methods.
/// Zero overhead when recording is disabled: patches are not installed until
/// <see cref="Enable"/> is called and are removed by <see cref="Disable"/>.
/// </summary>
public sealed class GlCommandProxy
{
    private const string HarmonyId = "zaldaryon.pharos.gl";

    private readonly Harmony _harmony = new(HarmonyId);
    private readonly object _lock = new();
    private bool _enabled;

    // Raw counters — incremented by Harmony prefix delegates that capture `this`.
    // Using regular int fields plus Interlocked for thread safety without allocations.
    private int _drawCalls;
    private int _multiDrawCalls;
    private int _indirectDrawCalls;
    private int _bufferAllocations;
    private int _bufferDeletions;
    private int _vertexArrayAllocations;
    private int _errors;

    // The active instance receiving increments from static patch delegates.
    // Only one proxy may record at a time (same as audio patcher pattern).
    private static GlCommandProxy? _active;
    private static readonly object _activeLock = new();

    public bool IsEnabled
    {
        get { lock (_lock) { return _enabled; } }
    }

    /// <summary>
    /// Activates GL command recording by installing Harmony patches.
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

    /// <summary>
    /// Deactivates recording and removes Harmony patches.
    /// </summary>
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
                PatchedTypes = Array.Empty<Type>();
            }
        }
    }

    /// <summary>
    /// Resets all counters to zero without changing enabled state.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _drawCalls, 0);
        Interlocked.Exchange(ref _multiDrawCalls, 0);
        Interlocked.Exchange(ref _indirectDrawCalls, 0);
        Interlocked.Exchange(ref _bufferAllocations, 0);
        Interlocked.Exchange(ref _bufferDeletions, 0);
        Interlocked.Exchange(ref _vertexArrayAllocations, 0);
        Interlocked.Exchange(ref _errors, 0);
    }

    /// <summary>
    /// Returns an immutable snapshot of current counter values.
    /// </summary>
    public GlCommandRecord Snapshot() => new()
    {
        DrawCalls = _drawCalls,
        MultiDrawCalls = _multiDrawCalls,
        IndirectDrawCalls = _indirectDrawCalls,
        BufferAllocations = _bufferAllocations,
        BufferDeletions = _bufferDeletions,
        VertexArrayAllocations = _vertexArrayAllocations,
        Errors = _errors,
    };

    // Counter increment helpers called by static patch methods.
    internal static void OnDrawCall() => Interlocked.Increment(ref _active!._drawCalls);
    internal static void OnMultiDrawCall() => Interlocked.Increment(ref _active!._multiDrawCalls);
    internal static void OnIndirectDrawCall() => Interlocked.Increment(ref _active!._indirectDrawCalls);
    internal static void OnBufferAllocated() => Interlocked.Increment(ref _active!._bufferAllocations);
    internal static void OnBufferDeleted() => Interlocked.Increment(ref _active!._bufferDeletions);
    internal static void OnVertexArrayAllocated() => Interlocked.Increment(ref _active!._vertexArrayAllocations);
    internal static void OnError() => Interlocked.Increment(ref _active!._errors);

    /// <summary>
    /// The OpenGL binding types this proxy patches while recording.
    /// </summary>
    /// <remarks>
    /// Both generations are needed. Vintage Story's <c>ClientPlatformWindows</c> issues its draws
    /// through the legacy <c>OpenTK.Graphics.OpenGL</c> bindings, while other code paths use
    /// <c>OpenTK.Graphics.OpenGL4</c>. Patching only one records nothing for the other.
    /// </remarks>
    public IReadOnlyList<Type> PatchedTypes { get; private set; } = Array.Empty<Type>();

    private void ApplyPatches()
    {
        // A proxy that only patches the OpenGL4 class silently records nothing at all, so a
        // draw-call assertion could never fail for the right reason.
        Type[] bindings = [typeof(GL), typeof(OpenTK.Graphics.OpenGL.GL)];
        foreach (Type gl in bindings)
        {
            ApplyPatchesTo(gl);
        }

        PatchedTypes = bindings;
    }

    private void ApplyPatchesTo(Type gl)
    {
        BindingFlags pub = BindingFlags.Public | BindingFlags.Static;

        PatchAllOverloads(gl, "DrawArrays", pub, nameof(Prefix_DrawCall));
        PatchAllOverloads(gl, "DrawElements", pub, nameof(Prefix_DrawCall));
        PatchAllOverloads(gl, "DrawArraysInstanced", pub, nameof(Prefix_DrawCall));
        PatchAllOverloads(gl, "DrawElementsInstanced", pub, nameof(Prefix_DrawCall));
        PatchAllOverloads(gl, "DrawRangeElements", pub, nameof(Prefix_DrawCall));

        PatchAllOverloads(gl, "MultiDrawArrays", pub, nameof(Prefix_MultiDrawCall));
        PatchAllOverloads(gl, "MultiDrawElements", pub, nameof(Prefix_MultiDrawCall));

        PatchAllOverloads(gl, "MultiDrawArraysIndirect", pub, nameof(Prefix_IndirectDrawCall));
        PatchAllOverloads(gl, "MultiDrawElementsIndirect", pub, nameof(Prefix_IndirectDrawCall));

        PatchFirst(gl, "GenBuffer", pub, nameof(Prefix_BufferAllocated));
        PatchAllOverloads(gl, "GenBuffers", pub, nameof(Prefix_BufferAllocated));

        PatchFirst(gl, "DeleteBuffer", pub, nameof(Prefix_BufferDeleted));
        PatchAllOverloads(gl, "DeleteBuffers", pub, nameof(Prefix_BufferDeleted));

        PatchFirst(gl, "GenVertexArray", pub, nameof(Prefix_VertexArrayAllocated));
        PatchAllOverloads(gl, "GenVertexArrays", pub, nameof(Prefix_VertexArrayAllocated));
    }

    private void PatchFirst(Type type, string methodName, BindingFlags flags, string prefixName)
    {
        MethodInfo? method = type.GetMethods(flags)
            .FirstOrDefault(m => m.Name == methodName);
        if (method != null)
        {
            _harmony.Patch(method, prefix: new HarmonyMethod(typeof(GlCommandProxy), prefixName));
        }
    }

    private void PatchAllOverloads(Type type, string methodName, BindingFlags flags, string prefixName)
    {
        foreach (MethodInfo method in type.GetMethods(flags).Where(m => m.Name == methodName))
        {
            if (method.IsGenericMethodDefinition)
            {
                Type[] typeArgs = method.GetGenericArguments();
                if (typeArgs.Length == 1)
                {
                    try
                    {
                        MethodInfo constructed = method.MakeGenericMethod(typeof(int));
                        _harmony.Patch(constructed, prefix: new HarmonyMethod(typeof(GlCommandProxy), prefixName));
                    }
                    catch { }
                }
            }
            else
            {
                try
                {
                    _harmony.Patch(method, prefix: new HarmonyMethod(typeof(GlCommandProxy), prefixName));
                }
                catch { }
            }
        }
    }

    private static void Prefix_DrawCall()
    {
        if (_active != null) OnDrawCall();
    }

    private static void Prefix_MultiDrawCall()
    {
        if (_active != null) OnMultiDrawCall();
    }

    private static void Prefix_IndirectDrawCall()
    {
        if (_active != null) OnIndirectDrawCall();
    }

    private static void Prefix_BufferAllocated()
    {
        if (_active != null) OnBufferAllocated();
    }

    private static void Prefix_BufferDeleted()
    {
        if (_active != null) OnBufferDeleted();
    }

    private static void Prefix_VertexArrayAllocated()
    {
        if (_active != null) OnVertexArrayAllocated();
    }
}
