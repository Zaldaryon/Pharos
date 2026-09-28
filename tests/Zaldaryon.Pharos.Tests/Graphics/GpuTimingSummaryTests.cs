using Zaldaryon.Pharos.Graphics;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Graphics;

public sealed class GpuTimingSummaryTests
{
    [Fact]
    public void SummaryCalculatesPercentilesAndPreservesGpuSampleOrder()
    {
        GpuTimingSummary summary = GpuTimingSummary.Create([4, 1, 3, 2], 5, 1, "partial");

        Assert.Equal("partial", summary.Status);
        Assert.Equal(5, summary.RequestedSamples);
        Assert.Equal(4, summary.CompletedSamples);
        Assert.Equal(1, summary.DroppedSamples);
        Assert.Equal(1, summary.MinMilliseconds);
        Assert.Equal(2.5, summary.P50Milliseconds);
        Assert.Equal(3.85, summary.P95Milliseconds!.Value, 6);
        Assert.Equal(3.97, summary.P99Milliseconds!.Value, 6);
        Assert.Equal(4, summary.MaxMilliseconds);
        Assert.Equal(2.5, summary.MeanMilliseconds);
        Assert.Equal(new[] { 4d, 1d, 3d, 2d }, summary.SamplesMilliseconds);
    }

    [Fact]
    public void EmptyGpuResultsAreExplicitlyUnavailable()
    {
        GpuTimingSummary summary = GpuTimingSummary.Create(Array.Empty<double>(), 12, 12, "partial");

        Assert.Equal(12, summary.DroppedSamples);
        Assert.Null(summary.P50Milliseconds);
        Assert.Empty(summary.SamplesMilliseconds);
    }
}
