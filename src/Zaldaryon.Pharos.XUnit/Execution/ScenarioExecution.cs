using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Collections.Concurrent;
using System.Collections;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;
using Zaldaryon.Pharos.Scenarios;

namespace Zaldaryon.Pharos.XUnit.Execution;

internal static class ScenarioSkipDispatch
{
    private static readonly ConcurrentDictionary<ITest, string> s_reasons = new();

    public static void Set(ITest test, string reason) => s_reasons[test] = reason;

    public static bool TryTake(ITest test, out string reason) => s_reasons.TryRemove(test, out reason!);
}

internal sealed class ScenarioSkipTracker
{
    private readonly ConcurrentDictionary<ITest, byte> _skipped = new();

    public int Count => _skipped.Count;

    public void MarkSkipped(ITest test) => _skipped.TryAdd(test, 0);
}


internal sealed class ScenarioSkipMessageBus(IMessageBus inner, ScenarioSkipTracker tracker) : IMessageBus
{
    public bool QueueMessage(IMessageSinkMessage message)
    {
        if (message is ITestPassed passed && ScenarioSkipDispatch.TryTake(passed.Test, out string reason))
        {
            tracker.MarkSkipped(passed.Test);
            return inner.QueueMessage(new TestSkipped(passed.Test, reason));
        }
        if (message is ITestFailed failed)
            ScenarioSkipDispatch.TryTake(failed.Test, out _);
        if (message is ITestFinished finished)
            ScenarioSkipDispatch.TryTake(finished.Test, out _);
        return inner.QueueMessage(message);
    }

    public void Dispose() { }
}

/// <summary>The watchdog timeouts of scenario attributes.</summary>
internal static class XunitScenarioTimeouts
{
    /// <summary>
    /// The timeout of a scenario test in milliseconds, 0 for none: the attribute's xUnit
    /// <c>Timeout</c> when set, else its <c>TimeoutMs</c>, scaled by <c>PHAROS_TIMEOUT_SCALE</c>.
    /// </summary>
    public static int Of(ITestMethod testMethod)
    {
        if (ScenarioTimeouts.Override is { } overridden) return overridden;

        IAttributeInfo? attribute = testMethod.Method.GetCustomAttributes(typeof(FactAttribute)).FirstOrDefault();
        if (attribute == null) return 0;

        int timeout = Named(attribute, nameof(FactAttribute.Timeout));
        if (timeout <= 0) timeout = Named(attribute, "TimeoutMs");
        if (timeout <= 0) return 0;

        return (int)Math.Clamp(timeout * ScenarioTimeouts.Scale, 1, int.MaxValue);
    }

    private static int Named(IAttributeInfo attribute, string name)
    {
        try
        {
            return attribute.GetNamedArgument<int>(name);
        }
        catch (Exception)
        {
            // The attribute has no such property.
            return 0;
        }
    }
}

/// <summary>Discovers <c>[ClientScenario]</c>, <c>[ServerScenario]</c> and <c>[ClientServerScenario]</c> tests.</summary>
public sealed class ScenarioFactDiscoverer(IMessageSink diagnosticMessageSink) : FactDiscoverer(diagnosticMessageSink)
{
    internal const string TypeName = "Zaldaryon.Pharos.XUnit.Execution.ScenarioFactDiscoverer";
    internal const string AssemblyName = "Zaldaryon.Pharos.XUnit";

    /// <inheritdoc />
    protected override IXunitTestCase CreateTestCase(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute) =>
        new ScenarioTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
}

/// <summary>Discovers <c>[ClientTheory]</c> and <c>[ServerTheory]</c> tests.</summary>
public sealed class ScenarioTheoryDiscoverer(IMessageSink diagnosticMessageSink) : TheoryDiscoverer(diagnosticMessageSink)
{
    internal const string TypeName = "Zaldaryon.Pharos.XUnit.Execution.ScenarioTheoryDiscoverer";
    internal const string AssemblyName = "Zaldaryon.Pharos.XUnit";

    /// <inheritdoc />
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute, object[] dataRow) =>
        [new ScenarioTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod, dataRow)];

    /// <inheritdoc />
    protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo theoryAttribute) =>
        [new ScenarioTheoryTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod)];
}

