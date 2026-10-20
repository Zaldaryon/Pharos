using System.Globalization;

namespace Zaldaryon.Pharos.Statistics;

/// <summary>The outcome of comparing one metric's samples from a baseline with a candidate.</summary>
/// <param name="U">The Mann-Whitney U statistic, the smaller of the two rank sums.</param>
/// <param name="PValue">The two-sided p-value: the chance of a U this far from the middle when nothing changed.</param>
/// <param name="BaselineMedian">The baseline's median.</param>
/// <param name="CandidateMedian">The candidate's median.</param>
/// <param name="MedianDifference">The candidate's median minus the baseline's.</param>
/// <param name="ConfidenceLow">The low end of the bootstrap confidence interval for the median difference.</param>
/// <param name="ConfidenceHigh">The high end of that interval.</param>
/// <param name="BaselineCount">How many baseline samples the test used.</param>
/// <param name="CandidateCount">How many candidate samples the test used.</param>
/// <param name="Exact">Whether the p-value came from the exact permutation distribution rather than the normal approximation.</param>
public sealed record RankTest(
    double U,
    double PValue,
    double BaselineMedian,
    double CandidateMedian,
    double MedianDifference,
    double ConfidenceLow,
    double ConfidenceHigh,
    int BaselineCount,
    int CandidateCount,
    bool Exact);

/// <summary>
/// Rank tests for metric samples. Frame and tick times are skewed with long tails, so a rank test
/// and a bootstrap interval hold up better than a t-test at the five to ten samples a comparison
/// usually has.
/// </summary>
/// <remarks>
/// The p-value is the exact permutation distribution of the rank sum (which is the Mann-Whitney U
/// distribution with ties averaged) while the sample sizes keep the dynamic programming small, and
/// the normal approximation with a tie correction above that. The interval is a percentile
/// bootstrap of the median difference, seeded so the same samples give the same interval.
/// </remarks>
public static class RankStatistics
{
    /// <summary>The largest total sample count the exact p-value is computed for.</summary>
    public const int ExactLimit = 40;

    /// <summary>The default number of bootstrap resamples behind the confidence interval.</summary>
    public const int DefaultBootstrapSamples = 2000;

    /// <summary>The default seed, so an interval is reproducible.</summary>
    public const int DefaultSeed = 20261019;

