using System.Collections.Concurrent;
using Xunit;
using Xunit.Sdk;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>
/// The scenario pipeline the scenario attributes run their tests through: the watchdog, the
/// logged-error check and the failure capture, in the order the scenario base classes rely on.
/// Runs sample classes that only record what happens to them, without booting the game.
/// </summary>
[Collection(FailureArtifactCollection.Name)]
public class ScenarioPipelineTests
{
    [Theory]
    [InlineData(typeof(ClientScenarioAttribute), typeof(ScenarioFactDiscoverer))]
    [InlineData(typeof(ServerScenarioAttribute), typeof(ScenarioFactDiscoverer))]
    [InlineData(typeof(ClientServerScenarioAttribute), typeof(ScenarioFactDiscoverer))]
    [InlineData(typeof(ClientTheoryAttribute), typeof(ScenarioTheoryDiscoverer))]
    [InlineData(typeof(ServerTheoryAttribute), typeof(ScenarioTheoryDiscoverer))]
    public void ScenarioAttributes_RunThroughThePipeline(Type attribute, Type discoverer)
    {
        System.Reflection.CustomAttributeData data = Assert.Single(
            attribute.GetCustomAttributesData(), a => a.AttributeType == typeof(XunitTestCaseDiscovererAttribute));

        Assert.Equal(discoverer, Type.GetType($"{data.ConstructorArguments[0].Value}, {data.ConstructorArguments[1].Value}"));
    }

