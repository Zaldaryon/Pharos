using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>A renderer the client calls at one stage of its frame.</summary>
/// <param name="Stage">The stage it is registered at.</param>
/// <param name="Index">Its place in the stage: renderers are called in this order.</param>
/// <param name="RenderOrder">Its render order; lower runs first.</param>
/// <param name="RenderRange">The range it renders in, in blocks.</param>
/// <param name="ProfilingName">The name it was registered with.</param>
/// <param name="TypeName">
/// Its type, or for a bare action registered with <c>RegisterRenderer(Action&lt;float&gt;, ...)</c>,
/// the type and method of the action.
/// </param>
/// <param name="Mod">The mod it belongs to, or null for the engine's own.</param>
public sealed record RendererInfo(EnumRenderStage Stage, int Index, double RenderOrder, int RenderRange, string ProfilingName, string TypeName, string? Mod)
{
    internal IRenderer Renderer { get; init; } = null!;

    /// <inheritdoc/>
    public bool Equals(RendererInfo? other) => other != null && ReferenceEquals(Renderer, other.Renderer) && Stage == other.Stage;

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(Renderer), Stage);
}

/// <summary>
/// The renderers a client calls each frame, stage by stage, with the mod each belongs to, and how
/// often they are called over some frames.
/// </summary>
/// <remarks>
/// Read from the client's event manager, the list the game calls renderers from, on an engine-mode
/// client. Calls are counted as the game starts each stage, for every renderer in it. Some stages
/// run only some of the time: <c>Ortho</c> not while the GUI is hidden, <c>OIT</c> only with the
/// transparent pass on, the shadow stages only with shadows on, and nothing renders before the
/// player and the blocks around them have loaded. A renderer that throws ends the frame: the ones
/// after it in its stage were counted but not called.
/// </remarks>
public sealed class RendererInspector
{
    // The counts of each client whose renderers are being counted, by renderer and stage: one
    // renderer can be registered at several stages. Written on that client's thread.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<ClientEventManager, Dictionary<(IRenderer, EnumRenderStage), int>> s_counts =
        new(ReferenceEqualityComparer.Instance);

    private readonly HeadlessClient _client;

    internal RendererInspector(HeadlessClient client)
    {
        _client = client;
    }

    /// <summary>Every renderer, stage by stage, in the order the game calls them.</summary>
    public IReadOnlyList<RendererInfo> All() => _client.RunOnClientThread(() =>
    {
        ClientEventManager events = Events();
        string[] stageNames = Enum.GetNames<EnumRenderStage>();
        ModOwners owners = ModOwners.Of(_client.Client.api);
        List<RendererInfo> renderers = [];
        for (int stage = 0; stage < events.renderersByStage.Length; stage++)
        {
            List<RenderHandler> handlers = events.renderersByStage[stage];
            for (int i = 0; i < handlers.Count; i++)
            {
                IRenderer renderer = handlers[i].Renderer;
                (string typeName, string? mod) = renderer is DummyRenderer { action: { } action }
                    ? (ModOwners.Describe(action), owners.OfDelegate(action))
                    : (renderer.GetType().FullName ?? renderer.GetType().Name, owners.OfType(renderer.GetType()));
                // The game stores "<stage>-<name>"; the name is what the mod gave.
                string profiling = handlers[i].ProfilingName ?? "";
                string prefix = (stage < stageNames.Length ? stageNames[stage] : "") + "-";
                if (profiling.StartsWith(prefix, StringComparison.Ordinal)) profiling = profiling[prefix.Length..];
                renderers.Add(new RendererInfo((EnumRenderStage)stage, i, renderer.RenderOrder, renderer.RenderRange, profiling, typeName, mod)
                {
                    Renderer = renderer,
                });
            }
        }

        return renderers;
    });

    /// <summary>The renderers of the mod <paramref name="modId"/>.</summary>
    public IReadOnlyList<RendererInfo> Of(string modId) => All().Where(r => r.Mod == modId).ToList();

    /// <summary>The renderers at <paramref name="stage"/>, in call order.</summary>
    public IReadOnlyList<RendererInfo> At(EnumRenderStage stage) => All().Where(r => r.Stage == stage).ToList();

    /// <summary>Steps <paramref name="frames"/> frames and returns how often <paramref name="renderer"/> was called.</summary>
    public async Task<int> CountCallsAsync(RendererInfo renderer, int frames, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        IReadOnlyDictionary<RendererInfo, int> counts = await CountCallsAsync(frames, ct).ConfigureAwait(false);
        return counts.FirstOrDefault(c => c.Key.Equals(renderer)).Value;
    }

    /// <summary>Steps <paramref name="frames"/> frames and returns how often every renderer was called.</summary>
    /// <remarks>Renderers registered during the frames are included; ones removed during them are not.</remarks>
    public async Task<IReadOnlyDictionary<RendererInfo, int>> CountCallsAsync(int frames, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        Dictionary<(IRenderer, EnumRenderStage), int> counts = new(new RendererKeyComparer());
        ClientEventManager events = _client.RunOnClientThread(Events);
        if (!s_counts.TryAdd(events, counts)) throw new InvalidOperationException("This client's renderer calls are already being counted.");

        try
        {
            for (int i = 0; i < frames; i++) await _client.StepAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            // Needs no client thread, so a client that died meanwhile still lets go.
            s_counts.TryRemove(events, out _);
        }

        Dictionary<(IRenderer, EnumRenderStage), int> final = _client.RunOnClientThread(() => new Dictionary<(IRenderer, EnumRenderStage), int>(counts, new RendererKeyComparer()));
        Dictionary<RendererInfo, int> result = [];

        // A renderer registered twice at one stage is listed twice and counted once per call of either.
        foreach (RendererInfo renderer in All()) result.TryAdd(renderer, final.GetValueOrDefault((renderer.Renderer, renderer.Stage)));
        return result;
    }

    private ClientEventManager Events() => ClientGame.Require(_client).eventManager
        ?? throw new InvalidOperationException("The client has no event manager yet: it has not started a game.");

    // Counts the stage's renderers just before the game calls them. Installed by ClientInspectionPatches.
    internal static void BeforeStage(ClientEventManager __instance, EnumRenderStage stage)
    {
        if (s_counts.IsEmpty || !s_counts.TryGetValue(__instance, out Dictionary<(IRenderer, EnumRenderStage), int>? counts)) return;
        foreach (RenderHandler handler in __instance.renderersByStage[(int)stage])
        {
            (IRenderer, EnumRenderStage) key = (handler.Renderer, stage);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }
    }

    // Renderers by reference, whatever their own Equals says.
    private sealed class RendererKeyComparer : IEqualityComparer<(IRenderer Renderer, EnumRenderStage Stage)>
    {
        public bool Equals((IRenderer Renderer, EnumRenderStage Stage) x, (IRenderer Renderer, EnumRenderStage Stage) y) =>
            ReferenceEquals(x.Renderer, y.Renderer) && x.Stage == y.Stage;

        public int GetHashCode((IRenderer Renderer, EnumRenderStage Stage) key) => HashCode.Combine(RuntimeHelpers.GetHashCode(key.Renderer), key.Stage);
    }
}
