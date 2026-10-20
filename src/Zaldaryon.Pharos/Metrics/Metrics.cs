using System.Reflection;

namespace Zaldaryon.Pharos.Metrics;

/// <summary>
/// The metric collector of the scenario running on this flow. Custom metrics are recorded through
/// <see cref="Record"/>, <see cref="Count"/> and <see cref="Time"/>; outside a scenario they are
/// no-ops, so mod and library code can record without knowing whether Pharos is running.
/// </summary>
public static class Metrics
{
    private static readonly AsyncLocal<MetricsCollector?> s_current = new();

    /// <summary>The collector of the scenario running on this flow, or null outside a scenario.</summary>
    public static MetricsCollector? Current
    {
        get => s_current.Value;
        internal set => s_current.Value = value;
    }

    /// <summary>Whether a scenario is collecting metrics on this flow.</summary>
    public static bool IsActive => Current != null;

    /// <summary>Records one value into the running scenario's collector.</summary>
    /// <param name="clock">Which clock produced the value; milliseconds default to the stopwatch, everything else to exact.</param>
    public static void Record(string name, double value, MetricUnit unit = MetricUnit.Count,
        MetricDirection direction = MetricDirection.Neutral, MetricClock? clock = null,
        IReadOnlyDictionary<string, string>? tags = null) =>
        Current?.Record(name, value, unit, direction, clock, tags);

    /// <summary>Records a count into the running scenario's collector.</summary>
    public static void Count(string name, long count, MetricDirection direction = MetricDirection.Neutral,
        IReadOnlyDictionary<string, string>? tags = null) =>
        Current?.Count(name, count, direction, tags);

    /// <summary>Records a byte total into the running scenario's collector.</summary>
    public static void Bytes(string name, long bytes, MetricDirection direction = MetricDirection.LowerIsBetter,
        IReadOnlyDictionary<string, string>? tags = null) =>
        Current?.Bytes(name, bytes, direction, tags);

    /// <summary>
    /// Starts a stopwatch that records milliseconds when disposed, or a timer that does nothing
    /// when no scenario is collecting. Its <see cref="MetricTimer.Elapsed"/> still works.
    /// </summary>
    public static MetricTimer Time(string name, MetricDirection direction = MetricDirection.LowerIsBetter,
        IReadOnlyDictionary<string, string>? tags = null) =>
        new(Current ?? NoCollector.Instance, name, direction, tags);

    /// <summary>Publishes a whole profile into the running scenario's collector.</summary>
    public static void Publish(IEnumerable<MetricReading> readings) => Current?.Publish(readings);

    /// <summary>Publishes a collector's series into the running scenario's collector.</summary>
    public static void Publish(MetricsCollector collector) => Current?.Merge(collector);

    /// <summary>Publishes every number of a server profile into the running scenario's collector.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    public static void Publish(Server.ServerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Current?.Publish(profile.Metrics());
    }

    /// <summary>Makes <paramref name="collector"/> current on this flow until the scope is disposed.</summary>
    internal static IDisposable BeginScope(MetricsCollector collector)
    {
        ArgumentNullException.ThrowIfNull(collector);
        MetricsCollector? previous = Current;
        Current = collector;
        return new Scope(previous);
    }

    private sealed class Scope(MetricsCollector? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Current = previous;
        }
    }

    // A collector that discards readings and whose timer only measures: outside a scenario,
    // Metrics.Time still measures the operation but records nothing.
    private sealed class NoCollector : MetricsCollector
    {
        public static readonly NoCollector Instance = new();

        public override void Record(string name, double value, MetricUnit unit = MetricUnit.Count,
            MetricDirection direction = MetricDirection.Neutral, MetricClock? clock = null,
            IReadOnlyDictionary<string, string>? tags = null)
        {
            // Outside a scenario the timer still measures; it records nothing.
        }
    }
}

/// <summary>Marks a scenario class, a test method or an assembly for automatic metric collection.</summary>
/// <remarks>
/// Attributes combine: the method's flags, the class's and the assembly's are ORed. A method without
/// the attribute still records custom metrics and still writes <c>result.json</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method | AttributeTargets.Assembly, AllowMultiple = false, Inherited = true)]
public sealed class CollectMetricsAttribute : Attribute
{
    /// <summary>Creates the attribute with the collectors to run.</summary>
    /// <param name="metrics">The automatic collectors.</param>
    public CollectMetricsAttribute(Metric metrics) => Metrics = metrics;

    /// <summary>The automatic collectors.</summary>
    public Metric Metrics { get; }

    /// <summary>
    /// The collectors requested for <paramref name="testClass"/> and, when known, the running test method.
    /// </summary>
    public static Metric Resolve(Type testClass, System.Reflection.MethodInfo? method)
    {
        ArgumentNullException.ThrowIfNull(testClass);
        Metric metrics = Metric.None;
        foreach (CollectMetricsAttribute attribute in testClass.Assembly.GetCustomAttributes<CollectMetricsAttribute>())
        {
            metrics |= attribute.Metrics;
        }

        foreach (CollectMetricsAttribute attribute in testClass.GetCustomAttributes<CollectMetricsAttribute>(inherit: true))
        {
            metrics |= attribute.Metrics;
        }

        if (method != null)
        {
            foreach (CollectMetricsAttribute attribute in method.GetCustomAttributes<CollectMetricsAttribute>(inherit: true))
            {
                metrics |= attribute.Metrics;
            }
        }

        return metrics;
    }
}