    /// <summary>
    /// Compares two samples. Empty or single-sample sides give a p-value of 1: there is not enough
    /// to say anything, which callers report as inconclusive.
    /// </summary>
    /// <param name="baseline">The samples before the change.</param>
    /// <param name="candidate">The samples after it. Both sides are read, never changed.</param>
    /// <param name="alpha">The significance level the p-value is compared against by callers.</param>
    /// <param name="bootstrapSamples">Resamples behind the interval; 0 skips the interval.</param>
    /// <param name="seed">The bootstrap seed.</param>
    /// <returns>The test result.</returns>
    /// <exception cref="ArgumentNullException">A side is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alpha"/> is not between 0 and 1, or <paramref name="bootstrapSamples"/> is negative.</exception>
    public static RankTest Compare(IReadOnlyList<double> baseline, IReadOnlyList<double> candidate,
        double alpha = 0.05, int bootstrapSamples = DefaultBootstrapSamples, int seed = DefaultSeed)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentOutOfRangeException.ThrowIfLessThan(alpha, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(alpha, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(bootstrapSamples);

        double[] first = [.. baseline];
        double[] second = [.. candidate];
        (double u, double p, bool exact) = UAndP(first, second);
        (double low, double high) = bootstrapSamples == 0
            ? (double.NaN, double.NaN)
            : BootstrapMedianDifference(first, second, alpha, bootstrapSamples, seed);
        double firstMedian = Median(first);
        double secondMedian = Median(second);
        return new RankTest(u, p, firstMedian, secondMedian, secondMedian - firstMedian, low, high,
            first.Length, second.Length, exact);
    }

    /// <summary>The median of the samples, or 0 when there are none.</summary>
    /// <param name="samples">The samples.</param>
    public static double Median(IReadOnlyList<double> samples)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Count == 0) return 0;
        double[] sorted = [.. samples];
        Array.Sort(sorted);
        int middle = sorted.Length / 2;
        return sorted.Length % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2;
    }

    /// <summary>
    /// The percentile bootstrap interval for the difference of medians. The samples are resampled
    /// with replacement <paramref name="bootstrapSamples"/> times and the interval is the alpha/2
    /// and 1 - alpha/2 nearest-rank percentiles of the differences.
    /// </summary>
    /// <param name="baseline">The samples before the change.</param>
    /// <param name="candidate">The samples after it.</param>
    /// <param name="alpha">The significance level.</param>
    /// <param name="bootstrapSamples">How many resamples to draw; above 0.</param>
    /// <param name="seed">The seed of the deterministic random source.</param>
    /// <returns>The low and high ends of the interval.</returns>
    public static (double Low, double High) BootstrapMedianDifference(IReadOnlyList<double> baseline, IReadOnlyList<double> candidate,
        double alpha, int bootstrapSamples, int seed = DefaultSeed)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentOutOfRangeException.ThrowIfLessThan(bootstrapSamples, 1);

        Random random = new(seed);
        double[] differences = new double[bootstrapSamples];
        double[] first = new double[baseline.Count];
        double[] second = new double[candidate.Count];
        for (int sample = 0; sample < bootstrapSamples; sample++)
        {
            for (int i = 0; i < first.Length; i++) first[i] = baseline[random.Next(baseline.Count)];
            for (int i = 0; i < second.Length; i++) second[i] = candidate[random.Next(candidate.Count)];
            differences[sample] = Median(second) - Median(first);
        }
        Array.Sort(differences);
        return (NearestRank(differences, alpha / 2), NearestRank(differences, 1 - alpha / 2));
    }

    /// <summary>The Mann-Whitney U statistic, the smaller rank sum adjusted for the smaller group.</summary>
    /// <param name="baseline">The samples before the change.</param>
    /// <param name="candidate">The samples after it.</param>
    /// <returns>U, where the expected value under the null is half the product of the sizes.</returns>
    public static double U(IReadOnlyList<double> baseline, IReadOnlyList<double> candidate)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        return UAndP([.. baseline], [.. candidate]).U;
    }

    // U and the two-sided p-value, exact while the dynamic programming stays small.
    private static (double U, double P, bool Exact) UAndP(double[] first, double[] second)
    {
        int n1 = first.Length, n2 = second.Length;
        if (n1 == 0 || n2 == 0) return (0, 1, true);

        double[] all = [.. first, .. second];
        double[] ranks = Ranks(all);
        // U1 is the smaller group's rank sum less the smallest it could be.
        double rankSumFirst = ranks.Take(n1).Sum();
        double u1 = rankSumFirst - n1 * (n1 + 1) / 2.0;
        double u2 = n1 * (double)n2 - u1;
        double u = Math.Min(u1, u2);

        bool exact = n1 + n2 <= ExactLimit;
        if (!exact) return (u, NormalP(u1, n1, n2, ranks), false);

        // The exact distribution of U1 under the null is the distribution of the rank sum of n1
        // ranks drawn from all n1 + n2. Ties are averaged, so the ranks are doubled to integers
        // and counted as subsets of exactly n1 items.
        long[] counts = ExactRankSumCounts(ranks, n1);
        long total = counts.Sum();
        long target = (long)Math.Round(rankSumFirst * 2);
        long atOrBelow = 0, atOrAbove = 0;
        for (int sum = 0; sum < counts.Length; sum++)
        {
            if (sum <= target) atOrBelow += counts[sum];
            if (sum >= target) atOrAbove += counts[sum];
        }
        double tail = Math.Min(atOrBelow, atOrAbove) / (double)total;
        return (u, Math.Min(1, 2 * tail), true);
    }

    // How many ways exactly n1 of the ranks can be chosen with each (doubled) rank sum.
    private static long[] ExactRankSumCounts(double[] ranks, int n1)
    {
        long[] doubled = [.. ranks.Select(rank => (long)Math.Round(rank * 2))];
        Array.Sort(doubled);
        long maximum = doubled.Skip(Math.Max(0, doubled.Length - n1)).Sum();
        // ways[k, sum]: how many ways k of the ranks seen so far add up to this doubled sum.
        long[] ways = new long[(n1 + 1) * (maximum + 1)];
        ways[0] = 1;
        int processed = 0;
        foreach (long rank in doubled)
        {
            processed++;
            for (int k = Math.Min(processed, n1); k >= 1; k--)
            {
                for (long sum = maximum; sum >= rank; sum--)
                {
                    long below = ways[(k - 1) * (maximum + 1) + sum - rank];
                    if (below != 0) ways[k * (maximum + 1) + sum] += below;
                }
            }
        }
        long[] counts = new long[maximum + 1];
        Array.Copy(ways, n1 * (maximum + 1), counts, 0, counts.Length);
        return counts;
    }

    // Average ranks for ties, 1-based.
    private static double[] Ranks(double[] all)
    {
        double[] ranks = new double[all.Length];
        int[] order = [.. Enumerable.Range(0, all.Length)];
        Array.Sort(order, (a, b) => all[a].CompareTo(all[b]));
        int i = 0;
        while (i < order.Length)
        {
            int j = i;
            while (j + 1 < order.Length && all[order[j + 1]].Equals(all[order[i]])) j++;
            double average = (i + j) / 2.0 + 1;
            for (int k = i; k <= j; k++) ranks[order[k]] = average;
            i = j + 1;
        }
        return ranks;
    }

    // The normal approximation, with the tie correction to the variance.
    private static double NormalP(double u1, int n1, int n2, double[] ranks)
    {
        double n = n1 + n2;
        double mean = n1 * (double)n2 / 2;
        double tieSum = ranks.GroupBy(rank => rank).Sum(group => Math.Pow(group.Count(), 3) - group.Count());
        double variance = n1 * (double)n2 / 12 * ((n + 1) - tieSum / (n * (n - 1)));
        if (variance <= 0) return 1;
        double z = (Math.Abs(u1 - mean) - 0.5) / Math.Sqrt(variance);
        if (z <= 0) return 1;
        return Math.Min(1, 2 * (1 - NormalCdf(z)));
    }

    // Abramowitz and Stegun 26.2.17, accurate to about 1e-7, which is far finer than any decision here.
    private static double NormalCdf(double z)
    {
        double t = 1 / (1 + 0.2316419 * z);
        double density = Math.Exp(-z * z / 2) / Math.Sqrt(2 * Math.PI);
        double tail = density * t * (0.319381530 + t * (-0.356563782 + t * (1.781477937 + t * (-1.821255978 + t * 1.330274429))));
        return 1 - tail;
    }

    // The nearest-rank percentile: P95 of 20 values is the 19th smallest.
    private static double NearestRank(double[] sorted, double percent)
    {
        if (sorted.Length == 0) return 0;
        int rank = (int)Math.Ceiling(Math.Clamp(percent, 0, 1) * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }

    /// <summary>Formats a number for a report: whole numbers stay whole, small values keep three places.</summary>
    /// <param name="value">The number.</param>
    public static string Format(double value)
    {
        // Negative zero prints as "-0", which reads as a change where there is none.
        if (value == 0) return "0";
        return Math.Abs(value) >= 100 || value == Math.Round(value)
            ? value.ToString("0.##", CultureInfo.InvariantCulture)
            : value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
