using System.Reflection;
using System.Runtime.CompilerServices;

namespace Zaldaryon.Pharos.Metrics;

/// <summary>
/// Forwards <c>Zaldaryon.Pharos.Bridge.PharosMetrics</c> readings from the bridge assembly into
/// the running scenario's collector. The bridge cannot reference the engine, so the subscription
/// is made by reflection and is absent when a test does not load the bridge.
/// </summary>
internal sealed class BridgeMetricSubscription : IDisposable
{
    private static readonly object s_lock = new();
    private static readonly ConditionalWeakTable<MetricsCollector, BridgeMetricSubscription> s_subscriptions = new();
    private static EventInfo? s_recorded;
    private static bool s_looked;

    private readonly MetricsCollector _collector;
    private readonly Delegate _handler;
    private int _disposed;

    private BridgeMetricSubscription(MetricsCollector collector, EventInfo recorded)
    {
        _collector = collector;
        MethodInfo handler = typeof(BridgeMetricSubscription).GetMethod(nameof(OnRecorded),
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        _handler = Delegate.CreateDelegate(recorded.EventHandlerType!, this, handler);
        recorded.AddEventHandler(null, _handler);
    }

    /// <summary>Subscribes <paramref name="collector"/> to the bridge; returns an existing subscription.</summary>
    public static IDisposable Subscribe(MetricsCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);
        lock (s_lock)
        {
            if (s_subscriptions.TryGetValue(collector, out BridgeMetricSubscription? existing)) return existing;
            EventInfo? recorded = FindEvent();
            if (recorded?.EventHandlerType == null) return EmptySubscription.Instance;

            BridgeMetricSubscription subscription = new(collector, recorded);
            s_subscriptions.Add(collector, subscription);
            return subscription;
        }
    }

    private void OnRecorded(string name, double value, string unit, string direction,
        IReadOnlyDictionary<string, string>? tags)
    {
        MetricFormat.TryParseUnit(unit, out MetricUnit parsedUnit);
        MetricFormat.TryParseDirection(direction, out MetricDirection parsedDirection);
        MetricClock clock = parsedUnit == MetricUnit.Milliseconds ? MetricClock.Stopwatch : MetricClock.Exact;
        _collector.Record(name, value, parsedUnit, parsedDirection, clock, tags);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        lock (s_lock)
        {
            s_recorded?.RemoveEventHandler(null, _handler);
            s_subscriptions.Remove(_collector);
        }
    }

    private static EventInfo? FindEvent()
    {
        if (s_looked) return s_recorded;
        s_looked = true;
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType("Zaldaryon.Pharos.Bridge.PharosMetrics", throwOnError: false);
            if (type?.GetEvent("Recorded", BindingFlags.Public | BindingFlags.Static) is { } recorded)
            {
                s_recorded = recorded;
                s_looked = true;
                break;
            }
        }

        return s_recorded;
    }

    private sealed class EmptySubscription : IDisposable
    {
        public static readonly EmptySubscription Instance = new();
        public void Dispose() { }
    }
}
