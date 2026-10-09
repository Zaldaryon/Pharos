using System.Diagnostics;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Memory;

/// <summary>Options for <see cref="MemoryInspector.TrackGlResources"/>.</summary>
public sealed record GlResourceTrackingOptions
{
    /// <summary>
    /// Whether to keep the stack that created each object, for the report. Slow; on by default when
    /// the <c>PHAROS_GL_STACKS</c> environment variable is 1.
    /// </summary>
    public bool CaptureStacks { get; init; } = Environment.GetEnvironmentVariable("PHAROS_GL_STACKS") == "1";

    /// <summary>
    /// Whether to leave out the objects the engine keeps for good: the textures of its block, item
    /// and entity texture sets, and its own framebuffers and their textures.
    /// </summary>
    public bool ExcludeEngineHeld { get; init; } = true;
}

/// <summary>An OpenGL object a scope saw created and not deleted.</summary>
/// <param name="Kind">What it is.</param>
/// <param name="Id">Its GL name.</param>
/// <param name="Generation">The order it was created in; a reused id gets a new one.</param>
/// <param name="Frame">The client frame it was created in.</param>
/// <param name="Stack">The code that created it, when stacks were captured.</param>
public sealed record GlLeakedResource(GlResourceKind Kind, uint Id, long Generation, long Frame, string? Stack);

/// <summary>What a <see cref="GlResourceScope"/> saw.</summary>
public sealed class GlResourceReport
{
    internal GlResourceReport(GlResourceKind kinds, IReadOnlyList<GlLeakedResource> leaks, int engineHeld, int preexistingDeleted, int zeroIds, int otherThreadCalls, bool stacks)
    {
        Kinds = kinds;
        Leaks = leaks;
        EngineHeld = engineHeld;
        PreexistingDeleted = preexistingDeleted;
        ZeroIds = zeroIds;
        OtherThreadCalls = otherThreadCalls;
        StacksCaptured = stacks;
    }

    /// <summary>The kinds the report covers.</summary>
    public GlResourceKind Kinds { get; }

    /// <summary>The objects created in the scope and still not deleted, oldest first.</summary>
    public IReadOnlyList<GlLeakedResource> Leaks { get; }

    /// <summary>Objects left out because the engine keeps them for good.</summary>
    public int EngineHeld { get; }

    /// <summary>Objects deleted in the scope that were created before it.</summary>
    public int PreexistingDeleted { get; }

    /// <summary>Creations the driver answered with id 0.</summary>
    public int ZeroIds { get; }

    /// <summary>Calls on other threads than the client's, left out.</summary>
    public int OtherThreadCalls { get; }

    /// <summary>Whether <see cref="GlLeakedResource.Stack"/> was captured.</summary>
    public bool StacksCaptured { get; }

    /// <summary>The leaks of one kind.</summary>
    public int Count(GlResourceKind kind) => Leaks.Count(l => (l.Kind & kind) != 0);

    /// <summary>Whether anything leaked.</summary>
    public bool HasLeaks => Leaks.Count > 0;

    /// <inheritdoc />
    public override string ToString()
    {
        StringBuilder text = new();
        text.Append($"{Leaks.Count} OpenGL object(s) created and not deleted ({Kinds}).");
        foreach (GlLeakedResource leak in Leaks.Take(20))
        {
            text.Append($"\n  {leak.Kind} #{leak.Id} (created in frame {leak.Frame})");
            if (leak.Stack != null) text.Append('\n').Append(leak.Stack);
        }

        if (Leaks.Count > 20) text.Append($"\n  ... and {Leaks.Count - 20} more");
        if (!StacksCaptured && Leaks.Count > 0) text.Append("\n  Pass CaptureStacks = true, or set PHAROS_GL_STACKS=1, to see where each was created.");
        if (EngineHeld > 0) text.Append($"\n  Left out: {EngineHeld} object(s) the engine keeps.");
        return text.ToString();
    }
}

/// <summary>
/// Tracks the OpenGL objects a client creates and deletes while it is open: every buffer, vertex
/// array, texture, framebuffer and renderbuffer, by id. Dispose it to stop.
/// See <see cref="MemoryInspector.TrackGlResources"/>.
/// </summary>
public sealed class GlResourceScope : IDisposable
{
    private readonly GlResourceLedger _ledger;
    private readonly long _start;
    private readonly HeadlessClient _client;
    private readonly GlResourceTrackingOptions _options;
    private GlResourceReport? _final;

