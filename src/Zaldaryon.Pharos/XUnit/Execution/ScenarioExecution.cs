using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Zaldaryon.Pharos.XUnit.Execution;

/// <summary>The test a scenario runs, as the failure capture names it.</summary>
/// <param name="DisplayName">The display name, theory arguments included.</param>
/// <param name="TestClass">The test class.</param>
/// <param name="MethodName">The test method's name.</param>
internal sealed record ScenarioTestInfo(string DisplayName, Type TestClass, string MethodName)
{
    private static readonly AsyncLocal<ScenarioTestInfo?> s_current = new();

    /// <summary>The scenario test running on this flow, set before its class is created.</summary>
    public static ScenarioTestInfo? Current
    {
        get => s_current.Value;
        internal set => s_current.Value = value;
    }
}

/// <summary>
/// What the scenario pipeline asks of a scenario class: implemented by the scenario base classes.
/// </summary>
internal interface IScenarioLifecycle
{
    /// <summary>Called just before the test body runs, after the class is initialized.</summary>
    void BeforeBody();

    /// <summary>Throws when the scenario logged errors it does not allow; called when the body passed.</summary>
    void CheckLoggedErrors();

    /// <summary>
    /// Called when the body ran out of time. <paramref name="stillRunning"/> when it did not stop
    /// after it was aborted: its host must be abandoned, never disposed or reused.
    /// </summary>
    void BodyTimedOut(bool stillRunning);

    /// <summary>
    /// Saves the failure artifacts while the client and server are still up. Returns the text to
    /// add to the failure message, or null to leave the failure as it is.
    /// </summary>
    string? CaptureFailure(ScenarioTestInfo test, Exception exception, bool timedOut);
}

/// <summary>The watchdog timeouts of scenario attributes.</summary>
internal static class ScenarioTimeouts
{
    /// <summary>Multiplies every scenario timeout, for slow machines: <c>PHAROS_TIMEOUT_SCALE=2</c>.</summary>
    public const string ScaleVariable = "PHAROS_TIMEOUT_SCALE";

    /// <summary>How long an aborted body gets to stop before its host is given up.</summary>
    internal static TimeSpan Grace { get; set; } = TimeSpan.FromSeconds(15);

    private static readonly AsyncLocal<int?> s_override = new();

    /// <summary>Replaces the attribute's timeout on this flow, in milliseconds: the smoke runner's <c>--timeout</c>.</summary>
    internal static int? Override
    {
        get => s_override.Value;
        set => s_override.Value = value;
    }

    /// <summary>
    /// The timeout of a scenario test in milliseconds, 0 for none: the attribute's xUnit
    /// <c>Timeout</c> when set, else its <c>TimeoutMs</c>, scaled by <c>PHAROS_TIMEOUT_SCALE</c>.
    /// </summary>
    public static int Of(ITestMethod testMethod)
    {
        if (Override is { } overridden) return overridden;

        IAttributeInfo? attribute = testMethod.Method.GetCustomAttributes(typeof(FactAttribute)).FirstOrDefault();
        if (attribute == null) return 0;

        int timeout = Named(attribute, nameof(FactAttribute.Timeout));
        if (timeout <= 0) timeout = Named(attribute, "TimeoutMs");
        if (timeout <= 0) return 0;

        return (int)Math.Clamp(timeout * Scale, 1, int.MaxValue);
    }

    /// <summary>The factor <c>PHAROS_TIMEOUT_SCALE</c> sets, 1 when unset.</summary>
    internal static double Scale =>
        double.TryParse(Environment.GetEnvironmentVariable(ScaleVariable), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) && scale > 0
            ? scale
            : 1;

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
    internal const string AssemblyName = "Zaldaryon.Pharos";

    /// <inheritdoc />
    protected override IXunitTestCase CreateTestCase(ITestFrameworkDiscoveryOptions discoveryOptions, ITestMethod testMethod, IAttributeInfo factAttribute) =>
        new ScenarioTestCase(DiagnosticMessageSink, discoveryOptions.MethodDisplayOrDefault(), discoveryOptions.MethodDisplayOptionsOrDefault(), testMethod);
}

/// <summary>Discovers <c>[ClientTheory]</c> and <c>[ServerTheory]</c> tests.</summary>
public sealed class ScenarioTheoryDiscoverer(IMessageSink diagnosticMessageSink) : TheoryDiscoverer(diagnosticMessageSink)
{
    internal const string TypeName = "Zaldaryon.Pharos.XUnit.Execution.ScenarioTheoryDiscoverer";
    internal const string AssemblyName = "Zaldaryon.Pharos";

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

    /// <inheritdoc />
    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        new ScenarioTestCaseRunner(this, DisplayName, SkipReason, constructorArguments, TestMethodArguments, messageBus, aggregator, cancellationTokenSource, ScenarioTimeouts.Of(TestMethod)).RunAsync();
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

