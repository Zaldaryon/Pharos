using System.Diagnostics;
using System.Reflection;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Scenarios;

/// <summary>The final outcome of a scenario execution.</summary>
public enum ScenarioOutcome
{
    /// <summary>The scenario passed all attempts and assertions.</summary>
    Passed,
    /// <summary>The scenario failed one or more assertions or timed out.</summary>
    Failed,
    /// <summary>The scenario was skipped due to gates or dynamic skip.</summary>
    Skipped
}

/// <summary>Represents a timeout during scenario execution.</summary>
public sealed class ScenarioTimeoutException : TimeoutException
{
    /// <summary>The timeout duration in milliseconds.</summary>
    public int TimeoutMs { get; }

    /// <summary>Creates a new scenario timeout exception.</summary>
    public ScenarioTimeoutException(int timeoutMs)
        : base($"Scenario execution timed out after {timeoutMs} ms.")
    {
        TimeoutMs = timeoutMs;
    }
}

/// <summary>Internal signal used to turn a body-time skip into a skipped result.</summary>
public sealed class ScenarioSkipException(string reason) : Exception(reason);

/// <summary>Request parameters for executing a scenario test.</summary>
public sealed record ScenarioExecutionRequest
{
    /// <summary>The display name of the test, including any theory arguments.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The test class type.</summary>
    public required Type TestClass { get; init; }

    /// <summary>The test method to invoke.</summary>
    public required MethodInfo Method { get; init; }

    /// <summary>Method arguments for parameterized tests.</summary>
    public object?[]? Arguments { get; init; }

    /// <summary>Constructor arguments for the test class.</summary>
    public object?[]? ConstructorArguments { get; init; }

    /// <summary>Explicit timeout in milliseconds, or 0 to resolve from attributes.</summary>
    public int TimeoutMs { get; init; }

    /// <summary>Optional custom delegate to invoke the test body.</summary>
    public Func<object, Task>? BodyInvoker { get; init; }

    /// <summary>Callback invoked when failure artifacts are captured: (name, path).</summary>
    public Action<string, string>? OnArtifact { get; init; }

    /// <summary>Cancellation token for aborting execution.</summary>
    public CancellationToken CancellationToken { get; init; } = default;
}

/// <summary>The result of a scenario execution.</summary>
public sealed record ScenarioExecutionResult
{
    /// <summary>The outcome of the execution.</summary>
    public ScenarioOutcome Outcome { get; init; }

    /// <summary>The total execution duration.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>The failure exception, when Failed.</summary>
    public Exception? Exception { get; init; }

    /// <summary>The skip reason, when Skipped.</summary>
    public string? SkipReason { get; init; }

    /// <summary>The complete formatted output from the run.</summary>
    public required string Output { get; init; }

    /// <summary>Directories containing captured failure artifacts.</summary>
    public IReadOnlyList<string> ArtifactDirectories { get; init; } = [];

    /// <summary>History of all attempts executed.</summary>
    public IReadOnlyList<ScenarioAttemptEntry> AttemptHistory { get; init; } = [];

    /// <summary>Creates a skipped result.</summary>
    public static ScenarioExecutionResult SkippedResult(string reason, string output) => new()
    {
        Outcome = ScenarioOutcome.Skipped,
        Elapsed = TimeSpan.Zero,
        SkipReason = reason,
        Output = output,
    };

    /// <summary>Creates a failed result.</summary>
    public static ScenarioExecutionResult FailedResult(Exception exception, TimeSpan elapsed, string output, IReadOnlyList<string>? artifactDirs = null) => new()
    {
        Outcome = ScenarioOutcome.Failed,
        Elapsed = elapsed,
        Exception = exception,
        Output = output,
        ArtifactDirectories = artifactDirs ?? [],
    };

    /// <summary>Creates a passed result.</summary>
    public static ScenarioExecutionResult PassedResult(TimeSpan elapsed, string output) => new()
    {
        Outcome = ScenarioOutcome.Passed,
        Elapsed = elapsed,
        Output = output,
    };
}

