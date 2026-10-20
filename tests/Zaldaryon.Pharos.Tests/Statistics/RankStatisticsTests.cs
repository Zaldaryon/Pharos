using Xunit;
using Zaldaryon.Pharos.Statistics;

namespace Zaldaryon.Pharos.Tests.Statistics;

public class RankStatisticsTests
{
    [Fact]
    public void Compare_IdenticalSamples_IsNotSignificant()
    {
        RankTest test = RankStatistics.Compare([1, 2, 3, 4, 5], [1, 2, 3, 4, 5]);

        Assert.Equal(1, test.PValue);
        Assert.Equal(0, test.MedianDifference);
        Assert.True(test.Exact);
    }

    [Fact]
    public void Compare_WellSeparatedSamples_UsesTheExactDistribution()
    {
        RankTest test = RankStatistics.Compare([1, 2, 3, 4], [5, 6, 7, 8]);

        // U is 0, and exactly one of the seventy assignments gives it: the p-value is 2/70.
        Assert.Equal(0, test.U);
        Assert.Equal(2.0 / 70, test.PValue, 6);
        Assert.True(test.Exact);
        Assert.Equal(4, test.BaselineCount);
        Assert.Equal(4, test.CandidateCount);
    }

    [Fact]
    public void Compare_FiveADay_IsSignificantAtTheDefaultAlpha()
    {
        // Two of 252 assignments: 2/252 = 0.0079, which is the smallest p five against five can give.
        RankTest test = RankStatistics.Compare([10, 11, 10, 11, 10], [20, 21, 20, 21, 20]);

        Assert.True(test.PValue < 0.05, $"p was {test.PValue}");
        Assert.Equal(10, test.MedianDifference, 6);
    }

    [Fact]
    public void Compare_Ties_StayWithinZeroAndOne()
    {
        RankTest test = RankStatistics.Compare([1, 1, 2, 2, 3], [1, 2, 2, 3, 3]);

        Assert.InRange(test.PValue, 0, 1);
        Assert.True(test.U >= 0);
    }

    [Fact]
    public void Compare_TooManySamplesForTheExactTable_FallsBackToTheApproximation()
    {
        double[] baseline = [.. Enumerable.Range(0, 40).Select(i => 100.0 + i)];
        double[] candidate = [.. Enumerable.Range(0, 40).Select(i => 200.0 + i)];

        RankTest test = RankStatistics.Compare(baseline, candidate);

        Assert.False(test.Exact);
        Assert.True(test.PValue < 0.001, $"p was {test.PValue}");
    }

    [Fact]
    public void Compare_OneSamplePerSide_CannotConclude()
    {
        RankTest test = RankStatistics.Compare([5], [9]);

        Assert.Equal(1, test.PValue);
        Assert.Equal(4, test.MedianDifference);
    }

    [Fact]
    public void Compare_AnEmptySide_CannotConclude()
    {
        RankTest test = RankStatistics.Compare([], [1, 2, 3]);

        Assert.Equal(1, test.PValue);
        Assert.Equal(0, test.BaselineMedian);
        Assert.Equal(2, test.CandidateMedian);
    }

    [Fact]
    public void Compare_BootstrapInterval_IsReproducibleAndCoversTheDifference()
    {
        double[] baseline = [10, 11, 12, 13, 14];
        double[] candidate = [14, 15, 16, 17, 18];

        RankTest first = RankStatistics.Compare(baseline, candidate, seed: 7);
        RankTest second = RankStatistics.Compare(baseline, candidate, seed: 7);

        Assert.Equal(first.ConfidenceLow, second.ConfidenceLow);
        Assert.Equal(first.ConfidenceHigh, second.ConfidenceHigh);
        Assert.True(first.ConfidenceLow <= 4 && 4 <= first.ConfidenceHigh, $"interval was [{first.ConfidenceLow}, {first.ConfidenceHigh}]");
        Assert.True(first.ConfidenceLow < first.ConfidenceHigh);
    }

    [Fact]
    public void Compare_NoBootstrapSamples_LeavesTheIntervalOut()
    {
        RankTest test = RankStatistics.Compare([1, 2, 3], [4, 5, 6], bootstrapSamples: 0);

        Assert.True(double.IsNaN(test.ConfidenceLow));
        Assert.True(double.IsNaN(test.ConfidenceHigh));
    }

    [Fact]
    public void Compare_RejectsAnAlphaOutsideZeroAndOne()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => RankStatistics.Compare([1], [2], alpha: 1.5));
        Assert.Throws<ArgumentOutOfRangeException>(() => RankStatistics.Compare([1], [2], bootstrapSamples: -1));
    }

    [Theory]
    [InlineData(new double[] { 1.0, 2.0, 3.0 }, 2.0)]
    [InlineData(new double[] { 1.0, 2.0, 3.0, 4.0 }, 2.5)]
    [InlineData(new double[] { 7.0 }, 7.0)]
    [InlineData(new double[0], 0.0)]
    public void Median_TakesTheMiddleOrTheAverageOfTheMiddleTwo(double[] samples, double expected) =>
        Assert.Equal(expected, RankStatistics.Median(samples));

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(-0.0, "0")]
    [InlineData(0.001, "0.001")]
    [InlineData(-0.001, "-0.001")]
    [InlineData(12.5, "12.5")]
    [InlineData(250.0, "250")]
    public void Format_KeepsWholeNumbersWholeAndNeverPrintsNegativeZero(double value, string expected) =>
        Assert.Equal(expected, RankStatistics.Format(value));
}
