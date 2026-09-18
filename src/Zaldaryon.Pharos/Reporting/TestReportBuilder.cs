using System.Diagnostics;

namespace Zaldaryon.Pharos.Reporting;

/// <summary>
/// Fluent builder for constructing TestReport instances.
/// Accumulates frame metrics and test results, then builds an immutable report.
/// </summary>
public sealed class TestReportBuilder
{
    private readonly string _title;
    private readonly List<FrameMetricsRecord> _frames = [];
    private readonly List<string> _passedTests = [];
    private readonly List<(string TestName, string Reason)> _failedTests = [];
    private readonly Stopwatch _stopwatch = new();
    private long? _overrideDurationMs;

    /// <summary>
    /// Creates a new builder with the specified report title.
    /// </summary>
    /// <param name="title">Report title (typically the test or scenario name).</param>
    public TestReportBuilder(string title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        _title = title;
        _stopwatch.Start();
    }

    /// <summary>
    /// Gets the current number of frames added to the builder.
    /// </summary>
    public int FrameCount => _frames.Count;

    /// <summary>
    /// Gets the current number of passed tests.
    /// </summary>
    public int PassCount => _passedTests.Count;

    /// <summary>
    /// Gets the current number of failed tests.
    /// </summary>
    public int FailCount => _failedTests.Count;

    /// <summary>
    /// Adds a frame metrics record to the report.
    /// </summary>
    /// <param name="frame">Frame metrics to add.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public TestReportBuilder AddFrame(FrameMetricsRecord frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        _frames.Add(frame);
        return this;
    }

    /// <summary>
    /// Adds a frame with the specified metrics.
    /// </summary>
    /// <returns>This builder for fluent chaining.</returns>
    public TestReportBuilder AddFrame(
        int frameIndex,
        float frameTimeMs,
        int drawCalls = 0,
        int indirectDrawCalls = 0,
        int culledChunks = 0,
        int visibleChunks = 0,
        long memoryBytes = 0)
    {
        return AddFrame(new FrameMetricsRecord
        {
            FrameIndex = frameIndex,
            FrameTimeMs = frameTimeMs,
            DrawCalls = drawCalls,
            IndirectDrawCalls = indirectDrawCalls,
            CulledChunks = culledChunks,
            VisibleChunks = visibleChunks,
            MemoryBytes = memoryBytes,
        });
    }

    /// <summary>
    /// Adds a range of frame metrics records to the report.
    /// </summary>
    /// <param name="frames">Frames to add.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public TestReportBuilder AddFrames(IEnumerable<FrameMetricsRecord> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        _frames.AddRange(frames);
        return this;
    }

    /// <summary>
    /// Records a passing test result.
    /// </summary>
    /// <param name="testName">Name of the passing test.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public TestReportBuilder AddPassResult(string testName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testName);
        _passedTests.Add(testName);
        return this;
    }

    /// <summary>
    /// Records a failing test result.
    /// </summary>
    /// <param name="testName">Name of the failing test.</param>
    /// <param name="reason">Reason for the failure.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public TestReportBuilder AddFailResult(string testName, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testName);
        _failedTests.Add((testName, reason ?? ""));
        return this;
    }

    /// <summary>
    /// Sets an explicit duration instead of using the elapsed time since construction.
    /// </summary>
    /// <param name="durationMs">Duration in milliseconds.</param>
    /// <returns>This builder for fluent chaining.</returns>
    public TestReportBuilder WithDuration(long durationMs)
    {
        _overrideDurationMs = durationMs;
        return this;
    }

    /// <summary>
    /// Builds the immutable TestReport.
    /// </summary>
    /// <returns>A new TestReport instance.</returns>
    public TestReport Build()
    {
        _stopwatch.Stop();

        return new TestReport
        {
            Title = _title,
            Frames = _frames.AsReadOnly(),
            PassCount = _passedTests.Count,
            FailCount = _failedTests.Count,
            DurationMs = _overrideDurationMs ?? _stopwatch.ElapsedMilliseconds,
            PassedTests = _passedTests.AsReadOnly(),
            FailedTests = _failedTests.AsReadOnly(),
        };
    }

    /// <summary>
    /// Creates a new builder with the specified title.
    /// </summary>
    /// <param name="title">Report title.</param>
    /// <returns>A new builder instance.</returns>
    public static TestReportBuilder Create(string title) => new(title);
}