    [Fact]
    public async Task PassingBody_ChecksLoggedErrorsAndCapturesNothing()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.Passes));

        Assert.Single(result.Passed);
        Assert.Equal(["init", "before", "environments", "body", "check", "dispose"], Recorder.Events);
    }

    [Fact]
    public async Task Retry_StopsWhenRequiredSuccessesAreSatisfied()
    {
        Recorder.Reset();
        Recorder.AttemptBodies = 0;
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.RequiredSuccesses));
        Assert.Single(result.Passed);
        Assert.Equal(3, Recorder.AttemptBodies);
        Assert.Equal(3, Recorder.Events.Count(e => e == "init"));
    }

    [Fact]
    public async Task TheoryRows_RetryIndependentlyAndEmitOneFinalResultPerRow()
    {
        Recorder.Reset();
        Recorder.RowAttempts.Clear();
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.RetryRows));
        Assert.Equal(2, result.Passed.Count);
        Assert.Empty(result.Failed);
        Assert.Equal(1, Recorder.RowAttempts[1]);
        Assert.Equal(2, Recorder.RowAttempts[2]);
        Assert.Equal(2, result.CasesFinished.Count);
    }

    [Fact]
    public async Task Retry_RecreatesTheFullScenarioInstanceAndEmitsOneFinalResult()
    {
        Recorder.Reset();
        Recorder.AttemptBodies = 0;
        Recorder.InstanceCount = 0;
        DirectoryInfo temp = Directory.CreateTempSubdirectory("pharos-attempts-");
        string? oldManifestDirectory = Environment.GetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable);
        string? oldArtifactDirectory = Zaldaryon.Pharos.Reporting.FailureArtifactWriter.RootOverride;
        Environment.SetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable, temp.FullName);
        Zaldaryon.Pharos.Reporting.FailureArtifactWriter.RootOverride = Path.Combine(temp.FullName, "artifacts");
        Recorder.CaptureArtifacts = true;

        ScenarioRunnerHarness.RunResult result;
        try
        {
            result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.FailsOnceThenPasses));
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable, oldManifestDirectory);
            Zaldaryon.Pharos.Reporting.FailureArtifactWriter.RootOverride = oldArtifactDirectory;
            Recorder.CaptureArtifacts = false;
        }

        Assert.Single(result.Passed);
        Assert.Empty(result.Failed);
        Assert.Equal(1, result.CasesFinished.Count);
        Assert.Equal(2, Recorder.InstanceCount);
        Assert.Equal(2, Recorder.Events.Count(e => e == "init"));
        Assert.Equal(2, Recorder.Events.Count(e => e == "dispose"));
        Assert.Equal(2, Recorder.Events.Count(e => e == "body"));
        string output = Assert.Single(result.Passed).Output;
        Assert.Contains("attempt 1 output", output);
        Assert.Contains("attempt 2 output", output);
        Assert.Contains("flaky=true", output);
        using System.Text.Json.JsonDocument fragment = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(temp.FullName, "*.json"))));
        Assert.Equal(typeof(Sample).Assembly.FullName, fragment.RootElement.GetProperty("assembly").GetString());
        Assert.Equal("Passed", fragment.RootElement.GetProperty("rawOutcome").GetString());
        Assert.True(fragment.RootElement.GetProperty("flaky").GetBoolean());
        string[] artifacts = fragment.RootElement.GetProperty("attemptHistory")[0].GetProperty("artifactDirectories").EnumerateArray().Select(a => a.GetString()!).ToArray();
        Assert.Single(artifacts);
        Assert.True(Directory.Exists(artifacts[0]));
    }

    [Fact]
    public async Task SkipFollowedByEnvironmentCleanupFailure_IsAFailure()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.SkipThenCleanupFails));

        Assert.Empty(result.Skipped);
        Assert.Single(result.Failed);
        Assert.Contains("skip cleanup failed", result.FailureMessage);
    }

    [Fact]
    public async Task Retry_WorksWithoutScenarioLifecycle()
    {
        SimpleRetry.AttemptsRun = 0;

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(SimpleRetry), nameof(SimpleRetry.FailsOnceThenPasses));

        Assert.Single(result.Passed);
        Assert.Equal(2, SimpleRetry.AttemptsRun);
    }

    [Fact]
    public async Task AttemptManifestWriteFailure_DoesNotChangeTestOutcome()
    {
        string blocker = Path.Combine(Path.GetTempPath(), "pharos-attempt-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "file blocks directory creation");
        string? oldManifestDirectory = Environment.GetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable);
        Environment.SetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable, blocker);
        try
        {
            ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(SimpleRetry), nameof(SimpleRetry.PassesWithoutManifest));
            Assert.Single(result.Passed);
            Assert.Empty(result.Failed);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable, oldManifestDirectory);
            File.Delete(blocker);
        }
    }

    [Fact]
    public async Task Retry_TreatsAggregateBodyExceptionAsRetryable()
    {
        SimpleRetry.AttemptsRun = 0;

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(SimpleRetry), nameof(SimpleRetry.ThrowsAggregateOnceThenPasses));

        Assert.Single(result.Passed);
        Assert.Equal(2, SimpleRetry.AttemptsRun);
    }

    [Theory]
    [InlineData(nameof(DisposeFailure.Passes), "teardown")]
    [InlineData(nameof(DisposeFailure.Fails), "teardown")]
    public async Task DisposeFailure_IsClassifiedAsTeardownInAttemptMetadata(string method, string expectedPhase)
    {
        DirectoryInfo temp = Directory.CreateTempSubdirectory("pharos-teardown-attempt-");
        string? oldManifestDirectory = Environment.GetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable);
        Environment.SetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable, temp.FullName);
        try
        {
            ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(DisposeFailure), method);
            Assert.Single(result.Failed);
            using System.Text.Json.JsonDocument fragment = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(temp.FullName, "*.json"))));
            Assert.Equal(expectedPhase, fragment.RootElement.GetProperty("attemptHistory")[0].GetProperty("phase").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable, oldManifestDirectory);
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task InvalidAttemptPolicyFailsBeforeConstructingTheScenario()
    {
        InvalidPolicy.ConstructorCalls = 0;

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(InvalidPolicy), nameof(InvalidPolicy.NeverRuns));

        Assert.Single(result.Failed);
        Assert.Contains("RequiredSuccesses", result.FailureMessage);
        Assert.Equal(0, InvalidPolicy.ConstructorCalls);
    }

    [Fact]
    public async Task FailingBody_IsCapturedBeforeTeardown_AndTheMessageCarriesTheDetails()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.Fails));

        Assert.Equal(["init", "before", "environments", "body", "capture:failed", "dispose"], Recorder.Events);
        Assert.Equal(typeof(ScenarioFailedException).FullName, result.FailureType);
        Assert.StartsWith("Xunit.Sdk.TrueException: the world is wrong", result.FailureMessage);
        Assert.Contains("captured by the sample", result.FailureMessage);
        Assert.Contains(nameof(Sample.Fails), Assert.Single(result.SingleFailure.StackTraces) ?? "");
    }

    [Fact]
    public async Task LoggedErrors_FailAPassingBody_AndAreCaptured()
    {
        Recorder.Reset();
        Recorder.FailCheck = true;

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.Passes));

        Assert.Equal(["init", "before", "environments", "body", "check", "capture:failed", "dispose"], Recorder.Events);
        Assert.Contains("logged an error", result.FailureMessage);
    }

    [Fact]
    public async Task DynamicSkip_RunsTeardownWithoutCheckingLoggedErrorsOrCapturingFailure()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.Skips));

        Assert.Empty(result.Failed);
        Assert.Contains("mod absent", Assert.Single(result.Skipped).Reason);
        Assert.Equal(["init", "before", "environments", "body", "dispose"], Recorder.Events);
        Assert.Equal(1, Recorder.AttemptBodies);
    }

    [Fact]
    public async Task NullDetails_LeaveTheFailureAsItIs()
    {
        Recorder.Reset();
        Recorder.Details = null;

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.Fails));

        Assert.Equal(typeof(TrueException).FullName, result.FailureType);
    }

    [Fact]
    public async Task TimedOutBody_IsAbortedAtItsNextStep_CapturedAndTornDown()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.StepsForever));

        Assert.Equal(["init", "before", "environments", "body", "timedOut:stopped", "capture:timedOut", "dispose"], Recorder.Events);
        Assert.StartsWith(typeof(TestTimeoutException).FullName + ": ", result.FailureMessage);
        // 200 ms, times PHAROS_TIMEOUT_SCALE where a slow lane sets it.
        Assert.Matches(@"timed out after \d+ milliseconds", result.FailureMessage);
        Assert.True(Recorder.BodyAborted.Task.Wait(TimeSpan.FromSeconds(5)), "The body was not stopped at its next step");
    }

    [Fact]
    public async Task XunitTimeoutOnASyncBody_IsEnforcedByThePipeline_AndTheClassIsStillDisposed()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.StepsSynchronously));

        // xUnit's own timeout would have skipped DisposeAsync, which releases the scenario host.
        Assert.Equal(["init", "before", "environments", "body", "timedOut:stopped", "capture:timedOut", "dispose"], Recorder.Events);
        Assert.StartsWith(typeof(TestTimeoutException).FullName, result.FailureMessage);
    }

    [Fact]
    public async Task BodyThatNeverStepsAgain_IsGivenUp()
    {
        Recorder.Reset();
        TimeSpan grace = ScenarioTimeouts.Grace;
        ScenarioTimeouts.Grace = TimeSpan.FromMilliseconds(200);

        try
        {
            ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.AwaitsForever));

            Assert.Equal(["init", "before", "environments", "body", "timedOut:stillRunning", "capture:timedOut", "dispose"], Recorder.Events);
            Assert.StartsWith(typeof(TestTimeoutException).FullName, result.FailureMessage);
        }
        finally
        {
            ScenarioTimeouts.Grace = grace;
            Recorder.Release.Set();
        }
    }

    [Fact]
    public async Task ZeroTimeout_DisablesTheWatchdog()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.SlowWithoutTimeout));

        Assert.Single(result.Passed);
    }

    [Fact]
    public async Task TheoryRows_EachRunThroughThePipeline()
    {
        Recorder.Reset();

        IReadOnlyList<IXunitTestCase> cases = ScenarioRunnerHarness.Discover(typeof(Sample), nameof(Sample.Rows));
        Assert.All(cases, c => Assert.IsType<ScenarioTestCase>(c));
        Assert.Equal(2, cases.Count);

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.Rows));

        Assert.Single(result.Passed);
        Assert.Contains("row: 2", result.SingleFailure.Test.DisplayName);
        Assert.Equal(2, Recorder.Events.Count(e => e == "before"));
    }

    [Fact]
    public async Task NonSerializableTheory_RunsThroughThePipelineToo()
    {
        Recorder.Reset();

        Assert.IsType<ScenarioTheoryTestCase>(Assert.Single(ScenarioRunnerHarness.Discover(typeof(Sample), nameof(Sample.ObjectRows))));

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.ObjectRows));

        Assert.Single(result.Failed);
        Assert.Equal(1, Recorder.Events.Count(e => e == "capture:failed"));
    }

    [Fact]
    public async Task SetupFailure_KnowsWhichTestItBelongsTo()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(FailsToStart), nameof(FailsToStart.Never));

        Assert.Equal(typeof(ScenarioFailedException).FullName, result.FailureType);
        Assert.Contains("boot failed", result.FailureMessage);
        Assert.Contains(nameof(FailsToStart.Never), result.FailureMessage);
    }

    [Fact]
    public async Task EnvironmentFailure_IsCapturedAndCleanupRunsAfterPartialApply()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.EnvironmentFails));

        Assert.Equal(["init", "before", "environments", "environment:apply", "capture:failed", "environment:cleanup", "dispose"], Recorder.Events);
        Assert.Equal(typeof(ScenarioFailedException).FullName, result.FailureType);
        Assert.Contains("environment setup failed", result.FailureMessage);
        Assert.Equal(1, Recorder.Events.Count(e => e == "environment:apply"));
    }

    [Fact]
    public async Task CleanupFailure_IsCapturedAndRemainsVisibleToFallbackDisposal()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.CleanupFails));

        Assert.Equal(
            ["init", "before", "environments", "body", "check", "environment:cleanup-fails", "capture:failed", "fallback:cleanup-fails", "dispose"],
            Recorder.Events);
        Assert.Equal(typeof(ScenarioFailedException).FullName, result.FailureType);
        Assert.Contains("environment cleanup failed", result.FailureMessage);
        Assert.Equal(1, Recorder.Events.Count(e => e == "environment:cleanup-fails"));
    }

    [Fact]
    public async Task EnvironmentTimeout_IsAbortedCapturedAndCleanedUp()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.EnvironmentTimesOut));

        Assert.Equal(["init", "before", "environments", "environment:wait", "timedOut:stopped", "capture:timedOut", "environment:cleanup", "dispose"], Recorder.Events);
        Assert.StartsWith(typeof(TestTimeoutException).FullName, result.FailureMessage);
    }

    [Fact]
    public async Task EnvironmentSkipUsesTheDynamicSkipPath()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.EnvironmentSkips));

        Assert.Empty(result.Failed);
        Assert.Contains("environment unavailable", Assert.Single(result.Skipped).Reason);
        Assert.Equal(["init", "before", "environments", "environment:skip", "environment:cleanup", "dispose"], Recorder.Events);
    }

    [Fact]
    public async Task AsyncEnvironmentCanYieldAndTheBodyKeepsThePipelineThread()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.EnvironmentYields));

        Assert.Single(result.Passed);
        Assert.Equal(["init", "before", "environments", "environment:yield", "environment:ready", "body", "check", "environment:cleanup", "dispose"], Recorder.Events);
    }

    [Fact]
    public void EngineThread_RefusesWorkFromAnAbortedBody_AndKeepsServingOthers()
    {
        using EngineThread thread = new("pharos-abort-test");
        using CancellationTokenSource abort = new();

        Task<Exception?> aborted = Task.Run(() =>
        {
            ScenarioAbort.Enter(new AbortScope(abort.Token));
            abort.Cancel();
            return Record.Exception(() => thread.Invoke(() => 1));
        });

        Assert.IsType<ScenarioAbortedException>(aborted.Result);
        Assert.Equal(2, thread.Invoke(() => 2));
    }

    [Fact]
    public void EngineThread_LetsAnAbortedBodyGoOnceItsWorkIsDone()
    {
        using EngineThread thread = new("pharos-abort-wait-test");
        using CancellationTokenSource abort = new();
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim release = new();
        AbortScope scope = new(abort.Token);

        Task<Exception?> body = Task.Run(() =>
        {
            ScenarioAbort.Enter(scope);
            return Record.Exception(() => thread.Invoke(() =>
            {
                started.Set();
                release.Wait();
            }));
        });

        Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
        abort.Cancel();

        // Work already on the game thread is waited for: the teardown would queue behind it.
        Assert.False(body.Wait(200), "The aborted body returned while its work still ran on the game thread");

        release.Set();
        Assert.IsType<ScenarioAbortedException>(body.Result);
        Assert.False(scope.Wedged);
        Assert.Equal(3, thread.Invoke(() => 3));
    }

    [Fact]
    public void EngineThread_ReportsAStuckGameThread_AndLetsTheBodyGo()
    {
        TimeSpan grace = ScenarioTimeouts.Grace;
        ScenarioTimeouts.Grace = TimeSpan.FromMilliseconds(200);
        using EngineThread thread = new("pharos-abort-stuck-test");
        using CancellationTokenSource abort = new();
        using ManualResetEventSlim started = new();
        using ManualResetEventSlim release = new();
        AbortScope scope = new(abort.Token);

        try
        {
            Task<Exception?> body = Task.Run(() =>
            {
                ScenarioAbort.Enter(scope);
                return Record.Exception(() => thread.Invoke(() =>
                {
                    started.Set();
                    release.Wait();
                }));
            });

            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            abort.Cancel();

            Assert.True(body.Wait(TimeSpan.FromSeconds(5)), "The body stayed stuck behind the game thread");
            Assert.IsType<ScenarioAbortedException>(body.Result);
            Assert.True(scope.Wedged, "The stuck game thread was not reported");
        }
        finally
        {
            ScenarioTimeouts.Grace = grace;
            release.Set();
        }
    }

    [Fact]
    public async Task SyncBodyStuckOnTheGameThread_IsGivenUp()
    {
        Recorder.Reset();
        TimeSpan grace = ScenarioTimeouts.Grace;
        ScenarioTimeouts.Grace = TimeSpan.FromMilliseconds(200);

        try
        {
            ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.HangsOnTheGameThread));

            Assert.Equal(["init", "before", "environments", "body", "timedOut:stillRunning", "capture:timedOut", "dispose"], Recorder.Events);
            Assert.StartsWith(typeof(TestTimeoutException).FullName, result.FailureMessage);
        }
        finally
        {
            ScenarioTimeouts.Grace = grace;
            Recorder.Release.Set();
        }
    }

    [Fact]
    public void EngineThread_DoesNotCarryTheAbortOfTheBodyThatStartedIt()
    {
        using CancellationTokenSource abort = new();

        EngineThread thread = Task.Run(() =>
        {
            ScenarioAbort.Enter(new AbortScope(abort.Token));
            return new EngineThread("pharos-started-in-body");
        }).Result;

        using (thread)
        {
            abort.Cancel();

            // Work the thread runs for itself, nested invokes included, is not aborted.
            Assert.Equal(4, thread.Invoke(() => thread.Invoke(() => 4)));
        }
    }

    [Fact]
    public void ScenarioTestCases_SurviveSerialization()
    {
        foreach (string method in new[] { nameof(Sample.Passes), nameof(Sample.Rows), nameof(Sample.ObjectRows) })
        {
            foreach (IXunitTestCase testCase in ScenarioRunnerHarness.Discover(typeof(Sample), method))
            {
                IXunitTestCase copy = SerializationHelper.Deserialize<IXunitTestCase>(SerializationHelper.Serialize(testCase));

                Assert.Equal(testCase.GetType(), copy.GetType());
                Assert.Equal(testCase.DisplayName, copy.DisplayName);
                Assert.Equal(0, copy.Timeout);
            }
        }
    }

    [Fact]
    public void ScenarioFailedException_KeepsEveryFailureOfAnAggregate()
    {
        Exception first = Thrown(new InvalidOperationException("first"));
        Exception second = Thrown(new ArgumentException("second"));

        ScenarioFailedException failure = new(new AggregateException(first, second), "details");

        Assert.Contains("System.InvalidOperationException: first", failure.Message);
        Assert.Contains("System.ArgumentException: second", failure.Message);
        Assert.Contains(nameof(Thrown), failure.StackTrace);
    }

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    /// <summary>What the samples record. Static: xUnit creates the sample class itself.</summary>
    private static class Recorder
    {
        private static readonly ConcurrentQueue<string> s_events = new();

        public static IReadOnlyList<string> Events => [.. s_events];
        public static bool FailCheck { get; set; }
        public static bool CaptureArtifacts { get; set; }
        public static int AttemptBodies;
        public static int InstanceCount;
        public static ConcurrentDictionary<int, int> RowAttempts { get; } = new();
        public static string? Details { get; set; }
        public static TaskCompletionSource BodyAborted { get; private set; } = new();
        public static ManualResetEventSlim Release { get; private set; } = new();

        public static void Add(string e) => s_events.Enqueue(e);

        public static void Reset()
        {
            s_events.Clear();
            FailCheck = false;
            CaptureArtifacts = false;
            AttemptBodies = 0;
            InstanceCount = 0;
            Details = "captured by the sample";
            BodyAborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Release = new ManualResetEventSlim();
        }
    }

