using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Server;

/// <summary>Which of the server's two event managers holds a listener or callback.</summary>
/// <remarks>Ids count up separately in each, so the same id can appear in both.</remarks>
public enum ListenerManager
{
    /// <summary><c>ServerMain.EventManager</c>, where every <c>sapi.Event.Register...</c> call lands.</summary>
    Core,

    /// <summary><c>ServerMain.ModEventManager</c>, which the server ticks after the core one.</summary>
    Mod,
}

/// <summary>A game tick listener the server calls at an interval.</summary>
/// <param name="Id">The id it was registered under, as <c>UnregisterGameTickListener</c> takes it.</param>
/// <param name="IntervalMs">How often it is called, in milliseconds.</param>
/// <param name="Kind">Plain, or registered with a block position.</param>
/// <param name="Pos">
/// The position it is tied to: its own for a block listener, else that of the block entity its
/// handler belongs to, or null.
/// </param>
/// <param name="Handler">The type and method it calls.</param>
/// <param name="Mod">The mod it belongs to, or null for the engine's own.</param>
/// <param name="OwnerType">
/// The full name of the type of the object its handler is bound to (a block entity, a behavior,
/// an entity), else of the type declaring the handler.
/// </param>
/// <param name="EntityId">The entity it belongs to, directly or through one of its behaviors, or null.</param>
/// <param name="SinceLastRunMs">
/// Milliseconds since it last ran, or since it was registered if it has not run yet; negative
/// while an initial delay is pending.
/// </param>
/// <param name="Manager">Which event manager holds it.</param>
public sealed record ServerTickListenerInfo(
    long Id, int IntervalMs, ListenerKind Kind, BlockPos? Pos, string Handler, string? Mod,
    string? OwnerType, long? EntityId, long SinceLastRunMs, ListenerManager Manager);

/// <summary>A callback the server calls once, later.</summary>
/// <param name="Id">The id it was registered under, as <c>UnregisterCallback</c> takes it.</param>
/// <param name="DueInMs">Milliseconds until it is called; negative when it is late.</param>
/// <param name="Kind">Plain, or registered with a block position.</param>
/// <param name="Pos">The position it is tied to, as for <see cref="ServerTickListenerInfo.Pos"/>.</param>
/// <param name="Handler">The type and method it calls.</param>
/// <param name="Mod">The mod it belongs to, or null for the engine's own.</param>
/// <param name="OwnerType">The type its handler is bound to, as for <see cref="ServerTickListenerInfo.OwnerType"/>.</param>
/// <param name="EntityId">The entity it belongs to, or null.</param>
/// <param name="Manager">Which event manager holds it.</param>
public sealed record ServerCallbackInfo(
    long Id, long DueInMs, ListenerKind Kind, BlockPos? Pos, string Handler, string? Mod,
    string? OwnerType, long? EntityId, ListenerManager Manager);

/// <summary>What <see cref="ServerTickListenerInspector.FastForwardAsync"/> ran.</summary>
/// <param name="Simulated">
/// The simulated time that ran: the span asked for, or less when nothing was left at the position.
/// </param>
/// <param name="Steps">How many steps ran.</param>
/// <param name="Calls">
/// How often each listener was called, by manager and id, including listeners removed during the
/// run. Listeners that were never called are absent.
/// </param>
/// <param name="CallbacksRun">How many delayed callbacks ran.</param>
public sealed record FastForwardResult(TimeSpan Simulated, int Steps, IReadOnlyDictionary<(ListenerManager Manager, long Id), int> Calls, int CallbacksRun)
{
    /// <summary>How often <paramref name="listener"/> was called.</summary>
    public int CallsTo(ServerTickListenerInfo listener)
    {
        ArgumentNullException.ThrowIfNull(listener);
        return Calls.GetValueOrDefault((listener.Manager, listener.Id));
    }
}

/// <summary>
/// The server's game tick listeners and delayed callbacks, where they are tied to and what owns
/// them, and a fast-forward that runs the ones at one block position on simulated time.
/// </summary>
/// <remarks>Everything runs on the server's game thread.</remarks>
public sealed class ServerTickListenerInspector
{
    private readonly EmbeddedServerHost _host;

    internal ServerTickListenerInspector(EmbeddedServerHost host)
    {
        _host = host;
    }

