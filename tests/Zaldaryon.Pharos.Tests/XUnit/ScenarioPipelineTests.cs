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
        Assert.Contains("200", result.FailureMessage);
        Assert.True(Recorder.BodyAborted.Task.Wait(TimeSpan.FromSeconds(5)), "The body was not stopped at its next step");
    }

    [Fact]
    public async Task XunitTimeoutOnASyncBody_IsEnforcedByThePipeline_AndTheClassIsStillDisposed()
    {
        Recorder.Reset();
        TimeSpan grace = ScenarioTimeouts.Grace;
        ScenarioTimeouts.Grace = TimeSpan.FromMilliseconds(200);

        try
        {
            ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Sample), nameof(Sample.BlocksSynchronously));

            // The body never reaches a step, so it is still running: its host must be given up.
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
            ScenarioAbort.Enter(abort.Token);
            abort.Cancel();
            return Record.Exception(() => thread.Invoke(() => 1));
        });

        Assert.IsType<ScenarioAbortedException>(aborted.Result);
        Assert.Equal(2, thread.Invoke(() => 2));
    }

    [Fact]
    public void EngineThread_StopsWaitingForWorkWhenTheBodyIsAborted()
    {
        using EngineThread thread = new("pharos-abort-wait-test");
        using CancellationTokenSource abort = new();
        using ManualResetEventSlim release = new();

        Task<Exception?> waiting = Task.Run(() =>
        {
            ScenarioAbort.Enter(abort.Token);
            return Record.Exception(() => thread.Invoke(() => release.Wait()));
        });

        Thread.Sleep(100);
        abort.Cancel();
        Assert.IsType<ScenarioAbortedException>(waiting.Result);

        // The work that was running finishes, and the thread goes on.
        release.Set();
        Assert.Equal(3, thread.Invoke(() => 3));
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
        public void BlocksSynchronously()
        {
            Recorder.Add("body");
            Recorder.Release.Wait(TimeSpan.FromSeconds(30));
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

/// <summary>Tests that change the failure-artifact statics run one at a time.</summary>
[CollectionDefinition(Name)]
public sealed class FailureArtifactCollection
{
    public const string Name = "Failure artifacts";
}