/// <summary>
/// Framework-neutral scenario engine that executes Pharos scenario tests.
/// Manages host isolation, watchdogs and timeouts, gates, failure artifacts, and retry attempts.
/// </summary>
public static class ScenarioEngine
{
    /// <summary>
    /// Executes a scenario test request according to its attempt policy and attributes.
    /// </summary>
    public static async ValueTask<ScenarioExecutionResult> ExecuteAsync(ScenarioExecutionRequest request)
    {
        // 1. Evaluate gates
        ScenarioGateResult gate = ScenarioGate.Evaluate(request.TestClass, request.Method);
        if (gate.Exception != null)
        {
            string outText = ScenarioOutput.VersionBanner + gate.Exception.Message + Environment.NewLine;
            return ScenarioExecutionResult.FailedResult(gate.Exception, TimeSpan.Zero, outText);
        }
        if (gate.SkipReason != null)
        {
            string outText = ScenarioOutput.VersionBanner + gate.SkipReason + Environment.NewLine;
            return ScenarioExecutionResult.SkippedResult(gate.SkipReason, outText);
        }

        // 2. Read attempt policy
        ScenarioAttemptPolicy policy;
        try { policy = ScenarioAttemptPolicy.Read(request.Method); }
        catch (Exception ex)
        {
            string outText = ScenarioOutput.VersionBanner + ex.Message + Environment.NewLine;
            return ScenarioExecutionResult.FailedResult(ex, TimeSpan.Zero, outText);
        }

        // 3. Resolve random seed and timeout
        ScenarioRandomSeed? randomSeed = ScenarioRandomSeed.Resolve(request.TestClass, request.Method, request.Arguments);
        int timeoutMs = request.TimeoutMs > 0 ? request.TimeoutMs : ScenarioTimeouts.Of(request.Method);

        // 4. Run attempt loop
        List<ScenarioAttemptEntry> history = [];
        List<string> attemptOutputs = [];
        int successes = 0;
        TimeSpan totalTime = TimeSpan.Zero;
        Exception? lastFailure = null;
        bool skipped = false;
        string? skipReason = null;
        List<string> allArtifactDirs = [];

        for (int number = 1; number <= policy.Attempts; number++)
        {
            if (request.CancellationToken.IsCancellationRequested) break;

            ScenarioAttemptState state = new();
            ScenarioAttemptState? previousState = ScenarioAttemptState.Current;
            IsolationLog.Notes notes = new();
            IsolationLog.Notes? previousNotes = IsolationLog.Current;
            ScenarioTestInfo info = new(request.DisplayName, request.TestClass, request.Method.Name, request.Method)
            {
                RandomSeed = randomSeed
            };
            ScenarioTestInfo? previousInfo = ScenarioTestInfo.Current;

            ScenarioAttemptState.Current = state;
            IsolationLog.Current = notes;
            ScenarioTestInfo.Current = info;

            Stopwatch attemptWatch = Stopwatch.StartNew();
            Exception? attemptException = null;

            object? instance = null;
            try
            {
                instance = Activator.CreateInstance(request.TestClass, request.ConstructorArguments ?? Array.Empty<object>());
                if (instance is IScenarioLifecycle lifecycle)
                {
                    await lifecycle.InitializeAsync().ConfigureAwait(false);
                }

                Func<Task> bodyAction = request.BodyInvoker != null
                    ? () => request.BodyInvoker(instance!)
                    : () => InvokeMethodAsync(request.Method, instance!, request.Arguments);

                await ExecuteBodyAsync(
                    info,
                    instance as IScenarioLifecycle,
                    bodyAction,
                    timeoutMs,
                    ex => attemptException = Combine(attemptException, ex),
                    reason => { skipped = true; skipReason = reason; },
                    request.OnArtifact).ConfigureAwait(false);

                if (instance is IScenarioLifecycle disposableLifecycle)
                {
                    await disposableLifecycle.DisposeAsync().ConfigureAwait(false);
                }
                else if (instance is IAsyncDisposable asyncDisposable)
                {
                    await asyncDisposable.DisposeAsync().ConfigureAwait(false);
                }
                else if (instance is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
            catch (Exception ex)
            {
                attemptException = Combine(attemptException, ex);
                if (instance is IScenarioLifecycle fallbackLifecycle)
                {
                    try { await fallbackLifecycle.DisposeAsync().ConfigureAwait(false); } catch { /* best effort */ }
                }
            }
            finally
            {
                ScenarioAttemptState.Current = previousState;
                IsolationLog.Current = previousNotes;
                ScenarioTestInfo.Current = previousInfo;
            }

            attemptWatch.Stop();
            totalTime += attemptWatch.Elapsed;
            allArtifactDirs.AddRange(state.ArtifactDirectories);

            ClassifyTerminalFailure(state, attemptException);
            lastFailure = attemptException;
            attemptOutputs.Add(notes.Text);

            string outcome = (skipped && attemptException == null) ? "Skipped" : attemptException == null ? "Passed" : "Failed";
            if (attemptException == null && !skipped) successes++;

            history.Add(new ScenarioAttemptEntry(number, state.Phase, outcome, attemptWatch.Elapsed.TotalSeconds, attemptException?.ToString(), state.ArtifactDirectories.ToArray()));

            if (successes >= policy.RequiredSuccesses || skipped || state.NonRetryableFailure ||
                (attemptException != null && (!state.RetryableBodyFailure || !ContainsOnlyRetryableFailure(attemptException, state.RetryableFailure))) ||
                request.CancellationToken.IsCancellationRequested)
            {
                break;
            }
            if (successes + policy.Attempts - number < policy.RequiredSuccesses) break;
        }

        bool passed = successes >= policy.RequiredSuccesses;
        bool flaky = passed && history.Count > 1 && history.Take(history.Count - 1).Any(a => a.Outcome == "Failed");
        string finalOutcome = skipped ? "Skipped" : passed ? "Passed" : "Failed";
        string historyText = ScenarioOutput.FormatHistory(history.Count, policy.Attempts, policy.RequiredSuccesses, successes, flaky, history);

        // 5. Write attempt manifest
        ScenarioAttemptManifestWriter.Write(
            request.TestClass.Assembly.GetName().Name ?? "",
            $"{request.TestClass.FullName}.{request.Method.Name}",
            request.DisplayName,
            policy,
            history,
            finalOutcome,
            flaky);

        // 6. Build combined output
        string outputBody = policy.Attempts == 1
            ? (attemptOutputs.Count > 0 ? attemptOutputs[0] : "")
            : string.Join(Environment.NewLine, attemptOutputs.Select((text, index) => $"--- attempt {index + 1} output ---{Environment.NewLine}{text}")) + Environment.NewLine + historyText;

        string fullOutput = ScenarioOutput.VersionBanner + randomSeed?.OutputLine + outputBody;

        if (skipped)
        {
            return ScenarioExecutionResult.SkippedResult(skipReason ?? "Skipped", fullOutput);
        }

        if (passed)
        {
            return ScenarioExecutionResult.PassedResult(totalTime, fullOutput);
        }

        lastFailure ??= new InvalidOperationException($"Scenario required {policy.RequiredSuccesses} successful attempts but got {successes} of {history.Count}.");
        Exception finalFailure = (successes == 0 && history.Count > 0) ? lastFailure : new InvalidOperationException(historyText, lastFailure);
        return ScenarioExecutionResult.FailedResult(finalFailure, totalTime, fullOutput, allArtifactDirs);
    }

    /// <summary>
    /// Executes the scenario body, watchdog, logged error checks, failure artifacts, and environment cleanup.
    /// This method is shared by both the standalone engine and framework adapters (e.g. xUnit v2).
    /// </summary>
    public static async Task<decimal> ExecuteBodyAsync(
        ScenarioTestInfo info,
        IScenarioLifecycle? lifecycle,
        Func<Task> invokeBody,
        int timeoutMs,
        Action<Exception> reportException,
        Action<string>? reportDynamicSkip = null,
        Action<string, string>? onArtifact = null)
    {
        if (lifecycle != null)
        {
            try { lifecycle.BeforeBody(); }
            catch (Exception ex)
            {
                ScenarioAttemptState.Current?.MarkNonRetryable();
                reportException(ex);
                return 0m;
            }
        }

        CancellationTokenSource abort = new();
        AbortScope scope = new(abort.Token);
        using Timer? watchdog = timeoutMs > 0 ? new Timer(_ => abort.Cancel(), null, timeoutMs, Timeout.Infinite) : null;
        ScenarioThreadPump pump = new();
        bool timedOut = false;
        bool timeoutReported = false;
        bool abandonHost = false;
        Exception? bodyException = null;

        Stopwatch bodyWatch = Stopwatch.StartNew();
        ScenarioAbort.Enter(scope);
        try
        {
            Exception? preparationException = null;
            if (lifecycle != null)
            {
                SynchronizationContext? previous = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(pump);
                    Task prepare = lifecycle.ApplyEnvironmentsAsync(abort.Token);
                    if (prepare != null && !pump.Run(prepare, abort.Token, ScenarioTimeouts.Grace))
                    {
                        ScenarioAttemptState.Current?.MarkNonRetryable("timeout");
                        lifecycle.BodyTimedOut(stillRunning: true);
                        abandonHost = true;
                        timeoutReported = true;
                        timedOut = true;
                        preparationException = new ScenarioTimeoutException(timeoutMs);
                    }
                }
                catch (OperationCanceledException) when (abort.IsCancellationRequested)
                {
                    ScenarioAttemptState.Current?.MarkNonRetryable("timeout");
                    lifecycle.BodyTimedOut(stillRunning: false);
                    timeoutReported = true;
                    timedOut = true;
                    preparationException = new ScenarioTimeoutException(timeoutMs);
                }
                catch (Exception ex)
                {
                    preparationException = ex;
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            }

            if (preparationException != null)
            {
                ScenarioAttemptState.Current?.MarkNonRetryable();
                bodyException = preparationException;
            }
            else
            {
                SynchronizationContext? previous = SynchronizationContext.Current;
                try
                {
                    SynchronizationContext.SetSynchronizationContext(pump);
                    ScenarioAttemptState.Current?.EnterBody();
                    Task bodyTask = invokeBody();
                    if (!pump.Run(bodyTask, abort.Token, ScenarioTimeouts.Grace))
                    {
                        ScenarioAttemptState.Current?.MarkNonRetryable("timeout");
                        lifecycle?.BodyTimedOut(stillRunning: true);
                        abandonHost = true;
                        timeoutReported = true;
                        timedOut = true;
                        bodyException = new ScenarioAbortedException();
                    }
                }
                catch (Exception ex)
                {
                    bodyException = ex;
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            }
        }
        finally
        {
            ScenarioAbort.Enter(null);
        }

        bodyWatch.Stop();
        decimal elapsedSeconds = (decimal)bodyWatch.Elapsed.TotalSeconds;

        if (abort.IsCancellationRequested)
        {
            timedOut = true;
            ScenarioAttemptState.Current?.MarkNonRetryable("timeout");
            abandonHost |= scope.Wedged;
            if (!timeoutReported) lifecycle?.BodyTimedOut(stillRunning: abandonHost);
            reportException(new ScenarioTimeoutException(timeoutMs));
        }
        else if (bodyException != null)
        {
            Exception actual = UnwrapTargetInvocation(bodyException);
            if (actual is ScenarioSkipException || actual.GetType().Name == "SkipException")
            {
                if (ScenarioAttemptState.Current is { } state) state.Skipped = true;
                if (ScenarioTestInfo.Current is { } cur) cur.DynamicSkipReason = actual.Message;
                reportDynamicSkip?.Invoke(actual.Message);
            }
            else
            {
                if (ScenarioAttemptState.Current is { } state)
                {
                    if (state.BodyEntered) state.RetryableBodyFailure = true;
                    else state.MarkNonRetryable();
                    if (state.RetryableBodyFailure) state.RetryableFailure = actual;
                }
                reportException(actual);
            }
        }

        if (lifecycle == null || ScenarioTestInfo.Current?.DynamicSkipReason != null)
        {
            return elapsedSeconds;
        }

        if (bodyException == null && !timedOut)
        {
            try
            {
                lifecycle.CheckLoggedErrors();
            }
            catch (Exception ex)
            {
                ScenarioAttemptState.Current?.MarkNonRetryable("logged-errors");
                reportException(ex);
                bodyException = ex;
            }

            if (bodyException == null && FixtureExport.Requested(info) is { } destination)
            {
                try
                {
                    await FixtureExport.SaveAsync(lifecycle.FixtureServer, info, destination).ConfigureAwait(false);
                    lifecycle.CheckLoggedErrors();
                }
                catch (Exception ex)
                {
                    ScenarioAttemptState.Current?.MarkNonRetryable("fixture-export");
                    reportException(ex);
                    bodyException = ex;
                }
            }
        }

        if (bodyException != null || timedOut)
        {
            Exception failureToReport = bodyException ?? new ScenarioTimeoutException(timeoutMs);
            string? details = null;
            try
            {
                details = lifecycle.CaptureFailure(info, failureToReport, timedOut);
                if (onArtifact != null && ScenarioAttemptState.Current?.ArtifactDirectories is { Count: > 0 } dirs)
                {
                    foreach (string dir in dirs)
                    {
                        if (Directory.Exists(dir))
                        {
                            foreach (string file in Directory.GetFiles(dir))
                            {
                                onArtifact(Path.GetFileName(file), file);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                details = $"Pharos could not save the failure artifacts: {ex.GetType().Name}: {ex.Message}";
            }

            if (details != null && !timedOut)
            {
                reportException(new ScenarioFailedException(failureToReport, details));
            }
        }

        if (!abandonHost)
        {
            Exception? cleanupFailure = null;
            try
            {
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
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(previous);
                }
            }
            catch (Exception ex) { cleanupFailure = ex; }

            if (cleanupFailure != null)
            {
                ScenarioAttemptState.Current?.MarkNonRetryable("cleanup");
                string? details = null;
                try { details = lifecycle.CaptureFailure(info, cleanupFailure, timedOut: false); }
                catch (Exception ex) { details = $"Pharos could not save cleanup failure artifacts: {ex.GetType().Name}: {ex.Message}"; }
                reportException(details == null ? cleanupFailure : new ScenarioFailedException(cleanupFailure, details));
            }
        }

        return elapsedSeconds;
    }

    private static async Task InvokeMethodAsync(MethodInfo method, object instance, object?[]? arguments)
    {
        object? result = method.Invoke(instance, arguments);
        if (result is Task task)
        {
            await task.ConfigureAwait(false);
        }
        else if (result is ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(false);
        }
    }

    private static Exception UnwrapTargetInvocation(Exception ex) =>
        ex is TargetInvocationException tie && tie.InnerException != null ? tie.InnerException : ex;

    private static Exception Combine(Exception? existing, Exception current)
    {
        if (existing == null) return current;
        if (existing is AggregateException agg) return new AggregateException(agg.InnerExceptions.Append(current));
        return new AggregateException(existing, current);
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
        if (failure != null && state.Phase == "body" &&
            (!state.RetryableBodyFailure || !ContainsOnlyRetryableFailure(failure, state.RetryableFailure)))
        {
            state.MarkNonRetryable("teardown");
        }
    }
}
