using Zaldaryon.Pharos.Benchmarks;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Benchmarks;

public sealed class ProcessResourceSummaryTests
{
    [Fact]
    public void FromSamplesCalculatesCpuMemoryAndGcDeltas()
    {
        ProcessResourceSample[] samples =
        [
            new(0, 10, 100, 80, 200, 4, 1000, 500, 1, 0, 0),
            new(1000, 510, 150, 130, 250, 7, 1800, 900, 3, 1, 0)
        ];

        ProcessResourceSummary summary = ProcessResourceSummary.FromSamples(samples, logicalProcessorCount: 4);

        Assert.Equal(2, summary.SampleCount);
        Assert.Equal(1000, summary.WallElapsedMilliseconds);
        Assert.Equal(500, summary.ProcessCpuMilliseconds);
        Assert.Equal(50, summary.CpuPercentOfOneLogicalProcessor);
        Assert.Equal(12.5, summary.CpuPercentOfMachine);
        Assert.Equal(150, summary.WorkingSetPeakBytes);
        Assert.Equal(130, summary.PrivateBytesPeak);
        Assert.Equal(800, summary.ManagedAllocatedBytes);
        Assert.Equal(900, summary.ManagedHeapPeakBytes);
        Assert.Equal(7, summary.PeakThreadCount);
        Assert.Equal(2, summary.Gen0Collections);
        Assert.Equal(1, summary.Gen1Collections);
        Assert.Equal(0, summary.Gen2Collections);
    }

    [Fact]
    public void EmptySamplesReturnExplicitZeroSummary()
    {
        ProcessResourceSummary summary = ProcessResourceSummary.FromSamples(Array.Empty<ProcessResourceSample>(), 6, "unavailable");

        Assert.Equal(0, summary.SampleCount);
        Assert.Equal(0, summary.WorkingSetPeakBytes);
        Assert.Equal("unavailable", summary.SamplingError);
    }

    [Fact]
    public void MonitorCapturesTheCurrentProcessAndCanStopIdempotently()
    {
        using var monitor = new ProcessResourceMonitor(TimeSpan.FromMilliseconds(50));

        ProcessResourceSummary first = monitor.Stop();
        ProcessResourceSummary second = monitor.Stop();

        Assert.True(first.SampleCount >= 2);
        Assert.True(first.WorkingSetPeakBytes > 0);
        Assert.Equal(first, second);
    }

    [Fact]
    public void MonitorRejectsIntervalsThatWouldAddExcessiveOverhead()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ProcessResourceMonitor(TimeSpan.FromMilliseconds(10)));
    }
}