/// <summary>A scenario test, or one row of a scenario theory.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ScenarioTestCase : XunitTestCase
{
    /// <summary>For deserialization only.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public ScenarioTestCase()
    {
    }

    /// <summary>Creates the test case.</summary>
    public ScenarioTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod, object[]? testMethodArguments = null)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod, testMethodArguments)
    {
    }

    /// <summary>
    /// Always 0: xUnit's own timeout throws past the class's <c>DisposeAsync</c>, which releases
    /// the scenario host. The pipeline enforces the timeout itself.
    /// </summary>
    protected override int GetTimeout(IAttributeInfo factAttribute) => 0;

    /// <summary>The attribute's skip reason, or else the one <see cref="RequireGameVersionAttribute"/> gives.</summary>
    protected override string GetSkipReason(IAttributeInfo factAttribute) =>
        base.GetSkipReason(factAttribute) ?? ScenarioExplicitSkip.Of(TestMethod) ?? GameVersionSkip.Of(TestMethod, ex => InitializationException = ex)!;

    /// <inheritdoc />
    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        new ScenarioTestCaseRunner(this, DisplayName, SkipReason, constructorArguments, TestMethodArguments, messageBus, aggregator, cancellationTokenSource, XunitScenarioTimeouts.Of(TestMethod), new ScenarioSkipTracker()).RunAsync();
}

/// <summary>A scenario theory whose data is enumerated when it runs.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class ScenarioTheoryTestCase : XunitTheoryTestCase
{
    /// <summary>For deserialization only.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [Obsolete("Called by the de-serializer; should only be called by deriving classes for de-serialization purposes")]
    public ScenarioTheoryTestCase()
    {
    }

    /// <summary>Creates the test case.</summary>
    public ScenarioTheoryTestCase(IMessageSink diagnosticMessageSink, TestMethodDisplay defaultMethodDisplay, TestMethodDisplayOptions defaultMethodDisplayOptions, ITestMethod testMethod)
        : base(diagnosticMessageSink, defaultMethodDisplay, defaultMethodDisplayOptions, testMethod)
    {
    }

    /// <inheritdoc cref="ScenarioTestCase" />
    protected override int GetTimeout(IAttributeInfo factAttribute) => 0;

    /// <inheritdoc cref="ScenarioTestCase" />
    protected override string GetSkipReason(IAttributeInfo factAttribute) =>
        base.GetSkipReason(factAttribute) ?? ScenarioExplicitSkip.Of(TestMethod) ?? GameVersionSkip.Of(TestMethod, ex => InitializationException = ex)!;

    /// <inheritdoc />
    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        new ScenarioTheoryTestCaseRunner(this, DisplayName, SkipReason, constructorArguments, diagnosticMessageSink, messageBus, aggregator, cancellationTokenSource, XunitScenarioTimeouts.Of(TestMethod), new ScenarioSkipTracker()).RunAsync();
}

internal static class ScenarioExplicitSkip
{
    public const string RunExplicitVariable = ScenarioGate.RunExplicitVariable;

    public static string? Of(ITestMethod testMethod)
    {
        MethodInfo? method = testMethod.Method.ToRuntimeMethod();
        if (method?.GetCustomAttributes().FirstOrDefault(a => a.GetType().GetProperty("Explicit")?.PropertyType == typeof(bool)) is not { } attribute ||
            attribute.GetType().GetProperty("Explicit")?.GetValue(attribute) is not true)
            return null;

        return Environment.GetEnvironmentVariable(RunExplicitVariable) == "1"
            ? null
            : $"Explicit scenario. Select it by name with pharos run --filter or set {RunExplicitVariable}=1.";
    }
}

