using System.Globalization;
using Zaldaryon.Pharos.Benchmarks;

namespace Zaldaryon.Pharos.Performance;

/// <summary>Which number of a <see cref="FrameMeasurement"/> a baseline holds.</summary>
public enum FrameStat
{
    /// <summary>The median frame's work, in milliseconds.</summary>
    Median,

    /// <summary>The 95th percentile frame's work, in milliseconds.</summary>
    P95,

    /// <summary>The average frame's work, in milliseconds.</summary>
    Mean,

    /// <summary>Bytes the client thread allocated per frame: close to the same on every machine.</summary>
    AllocatedBytesPerFrame,
}

/// <summary>The outcome of checking a measurement against its baseline.</summary>
/// <param name="Key">The baseline's key.</param>
/// <param name="Stat">What was compared.</param>
/// <param name="Measured">The measured value.</param>
/// <param name="Baseline">The baseline, or null when the file has none.</param>
/// <param name="Limit">The most the value may be: the baseline times one plus the tolerance.</param>
/// <param name="Updated">The measured value was written as the new baseline.</param>
public sealed record FrameBaselineResult(string Key, FrameStat Stat, double Measured, double? Baseline, double? Limit, bool Updated)
{
    /// <summary>Whether the value is within its limit, or the baseline was written.</summary>
    public bool Passed => Updated || (Limit is { } limit && Measured <= limit);

    /// <inheritdoc/>
    public override string ToString() => Updated
        ? string.Create(CultureInfo.InvariantCulture, $"{Key}: wrote {Measured:0.###} as the {Stat} baseline")
        : Baseline is null
            ? string.Create(CultureInfo.InvariantCulture, $"{Key}: no {Stat} baseline; measured {Measured:0.###}. Run with {FrameBaseline.UpdateVariable}=1 to write one.")
            : string.Create(CultureInfo.InvariantCulture, $"{Key}: {Stat} {Measured:0.###} against a baseline of {Baseline:0.###} (limit {Limit:0.###}): {(Passed ? "within" : "over")}");
}

/// <summary>
/// Checks a <see cref="FrameMeasurement"/> against a baseline in a checked-in baselines file, the
/// one <c>pharos benchmark</c> uses. See "Frame measurements" in <c>docs/inspection-api.md</c>.
/// </summary>
/// <remarks>
/// Times differ from machine to machine, so a time baseline needs a generous tolerance and a
/// machine like the one that wrote it; allocations per frame hardly differ. Comparing two
/// measurements in the same run needs no baseline at all.
/// </remarks>
public static class FrameBaseline
{
    /// <summary>Set to <c>1</c> to write measured values as the new baselines instead of checking them.</summary>
    public const string UpdateVariable = "PHAROS_UPDATE_BASELINES";

    private static readonly object s_lock = new();

    /// <summary>
    /// Compares <paramref name="stat"/> of <paramref name="measurement"/> with the baseline under
    /// <paramref name="key"/> in <paramref name="baselinesFile"/>, or writes it there when
    /// <c>PHAROS_UPDATE_BASELINES=1</c>. Other keys in the file are kept.
    /// </summary>
    public static FrameBaselineResult Check(FrameMeasurement measurement, string baselinesFile, string key, FrameStat stat = FrameStat.Median, double tolerance = 0.25)
    {
        ArgumentNullException.ThrowIfNull(measurement);
        ArgumentException.ThrowIfNullOrWhiteSpace(baselinesFile);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);
        double measured = Value(measurement, stat);

        lock (s_lock)
        {
            using FileStream gate = Lock(baselinesFile);
            if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
            {
                // Read and write together, so tests updating one file keep each other's keys. A
                // file that does not read throws rather than being replaced by this one key.
                Dictionary<string, float> baselines = File.Exists(baselinesFile) ? BaselinesFile.ReadBaselines(baselinesFile) : [];
                baselines[key] = (float)measured;
                BaselinesFile.WriteBaselines(baselinesFile, baselines);
                return new FrameBaselineResult(key, stat, measured, measured, measured, Updated: true);
            }

            Dictionary<string, float>? existing = File.Exists(baselinesFile) ? BaselinesFile.ReadBaselines(baselinesFile) : null;
            return existing != null && existing.TryGetValue(key, out float baseline)
                ? new FrameBaselineResult(key, stat, measured, baseline, baseline * (1 + tolerance), Updated: false)
                : new FrameBaselineResult(key, stat, measured, null, null, Updated: false);
        }
    }

    // A lock other processes see: pharos run's workers may check or update one file at once.
    private static FileStream Lock(string baselinesFile)
    {
        string path = Path.GetFullPath(baselinesFile) + ".lock";
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (waited.Elapsed < TimeSpan.FromMinutes(2))
            {
                Thread.Sleep(50);
            }
        }
    }

    /// <summary>As <see cref="Check"/>, and throws when the value is over its limit or has no baseline.</summary>
    /// <exception cref="FrameBudgetExceededException">The value is over its limit, or there is no baseline.</exception>
    public static FrameBaselineResult Assert(FrameMeasurement measurement, string baselinesFile, string key, FrameStat stat = FrameStat.Median, double tolerance = 0.25)
    {
        FrameBaselineResult result = Check(measurement, baselinesFile, key, stat, tolerance);
        return result.Passed ? result : throw new FrameBudgetExceededException(result);
    }

    /// <summary>The number <paramref name="stat"/> picks from <paramref name="measurement"/>.</summary>
    public static double Value(FrameMeasurement measurement, FrameStat stat) => stat switch
    {
        FrameStat.Median => measurement.Median.TotalMilliseconds,
        FrameStat.P95 => measurement.P95.TotalMilliseconds,
        FrameStat.Mean => measurement.Mean.TotalMilliseconds,
        FrameStat.AllocatedBytesPerFrame => measurement.AllocatedBytesPerFrame,
        _ => throw new ArgumentOutOfRangeException(nameof(stat), stat, null),
    };
}

/// <summary>A frame measurement went over its baseline.</summary>
public sealed class FrameBudgetExceededException(FrameBaselineResult result) : Exception(result.ToString())
{
    /// <summary>The check that failed.</summary>
    public FrameBaselineResult Result { get; } = result;
}
