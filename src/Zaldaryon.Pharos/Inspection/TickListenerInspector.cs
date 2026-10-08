using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>Whether a listener or callback is a plain one or one tied to a block position.</summary>
public enum ListenerKind
{
    /// <summary>Registered with <c>RegisterGameTickListener</c> or <c>RegisterCallback</c>.</summary>
    World,

    /// <summary>Registered with a block position, as block entities do.</summary>
    Block,
}

/// <summary>A game tick listener the client calls at an interval.</summary>
/// <param name="Id">The id it was registered under, as <c>UnregisterGameTickListener</c> takes it.</param>
/// <param name="IntervalMs">How often it is called, in milliseconds on the client's clock, which follows real time.</param>
/// <param name="Kind">Plain, or tied to <paramref name="Pos"/>.</param>
/// <param name="Pos">The block position of a block listener.</param>
/// <param name="Handler">The type and method it calls.</param>
/// <param name="Mod">The mod it belongs to, or null for the engine's own.</param>
public sealed record TickListenerInfo(long Id, int IntervalMs, ListenerKind Kind, BlockPos? Pos, string Handler, string? Mod);

/// <summary>A callback the client calls once, later.</summary>
/// <param name="Id">The id it was registered under.</param>
/// <param name="DueInMs">
/// About how long until it is called, in milliseconds on the client's clock, which follows real
/// time; negative when it is late.
/// </param>
/// <param name="Kind">Plain, or tied to <paramref name="Pos"/>.</param>
/// <param name="Pos">The block position of a block callback.</param>
/// <param name="Handler">The type and method it calls.</param>
/// <param name="Mod">The mod it belongs to, or null for the engine's own.</param>
public sealed record CallbackInfo(long Id, long DueInMs, ListenerKind Kind, BlockPos? Pos, string Handler, string? Mod);

/// <summary>
/// The client's game tick listeners and delayed callbacks, with the mod each belongs to, and how
/// often a listener is called over some frames.
/// </summary>
public sealed class TickListenerInspector
{
    private readonly HeadlessClient _client;

    // Touched on the client thread only.
    private readonly Dictionary<Delegate, Delegate> _wrapped = new(ReferenceEqualityComparer.Instance);
    private bool _counting;

    internal TickListenerInspector(HeadlessClient client)
    {
        _client = client;
    }

    /// <summary>Every game tick listener, plain ones first.</summary>
    public IReadOnlyList<TickListenerInfo> All() => _client.RunOnClientThread(() =>
    {
        EventManager events = Events();
        ModOwners owners = ModOwners.Of(_client.Client.api);
        List<TickListenerInfo> listeners = [];
        foreach (GameTickListener listener in ListenerWatermark.Internals.EntityListeners(events))
        {
            if (listener == null) continue;
            Delegate? handler = Unwrapped(listener.Handler);
            listeners.Add(new TickListenerInfo(listener.ListenerId, listener.Millisecondinterval, ListenerKind.World, null,
                ModOwners.Describe(handler), owners.OfDelegate(handler)));
        }

        foreach (GameTickListenerBlock listener in ListenerWatermark.Internals.BlockListeners(events))
        {
            if (listener == null) continue;
            Delegate? handler = Unwrapped((Delegate?)listener.HandlerBare ?? listener.Handler);
            listeners.Add(new TickListenerInfo(listener.ListenerId, listener.Millisecondinterval, ListenerKind.Block, listener.Pos?.Copy(),
                ModOwners.Describe(handler), owners.OfDelegate(handler)));
        }

        return listeners;
    });

    /// <summary>The game tick listeners of the mod <paramref name="modId"/>.</summary>
    public IReadOnlyList<TickListenerInfo> Of(string modId) => All().Where(l => l.Mod == modId).ToList();

    /// <summary>Every delayed callback not called yet.</summary>
    public IReadOnlyList<CallbackInfo> Callbacks() => _client.RunOnClientThread(() =>
    {
        EventManager events = Events();
        ModOwners owners = ModOwners.Of(_client.Client.api);
        long now = events.InWorldEllapsedMs;
        List<CallbackInfo> callbacks = [];
        foreach (DelayedCallback callback in ListenerWatermark.Internals.EntityCallbacks(events).Values)
        {
            callbacks.Add(new CallbackInfo(callback.ListenerId, callback.CallAtEllapsedMilliseconds - now, ListenerKind.World, null,
                ModOwners.Describe(callback.Handler), owners.OfDelegate(callback.Handler)));
        }

        foreach (DelayedCallbackBlock callback in ListenerWatermark.Internals.BlockCallbacks(events)
                     .Concat(ListenerWatermark.Internals.SingleBlockCallbacks(events).Values))
        {
            if (callback == null) continue;
            callbacks.Add(new CallbackInfo(callback.ListenerId, callback.CallAtEllapsedMilliseconds - now, ListenerKind.Block, callback.Pos?.Copy(),
                ModOwners.Describe(callback.Handler), owners.OfDelegate(callback.Handler)));
        }

        return callbacks;
    });