    internal GlResourceScope(HeadlessClient client, GlResourceTrackingOptions options)
    {
        _client = client;
        _options = options;
        int thread = client.RunOnClientThread(() => Environment.CurrentManagedThreadId);
        _ledger = GlResourceLedger.Open(thread, () => client.FrameController.TotalFrames, options.CaptureStacks);
        _start = _ledger.Generation;
    }

    /// <summary>What the scope has seen so far, or what it saw once disposed.</summary>
    public GlResourceReport Report(GlResourceKind kinds = GlResourceKind.All)
    {
        if (_final != null) return kinds == GlResourceKind.All ? _final : Filter(_final, kinds);

        IReadOnlyList<GlResourceLedger.Entry> live = _ledger.LiveSince(_start, kinds);
        (HashSet<uint> Textures, HashSet<uint> Framebuffers) held = _options.ExcludeEngineHeld && live.Count > 0 ? EngineHeld() : ([], []);
        List<GlLeakedResource> leaks = [];
        int engineHeld = 0;
        foreach (GlResourceLedger.Entry entry in live)
        {
            if ((entry.Kind == GlResourceKind.Texture && held.Textures.Contains(entry.Id))
                || (entry.Kind == GlResourceKind.Framebuffer && held.Framebuffers.Contains(entry.Id)))
            {
                engineHeld++;
                continue;
            }

            leaks.Add(new GlLeakedResource(entry.Kind, entry.Id, entry.Generation, entry.Frame, Trim(entry.Stack)));
        }

        return new GlResourceReport(kinds, leaks, engineHeld, _ledger.PreexistingDeleted, _ledger.ZeroIds, _ledger.OtherThreadCalls, _options.CaptureStacks);
    }

    /// <summary>Stops tracking; <see cref="Report"/> keeps answering with what it saw.</summary>
    public void Dispose()
    {
        if (_final != null) return;
        try
        {
            _final = Report();
        }
        finally
        {
            GlResourceLedger.Close(_ledger, _options.CaptureStacks);
        }
    }

    private static GlResourceReport Filter(GlResourceReport report, GlResourceKind kinds) =>
        new(kinds, [.. report.Leaks.Where(l => (l.Kind & kinds) != 0)], report.EngineHeld, report.PreexistingDeleted, report.ZeroIds, report.OtherThreadCalls, report.StacksCaptured);

    // The ids of the textures and framebuffers the engine keeps for as long as it runs: the
    // textures of its block, item and entity texture sets, and its own framebuffers with their
    // color and depth textures. Texture and framebuffer ids are separate GL namespaces.
    private (HashSet<uint> Textures, HashSet<uint> Framebuffers) EngineHeld() => _client.RunOnClientThread(() =>
    {
        HashSet<uint> textures = [], framebuffers = [];
        ClientMain game = _client.Client;
        foreach (ITextureAtlasAPI? set in (ITextureAtlasAPI?[])[game.BlockAtlasManager, game.ItemAtlasManager, game.EntityAtlasManager])
        {
            if (set?.AtlasTextures == null) continue;
            foreach (LoadedTexture texture in set.AtlasTextures) textures.Add((uint)texture.TextureId);
        }

        foreach (FrameBufferRef? buffer in _client.Platform.FrameBuffers ?? [])
        {
            if (buffer == null) continue;
            framebuffers.Add((uint)buffer.FboId);
            textures.Add((uint)buffer.DepthTextureId);
            foreach (int texture in buffer.ColorTextureIds ?? []) textures.Add((uint)texture);
        }

        textures.Remove(0);
        framebuffers.Remove(0);
        return (textures, framebuffers);
    });

    // From the first frame outside Pharos, Harmony and OpenTK: the code that asked for the object.
    private static string? Trim(StackTrace? stack)
    {
        if (stack == null) return null;
        IEnumerable<string> frames = stack.ToString().Split('\n')
            .Select(line => line.TrimEnd())
            .SkipWhile(line => line.Contains("Zaldaryon.Pharos.Graphics", StringComparison.Ordinal)
                || line.Contains("Zaldaryon.Pharos.Memory", StringComparison.Ordinal)
                || line.Contains("HarmonyLib", StringComparison.Ordinal)
                || line.Contains("OpenTK.", StringComparison.Ordinal)
                || line.Contains("DMD<", StringComparison.Ordinal))
            .Take(12);
        return string.Join("\n", frames);
    }
}
