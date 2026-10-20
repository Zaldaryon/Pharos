namespace Zaldaryon.Pharos.Metrics;

/// <summary>A profile that can publish its numbers into the scenario running on the current flow.</summary>
public interface IMetricSource
{
    /// <summary>The profile's readings, ready for <see cref="Metrics.Publish(IMetricSource[])"/>.</summary>
    IReadOnlyList<MetricReading> Metrics();
}