    /// <summary>Every game tick listener: the core manager's, then the mod manager's; plain ones first in each.</summary>
    public IReadOnlyList<ServerTickListenerInfo> All() => Listeners(_ => true);

    /// <summary>The game tick listeners of the mod <paramref name="modId"/>.</summary>
    public IReadOnlyList<ServerTickListenerInfo> Of(string modId)
    {
        ArgumentNullException.ThrowIfNull(modId);
        return All().Where(l => l.Mod == modId).ToList();
    }

    /// <summary>The game tick listeners tied to <paramref name="pos"/>; see <see cref="ServerTickListenerInfo.Pos"/>.</summary>
    public IReadOnlyList<ServerTickListenerInfo> At(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return All().Where(l => pos.Equals(l.Pos)).ToList();
    }

    /// <summary>The game tick listeners of the entity <paramref name="entityId"/> and its behaviors.</summary>
    public IReadOnlyList<ServerTickListenerInfo> OfEntity(long entityId) => All().Where(l => l.EntityId == entityId).ToList();

    /// <summary>The game tick listeners whose handler is bound to an instance of <paramref name="ownerType"/>.</summary>
    public IReadOnlyList<ServerTickListenerInfo> OfType(Type ownerType)
    {
        ArgumentNullException.ThrowIfNull(ownerType);
        return Listeners(handler => ownerType.IsInstanceOfType(handler?.Target));
    }

    /// <summary>The game tick listeners whose handler is bound to a <typeparamref name="T"/>.</summary>
    public IReadOnlyList<ServerTickListenerInfo> OfType<T>() => OfType(typeof(T));

    /// <summary>Every delayed callback not called yet.</summary>
    public IReadOnlyList<ServerCallbackInfo> Callbacks() => _host.RunOnGameThread(() =>
    {
        ModOwners owners = ModOwners.Of(_host.Server.Api);
        List<ServerCallbackInfo> callbacks = [];
        foreach ((EventManager events, ListenerManager which) in Managers())
        {
            long now = events.InWorldEllapsedMs;
            IEnumerable<DelayedCallbackBase> all = ListenerWatermark.Internals.EntityCallbacks(events).Values.Cast<DelayedCallbackBase>()
                .Concat(ListenerWatermark.Internals.BlockCallbacks(events))
                .Concat(ListenerWatermark.Internals.SingleBlockCallbacks(events).Values);
            foreach (DelayedCallbackBase callback in all)
            {
                if (callback == null) continue;
                Delegate? handler = TickItems.HandlerOf(callback);
                callbacks.Add(new ServerCallbackInfo(callback.ListenerId, callback.CallAtEllapsedMilliseconds - now,
                    callback is DelayedCallbackBlock ? ListenerKind.Block : ListenerKind.World, TickItems.PosOf(callback)?.Copy(),
                    ModOwners.Describe(handler), owners.OfDelegate(handler), TickItems.OwnerTypeOf(handler), TickItems.EntityIdOf(handler), which));
            }
        }

        return callbacks;
    });

    /// <summary>The delayed callbacks tied to <paramref name="pos"/>; see <see cref="ServerCallbackInfo.Pos"/>.</summary>
    public IReadOnlyList<ServerCallbackInfo> CallbacksAt(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return Callbacks().Where(c => pos.Equals(c.Pos)).ToList();
    }

