using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Server;

/// <summary>What a fast-forward did, before it is turned into a <see cref="FastForwardResult"/>.</summary>
internal sealed record FastForwardOutcome(long SimulatedMs, int Steps, IReadOnlyDictionary<(int Manager, long Id), int> Calls, int CallbacksRun);

/// <summary>
/// Runs the tick listeners and delayed callbacks tied to one block position on a simulated
/// timeline, without the rest of the world. See <see cref="ServerTickListenerInspector.FastForwardAsync"/>.
/// </summary>
/// <remarks>Runs on the game thread that owns the managers.</remarks>
internal static class TickFastForward
{
    /// <summary>The most steps one run may take.</summary>
    public const long MaxSteps = 1_000_000;

    /// <summary>The step used when only callbacks are tied to the position.</summary>
    public const long CallbackOnlyStepMs = 50;

    // What the last run wrote into a listener, so the next run picks up where it left off.
    private static readonly ConditionalWeakTable<GameTickListenerBase, Leftover> Leftovers = new();

    [ThreadStatic]
    private static bool t_running;

    private sealed record Leftover(long Written, long LeftMs);

    private sealed record Listener(int Manager, GameTickListenerBase Item);

    private sealed record Callback(int Manager, DelayedCallbackBase Item);

    /// <summary>
    /// Runs what <paramref name="managers"/> hold at <paramref name="pos"/> for
    /// <paramref name="spanMs"/> simulated milliseconds, in steps of <paramref name="stepMs"/>,
    /// or of the smallest listener interval there when null.
    /// </summary>
    public static FastForwardOutcome Run(IReadOnlyList<EventManager> managers, IWorldAccessor world, BlockPos pos, long spanMs, long? stepMs, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(spanMs);
        if (stepMs is < 1) throw new ArgumentOutOfRangeException(nameof(stepMs), stepMs, "A step is at least 1 ms.");
        if (t_running) throw new InvalidOperationException("A fast-forward is already running: a handler cannot start another.");
        t_running = true;
        try
        {
            return RunCore(managers, world, pos, spanMs, stepMs, ct);
        }
        finally
        {
            t_running = false;
        }
    }

    private static FastForwardOutcome RunCore(IReadOnlyList<EventManager> managers, IWorldAccessor world, BlockPos pos, long spanMs, long? stepMs, CancellationToken ct)
    {
        long v0 = managers[0].InWorldEllapsedMs;
        List<Listener> listeners = [];
        List<Callback> callbacks = [];
        for (int m = 0; m < managers.Count; m++) Collect(managers[m], m, pos, 0, 0, listeners, callbacks);

        long step = stepMs ?? (listeners.Count > 0 ? Math.Max(1, listeners.Min(l => (long)l.Item.Millisecondinterval)) : CallbackOnlyStepMs);
        if ((spanMs + step - 1) / step > MaxSteps)
            throw new ArgumentOutOfRangeException(nameof(spanMs), spanMs, $"A fast-forward takes at most {MaxSteps} steps; use a longer step.");

        // Start every listener on the simulated timeline: from now, or from where the last run left it.
        foreach (Listener l in listeners)
        {
            GameTickListenerBase item = l.Item;
            if (Leftovers.TryGetValue(item, out Leftover? left) && item.LastUpdateMilliseconds == left.Written) item.LastUpdateMilliseconds = v0 - left.LeftMs;
            else if (item.LastUpdateMilliseconds < v0) item.LastUpdateMilliseconds = v0;
        }

        Dictionary<(int, long), int> calls = [];
        int callbacksRun = 0;
        int steps = 0;
        long elapsed = 0;
        long v = v0;
        try
        {
            while (elapsed < spanMs && (listeners.Count > 0 || callbacks.Count > 0))
            {
                ct.ThrowIfCancellationRequested();
                elapsed += Math.Min(step, spanMs - elapsed);
                v = v0 + elapsed;
                steps++;
                long now = v;

                List<Listener> due = listeners
                    .Where(l => now - l.Item.LastUpdateMilliseconds >= l.Item.Millisecondinterval)
                    .OrderBy(l => l.Item.Millisecondinterval).ThenBy(l => l.Item.ListenerId).ThenBy(l => l.Manager)
                    .ToList();
                foreach (Listener l in due)
                {
                    // An earlier handler may have removed it, or run it.
                    if (!IsLive(managers[l.Manager], l.Item) || now - l.Item.LastUpdateMilliseconds < l.Item.Millisecondinterval) continue;
                    (int, long) key = (l.Manager, l.Item.ListenerId);
                    calls[key] = calls.GetValueOrDefault(key) + 1;
                    Call(managers, pos, now, listeners, callbacks, () =>
                    {
                        if (l.Item is GameTickListenerBlock block) block.OnTriggered(world, block.Pos, now);
                        else ((GameTickListener)l.Item).OnTriggered(now);
                    });
                }

                List<Callback> dueCallbacks = callbacks
                    .Where(c => now >= c.Item.CallAtEllapsedMilliseconds)
                    .OrderBy(c => c.Item.CallAtEllapsedMilliseconds).ThenBy(c => c.Item.ListenerId).ThenBy(c => c.Manager)
                    .ToList();
                foreach (Callback c in dueCallbacks)
                {
                    // Taken out before it runs, as the game does with block callbacks.
                    if (!Remove(managers[c.Manager], c.Item)) continue;
                    callbacks.Remove(c);
                    callbacksRun++;
                    float dt = (now - c.Item.CallAtEllapsedMilliseconds) / 1000f;
                    Call(managers, pos, now, listeners, callbacks, () =>
                    {
                        if (c.Item is DelayedCallbackBlock block) block.Handler(world, block.Pos, dt);
                        else ((DelayedCallback)c.Item).Handler(dt);
                    });
                }

                listeners.RemoveAll(l => !IsLive(managers[l.Manager], l.Item));
                callbacks.RemoveAll(c => !IsLive(managers[c.Manager], c.Item));
            }
        }
        finally
        {
            // Back to the real timeline: what was left over stays owed, the run's own wall time does not.
            long shift = managers[0].InWorldEllapsedMs - v;
            foreach (Listener l in listeners)
            {
                if (!IsLive(managers[l.Manager], l.Item)) continue;
                long leftMs = v - l.Item.LastUpdateMilliseconds;
                l.Item.LastUpdateMilliseconds += shift;
                Leftovers.AddOrUpdate(l.Item, new Leftover(l.Item.LastUpdateMilliseconds, leftMs));
            }

            foreach (Callback c in callbacks)
            {
                if (IsLive(managers[c.Manager], c.Item)) c.Item.CallAtEllapsedMilliseconds += shift;
            }
        }

        return new FastForwardOutcome(elapsed, steps, calls, callbacksRun);
    }

