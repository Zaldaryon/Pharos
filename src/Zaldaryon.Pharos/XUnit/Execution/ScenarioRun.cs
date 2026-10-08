using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.XUnit.Execution;

/// <summary>
/// The state the scenario pipeline keeps for one test of a scenario class: shared by the
/// scenario base classes.
/// </summary>
internal sealed class ScenarioRun
{
    /// <summary>
    /// Whether the pipeline checks the logged errors before teardown, so <c>DisposeAsync</c> must
    /// not check them again. False for a test run by a plain <c>[Fact]</c>.
    /// </summary>
    public bool PipelineChecksLoggedErrors { get; private set; }

    /// <summary>Whether the body ran out of time; its host is not reused.</summary>
    public bool TimedOut { get; private set; }

    /// <summary>
    /// Whether the body kept running after it was aborted: its host must be left alone, neither
    /// disposed nor reused, and no other scenario may boot in this process.
    /// </summary>
    public bool Abandoned { get; private set; }

    /// <summary>The client's frame count when the body started.</summary>
    public long? FramesAtStart { get; private set; }

    /// <summary>The server's tick count when the body started.</summary>
    public long? TicksAtStart { get; private set; }

    /// <summary>Records the start of the body.</summary>
    public void BodyStarting(long? frames, long? ticks)
    {
        PipelineChecksLoggedErrors = true;
        FramesAtStart = frames;
        TicksAtStart = ticks;
    }

    /// <summary>
    /// Records that the scenario failed before its body ran: the teardown that follows must not
    /// fail on the errors the failed setup logged, which would replace the setup's own failure.
    /// </summary>
    public void SetupFailing() => PipelineChecksLoggedErrors = true;

    /// <summary>Records a timeout, and gives up the process when the body did not stop.</summary>
    public void BodyTimedOut(bool stillRunning)
    {
        TimedOut = true;
        if (!stillRunning) return;

        Abandoned = true;
        ScenarioHostPool.Poison(ScenarioTestInfo.Current?.DisplayName ?? "a scenario");
    }

    /// <summary>How far a counter moved since the body started.</summary>
    public static long? Since(long? start, long? now) => start is { } s && now is { } n ? n - s : null;

    /// <summary>
    /// Saves the artifacts of a scenario that failed before its body ran, and returns the
    /// exception to throw: <paramref name="error"/> with the artifacts' summary, or as it is when
    /// no scenario pipeline runs the test.
    /// </summary>
    public static Exception SetupFailed(Exception error, Func<Exception, ScenarioTestInfo, ScenarioFailure> describe)
    {
        if (error is ScenarioFailedException || ScenarioTestInfo.Current is not { } test) return error;

        try
        {
            FailureReport report = FailureArtifactWriter.Write(describe(error, test));
            return new ScenarioFailedException(error, "The scenario failed before its test body ran. " + report.Summary());
        }
        catch (Exception ex)
        {
            return new ScenarioFailedException(error, $"Pharos could not save the failure artifacts: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
