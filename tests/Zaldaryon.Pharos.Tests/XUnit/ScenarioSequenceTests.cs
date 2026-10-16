using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

public sealed class ScenarioSequenceTests
{
    [Fact]
    public async Task SucceedWhenAsync_RetriesAssertionOnEachTick()
    {
        int ticks = 0;
        await ScenarioTickWait.SucceedWhenAsync(() => Assert.True(ticks >= 3), 4,
            (count, _) => { ticks += count; return Task.CompletedTask; }, CancellationToken.None);
        Assert.Equal(3, ticks);
    }

    [Fact]
    public async Task SucceedWhenAsync_ReportsLastAssertionAndBudget()
    {
        TimeoutException error = await Assert.ThrowsAsync<TimeoutException>(() =>
            ScenarioTickWait.SucceedWhenAsync(() => Assert.Fail("lamp stayed dark"), 2,
                (_, _) => Task.CompletedTask, CancellationToken.None));
        Assert.Contains("2 ticks or frames", error.Message);
        Assert.Contains("lamp stayed dark", error.Message);
    }

    [Fact]
    public async Task SucceedWhenAsync_DoesNotRetryProgrammingErrors()
    {
        int advances = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ScenarioTickWait.SucceedWhenAsync(() => throw new InvalidOperationException("bug"), 2,
                (_, _) => { advances++; return Task.CompletedTask; }, CancellationToken.None));
        Assert.Equal(0, advances);
    }

    [Fact]
    public async Task SucceedWhenAsync_RetriesPharosAssertions()
    {
        int attempts = 0;
        await ScenarioTickWait.SucceedWhenAsync(() =>
        {
            attempts++;
            if (attempts < 2) throw new PharosAssertException("not ready");
        }, 2, (_, _) => Task.CompletedTask, CancellationToken.None);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public async Task SucceedWhenAsync_PreservesCancellationWithoutAdvancing()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        int advances = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ScenarioTickWait.SucceedWhenAsync(
            () => Assert.Fail("must not poll after cancellation"), 2,
            (_, _) => { advances++; return Task.CompletedTask; }, cts.Token));
        Assert.Equal(0, advances);
    }

    [Fact]
    public async Task FailIf_IsCheckedDuringLaterIdleAndStopsImmediately()
    {
        int ticks = 0;
        ScenarioSequence sequence = new ScenarioSequence((count, _) => { ticks += count; return Task.CompletedTask; });
        sequence.FailIf(() => Assert.True(ticks >= 2, "tick threshold reached")).Idle(5);

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => sequence.RunAsync(5));
        Assert.Contains("FailIf assertion passed", error.Message);
        Assert.Equal(2, ticks);
    }

    [Fact]
    public async Task Idle_RejectsAnAdvancePastTheRemainingBudget()
    {
        int ticks = 0;
        ScenarioSequence sequence = new ScenarioSequence((count, _) => { ticks += count; return Task.CompletedTask; }).Idle(3);
        await Assert.ThrowsAsync<TimeoutException>(() => sequence.RunAsync(2));
        Assert.Equal(0, ticks);
    }

    [Fact]
    public async Task MultipleWaitUntilSteps_SucceedAtTheExactTotalBudget()
    {
        int ticks = 0;
        ScenarioSequence sequence = new ScenarioSequence((count, _) => { ticks += count; return Task.CompletedTask; })
            .WaitUntil(() => Assert.True(ticks >= 2))
            .WaitUntil(() => Assert.True(ticks >= 4));
        await sequence.RunAsync(4);
        Assert.Equal(4, ticks);
    }

    [Fact]
    public async Task WaitUntil_PreservesCancellationDuringClockAdvance()
    {
        using CancellationTokenSource cts = new();
        int ticks = 0;
        ScenarioSequence sequence = new ScenarioSequence((count, _) =>
        {
            ticks += count;
            cts.Cancel();
            return Task.CompletedTask;
        }).WaitUntil(() => Assert.True(ticks >= 2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sequence.RunAsync(3, cts.Token));
        Assert.Equal(1, ticks);
    }

    [Fact]
    public async Task RunAsync_RejectsRunningASequenceTwice()
    {
        ScenarioSequence sequence = new ScenarioSequence((_, _) => Task.CompletedTask).Idle(1);
        await sequence.RunAsync(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sequence.RunAsync(1));
    }

    [Fact]
    public async Task PharosAssertSkip_ProducesAnXunitSkippedResult()
    {
        string? old = Environment.GetEnvironmentVariable("PHAROS_RUN_EXPLICIT");
        try
        {
            Environment.SetEnvironmentVariable("PHAROS_RUN_EXPLICIT", "1");
            ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(ScenarioSkipRunnerFixtures), nameof(ScenarioSkipRunnerFixtures.DynamicSkip));
            Assert.Empty(result.Failed);
            Assert.Single(result.Skipped);
            Assert.Contains("mod absent", Assert.Single(result.Skipped).Reason);
            Assert.Equal(1, Assert.Single(result.Summaries).Skipped);
            Assert.Equal(1, Assert.Single(result.Summaries).Total);
            ITestCaseFinished caseFinished = Assert.Single(result.CasesFinished);
            Assert.Equal(1, caseFinished.TestsSkipped);
            Assert.Equal(1, caseFinished.TestsRun);
        }
        finally { Environment.SetEnvironmentVariable("PHAROS_RUN_EXPLICIT", old); }
    }

    [Fact]
    public async Task PharosAssertSkip_TheoryRowsAreSkippedAndSummariesAgree()
    {
        string? old = Environment.GetEnvironmentVariable("PHAROS_RUN_EXPLICIT");
        try
        {
            Environment.SetEnvironmentVariable("PHAROS_RUN_EXPLICIT", "1");
            ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(ScenarioSkipRunnerFixtures), nameof(ScenarioSkipRunnerFixtures.DynamicSkipRows));
            Assert.Empty(result.Failed);
            Assert.Equal(2, result.Skipped.Count);
            Assert.Equal(2, result.Summaries.Sum(summary => summary.Skipped));
            Assert.Equal(2, result.Summaries.Sum(summary => summary.Total));
            Assert.Equal(2, result.CasesFinished.Sum(caseFinished => caseFinished.TestsSkipped));
            Assert.Equal(2, result.CasesFinished.Sum(caseFinished => caseFinished.TestsRun));
        }
        finally { Environment.SetEnvironmentVariable("PHAROS_RUN_EXPLICIT", old); }
    }

    [Fact]
    public async Task TeardownFailureAfterSkip_IsReportedAsFailure()
    {
        string? old = Environment.GetEnvironmentVariable("PHAROS_RUN_EXPLICIT");
        try
        {
            Environment.SetEnvironmentVariable("PHAROS_RUN_EXPLICIT", "1");
            ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(ScenarioSkipRunnerFixtures.TeardownFailureFixture, "Skip");
            Assert.Empty(result.Skipped);
            Assert.Single(result.Failed);
            Assert.Contains("teardown failed", result.FailureMessage);
            Assert.Equal(1, Assert.Single(result.Summaries).Failed);
        }
        finally { Environment.SetEnvironmentVariable("PHAROS_RUN_EXPLICIT", old); }
    }

    [Fact]
    public async Task ExplicitScenario_IsSkippedUnlessEnabled()
    {
        string? old = Environment.GetEnvironmentVariable("PHAROS_RUN_EXPLICIT");
        try
        {
            Environment.SetEnvironmentVariable("PHAROS_RUN_EXPLICIT", null);
            ScenarioRunnerHarness.RunResult skipped = await ScenarioRunnerHarness.RunAsync(typeof(ScenarioSkipRunnerFixtures), nameof(ScenarioSkipRunnerFixtures.ExplicitPasses));
            Assert.Single(skipped.Skipped);

            Environment.SetEnvironmentVariable("PHAROS_RUN_EXPLICIT", "1");
            ScenarioRunnerHarness.RunResult passed = await ScenarioRunnerHarness.RunAsync(typeof(ScenarioSkipRunnerFixtures), nameof(ScenarioSkipRunnerFixtures.ExplicitPasses));
            Assert.Single(passed.Passed);
        }
        finally { Environment.SetEnvironmentVariable("PHAROS_RUN_EXPLICIT", old); }
    }
}
