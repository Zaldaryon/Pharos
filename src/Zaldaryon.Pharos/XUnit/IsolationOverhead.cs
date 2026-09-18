using System;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Represents the overhead of an isolation operation for diagnostics and benchmarking.
/// </summary>
public readonly struct IsolationOverhead
{
    /// <summary>
    /// The wall-clock duration of the isolation operation.
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// The isolation mode that was applied.
    /// </summary>
    public IsolationMode Mode { get; init; }

    /// <summary>
    /// A zero-overhead sentinel value for SharedClient mode.
    /// </summary>
    public static IsolationOverhead Zero => new() { Duration = TimeSpan.Zero, Mode = IsolationMode.SharedClient };
}