    /// <inheritdoc />
    public override Task<RunSummary> RunAsync(IMessageSink diagnosticMessageSink, IMessageBus messageBus, object[] constructorArguments, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        new ScenarioTheoryTestCaseRunner(this, DisplayName, SkipReason, constructorArguments, diagnosticMessageSink, messageBus, aggregator, cancellationTokenSource, ScenarioTimeouts.Of(TestMethod)).RunAsync();
}

internal sealed class ScenarioTestCaseRunner(
    IXunitTestCase testCase, string displayName, string skipReason, object[] constructorArguments, object[] testMethodArguments,
    IMessageBus messageBus, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource, int timeoutMs)
    : XunitTestCaseRunner(testCase, displayName, skipReason, constructorArguments, testMethodArguments, messageBus, aggregator, cancellationTokenSource)
{
    protected override XunitTestRunner CreateTestRunner(ITest test, IMessageBus messageBus, Type testClass, object[] constructorArguments, MethodInfo testMethod, object[] testMethodArguments, string skipReason, IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        new ScenarioTestRunner(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, skipReason, beforeAfterAttributes, new ExceptionAggregator(aggregator), cancellationTokenSource, timeoutMs);
}

internal sealed class ScenarioTheoryTestCaseRunner(
    IXunitTestCase testCase, string displayName, string skipReason, object[] constructorArguments, IMessageSink diagnosticMessageSink,
    IMessageBus messageBus, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource, int timeoutMs)
    : XunitTheoryTestCaseRunner(testCase, displayName, skipReason, constructorArguments, diagnosticMessageSink, messageBus, aggregator, cancellationTokenSource)
{
    protected override XunitTestRunner CreateTestRunner(ITest test, IMessageBus messageBus, Type testClass, object[] constructorArguments, MethodInfo testMethod, object[] testMethodArguments, string skipReason, IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, ExceptionAggregator aggregator, CancellationTokenSource cancellationTokenSource) =>
        new ScenarioTestRunner(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, skipReason, beforeAfterAttributes, new ExceptionAggregator(aggregator), cancellationTokenSource, timeoutMs);
}

internal sealed class ScenarioTestRunner(
    ITest test, IMessageBus messageBus, Type testClass, object[] constructorArguments, MethodInfo testMethod, object[] testMethodArguments,
    string skipReason, IReadOnlyList<BeforeAfterTestAttribute> beforeAfterAttributes, ExceptionAggregator aggregator,
    CancellationTokenSource cancellationTokenSource, int timeoutMs)
    : XunitTestRunner(test, messageBus, testClass, constructorArguments, testMethod, testMethodArguments, skipReason, beforeAfterAttributes, aggregator, cancellationTokenSource)
{
    // By the time the base returns, the class has torn down: its isolation lines are complete.
    // They go to the test's output, where test explorers and trx files show it.
    protected override async Task<Tuple<decimal, string>> InvokeTestAsync(ExceptionAggregator aggregator)
    {
        IsolationLog.Notes notes = new();
        IsolationLog.Current = notes;
        Tuple<decimal, string> result = await base.InvokeTestAsync(aggregator).ConfigureAwait(false);
        return Tuple.Create(result.Item1, result.Item2 + notes.Text);
    }

    protected override async Task<decimal> InvokeTestMethodAsync(ExceptionAggregator aggregator)
    {
        // Async, so the test info stays on this test's flow: the class's InitializeAsync reads it
        // to save artifacts when the scenario cannot even start.
        ScenarioTestInfo.Current = new ScenarioTestInfo(Test.DisplayName, TestClass, TestMethod.Name);
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
            if (Aggregator.HasExceptions) return 0m;
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
        ScenarioAbort.Enter(scope);
        try
        {
            run = body.InvokeBody(testClassInstance);
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

        bool timedOut;
        decimal elapsed;
        if (!run.IsCompleted && abort.IsCancellationRequested)
        {
            // Aborted while still running: it gets a grace period to reach its next step.
            bool stopped = await Task.WhenAny(run, Task.Delay(ScenarioTimeouts.Grace)).ConfigureAwait(false) == run;
            timedOut = true;
            watchdog?.Dispose();
            lifecycle?.BodyTimedOut(stillRunning: !stopped || scope.Wedged);
            Aggregator.Add(new TestTimeoutException(timeoutMs));
            elapsed = timeoutMs / 1000m;
        }
        else
        {
            elapsed = await run.ConfigureAwait(false);
            watchdog?.Dispose();

            // A timeout only when the body was stopped by the abort, or left the game stuck even
            // if it swallowed the abort, not when the timer fired as it finished on its own.
            timedOut = abort.IsCancellationRequested && (scope.Wedged || (bodyAggregator.HasExceptions && WasAborted(bodyAggregator.ToException())));
            if (timedOut)
            {
                // The body ran out of time and stopped at its next step: a timeout, not whatever
                // the abort made it throw.
                lifecycle?.BodyTimedOut(stillRunning: scope.Wedged);
                Aggregator.Add(new TestTimeoutException(timeoutMs));
            }
            else if (bodyAggregator.HasExceptions)
            {
                Aggregator.Add(bodyAggregator.ToException());
            }
        }

        // The body ran on an invoker of its own: its time is this test's time.
        Timer.Aggregate(TimeSpan.FromSeconds((double)elapsed));

        if (lifecycle == null) return elapsed;

        if (!Aggregator.HasExceptions)
        {
            Aggregator.Run(lifecycle.CheckLoggedErrors);
        }

        if (Aggregator.HasExceptions)
        {
            Exception failure = Aggregator.ToException();
            string? details = null;
            try
            {
                details = lifecycle.CaptureFailure(new ScenarioTestInfo(Test.DisplayName, TestClass, TestMethod.Name), failure, timedOut);
            }
            catch (Exception ex)
            {
                details = $"Pharos could not save the failure artifacts: {ex.GetType().Name}: {ex.Message}";
            }

            if (details != null)
            {
                Aggregator.Clear();
                Aggregator.Add(new ScenarioFailedException(failure, details));
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
    }
}
