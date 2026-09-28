using System.Diagnostics;
using OpenTK.Graphics.OpenGL4;

namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Captures GPU execution time for a bounded batch of OpenGL actions. Results are read
/// asynchronously; unavailable queries are reported as dropped rather than blocking the GPU.
/// This measures GL command execution, not presentation or whole-device utilization.
/// </summary>
public sealed class GpuTimerQueryCapture : IDisposable
{
    private readonly List<int> _queries = new();
    private readonly HashSet<int> _collectedQueries = new();
    private readonly List<double> _samplesMilliseconds = new();
    private bool _disposed;
    private bool _queryOpen;

    public bool IsSupported { get; }
    public string Status { get; }

    public GpuTimerQueryCapture()
    {
        try
        {
            GL.GetInteger(GetPName.MajorVersion, out int major);
            GL.GetInteger(GetPName.MinorVersion, out int minor);
            IsSupported = major > 3 || major == 3 && minor >= 3;
            Status = IsSupported ? "supported" : $"unsupported OpenGL {major}.{minor}; timer queries require 3.3+";
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or NotSupportedException)
        {
            Status = "unsupported: " + exception.GetType().Name;
        }
    }

    /// <summary>Times one GL action without waiting for its GPU result.</summary>
    public bool TryMeasure(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsSupported) return false;

        int query = GL.GenQuery();
        GL.BeginQuery(QueryTarget.TimeElapsed, query);
        _queryOpen = true;
        try
        {
            action();
        }
        finally
        {
            GL.EndQuery(QueryTarget.TimeElapsed);
            _queryOpen = false;
            _queries.Add(query);
        }

        return true;
    }

    /// <summary>
    /// Polls completed results for at most <paramref name="waitBudget"/>; never reads a
    /// pending GL_QUERY_RESULT because that would stall the CPU until the GPU catches up.
    /// </summary>
    public GpuTimingSummary Complete(TimeSpan? waitBudget = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_queryOpen) throw new InvalidOperationException("Cannot collect GPU timing while a query is active.");
        if (!IsSupported) return GpuTimingSummary.Unsupported(Status);

        TimeSpan budget = waitBudget ?? TimeSpan.FromMilliseconds(250);
        if (budget < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(waitBudget));
        Stopwatch wait = Stopwatch.StartNew();
        var pending = new HashSet<int>(_queries.Where(query => !_collectedQueries.Contains(query)));
        while (pending.Count > 0 && wait.Elapsed < budget)
        {
            foreach (int query in pending.ToArray())
            {
                GL.GetQueryObject(query, GetQueryObjectParam.QueryResultAvailable, out int available);
                if (available == 0) continue;

                GL.GetQueryObject(query, GetQueryObjectParam.QueryResult, out long elapsedNanoseconds);
                _samplesMilliseconds.Add(elapsedNanoseconds / 1_000_000.0);
                _collectedQueries.Add(query);
                pending.Remove(query);
            }

            if (pending.Count > 0) Thread.Sleep(1);
        }

        return GpuTimingSummary.Create(_samplesMilliseconds, _queries.Count,
            pending.Count, pending.Count == 0 ? "complete" : "partial: query results remained pending at timeout");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_queryOpen)
        {
            GL.EndQuery(QueryTarget.TimeElapsed);
            _queryOpen = false;
        }
        if (_queries.Count > 0) GL.DeleteQueries(_queries.Count, _queries.ToArray());
    }
}

public sealed record GpuTimingSummary(
    string Status,
    int RequestedSamples,
    int CompletedSamples,
    int DroppedSamples,
    double? MinMilliseconds,
    double? P50Milliseconds,
    double? P95Milliseconds,
    double? P99Milliseconds,
    double? MaxMilliseconds,
    double? MeanMilliseconds,
    IReadOnlyList<double> SamplesMilliseconds)
{
    public static GpuTimingSummary Unsupported(string status) =>
        new(status, 0, 0, 0, null, null, null, null, null, null, Array.Empty<double>());

    public static GpuTimingSummary Create(
        IReadOnlyList<double> samples,
        int requestedSamples,
        int droppedSamples,
        string status)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (requestedSamples < samples.Count) throw new ArgumentOutOfRangeException(nameof(requestedSamples));

        if (samples.Count == 0)
        {
            return new GpuTimingSummary(status, requestedSamples, 0, droppedSamples,
                null, null, null, null, null, null, Array.Empty<double>());
        }

        double[] sorted = samples.Order().ToArray();
        return new GpuTimingSummary(
            status,
            requestedSamples,
            samples.Count,
            droppedSamples,
            sorted[0],
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99),
            sorted[^1],
            samples.Average(),
            samples.ToArray());
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        double rank = percentile * (sorted.Length - 1);
        int lower = (int)Math.Floor(rank);
        int upper = (int)Math.Ceiling(rank);
        return lower == upper
            ? sorted[lower]
            : sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
    }
}
