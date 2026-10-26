using System.Reflection;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;
using Zaldaryon.Pharos.Scenarios;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.XUnit.V3.Execution;

/// <summary>
/// Runs an individual scenario test in xUnit v3, managing the scenario lifecycle,
/// watchdog timeouts, gates, dynamic skips, and attaching failure artifacts to TestContext.
/// </summary>
public class ScenarioTestRunnerV3 : XunitTestRunner
{
    /// <summary>The singleton instance of the scenario test runner.</summary>
    public static readonly new ScenarioTestRunnerV3 Instance = new();

    /// <summary>The maximum size for a single attachment in bytes (10 MB).</summary>
    public const long MaxAttachmentSize = 10L * 1024 * 1024;

    /// <inheritdoc />
    protected override async ValueTask<TimeSpan> InvokeTest(XunitTestRunnerContext ctxt, object? testClassInstance)
    {
        Type testClass = ctxt.TestMethod.DeclaringType!;
        MethodInfo method = ctxt.TestMethod;

        // 1. Evaluate gates
        var gate = ScenarioGate.Evaluate(testClass, method);
        if (gate.Exception != null)
        {
            ctxt.Aggregator.Add(gate.Exception);
            return TimeSpan.Zero;
        }
        if (gate.SkipReason != null)
        {
            throw new ScenarioSkipException(gate.SkipReason);
        }

        // 2. Read attempt policy and timeout
        ScenarioAttemptPolicy policy = ScenarioAttemptPolicy.Read(method);
        int timeoutMs = ScenarioTimeouts.Of(method);

        // Get the test's cancellation token from TestContext
        CancellationToken testToken = TestContext.Current?.CancellationToken ?? CancellationToken.None;
        using CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
            testToken, ctxt.CancellationTokenSource.Token);

        Action<string, string> onArtifact = (name, path) => AttachArtifact(name, path);

        // 3. Single attempt path (default)
        if (policy.Attempts <= 1)
        {
            return await RunAttemptAsync(ctxt, testClassInstance, method, timeoutMs, onArtifact, linkedCts.Token).ConfigureAwait(false);
        }

        // 4. Multiple attempts retry loop
        TimeSpan totalTime = TimeSpan.Zero;
        int successes = 0;
        Exception? lastFailure = null;

        for (int attempt = 1; attempt <= policy.Attempts; attempt++)
        {
            if (linkedCts.IsCancellationRequested) break;

            object? currentInstance = attempt == 1
                ? testClassInstance
                : Activator.CreateInstance(testClass, ctxt.ConstructorArguments ?? Array.Empty<object>());

            ExceptionAggregator attemptAggregator = new();
            XunitTestRunnerContext attemptContext = new(
                ctxt.Test,
                ctxt.MessageBus,
                ctxt.ExplicitOption,
                attemptAggregator,
                ctxt.CancellationTokenSource,
                ctxt.BeforeAfterTestAttributes,
                ctxt.ConstructorArguments!);

            TimeSpan attemptTime = await RunAttemptAsync(attemptContext, currentInstance, method, timeoutMs, onArtifact, linkedCts.Token).ConfigureAwait(false);
            totalTime += attemptTime;

            if (!attemptAggregator.HasExceptions)
            {
                successes++;
                if (successes >= policy.RequiredSuccesses)
                {
                    return totalTime;
                }
            }
            else
            {
                lastFailure = attemptAggregator.ToException();
                if (ScenarioAttemptState.Current is { NonRetryableFailure: true })
                {
                    break;
                }
            }
        }

        if (successes < policy.RequiredSuccesses && lastFailure != null)
        {
            ctxt.Aggregator.Add(lastFailure);
        }

