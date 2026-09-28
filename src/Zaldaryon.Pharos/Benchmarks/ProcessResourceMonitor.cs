using System.Diagnostics;

namespace Zaldaryon.Pharos.Benchmarks;

/// <summary>
/// Samples resource use for the current process, including all engine and worker threads.
/// Callers should keep this outside the timed frame loop; the periodic sampler runs on a timer.
/// </summary>
public sealed class ProcessResourceMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly Process _process;
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly List<ProcessResourceSample> _samples = new();
    private readonly Timer _timer;
    private readonly int _logicalProcessorCount;
    private bool _stopped;
    private string? _error;

    public ProcessResourceMonitor(TimeSpan? sampleInterval = null)
    {
        TimeSpan interval = sampleInterval ?? TimeSpan.FromMilliseconds(250);
        if (interval < TimeSpan.FromMilliseconds(50))
        {
            throw new ArgumentOutOfRangeException(nameof(sampleInterval), interval,
                "Resource sampling interval must be at least 50 ms to limit measurement overhead.");
        }

        _logicalProcessorCount = Math.Max(1, Environment.ProcessorCount);
        _process = Process.GetCurrentProcess();
        CaptureSample();
        _timer = new Timer(_ => CaptureSample(), null, interval, interval);
    }

    /// <summary>Stops sampling and returns a summary. Repeated calls return the same summary.</summary>
    public ProcessResourceSummary Stop()
    {
        lock (_gate)
        {
            if (!_stopped)
            {
                _stopped = true;
                _timer.Dispose();
                CaptureSampleLocked();
                _elapsed.Stop();
            }

            return ProcessResourceSummary.FromSamples(_samples, _logicalProcessorCount, _error);
        }
    }

    public void Dispose()
    {
        _ = Stop();
        _process.Dispose();
    }

    private void CaptureSample()
    {
        lock (_gate)
        {
            if (!_stopped) CaptureSampleLocked();
        }
    }

    private void CaptureSampleLocked()
    {
        try
        {
            _process.Refresh();
            _samples.Add(new ProcessResourceSample(
                _elapsed.Elapsed.TotalMilliseconds,
                _process.TotalProcessorTime.TotalMilliseconds,
                _process.WorkingSet64,
                _process.PrivateMemorySize64,
                _process.VirtualMemorySize64,
                _process.Threads.Count,
                GC.GetTotalAllocatedBytes(precise: false),
                GC.GetTotalMemory(forceFullCollection: false),
                GC.CollectionCount(0),
                GC.CollectionCount(1),
                GC.CollectionCount(2)));
        }
        catch (Exception exception) when (exception is InvalidOperationException or
                                          System.ComponentModel.Win32Exception or
                                          ObjectDisposedException)
        {
            _error ??= exception.GetType().Name + ": " + exception.Message;
        }
    }
}

public sealed record ProcessResourceSample(
    double ElapsedMilliseconds,
    double ProcessCpuMilliseconds,
    long WorkingSetBytes,
    long PrivateBytes,
    long VirtualBytes,
    int ThreadCount,
    long ManagedAllocatedBytes,
    long ManagedHeapBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

/// <summary>
/// Process-local resource summary. GPU counters and child-process usage are intentionally not inferred.
/// </summary>
public sealed record ProcessResourceSummary(
    int SampleCount,
    int LogicalProcessorCount,
    double WallElapsedMilliseconds,
    double ProcessCpuMilliseconds,
    double CpuPercentOfOneLogicalProcessor,
    double CpuPercentOfMachine,
    long WorkingSetStartBytes,
    long WorkingSetPeakBytes,
    long WorkingSetEndBytes,
    long PrivateBytesStart,
    long PrivateBytesPeak,
    long PrivateBytesEnd,
    long VirtualBytesStart,
    long VirtualBytesPeak,
    long VirtualBytesEnd,
    long ManagedAllocatedBytes,
    long ManagedHeapStartBytes,
    long ManagedHeapPeakBytes,
    long ManagedHeapEndBytes,
    int PeakThreadCount,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    string? SamplingError)
{
    public static ProcessResourceSummary FromSamples(
        IReadOnlyList<ProcessResourceSample> samples,
        int logicalProcessorCount,
        string? samplingError = null)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (logicalProcessorCount <= 0) throw new ArgumentOutOfRangeException(nameof(logicalProcessorCount));
        if (samples.Count == 0)
        {
            return new ProcessResourceSummary(0, logicalProcessorCount, 0, 0, 0, 0,
                0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, samplingError);
        }

        ProcessResourceSample first = samples[0];
        ProcessResourceSample last = samples[^1];
        double wallMs = Math.Max(0, last.ElapsedMilliseconds - first.ElapsedMilliseconds);
        double cpuMs = Math.Max(0, last.ProcessCpuMilliseconds - first.ProcessCpuMilliseconds);
        double cpuOneCore = wallMs > 0 ? cpuMs / wallMs * 100 : 0;
        return new ProcessResourceSummary(
            samples.Count,
            logicalProcessorCount,
            wallMs,
            cpuMs,
            cpuOneCore,
            cpuOneCore / logicalProcessorCount,
            first.WorkingSetBytes,
            samples.Max(sample => sample.WorkingSetBytes),
            last.WorkingSetBytes,
            first.PrivateBytes,
            samples.Max(sample => sample.PrivateBytes),
            last.PrivateBytes,
            first.VirtualBytes,
            samples.Max(sample => sample.VirtualBytes),
            last.VirtualBytes,
            Math.Max(0, last.ManagedAllocatedBytes - first.ManagedAllocatedBytes),
            first.ManagedHeapBytes,
            samples.Max(sample => sample.ManagedHeapBytes),
            last.ManagedHeapBytes,
            samples.Max(sample => sample.ThreadCount),
            Math.Max(0, last.Gen0Collections - first.Gen0Collections),
            Math.Max(0, last.Gen1Collections - first.Gen1Collections),
            Math.Max(0, last.Gen2Collections - first.Gen2Collections),
            samplingError);
    }
}