#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    private sealed class Sample : IAsyncLifetime, IScenarioLifecycle
    {
        private ScenarioContext? _environmentContext;

        public Sample() => Interlocked.Increment(ref Recorder.InstanceCount);

        public Task InitializeAsync()
        {
            Recorder.Add("init");
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            if (_environmentContext is { } context)
            {
                try { await context.DisposeAsync(); }
                catch (InvalidOperationException ex) when (ex.Message == "environment cleanup failed")
                {
                    Recorder.Add("fallback:cleanup-fails");
                }
            }
            Recorder.Add("dispose");
        }

        void IScenarioLifecycle.BeforeBody() => Recorder.Add("before");

        Task IScenarioLifecycle.ApplyEnvironmentsAsync(CancellationToken cancellationToken)
        {
            Recorder.Add("environments");
            ScenarioTestInfo test = ScenarioTestInfo.Current!;
            _environmentContext = new ScenarioContext(server: null, client: null, session: null);
            return ScenarioEnvironments.ApplyAsync(test.TestClass, test.Method, _environmentContext, cancellationToken);
        }

        async Task IScenarioLifecycle.CleanupEnvironmentsAsync(CancellationToken cancellationToken)
        {
            if (_environmentContext is { } context)
            {
                await context.DisposeAsync(cancellationToken);
                if (ReferenceEquals(_environmentContext, context)) _environmentContext = null;
            }
        }

        void IScenarioLifecycle.CheckLoggedErrors()
        {
            Recorder.Add("check");
            if (Recorder.FailCheck) throw new InvalidOperationException("The scenario logged an error.");
        }

        void IScenarioLifecycle.BodyTimedOut(bool stillRunning) => Recorder.Add(stillRunning ? "timedOut:stillRunning" : "timedOut:stopped");

        string? IScenarioLifecycle.CaptureFailure(ScenarioTestInfo test, Exception exception, bool timedOut)
        {
            Recorder.Add(timedOut ? "capture:timedOut" : "capture:failed");
            if (Recorder.CaptureArtifacts)
            {
                Zaldaryon.Pharos.Reporting.FailureArtifactWriter.Write(new Zaldaryon.Pharos.Reporting.ScenarioFailure
                {
                    DisplayName = test.DisplayName,
                    TestClass = test.TestClass,
                    MethodName = test.MethodName,
                    Exception = exception,
                });
            }
            return Recorder.Details;
        }

        Zaldaryon.Pharos.Server.EmbeddedServerHost? IScenarioLifecycle.FixtureServer => null;

        [ServerScenario]
        public void Passes() => Recorder.Add("body");

        [ServerScenario(Attempts = 2)]
        public void FailsOnceThenPasses()
        {
            Recorder.Add("body");
            if (Interlocked.Increment(ref Recorder.AttemptBodies) == 1) Assert.Fail("first attempt fails");
        }

        [ServerScenario(Attempts = 3, RequiredSuccesses = 2)]
        public void RequiredSuccesses()
        {
            if (++Recorder.AttemptBodies == 1) Assert.Fail("one failed attempt is allowed");
        }

        [ServerScenario(Attempts = 2)]
        [TestEnvironment(typeof(SkipCleanupFailingEnvironment))]
        public void SkipThenCleanupFails() => PharosAssert.Skip("skip before cleanup");

        [ServerScenario(Attempts = 2)]
        [TestEnvironment(typeof(FailingEnvironment))]
        public void EnvironmentFails() => Recorder.Add("body");

        [ServerScenario(Attempts = 2)]
        [TestEnvironment(typeof(CleanupFailingEnvironment))]
        public void CleanupFails() => Recorder.Add("body");

        [ServerScenario(TimeoutMs = 200)]
        [TestEnvironment(typeof(CancelledEnvironment))]
        public void EnvironmentTimesOut() => Recorder.Add("body");

        [ServerScenario]
        [TestEnvironment(typeof(SkippingEnvironment))]
        public void EnvironmentSkips() => Recorder.Add("body");

        [ServerScenario(TimeoutMs = 1000)]
        [TestEnvironment(typeof(YieldingEnvironment))]
        public void EnvironmentYields() => Recorder.Add("body");

        [ServerScenario]
        public void Fails()
        {
            Recorder.Add("body");
            Assert.True(false, "the world is wrong");
        }

        [ServerScenario(Attempts = 3)]
        public void Skips()
        {
            Recorder.Add("body");
            Recorder.AttemptBodies++;
            PharosAssert.Skip("mod absent");
        }

        [ClientServerScenario(TimeoutMs = 200, Attempts = 3)]
        public async Task StepsForever()
        {
            Recorder.Add("body");
            using EngineThread thread = new("pharos-sample-steps");
            try
            {
                while (true)
                {
                    thread.Invoke(() => Thread.Sleep(10));
                    await Task.Yield();
                }
            }
            catch (ScenarioAbortedException)
            {
                Recorder.BodyAborted.TrySetResult();
                throw;
            }
        }

        [ClientScenario(Timeout = 200)]
        public void StepsSynchronously()
        {
            Recorder.Add("body");
            using EngineThread thread = new("pharos-sample-sync-steps");
            while (true)
            {
                thread.Invoke(() => Thread.Sleep(10));
            }
        }

        [ServerScenario(TimeoutMs = 200)]
        public void HangsOnTheGameThread()
        {
            Recorder.Add("body");
            EngineThread thread = new("pharos-sample-stuck");
            thread.Invoke(() => Recorder.Release.Wait(TimeSpan.FromSeconds(30)));
        }

        [ServerScenario(TimeoutMs = 200)]
        public async Task AwaitsForever()
        {
            Recorder.Add("body");
            await Task.Run(() => Recorder.Release.Wait(TimeSpan.FromSeconds(30)));
        }

        [ClientScenario(TimeoutMs = 0)]
        public async Task SlowWithoutTimeout() => await Task.Delay(50);

        [ServerTheory]
        [InlineData(1)]
        [InlineData(2)]
        public void Rows(int row) => Assert.Equal(1, row);

        [ClientTheory]
        [MemberData(nameof(Objects))]
        public void ObjectRows(object value) => Assert.Fail("row " + value);

        [ServerTheory(Attempts = 2)]
        [InlineData(1)]
        [InlineData(2)]
        public void RetryRows(int row)
        {
            int attempt = Recorder.RowAttempts.AddOrUpdate(row, 1, (_, current) => current + 1);
            if (row == 2 && attempt == 1) Assert.Fail("first row 2 attempt fails");
        }

        public static IEnumerable<object[]> Objects() => [[new object()]];
    }

    private sealed class SimpleRetry
    {
        public static int AttemptsRun;

        [ServerScenario]
        public void PassesWithoutManifest() { }

        [ServerScenario(Attempts = 2)]
        public void FailsOnceThenPasses()
        {
            if (Interlocked.Increment(ref AttemptsRun) == 1) throw new InvalidOperationException("first simple attempt");
        }

        [ServerScenario(Attempts = 2)]
        public void ThrowsAggregateOnceThenPasses()
        {
            if (Interlocked.Increment(ref AttemptsRun) == 1)
                throw new AggregateException(new InvalidOperationException("first"), new ArgumentException("second"));
        }
    }

    private sealed class DisposeFailure : IAsyncLifetime
    {
        public Task InitializeAsync() => Task.CompletedTask;
        public Task DisposeAsync() => Task.FromException(new InvalidOperationException("dispose failed"));

        [ServerScenario]
        public void Passes() { }

        [ServerScenario]
        public void Fails() => Assert.Fail("body failed");
    }

    private sealed class InvalidPolicy
    {
        public static int ConstructorCalls;

        public InvalidPolicy() => ConstructorCalls++;

        [ServerScenario(Attempts = 1, RequiredSuccesses = 2)]
        public void NeverRuns() => Assert.Fail("invalid retry policy must fail before setup");
    }

    public sealed class FailingEnvironment : ITestEnvironment
    {
        public Task ApplyAsync(ScenarioContext context, CancellationToken cancellationToken)
        {
            Recorder.Add("environment:apply");
            context.OnCleanup(() => Recorder.Add("environment:cleanup"));
            throw new InvalidOperationException("environment setup failed");
        }
    }

    public sealed class CleanupFailingEnvironment : ITestEnvironment
    {
        public Task ApplyAsync(ScenarioContext context, CancellationToken cancellationToken)
        {
            context.OnCleanup(() =>
            {
                Recorder.Add("environment:cleanup-fails");
                throw new InvalidOperationException("environment cleanup failed");
            });
            return Task.CompletedTask;
        }
    }

    public sealed class SkipCleanupFailingEnvironment : ITestEnvironment
    {
        public Task ApplyAsync(ScenarioContext context, CancellationToken cancellationToken)
        {
            context.OnCleanup(() => throw new InvalidOperationException("skip cleanup failed"));
            return Task.CompletedTask;
        }
    }

    public sealed class CancelledEnvironment : ITestEnvironment
    {
        public async Task ApplyAsync(ScenarioContext context, CancellationToken cancellationToken)
        {
            Recorder.Add("environment:wait");
            context.OnCleanup(() => Recorder.Add("environment:cleanup"));
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
    }

    public sealed class SkippingEnvironment : ITestEnvironment
    {
        public Task ApplyAsync(ScenarioContext context, CancellationToken cancellationToken)
        {
            Recorder.Add("environment:skip");
            context.OnCleanup(() => Recorder.Add("environment:cleanup"));
            PharosAssert.Skip("environment unavailable");
            return Task.CompletedTask;
        }
    }

    public sealed class YieldingEnvironment : ITestEnvironment
    {
        public async Task ApplyAsync(ScenarioContext context, CancellationToken cancellationToken)
        {
            Recorder.Add("environment:yield");
            context.OnCleanup(() => Recorder.Add("environment:cleanup"));
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            Recorder.Add("environment:ready");
        }
    }

    private sealed class FailsToStart : IAsyncLifetime
    {
        public Task InitializeAsync()
        {
            ScenarioTestInfo test = ScenarioTestInfo.Current ?? throw new InvalidOperationException("no test info");
            throw new ScenarioFailedException(new InvalidOperationException("boot failed"), "while running " + test.MethodName);
        }

        public Task DisposeAsync() => Task.CompletedTask;

        [ServerScenario]
        public void Never() => throw new InvalidOperationException("The body ran after a failed setup.");
    }
#pragma warning restore xUnit1000
}

/// <summary>
/// Tests that change the failure-artifact statics, such as the grace period, run one at a time and
/// alone: a live scenario running beside them would see the shortened limits.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class FailureArtifactCollection
{
    public const string Name = "Failure artifacts";
}