    // Runs one handler, then takes in what it registered at pos, moved onto the simulated timeline.
    private static void Call(IReadOnlyList<EventManager> managers, BlockPos pos, long v, List<Listener> listeners, List<Callback> callbacks, Action handler)
    {
        long r0 = managers[0].InWorldEllapsedMs;
        (long Listener, long Callback)[] marks = managers.Select(m => (ListenerWatermark.Internals.ListenerId(m), ListenerWatermark.Internals.CallbackId(m))).ToArray();
        try
        {
            handler();
        }
        finally
        {
            for (int m = 0; m < managers.Count; m++)
            {
                if (ListenerWatermark.Internals.ListenerId(managers[m]) == marks[m].Listener && ListenerWatermark.Internals.CallbackId(managers[m]) == marks[m].Callback) continue;
                int firstListener = listeners.Count, firstCallback = callbacks.Count;
                Collect(managers[m], m, pos, marks[m].Listener, marks[m].Callback, listeners, callbacks);
                for (int i = firstListener; i < listeners.Count; i++) listeners[i].Item.LastUpdateMilliseconds += v - r0;
                for (int i = firstCallback; i < callbacks.Count; i++) callbacks[i].Item.CallAtEllapsedMilliseconds += v - r0;
            }
        }
    }

    // Adds what manager holds at pos with ids above the given ones.
    private static void Collect(EventManager manager, int index, BlockPos pos, long aboveListenerId, long aboveCallbackId, List<Listener> listeners, List<Callback> callbacks)
    {
        foreach (GameTickListener l in ListenerWatermark.Internals.LiveEntityListeners(manager))
        {
            if (l != null && l.ListenerId > aboveListenerId && pos.Equals(TickItems.PosOf(l))) listeners.Add(new Listener(index, l));
        }

        foreach (GameTickListenerBlock l in ListenerWatermark.Internals.LiveBlockListeners(manager))
        {
            if (l != null && l.ListenerId > aboveListenerId && pos.Equals(l.Pos)) listeners.Add(new Listener(index, l));
        }

        foreach (DelayedCallback c in ListenerWatermark.Internals.EntityCallbacks(manager).Values)
        {
            if (c != null && c.ListenerId > aboveCallbackId && pos.Equals(TickItems.PosOf(c))) callbacks.Add(new Callback(index, c));
        }

        foreach (DelayedCallbackBlock c in ListenerWatermark.Internals.LiveBlockCallbacks(manager))
        {
            if (c != null && c.ListenerId > aboveCallbackId && pos.Equals(c.Pos)) callbacks.Add(new Callback(index, c));
        }

        foreach (DelayedCallbackBlock c in ListenerWatermark.Internals.LiveSingleBlockCallbacks(manager).Values)
        {
            if (c != null && c.ListenerId > aboveCallbackId && pos.Equals(c.Pos)) callbacks.Add(new Callback(index, c));
        }
    }