    /// <summary>
    /// Steps <paramref name="frames"/> frames and returns how often <paramref name="listener"/>
    /// was called.
    /// </summary>
    /// <remarks>
    /// The client runs a listener once its interval has passed on the client's own clock, which
    /// follows real time: how many calls some frames make depends on how long they take, not on
    /// their number. Compare with what the mod counted over the same frames, or wait for a count.
    /// </remarks>
    public async Task<int> CountCallsAsync(TickListenerInfo listener, int frames, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(listener);
        IReadOnlyDictionary<TickListenerInfo, int> counts = await CountCallsAsync(frames, ct).ConfigureAwait(false);
        return counts.FirstOrDefault(c => c.Key.Id == listener.Id && c.Key.Kind == listener.Kind).Value;
    }

    /// <summary>
    /// Steps <paramref name="frames"/> frames and returns how often every listener was called.
    /// Listeners added during the frames are not counted; ones removed during them keep the calls
    /// they had.
    /// </summary>
    /// <remarks>
    /// Each listener's handler is wrapped for the frames and put back after; <see cref="All"/>
    /// shows the original meanwhile.
    /// </remarks>
    public async Task<IReadOnlyDictionary<TickListenerInfo, int>> CountCallsAsync(int frames, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frames);
        IReadOnlyList<TickListenerInfo> listeners = All();
        Dictionary<(ListenerKind, long), int> counts = [];
        List<Action> restore = _client.RunOnClientThread(() =>
        {
            if (_counting) throw new InvalidOperationException("Tick listener calls are already being counted.");
            _counting = true;
            return Wrap(counts);
        });

        try
        {
            for (int i = 0; i < frames; i++) await _client.StepAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                _client.RunOnClientThread(() =>
                {
                    foreach (Action put in restore) put();
                    _wrapped.Clear();
                    _counting = false;
                });
            }
            catch (Exception) when (_client.IsDisposed)
            {
                // A client that is gone needs nothing put back, and must not hide why it went.
            }
        }

        return listeners.ToDictionary(l => l, l => counts.GetValueOrDefault((l.Kind, l.Id)));
    }

    // Wraps every listener's handler with one that counts, and returns what puts each back.
    private List<Action> Wrap(Dictionary<(ListenerKind, long), int> counts)
    {
        EventManager events = Events();
        List<Action> restore = [];
        foreach (GameTickListener listener in ListenerWatermark.Internals.EntityListeners(events))
        {
            if (listener?.Handler is not { } original) continue;
            (ListenerKind, long) key = (ListenerKind.World, listener.ListenerId);
            Action<float> wrapper = dt =>
            {
                counts[key] = counts.GetValueOrDefault(key) + 1;
                original(dt);
            };
            _wrapped[wrapper] = original;
            listener.Handler = wrapper;
            restore.Add(() =>
            {
                if (ReferenceEquals(listener.Handler, wrapper)) listener.Handler = original;
            });
        }

        foreach (GameTickListenerBlock listener in ListenerWatermark.Internals.BlockListeners(events))
        {
            if (listener == null) continue;
            (ListenerKind, long) key = (ListenerKind.Block, listener.ListenerId);

            // The game calls the bare handler when there is one.
            if (listener.HandlerBare is { } bare)
            {
                Action<float> wrapper = dt =>
                {
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                    bare(dt);
                };
                _wrapped[wrapper] = bare;
                listener.HandlerBare = wrapper;
                restore.Add(() =>
                {
                    if (ReferenceEquals(listener.HandlerBare, wrapper)) listener.HandlerBare = bare;
                });
            }
            else if (listener.Handler is { } handler)
            {
                Action<IWorldAccessor, BlockPos, float> wrapper = (world, pos, dt) =>
                {
                    counts[key] = counts.GetValueOrDefault(key) + 1;
                    handler(world, pos, dt);
                };
                _wrapped[wrapper] = handler;
                listener.Handler = wrapper;
                restore.Add(() =>
                {
                    if (ReferenceEquals(listener.Handler, wrapper)) listener.Handler = handler;
                });
            }
        }

        return restore;
    }

    // While counting, a listener's handler is a wrapper: show what it wraps.
    private Delegate? Unwrapped(Delegate? handler) => handler != null && _wrapped.TryGetValue(handler, out Delegate? original) ? original : handler;

    private EventManager Events() =>
        ClientGame.Require(_client).eventManager
        ?? throw new InvalidOperationException("The client has no event manager yet: it has not started a game.");
}
