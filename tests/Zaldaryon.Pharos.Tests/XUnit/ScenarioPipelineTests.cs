using System.Collections.Concurrent;
using Xunit;
using Xunit.Sdk;
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
        Assert.Equal(["init", "before", "body", "check", "dispose"], Recorder.Events);
    }

    [Fact]
    public async Task FailingBody_IsCapturedBeforeTeardown_AndTheMessageCarriesTheDetails()
    {
        Recorder.Reset();

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.Fails));

        Assert.Equal(["init", "before", "body", "capture:failed", "dispose"], Recorder.Events);
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

        Assert.Equal(["init", "before", "body", "check", "capture:failed", "dispose"], Recorder.Events);
        Assert.Contains("logged an error", result.FailureMessage);
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

        Assert.Equal(["init", "before", "body", "timedOut:stopped", "capture:timedOut", "dispose"], Recorder.Events);
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
        Assert.Equal(["init", "before", "body", "timedOut:stopped", "capture:timedOut", "dispose"], Recorder.Events);
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

            Assert.Equal(["init", "before", "body", "timedOut:stillRunning", "capture:timedOut", "dispose"], Recorder.Events);
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

            Assert.Equal(["init", "before", "body", "timedOut:stillRunning", "capture:timedOut", "dispose"], Recorder.Events);
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
        public static string? Details { get; set; }
        public static TaskCompletionSource BodyAborted { get; private set; } = new();
        public static ManualResetEventSlim Release { get; private set; } = new();

        public static void Add(string e) => s_events.Enqueue(e);

        public static void Reset()
        {
            s_events.Clear();
            FailCheck = false;
            Details = "captured by the sample";
            BodyAborted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Release = new ManualResetEventSlim();
        }
    }

#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    private sealed class Sample : IAsyncLifetime, IScenarioLifecycle
    {
        public Task InitializeAsync()
        {
            Recorder.Add("init");
            return Task.CompletedTask;
        }

        public Task DisposeAsync()
        {
            Recorder.Add("dispose");
            return Task.CompletedTask;
        }

        void IScenarioLifecycle.BeforeBody() => Recorder.Add("before");

        void IScenarioLifecycle.CheckLoggedErrors()
        {
            Recorder.Add("check");
            if (Recorder.FailCheck) throw new InvalidOperationException("The scenario logged an error.");
        }

        void IScenarioLifecycle.BodyTimedOut(bool stillRunning) => Recorder.Add(stillRunning ? "timedOut:stillRunning" : "timedOut:stopped");

        string? IScenarioLifecycle.CaptureFailure(ScenarioTestInfo test, Exception exception, bool timedOut)
        {
            Recorder.Add(timedOut ? "capture:timedOut" : "capture:failed");
            return Recorder.Details;
        }

        Zaldaryon.Pharos.Server.EmbeddedServerHost? IScenarioLifecycle.FixtureServer => null;

        [ServerScenario]
        public void Passes() => Recorder.Add("body");

        [ServerScenario]
        public void Fails()
        {
            Recorder.Add("body");
            Assert.True(false, "the world is wrong");
        }

        [ClientServerScenario(TimeoutMs = 200)]
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

        public static IEnumerable<object[]> Objects() => [[new object()]];
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