    /// <summary>
    /// Runs the listeners and callbacks tied to <paramref name="pos"/> for <paramref name="span"/>
    /// of simulated time, in steps of <paramref name="step"/>, at once and without the rest of
    /// the world. A firepit cooks or a quern grinds as far as it would in that time.
    /// </summary>
    /// <param name="pos">The block position; see <see cref="ServerTickListenerInfo.Pos"/>.</param>
    /// <param name="span">How much simulated time to run, cut to whole milliseconds.</param>
    /// <param name="step">
    /// How far simulated time moves per step, in whole milliseconds; by default the smallest
    /// interval of the listeners at <paramref name="pos"/>, or 50 ms when there are only callbacks.
    /// </param>
    /// <param name="ct">Checked between steps.</param>
    /// <remarks>
    /// <para>
    /// Simulated time starts now and moves in steps. Each step, a listener runs once if at least
    /// its interval has passed since it last ran, with <c>dt</c> the simulated time since then;
    /// the game itself waits for strictly more than the interval. Listeners run in order of
    /// interval, then id, across both kinds and both managers; then due callbacks run, by due
    /// time, each taken out before it runs.
    /// </para>
    /// <para>
    /// The run starts every listener afresh, from now: the real time it was owed is dropped,
    /// unless the previous fast-forward left it, in which case what that run left over carries on.
    /// Afterwards, every listener and callback is moved back to real time, so the next tick owes
    /// only what was left over and does not run the span again.
    /// </para>
    /// <para>
    /// Only the listeners and callbacks at <paramref name="pos"/> run. <c>world.ElapsedMilliseconds</c>,
    /// the calendar and every other system stay where they were.
    /// </para>
    /// <para>
    /// Handlers may register and unregister listeners and callbacks: new ones at
    /// <paramref name="pos"/> join the run with their initial delay, removed ones are not called
    /// again, and the run stops once nothing is left there. Errors go to a listener's error
    /// handler, as in the game; one with none, or a callback that throws, stops the run with its
    /// exception, and time is still put back.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="pos"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="span"/> is negative, <paramref name="step"/> is under 1 ms or not whole
    /// milliseconds, or the run would take more than a million steps.
    /// </exception>
    /// <exception cref="InvalidOperationException">A handler started a fast-forward inside one.</exception>
    public Task<FastForwardResult> FastForwardAsync(BlockPos pos, TimeSpan span, TimeSpan? step = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentOutOfRangeException.ThrowIfLessThan(span, TimeSpan.Zero);
        if (step is { } s && (s < TimeSpan.FromMilliseconds(1) || s.Ticks % TimeSpan.TicksPerMillisecond != 0))
            throw new ArgumentOutOfRangeException(nameof(step), step, "A step is a whole number of milliseconds, at least 1.");

        BlockPos at = pos.Copy();
        long spanMs = span.Ticks / TimeSpan.TicksPerMillisecond;
        long? stepMs = step is { } given ? given.Ticks / TimeSpan.TicksPerMillisecond : null;
        (FastForwardOutcome outcome, ListenerManager[] which) = _host.RunOnGameThread(() =>
        {
            List<(EventManager Events, ListenerManager Which)> managers = [.. Managers()];
            return (TickFastForward.Run([.. managers.Select(m => m.Events)], _host.Server, at, spanMs, stepMs, ct), managers.Select(m => m.Which).ToArray());
        });
        return Task.FromResult(new FastForwardResult(
            TimeSpan.FromMilliseconds(outcome.SimulatedMs),
            outcome.Steps,
            outcome.Calls.ToDictionary(c => (which[c.Key.Manager], c.Key.Id), c => c.Value),
            outcome.CallbacksRun));
    }

    private List<ServerTickListenerInfo> Listeners(Func<Delegate?, bool> include) => _host.RunOnGameThread(() =>
    {
        ModOwners owners = ModOwners.Of(_host.Server.Api);
        List<ServerTickListenerInfo> listeners = [];
        foreach ((EventManager events, ListenerManager which) in Managers())
        {
            long now = events.InWorldEllapsedMs;
            IEnumerable<GameTickListenerBase> all = ListenerWatermark.Internals.EntityListeners(events).Cast<GameTickListenerBase>()
                .Concat(ListenerWatermark.Internals.BlockListeners(events));
            foreach (GameTickListenerBase listener in all)
            {
                if (listener == null) continue;
                Delegate? handler = TickItems.HandlerOf(listener);
                if (!include(handler)) continue;
                listeners.Add(new ServerTickListenerInfo(listener.ListenerId, listener.Millisecondinterval,
                    listener is GameTickListenerBlock ? ListenerKind.Block : ListenerKind.World, TickItems.PosOf(listener)?.Copy(),
                    ModOwners.Describe(handler), owners.OfDelegate(handler), TickItems.OwnerTypeOf(handler), TickItems.EntityIdOf(handler),
                    now - listener.LastUpdateMilliseconds, which));
            }
        }

        return listeners;
    });

    private IEnumerable<(EventManager Events, ListenerManager Which)> Managers()
    {
        Vintagestory.Server.ServerMain server = _host.Server;
        if (server.EventManager is { } core) yield return (core, ListenerManager.Core);
        if (server.ModEventManager is { } mod && !ReferenceEquals(mod, server.EventManager)) yield return (mod, ListenerManager.Mod);
    }
}
