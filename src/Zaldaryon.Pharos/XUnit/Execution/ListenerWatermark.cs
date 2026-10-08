using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.XUnit.Execution;

/// <summary>
/// What one side's event managers held when a world's baseline was captured, so a rollback can
/// take out what was added since.
/// </summary>
/// <remarks>
/// <para>
/// Tick listeners and delayed callbacks get ids from counters that only grow, so anything with a
/// higher id than the counter held at capture was added later. Event-bus listeners have no id;
/// the ones present at capture are remembered.
/// </para>
/// <para>
/// A rollback removes, before the world is restored:
/// <list type="bullet">
/// <item>tick listeners, delayed callbacks and event-bus listeners added since, whose handler belongs to the test;</item>
/// <item>every block-position delayed callback added since, whoever scheduled it: those carry on world
/// changes the restore undoes, such as water spreading.</item>
/// </list>
/// Listeners the game or mods registered stay: mods clean up their own state on
/// <see cref="RollbackEvents.Restored"/>. Everything runs on the side's own game thread.
/// </para>
/// </remarks>
internal sealed class ListenerWatermark
{
    private readonly (EventManager Manager, long ListenerId, long CallbackId)[] _managers;
    private readonly List<EventBusListener>? _bus;
    private readonly HashSet<EventBusListener> _busBefore;

    private ListenerWatermark(EventManager[] managers, List<EventBusListener>? bus)
    {
        _managers = managers.Select(m => (m, Internals.ListenerId(m), Internals.CallbackId(m))).ToArray();
        _bus = bus;
        _busBefore = new HashSet<EventBusListener>(bus ?? [], ReferenceEqualityComparer.Instance);
    }

    /// <summary>Records what <paramref name="managers"/> and the event bus hold now.</summary>
    public static ListenerWatermark Capture(IEnumerable<EventManager?> managers, List<EventBusListener>? bus) =>
        new(managers.OfType<EventManager>().Distinct().ToArray(), bus);

    /// <summary>
    /// Removes what was added since the capture and is owned by the test, as
    /// <paramref name="ownedByTest"/> judges each handler, and every block-position delayed
    /// callback added since.
    /// </summary>
    /// <returns>How many listeners and callbacks were removed.</returns>
    public int RemoveAddedSince(Func<Delegate, bool> ownedByTest)
    {
        int removed = 0;
        foreach ((EventManager manager, long listenerWatermark, long callbackWatermark) in _managers)
        {
            List<long> listeners = [];
            listeners.AddRange(Internals.EntityListeners(manager).Where(l => l != null && l.ListenerId > listenerWatermark && l.Handler != null && ownedByTest(l.Handler)).Select(l => l.ListenerId));
            listeners.AddRange(Internals.BlockListeners(manager).Where(l => l != null && l.ListenerId > listenerWatermark && Owned(ownedByTest, l.Handler, l.HandlerBare)).Select(l => l.ListenerId));
            foreach (long id in listeners) manager.RemoveGameTickListener(id);

            List<long> callbacks = [];
            callbacks.AddRange(Internals.EntityCallbacks(manager).Values.Where(c => c != null && c.ListenerId > callbackWatermark && c.Handler != null && ownedByTest(c.Handler)).Select(c => c.ListenerId));
            callbacks.AddRange(Internals.BlockCallbacks(manager).Where(c => c != null && c.ListenerId > callbackWatermark).Select(c => c.ListenerId));
            callbacks.AddRange(Internals.SingleBlockCallbacks(manager).Values.Where(c => c.ListenerId > callbackWatermark).Select(c => c.ListenerId));
            foreach (long id in callbacks) manager.RemoveDelayedCallback(id);

            removed += listeners.Count + callbacks.Count;
        }

        if (_bus != null)
        {
            for (int i = _bus.Count - 1; i >= 0; i--)
            {
                EventBusListener listener = _bus[i];
                if (!_busBefore.Contains(listener) && listener.handler != null && ownedByTest(listener.handler))
                {
                    _bus.RemoveAt(i);
                    removed++;
                }
            }
        }

        return removed;
    }

    private static bool Owned(Func<Delegate, bool> ownedByTest, Delegate? handler, Delegate? bare) =>
        (handler != null && ownedByTest(handler)) || (bare != null && ownedByTest(bare));

    /// <summary>
    /// Whether any part of <paramref name="handler"/> was compiled into one of <paramref name="assemblies"/>.
    /// Lambdas and local functions land in compiler-generated types of the class that wrote them.
    /// </summary>
    public static bool DeclaredIn(Delegate handler, IReadOnlySet<System.Reflection.Assembly> assemblies) =>
        handler.GetInvocationList().Any(d =>
            assemblies.Contains(d.Method.Module.Assembly)
            || (d.Target != null && assemblies.Contains(d.Target.GetType().Assembly)));

    /// <summary>The game's fields, read where they are.</summary>
    internal static class Internals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "listenerId")]
        private static extern ref long ListenerIdField(EventManager manager);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "callBackId")]
        private static extern ref long CallbackIdField(EventManager manager);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "GameTickListenersEntity")]
        private static extern ref List<GameTickListener> EntityListenersField(EventManager manager);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "GameTickListenersBlock")]
        private static extern ref List<GameTickListenerBlock> BlockListenersField(EventManager manager);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "DelayedCallbacksEntity")]
        private static extern ref ConcurrentDictionary<long, DelayedCallback> EntityCallbacksField(EventManager manager);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "DelayedCallbacksBlock")]
        private static extern ref List<DelayedCallbackBlock> BlockCallbacksField(EventManager manager);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "SingleDelayedCallbacksBlock")]
        private static extern ref Dictionary<BlockPos, DelayedCallbackBlock> SingleBlockCallbacksField(EventManager manager);

        public static long ListenerId(EventManager manager) => Interlocked.Read(ref ListenerIdField(manager));

        public static long CallbackId(EventManager manager) => Interlocked.Read(ref CallbackIdField(manager));

        public static List<GameTickListener> EntityListeners(EventManager manager) => [.. EntityListenersField(manager)];

        public static List<GameTickListenerBlock> BlockListeners(EventManager manager) => [.. BlockListenersField(manager)];

        public static ConcurrentDictionary<long, DelayedCallback> EntityCallbacks(EventManager manager) => EntityCallbacksField(manager);

        public static List<DelayedCallbackBlock> BlockCallbacks(EventManager manager) => [.. BlockCallbacksField(manager)];

        public static Dictionary<BlockPos, DelayedCallbackBlock> SingleBlockCallbacks(EventManager manager) => new(SingleBlockCallbacksField(manager));
    }
}