    private static bool IsLive(EventManager manager, GameTickListenerBase listener)
    {
        if (listener is GameTickListenerBlock block)
        {
            foreach (GameTickListenerBlock l in ListenerWatermark.Internals.LiveBlockListeners(manager))
            {
                if (ReferenceEquals(l, block)) return true;
            }

            return false;
        }

        foreach (GameTickListener l in ListenerWatermark.Internals.LiveEntityListeners(manager))
        {
            if (ReferenceEquals(l, listener)) return true;
        }

        return false;
    }

    private static bool IsLive(EventManager manager, DelayedCallbackBase callback)
    {
        if (callback is DelayedCallbackBlock block)
        {
            if (ListenerWatermark.Internals.LiveSingleBlockCallbacks(manager).TryGetValue(block.Pos, out DelayedCallbackBlock? single) && ReferenceEquals(single, block)) return true;
            foreach (DelayedCallbackBlock c in ListenerWatermark.Internals.LiveBlockCallbacks(manager))
            {
                if (ReferenceEquals(c, block)) return true;
            }

            return false;
        }

        return ListenerWatermark.Internals.EntityCallbacks(manager).TryGetValue(callback.ListenerId, out DelayedCallback? found) && ReferenceEquals(found, callback);
    }

    // Takes a callback out of the game's collections; false when it was not there.
    private static bool Remove(EventManager manager, DelayedCallbackBase callback)
    {
        if (callback is DelayedCallbackBlock block)
        {
            Dictionary<BlockPos, DelayedCallbackBlock> singles = ListenerWatermark.Internals.LiveSingleBlockCallbacks(manager);
            if (singles.TryGetValue(block.Pos, out DelayedCallbackBlock? single) && ReferenceEquals(single, block)) return singles.Remove(block.Pos);
            List<DelayedCallbackBlock> list = ListenerWatermark.Internals.LiveBlockCallbacks(manager);
            int index = list.FindIndex(c => ReferenceEquals(c, block));
            if (index < 0) return false;
            list.RemoveAt(index);
            return true;
        }

        var entity = ListenerWatermark.Internals.EntityCallbacks(manager);
        return entity.TryGetValue(callback.ListenerId, out DelayedCallback? found) && ReferenceEquals(found, callback) && entity.TryRemove(callback.ListenerId, out _);
    }
}

/// <summary>Reads the parts of a listener or callback the inspector reports.</summary>
internal static class TickItems
{
    /// <summary>The handler the game calls.</summary>
    public static Delegate? HandlerOf(object item) => item switch
    {
        GameTickListenerBlock l => ListenerWatermark.Internals.HandlerOf(l),
        GameTickListener l => l.Handler,
        DelayedCallbackBlock c => c.Handler,
        DelayedCallback c => c.Handler,
        _ => null,
    };

    /// <summary>
    /// The block position <paramref name="item"/> is tied to: its own for a block listener or
    /// callback, else that of the block entity its handler belongs to, or null.
    /// </summary>
    public static BlockPos? PosOf(object item) => item switch
    {
        GameTickListenerBlock l => l.Pos,
        DelayedCallbackBlock c => c.Pos,
        _ => HandlerOf(item)?.Target switch
        {
            BlockEntity be => be.Pos,
            BlockEntityBehavior behavior => behavior.Blockentity?.Pos,
            _ => null,
        },
    };

    /// <summary>The entity the handler belongs to, directly or through one of its behaviors.</summary>
    public static long? EntityIdOf(Delegate? handler) => handler?.Target switch
    {
        Entity entity => entity.EntityId,
        EntityBehavior behavior => behavior.entity?.EntityId,
        _ => null,
    };

    /// <summary>The full name of the type of the object the handler is bound to, else of the type declaring it.</summary>
    public static string? OwnerTypeOf(Delegate? handler) => handler?.Target?.GetType().FullName ?? handler?.Method.DeclaringType?.FullName;
}
