using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL4;
using Zaldaryon.Pharos.Memory;

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
    private int _vertexArrayDeletions;
    private int _textureAllocations;
    private int _textureDeletions;
    private int _framebufferAllocations;
    private int _framebufferDeletions;
    private int _renderbufferAllocations;
    private int _renderbufferDeletions;
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
        Interlocked.Exchange(ref _vertexArrayDeletions, 0);
        Interlocked.Exchange(ref _textureAllocations, 0);
        Interlocked.Exchange(ref _textureDeletions, 0);
        Interlocked.Exchange(ref _framebufferAllocations, 0);
        Interlocked.Exchange(ref _framebufferDeletions, 0);
        Interlocked.Exchange(ref _renderbufferAllocations, 0);
        Interlocked.Exchange(ref _renderbufferDeletions, 0);
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
        VertexArrayDeletions = _vertexArrayDeletions,
        TextureAllocations = _textureAllocations,
        TextureDeletions = _textureDeletions,
        FramebufferAllocations = _framebufferAllocations,
        FramebufferDeletions = _framebufferDeletions,
        RenderbufferAllocations = _renderbufferAllocations,
        RenderbufferDeletions = _renderbufferDeletions,
        Errors = _errors,
    };

    /// <summary>Whether a proxy is recording.</summary>
    internal static bool IsRecording => Volatile.Read(ref _active) != null;

    // Called by GlResourceHooks for each object created or deleted, once per id.
    internal static void OnResource(GlResourceKind kind, bool created)
    {
        GlCommandProxy? proxy = Volatile.Read(ref _active);
        if (proxy == null) return;
        switch (kind, created)
        {
            case (GlResourceKind.Buffer, true): Interlocked.Increment(ref proxy._bufferAllocations); break;
            case (GlResourceKind.Buffer, false): Interlocked.Increment(ref proxy._bufferDeletions); break;
            case (GlResourceKind.VertexArray, true): Interlocked.Increment(ref proxy._vertexArrayAllocations); break;
            case (GlResourceKind.VertexArray, false): Interlocked.Increment(ref proxy._vertexArrayDeletions); break;
            case (GlResourceKind.Texture, true): Interlocked.Increment(ref proxy._textureAllocations); break;
            case (GlResourceKind.Texture, false): Interlocked.Increment(ref proxy._textureDeletions); break;
            case (GlResourceKind.Framebuffer, true): Interlocked.Increment(ref proxy._framebufferAllocations); break;
            case (GlResourceKind.Framebuffer, false): Interlocked.Increment(ref proxy._framebufferDeletions); break;
            case (GlResourceKind.Renderbuffer, true): Interlocked.Increment(ref proxy._renderbufferAllocations); break;
            case (GlResourceKind.Renderbuffer, false): Interlocked.Increment(ref proxy._renderbufferDeletions); break;
        }
    }

    // Counter increment helpers called by static patch methods.
    internal static void OnDrawCall() { if (Volatile.Read(ref _active) is { } proxy) Interlocked.Increment(ref proxy._drawCalls); }
    internal static void OnMultiDrawCall() { if (Volatile.Read(ref _active) is { } proxy) Interlocked.Increment(ref proxy._multiDrawCalls); }
    internal static void OnIndirectDrawCall() { if (Volatile.Read(ref _active) is { } proxy) Interlocked.Increment(ref proxy._indirectDrawCalls); }
    internal static void OnBufferAllocated() { if (Volatile.Read(ref _active) is { } proxy) Interlocked.Increment(ref proxy._bufferAllocations); }
    internal static void OnBufferDeleted() { if (Volatile.Read(ref _active) is { } proxy) Interlocked.Increment(ref proxy._bufferDeletions); }
    internal static void OnVertexArrayAllocated() { if (Volatile.Read(ref _active) is { } proxy) Interlocked.Increment(ref proxy._vertexArrayAllocations); }
    internal static void OnError() { if (Volatile.Read(ref _active) is { } proxy) Interlocked.Increment(ref proxy._errors); }

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

        // Buffers, vertex arrays and the other objects are counted by GlResourceHooks, which is
        // installed before anything boots and counts each id.
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
}