// Reads [RequireGameVersion], [RequireOptimum] and [SkipOnOptimum] where the test runs. A range
// that cannot be read, or attributes that contradict each other, fail the test.
internal static class GameVersionSkip
{
    public static string? Of(ITestMethod testMethod, Action<Exception> fail)
    {
        try
        {
            Type? type = testMethod.TestClass.Class.ToRuntimeType();
            MethodInfo? method = testMethod.Method.ToRuntimeMethod();
            if (type == null || method == null) return null;
            // The Optimum gate first, so contradicting attributes fail even on a version that skips.
            string? optimum = OptimumGate.SkipReasonFor(type, method, OptimumInstall.Loaded);
            return RequireGameVersionAttribute.SkipReasonFor(type, method, InstalledGame.ParsedVersion) ?? optimum;
        }
        catch (FormatException ex)
        {
            fail(new InvalidOperationException($"Invalid [RequireGameVersion] range: {ex.Message}", ex));
            return null;
        }
        catch (OptimumGateConflictException ex)
        {
            fail(ex);
            return null;
        }
    }
}

internal sealed class ScenarioTestCaseRunner(
    IXunitTestCase testCase, string displayName, string skipReason, object[] constructorArguments, object[] testMethodArguments,
    IMessageBus messageBus, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource, int timeoutMs, ScenarioSkipTracker skipTracker)
    : XunitTestCaseRunner(testCase, displayName, skipReason, constructorArguments, testMethodArguments, messageBus, aggregator, cancellationTokenSource)
{
    protected override XunitTestRunner CreateTestRunner(ITest test, IMessageBus messageBus, Type testClass, object[] constructorArguments, MethodInfo testMethod, object[] testMethodArguments, string skipReason, IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        new ScenarioTestRunner(test, new ScenarioSkipMessageBus(messageBus, skipTracker), testClass, constructorArguments, testMethod, testMethodArguments, skipReason, beforeAfterAttributes, new ExceptionAggregator(aggregator), cancellationTokenSource, timeoutMs);

    protected override async Task<RunSummary> RunTestAsync()
    {
        RunSummary summary = await base.RunTestAsync().ConfigureAwait(false);
        summary.Skipped += skipTracker.Count;
        return summary;
    }
}

internal sealed class ScenarioTheoryTestCaseRunner(
    IXunitTestCase testCase, string displayName, string skipReason, object[] constructorArguments, IMessageSink diagnosticMessageSink,
    IMessageBus messageBus, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource, int timeoutMs, ScenarioSkipTracker skipTracker)
    : XunitTheoryTestCaseRunner(testCase, displayName, skipReason, constructorArguments, diagnosticMessageSink, messageBus, aggregator, cancellationTokenSource)
{
    protected override XunitTestRunner CreateTestRunner(ITest test, IMessageBus messageBus, Type testClass, object[] constructorArguments, MethodInfo testMethod, object[] testMethodArguments, string skipReason, IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        new ScenarioTestRunner(test, new ScenarioSkipMessageBus(messageBus, skipTracker), testClass, constructorArguments, testMethod, testMethodArguments, skipReason, beforeAfterAttributes, new ExceptionAggregator(aggregator), cancellationTokenSource, timeoutMs);

    protected override async Task<RunSummary> RunTestAsync()
    {
        RunSummary summary = await base.RunTestAsync().ConfigureAwait(false);
        summary.Skipped += skipTracker.Count;
        return summary;
    }
}

internal sealed class ScenarioTestRunner(
    ITest test, IMessageBus messageBus, Type testClass, object[] constructorArguments, MethodInfo testMethod, object[] testMethodArguments,
    string skipReason, IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, ExceptionAggregator aggregator,
    CancellationTokenSource cancellationTokenSource, int timeoutMs)
    : XunitTestRunner(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, skipReason, beforeAfterAttributes, aggregator, cancellationTokenSource)
{
    // Which game and which Pharos ran the test, first in its output.
    private static readonly string s_versions =
        $"Vintage Story {InstalledGame.Version}{(OptimumInstall.Loaded is { } optimum ? $" ({OptimumInstall.Describe(optimum)})" : "")}, Pharos {Reporting.FailureArtifactWriter.PharosVersion}{Environment.NewLine}";

    // By the time the base returns, the class has torn down: its isolation lines are complete.
    // They go to the test's output, where test explorers and trx files show it.
    protected override async Task<Tuple<decimal, string>> InvokeTestAsync(ExceptionAggregator aggregator)
    {
        ScenarioAttemptPolicy policy;
        try { policy = ScenarioAttemptPolicy.Read(TestMethod); }
        catch (Exception ex)
        {
            aggregator.Add(ex);
            return Tuple.Create(0m, s_versions + ex.Message + Environment.NewLine);
        }

        _randomSeed = ScenarioRandomSeed.Resolve(TestClass, TestMethod, TestMethodArguments);
        if (policy.Attempts == 1)
        {
            ScenarioAttemptState state = new();
            ScenarioAttemptState? previousState = ScenarioAttemptState.Current;
            IsolationLog.Notes notes = new();
            IsolationLog.Notes? previousNotes = IsolationLog.Current;
            ScenarioAttemptState.Current = state;
            IsolationLog.Current = notes;
            try
            {
                Tuple<decimal, string> result = await base.InvokeTestAsync(aggregator).ConfigureAwait(false);
                Exception? failure = aggregator.ToException();
                ClassifyTerminalFailure(state, failure);
                string outcome = state.Skipped && failure == null ? "Skipped" : failure == null ? "Passed" : "Failed";
                ScenarioAttemptManifestWriterAdapter.Write(Test, policy,
                    [new ScenarioAttemptEntry(1, state.Phase, outcome, (double)result.Item1, failure?.ToString(), state.ArtifactDirectories.ToArray())],
                    outcome, false);
                return Tuple.Create(result.Item1, s_versions + _randomSeed?.OutputLine + result.Item2 + notes.Text);
            }
            finally
            {
                ScenarioAttemptState.Current = previousState;
                IsolationLog.Current = previousNotes;
            }
        }

        List<ScenarioAttemptEntry> history = [];
        int successes = 0;
        decimal totalTime = 0;
        Exception? lastFailure = null;
        List<string> attemptOutputs = [];
        bool skipped = false;
        CapturingMessageBus? finalMessages = null;
        for (int number = 1; number <= policy.Attempts; number++)
        {
            if (CancellationTokenSource.IsCancellationRequested) break;

            ScenarioAttemptState state = new();
            ScenarioAttemptState? previousState = ScenarioAttemptState.Current;
            IsolationLog.Notes notes = new();
            IsolationLog.Notes? previousNotes = IsolationLog.Current;
            IMessageBus outerBus = MessageBus;
            CapturingMessageBus attemptMessages = new();
            ExceptionAggregator attemptAggregator = new(aggregator);
            ScenarioAttemptState.Current = state;
            IsolationLog.Current = notes;
            MessageBus = attemptMessages;
            Tuple<decimal, string> result;
            try
            {
                result = await base.InvokeTestAsync(attemptAggregator).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                attemptAggregator.Add(ex);
                result = Tuple.Create(0m, "");
            }
            finally
            {
                MessageBus = outerBus;
                ScenarioAttemptState.Current = previousState;
                IsolationLog.Current = previousNotes;
            }

            Exception? failure = attemptAggregator.ToException();
            ClassifyTerminalFailure(state, failure);
            lastFailure = failure;
            totalTime += result.Item1;
            attemptOutputs.Add(result.Item2 + notes.Text);
            // A body skip is only the final outcome if teardown and reporting also succeeded.
            skipped = state.Skipped && failure == null;
            finalMessages = attemptMessages;
            if (failure == null && !skipped) successes++;
            string outcome = skipped ? "Skipped" : failure == null ? "Passed" : "Failed";
            history.Add(new ScenarioAttemptEntry(number, state.Phase, outcome, (double)result.Item1, failure?.ToString(), state.ArtifactDirectories.ToArray()));

            if (successes >= policy.RequiredSuccesses || skipped || state.NonRetryableFailure ||
                (failure != null && (!state.RetryableBodyFailure || !ContainsOnlyRetryableFailure(failure, state.RetryableFailure))) ||
                CancellationTokenSource.IsCancellationRequested)
                break;
            if (successes + policy.Attempts - number < policy.RequiredSuccesses) break;
        }

        // Each attempt's fixture and body messages are private until the final status is known.
        if (finalMessages != null)
            foreach (IMessageSinkMessage message in finalMessages.Messages)
                if (!MessageBus.QueueMessage(message)) CancellationTokenSource.Cancel();

        bool passed = successes >= policy.RequiredSuccesses;
        bool flaky = passed && history.Count > 1 && history.Take(history.Count - 1).Any(a => a.Outcome == "Failed");
        string finalOutcome = skipped ? "Skipped" : passed ? "Passed" : "Failed";
        string historyText = $"Pharos attempts: {history.Count}/{policy.Attempts}; required successes: {policy.RequiredSuccesses}; successes: {successes}; flaky={flaky.ToString().ToLowerInvariant()}.";
        if (history.Count > 1)
            historyText += Environment.NewLine + string.Join(Environment.NewLine, history.Select(a => $"  attempt {a.Number}: {a.Outcome}{(string.IsNullOrWhiteSpace(a.Failure) ? "" : ": " + FirstLine(a.Failure))}"));
        ScenarioAttemptManifestWriterAdapter.Write(Test, policy, history, finalOutcome, flaky);

        if (!passed && !skipped)
        {
            lastFailure ??= new InvalidOperationException($"Scenario required {policy.RequiredSuccesses} successful attempts but got {successes} of {history.Count}.");
            aggregator.Add(successes == 0 && history.Count > 0 ? lastFailure : new InvalidOperationException(historyText, lastFailure));
        }
        string output = string.Join(Environment.NewLine, attemptOutputs.Select((text, index) => $"--- attempt {index + 1} output ---{Environment.NewLine}{text}"));
        return Tuple.Create(totalTime, s_versions + _randomSeed?.OutputLine + output + Environment.NewLine + historyText);
    }

    private static bool ContainsOnlyRetryableFailure(Exception exception, Exception? retryableFailure)
    {
        if (retryableFailure == null) return false;
        if (ReferenceEquals(exception, retryableFailure)) return true;
        return exception is AggregateException aggregate &&
            aggregate.Flatten().InnerExceptions.Count == 1 && ReferenceEquals(aggregate.Flatten().InnerExceptions[0], retryableFailure);
    }

    private static void ClassifyTerminalFailure(ScenarioAttemptState state, Exception? failure)
    {
        // xUnit disposes the class after the scenario invoker has finished. A disposal or
        // AfterTest failure can therefore be added after the body left Phase=body; fail closed
        // for retries/quarantine when it is not the exact captured body failure.
        if (failure != null && state.Phase == "body" &&
            (!state.RetryableBodyFailure || !ContainsOnlyRetryableFailure(failure, state.RetryableFailure)))
            state.MarkNonRetryable("teardown");
    }

    private static string FirstLine(string? text) => text?.Trim().Split('\n')[0] ?? "";

    private sealed class CapturingMessageBus : IMessageBus
    {
        public List<IMessageSinkMessage> Messages { get; } = [];
        public bool QueueMessage(IMessageSinkMessage message) { Messages.Add(message); return true; }
        public void Dispose() { }
    }

    // The seed of this test, which its output starts with and its class seeds the game with.
    private ScenarioRandomSeed? _randomSeed;

    protected override async Task<decimal> InvokeTestMethodAsync(ExceptionAggregator aggregator)
    {
        // Async, so the test info stays on this test's flow: the class's InitializeAsync reads it
        // to save artifacts when the scenario cannot even start.
        ScenarioTestInfo.Current = new ScenarioTestInfo(Test.DisplayName, TestClass, TestMethod.Name, TestMethod) { RandomSeed = _randomSeed };
        return await new ScenarioTestInvoker(Test, MessageBus, TestClass, ConstructorArguments, TestMethod, TestMethodArguments, BeforeAfterAttributes, aggregator, CancellationTokenSource, timeoutMs)
            .RunAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Runs the test body under the scenario watchdog, checks the logged errors, and saves the
/// failure artifacts before the class's <c>DisposeAsync</c> tears the client and server down.
/// </summary>
internal sealed class ScenarioTestInvoker(
    ITest test, IMessageBus messageBus, Type testClass, object[] constructorArguments, MethodInfo testMethod, object[] testMethodArguments,
    IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource, int timeoutMs)
    : XunitTestInvoker(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, beforeAfterAttributes, aggregator, cancellationTokenSource)
{
    protected override async Task<decimal> InvokeTestMethodAsync(object testClassInstance)
    {
        if (TestCase.InitializationException != null) return await base.InvokeTestMethodAsync(testClassInstance).ConfigureAwait(false);

        IScenarioLifecycle? lifecycle = testClassInstance as IScenarioLifecycle;
        if (lifecycle != null && !Aggregator.HasExceptions)
        {
            Aggregator.Run(lifecycle.BeforeBody);
            if (Aggregator.HasExceptions)
            {
                ScenarioAttemptState.Current?.MarkNonRetryable();
                return 0m;
            }
        }

        // The body gets an aggregator and a timer of its own: a body that outlives its timeout
        // must not write into this test's results after they are reported.
        ExceptionAggregator bodyAggregator = new();
        BodyInvoker body = new(Test, MessageBus, TestClass, ConstructorArguments, TestMethod, TestMethodArguments, BeforeAfterAttributes, bodyAggregator, CancellationTokenSource);

        // The body runs inline, on the thread xUnit gives it, as it always has: a fixture-mode
        // client's GL context is current on the thread that booted it. The watchdog is a timer
        // that aborts the body, which throws at its next step into the game, whether it is
        // synchronous or not.
        CancellationTokenSource abort = new();
        AbortScope scope = new(abort.Token);
        Timer? watchdog = timeoutMs > 0 ? new Timer(_ => abort.Cancel(), null, timeoutMs, Timeout.Infinite) : null;
        Task<decimal> run;
        ScenarioThreadPump? threadPump = null;
        bool timedOut = false;
        bool timeoutReported = false;
        bool abandonHost = false;
        ScenarioAbort.Enter(scope);
        try
        {
            ExceptionAggregator preparation = new();
            if (lifecycle != null)
            {
                Task? prepare = null;
                try
                {
                    // Start setup on xUnit's original thread and pump captured awaits there.
                    // Fixture-mode HeadlessClient has no engine thread to marshal onto; its GL
                    // context belongs to this thread, so Task.Run would silently call game APIs
                    // from the wrong thread.
                    SynchronizationContext? previous = SynchronizationContext.Current;
                    ScenarioThreadPump pump = threadPump = new();
                    try
                    {
                        SynchronizationContext.SetSynchronizationContext(pump);
                        prepare = lifecycle.ApplyEnvironmentsAsync(abort.Token);
                        if (prepare != null && !pump.Run(prepare, abort.Token, ScenarioTimeouts.Grace))
                        {
                            ScenarioAttemptState.Current?.MarkNonRetryable("timeout");
                            lifecycle.BodyTimedOut(stillRunning: true);
                            abandonHost = true;
                            timeoutReported = true;
                            timedOut = true;
                            preparation.Add(new TestTimeoutException(timeoutMs));
                        }
                    }
                    finally { SynchronizationContext.SetSynchronizationContext(previous); }
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException && abort.IsCancellationRequested)
                    {
                        ScenarioAttemptState.Current?.MarkNonRetryable("timeout");
                        lifecycle.BodyTimedOut(stillRunning: false);
                        timeoutReported = true;
                        timedOut = true;
                        preparation.Add(new TestTimeoutException(timeoutMs));
                    }
                    else preparation.Add(ex);
                }

                if (!timedOut && prepare != null && !preparation.HasExceptions)
                {
                    try { prepare.GetAwaiter().GetResult(); }
                    catch (OperationCanceledException) when (abort.IsCancellationRequested)
                    {
                        ScenarioAttemptState.Current?.MarkNonRetryable("timeout");
                        abandonHost |= scope.Wedged;
                        lifecycle.BodyTimedOut(stillRunning: abandonHost);
                        timeoutReported = true;
                        timedOut = true;
                        preparation.Add(new TestTimeoutException(timeoutMs));
                    }
                    catch (Exception ex)
                    {
                        preparation.Add(ex);
                    }
                }
            }

            if (preparation.HasExceptions)
            {
                ScenarioAttemptState.Current?.MarkNonRetryable();
                // Setup failures take the same reporting path as body failures so their artifacts
                // are captured before IAsyncLifetime.DisposeAsync tears down the game instances.
                bodyAggregator.Add(preparation.ToException());
                run = Task.FromResult(0m);
            }
            else
            {
                SynchronizationContext? previous = SynchronizationContext.Current;
                ScenarioThreadPump pump = threadPump ??= new ScenarioThreadPump();
                try
                {
                    SynchronizationContext.SetSynchronizationContext(pump);
                    run = body.InvokeBody(testClassInstance);
                    try
                    {
                        if (!pump.Run(run, abort.Token, ScenarioTimeouts.Grace))
                        {
                            ScenarioAttemptState.Current?.MarkNonRetryable("timeout");
                            lifecycle?.BodyTimedOut(stillRunning: true);
                            abandonHost = true;
                            timeoutReported = true;
                            timedOut = true;
                            bodyAggregator.Add(new ScenarioAbortedException());
                        }
                    }
                    catch { /* The body's own aggregator reports its exception below. */ }
                }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
            }
        }
        finally
        {
            // The body's continuations keep the token; what runs here next must not.
            ScenarioAbort.Enter(null);
        }

        if (!run.IsCompleted && watchdog != null)
        {
            Task aborted = Task.Delay(Timeout.Infinite, abort.Token);
            await Task.WhenAny(run, aborted).ConfigureAwait(false);
        }

        decimal elapsed;
        if (!run.IsCompleted && abort.IsCancellationRequested)
        {
            ScenarioAttemptState.Current?.MarkNonRetryable("timeout");
            // Aborted while still running: it gets a grace period to reach its next step.
            bool stopped = await Task.WhenAny(run, Task.Delay(ScenarioTimeouts.Grace)).ConfigureAwait(false) == run;
            timedOut = true;
            watchdog?.Dispose();
            abandonHost |= !stopped || scope.Wedged;
            if (!timeoutReported) lifecycle?.BodyTimedOut(stillRunning: abandonHost);
            Aggregator.Add(new TestTimeoutException(timeoutMs));
            elapsed = timeoutMs / 1000m;
        }
        else
        {
            elapsed = await run.ConfigureAwait(false);
            watchdog?.Dispose();

            // A timeout only when the body was stopped by the abort, or left the game stuck even
            // if it swallowed the abort, not when the timer fired as it finished on its own.
            timedOut |= abort.IsCancellationRequested && (scope.Wedged || (bodyAggregator.HasExceptions && WasAborted(bodyAggregator.ToException())));
            if (timedOut)
            {
                ScenarioAttemptState.Current?.MarkNonRetryable("timeout");
                // The body ran out of time and stopped at its next step: a timeout, not whatever
                // the abort made it throw.
                abandonHost |= scope.Wedged;
                if (!timeoutReported) lifecycle?.BodyTimedOut(stillRunning: abandonHost);
                Aggregator.Add(new TestTimeoutException(timeoutMs));
            }
            else if (bodyAggregator.HasExceptions)
            {
                Exception bodyFailure = bodyAggregator.ToException();
                if (bodyFailure is ScenarioSkipException skip)
                {
                    if (ScenarioAttemptState.Current is { } state) state.Skipped = true;
                    if (ScenarioTestInfo.Current is { } currentInfo)
                    {
                        currentInfo.DynamicSkipReason = skip.Message;
                        ScenarioSkipDispatch.Set(Test, skip.Message);
                    }
                }
                else
                {
                    if (ScenarioAttemptState.Current is { } state)
                    {
                        if (state.BodyEntered) state.RetryableBodyFailure = true;
                        else state.MarkNonRetryable();
                    }
                    Aggregator.Add(bodyFailure);
                    if (ScenarioAttemptState.Current is { } retryState && retryState.RetryableBodyFailure)
                        retryState.RetryableFailure = bodyFailure;
                }
            }
        }

        // The body ran on an invoker of its own: its time is this test's time.
        Timer.Aggregate(TimeSpan.FromSeconds((double)elapsed));

        if (lifecycle == null) return elapsed;

        ScenarioTestInfo info = new(Test.DisplayName, TestClass, TestMethod.Name);
        if (ScenarioTestInfo.Current?.DynamicSkipReason == null)
        {
            if (!Aggregator.HasExceptions)
            {
                Aggregator.Run(lifecycle.CheckLoggedErrors);
                if (Aggregator.HasExceptions) ScenarioAttemptState.Current?.MarkNonRetryable("logged-errors");
            }

            // pharos fixture: the world the test built, saved before the class rolls it back. What mods
            // log while saving counts as well.
            if (!Aggregator.HasExceptions && FixtureExport.Requested(info) is { } destination)
            {
                await Aggregator.RunAsync(() => FixtureExport.SaveAsync(lifecycle.FixtureServer, info, destination)).ConfigureAwait(false);
                if (!Aggregator.HasExceptions) Aggregator.Run(lifecycle.CheckLoggedErrors);
                if (Aggregator.HasExceptions) ScenarioAttemptState.Current?.MarkNonRetryable("fixture-export");
            }

            if (Aggregator.HasExceptions)
            {
                Exception failure = Aggregator.ToException();
                string? details = null;
                try { details = lifecycle.CaptureFailure(info, failure, timedOut); }
                catch (Exception ex) { details = $"Pharos could not save the failure artifacts: {ex.GetType().Name}: {ex.Message}"; }

                if (details != null)
                {
                    Aggregator.Clear();
                    Aggregator.Add(new ScenarioFailedException(failure, details));
                }
                if (ScenarioAttemptState.Current is { RetryableBodyFailure: true } attempt)
                    attempt.RetryableFailure = Aggregator.ToException();
            }
        }

        // Capture the body's state before cleanup can roll it back, then run cleanup on the
        // fixture-mode client's GL-owning thread. Base DisposeAsync remains an idempotent fallback.
        if (!abandonHost)
        {
            Exception? cleanupFailure = null;
            try
            {
                ScenarioThreadPump pump = threadPump ??= new ScenarioThreadPump();
                SynchronizationContext? previous = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(pump);
                    using CancellationTokenSource cleanupAbort = new(ScenarioTimeouts.Grace);
                    Task cleanup = lifecycle.CleanupEnvironmentsAsync(cleanupAbort.Token);
                    if (!pump.Run(cleanup, cleanupAbort.Token, ScenarioTimeouts.Grace))
                    {
                        abandonHost = true;
                        lifecycle.BodyTimedOut(stillRunning: true);
                        cleanupFailure = new TimeoutException("Scenario environment cleanup exceeded the grace period.");
                    }
                }
                finally { SynchronizationContext.SetSynchronizationContext(previous); }
            }
            catch (Exception ex) { cleanupFailure = ex; }

            if (cleanupFailure != null)
            {
                ScenarioAttemptState.Current?.MarkNonRetryable("cleanup");
                string? details = null;
                try { details = lifecycle.CaptureFailure(info, cleanupFailure, timedOut: false); }
                catch (Exception ex) { details = $"Pharos could not save cleanup failure artifacts: {ex.GetType().Name}: {ex.Message}"; }
                Aggregator.Add(details == null ? cleanupFailure : new ScenarioFailedException(cleanupFailure, details));
            }
        }

        return elapsed;
    }

    private static bool WasAborted(Exception exception) => exception switch
    {
        ScenarioAbortedException => true,
        AggregateException aggregate => aggregate.InnerExceptions.Any(WasAborted),
        _ => exception.InnerException is { } inner && WasAborted(inner),
    };

    /// <summary>Gives the pipeline the stock invocation of the test method.</summary>
    private sealed class BodyInvoker(
        ITest test, IMessageBus messageBus, Type testClass, object[] constructorArguments, MethodInfo testMethod, object[] testMethodArguments,
        IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource)
        : XunitTestInvoker(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, beforeAfterAttributes, aggregator, cancellationTokenSource)
    {
        public Task<decimal> InvokeBody(object testClassInstance) => base.InvokeTestMethodAsync(testClassInstance);

        protected override object CallTestMethod(object testClassInstance)
        {
            if (ScenarioAttemptState.Current is { } state) state.EnterBody();
            return base.CallTestMethod(testClassInstance);
        }
    }
}
