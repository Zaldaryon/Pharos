using System.Diagnostics;

namespace Zaldaryon.Pharos.Benchmarks;

/// <summary>
/// Abstract base class for performance benchmarks.
/// Handles timing and result creation, allowing subclasses to focus on the workload.
/// </summary>
public abstract class PerformanceBenchmark
{
    /// <summary>
    /// Human-readable name of this benchmark.
    /// </summary>
    public abstract string Name { get; }

    /// <summary>
    /// Description of what this benchmark measures.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Expected baseline execution time in milliseconds.
    /// Used to detect regressions.
    /// </summary>
    public abstract float BaselineMs { get; }

    /// <summary>
    /// Number of warmup iterations to run before measuring.
    /// Default is 3.
    /// </summary>
    public virtual int WarmupIterations => 3;

    /// <summary>
    /// Number of measurement iterations to average.
    /// Default is 5.
    /// </summary>
    public virtual int MeasurementIterations => 5;

    /// <summary>
    /// Regression threshold multiplier. Benchmark fails if elapsed > baseline × threshold.
    /// Default is 1.2 (20% regression tolerance).
    /// </summary>
    public virtual float RegressionThreshold => BenchmarkResult.DefaultThreshold;

    /// <summary>
    /// Tries to load the baseline from an external source (e.g., baselines file).
    /// Override this to provide external baseline loading.
    /// </summary>
    /// <param name="baselines">Dictionary of baseline values from an external source.</param>
    /// <param name="baseline">The loaded baseline value if successful.</param>
    /// <returns>True if a baseline was loaded, false to use the hard-coded default.</returns>
    public virtual bool TryLoadFromFile(IReadOnlyDictionary<string, float>? baselines, out float baseline)
    {
        return BaselinesFile.TryGetBaseline(baselines, Name, out baseline);
    }

    /// <summary>
    /// Runs the benchmark and returns the result.
    /// </summary>
    /// <returns>A BenchmarkResult with timing and pass/fail status.</returns>
    public BenchmarkResult RunBenchmark()
    {
        return RunBenchmark(null);
    }

    /// <summary>
    /// Runs the benchmark with optional external baselines.
    /// </summary>
    /// <param name="fileBaselines">Optional dictionary of baselines loaded from file.</param>
    /// <returns>A BenchmarkResult with timing and pass/fail status.</returns>
    public BenchmarkResult RunBenchmark(IReadOnlyDictionary<string, float>? fileBaselines)
    {
        // Determine baseline source
        float baselineMs;
        bool sourcedFromFile;

        if (TryLoadFromFile(fileBaselines, out float fileBaseline))
        {
            baselineMs = fileBaseline;
            sourcedFromFile = true;
        }
        else
        {
            baselineMs = BaselineMs;
            sourcedFromFile = false;
        }

        // Warmup iterations (discarded)
        for (int i = 0; i < WarmupIterations; i++)
        {
            ExecuteWorkload();
        }

        // Measurement iterations
        var stopwatch = new Stopwatch();
        long[] timings = new long[MeasurementIterations];

        for (int i = 0; i < MeasurementIterations; i++)
        {
            Setup();
            
            stopwatch.Restart();
            ExecuteWorkload();
            stopwatch.Stop();
            
            timings[i] = stopwatch.ElapsedTicks;
            
            Cleanup();
        }

        // Calculate average (excluding outliers for stability)
        Array.Sort(timings);
        
        // Use median-trimmed mean if we have enough samples
        long sumTicks;
        int count;
        
        if (MeasurementIterations >= 5)
        {
            // Exclude lowest and highest
            sumTicks = 0;
            for (int i = 1; i < timings.Length - 1; i++)
            {
                sumTicks += timings[i];
            }
            count = timings.Length - 2;
        }
        else
        {
            sumTicks = timings.Sum();
            count = timings.Length;
        }

        double avgTicks = sumTicks / (double)count;
        float avgMs = (float)(avgTicks / Stopwatch.Frequency * 1000.0);

        return BenchmarkResult.Create(Name, avgMs, baselineMs, RegressionThreshold, sourcedFromFile);
    }

    /// <summary>
    /// Called before each measurement iteration.
    /// Override to set up test data.
    /// </summary>
    protected virtual void Setup() { }

    /// <summary>
    /// Called after each measurement iteration.
    /// Override to clean up resources.
    /// </summary>
    protected virtual void Cleanup() { }

    /// <summary>
    /// The workload to benchmark. Override to implement the actual work.
    /// </summary>
    protected abstract void ExecuteWorkload();
}