        return totalTime;
    }

    private static async ValueTask<TimeSpan> RunAttemptAsync(
        XunitTestRunnerContext ctxt,
        object? instance,
        MethodInfo method,
        int timeoutMs,
        Action<string, string> onArtifact,
        CancellationToken ct)
    {
        IScenarioLifecycle? lifecycle = instance as IScenarioLifecycle;
        bool hasNativeAsyncLifetime = instance is Xunit.IAsyncLifetime;
        ScenarioRandomSeed? randomSeed = ScenarioRandomSeed.Resolve(method.DeclaringType!, method, ctxt.TestMethodArguments);
        ScenarioTestInfo info = new(ctxt.Test.TestDisplayName, method.DeclaringType!, method.Name, method)
        {
            RandomSeed = randomSeed,
            MethodArguments = [.. ctxt.TestMethodArguments],
            CancellationToken = ct,
        };
        ScenarioAttemptState state = new();
        IsolationLog.Notes notes = new();

        ScenarioTestInfo? previousInfo = ScenarioTestInfo.Current;
        ScenarioAttemptState? previousState = ScenarioAttemptState.Current;
        IsolationLog.Notes? previousNotes = IsolationLog.Current;

        ScenarioTestInfo.Current = info;
        ScenarioAttemptState.Current = state;
        IsolationLog.Current = notes;

        bool initialized = false;
        try
        {
            if (lifecycle != null && !hasNativeAsyncLifetime)
            {
                try
                {
                    await lifecycle.InitializeAsync().ConfigureAwait(false);
                    initialized = true;
                }
                catch (Exception ex)
                {
                    ctxt.Aggregator.Add(ex);
                    return TimeSpan.Zero;
                }
            }

            decimal elapsed = await ScenarioEngine.ExecuteBodyAsync(
                info,
                lifecycle,
                () =>
                {
                    object? result = method.Invoke(instance, ctxt.TestMethodArguments);
                    return result switch
                    {
                        Task t => t,
                        ValueTask vt => vt.AsTask(),
                        _ => Task.CompletedTask
                    };
                },
                timeoutMs,
                ex => ctxt.Aggregator.Add(ex),
                reason => throw new ScenarioSkipException(reason),
                onArtifact,
                ct).ConfigureAwait(false);

            // Add isolation warnings to TestContext
            AddIsolationWarnings(notes);

            return TimeSpan.FromSeconds((double)elapsed);
        }
        finally
        {
            if (initialized && lifecycle != null)
            {
                try { await lifecycle.DisposeAsync().ConfigureAwait(false); } catch { }
            }
            ScenarioTestInfo.Current = previousInfo;
            ScenarioAttemptState.Current = previousState;
            IsolationLog.Current = previousNotes;
        }
    }

    /// <summary>
    /// Attaches an artifact file to the test context, respecting size limits.
    /// </summary>
    private static void AttachArtifact(string name, string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            FileInfo fileInfo = new(path);
            if (fileInfo.Length > MaxAttachmentSize)
            {
                TestContext.Current?.AddWarning($"Artifact '{name}' exceeds {MaxAttachmentSize / (1024 * 1024)} MB limit and was not attached.");
                return;
            }

            string mediaType = GetMediaType(path);
            TestContext.Current?.AddAttachment(name, File.ReadAllBytes(path), mediaType);
        }
        catch (Exception ex)
        {
            TestContext.Current?.AddWarning($"Failed to attach artifact '{name}': {ex.Message}");
        }
    }

    /// <summary>
    /// Gets the media type for a file based on its extension.
    /// </summary>
    private static string GetMediaType(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".txt" or ".log" => "text/plain",
            ".csv" => "text/csv",
            ".html" => "text/html",
            _ => "application/octet-stream"
        };
    }

    /// <summary>
    /// Adds isolation warnings from the isolation log to the test context.
    /// </summary>
    private static void AddIsolationWarnings(IsolationLog.Notes notes)
    {
        if (TestContext.Current == null) return;

        string text = notes.Text;
        if (string.IsNullOrWhiteSpace(text)) return;

        foreach (string line in text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("isolation:", StringComparison.OrdinalIgnoreCase) &&
                (line.Contains("booted again", StringComparison.OrdinalIgnoreCase) ||
                 line.Contains("restarted", StringComparison.OrdinalIgnoreCase)))
            {
                TestContext.Current.AddWarning(line);
            }
        }
    }
}
