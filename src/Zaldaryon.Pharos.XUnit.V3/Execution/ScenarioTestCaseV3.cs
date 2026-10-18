using Xunit.Sdk;
using Xunit.v3;
using Zaldaryon.Pharos.Scenarios;

namespace Zaldaryon.Pharos.XUnit.V3.Execution;

/// <summary>
/// Represents a self-executing Pharos scenario test case in xUnit v3.
/// </summary>
public class ScenarioTestCaseV3 : XunitTestCase, ISelfExecutingXunitTestCase
{
    /// <summary>Initializes a new instance for deserialization.</summary>
    [Obsolete("Called by the de-serializer; should only be called for de-serialization purposes.")]
    public ScenarioTestCaseV3() { }

    /// <summary>Initializes a new instance of the ScenarioTestCaseV3 class.</summary>
    public ScenarioTestCaseV3(
        IXunitTestMethod testMethod,
        string testCaseDisplayName,
        string uniqueID,
        bool @explicit,
        Type[]? skipExceptions,
        string? skipReason,
        Type? skipType,
        string? skipUnless,
        string? skipWhen,
        Dictionary<string, HashSet<string>>? traits,
        object?[]? testMethodArguments,
        string? sourceFilePath,
        int? sourceLineNumber,
        int? timeout)
        : base(
            testMethod,
            testCaseDisplayName,
            uniqueID,
            @explicit,
            skipExceptions is { Length: > 0 }
                ? [.. skipExceptions, typeof(ScenarioSkipException)]
                : [typeof(ScenarioSkipException)],
            skipReason,
            skipType,
            skipUnless,
            skipWhen,
            traits,
            testMethodArguments!,
            sourceFilePath,
            sourceLineNumber,
            timeout)
    {
    }

    /// <inheritdoc />
    public async ValueTask<RunSummary> Run(
        ExplicitOption explicitOption,
        IMessageBus messageBus,
        object?[] constructorArguments,
        ExceptionAggregator aggregator,
        CancellationTokenSource cancellationTokenSource)
    {
        var tests = await CreateTests().ConfigureAwait(false);
        return await ScenarioTestCaseRunnerV3.Instance.Run(
            this,
            tests,
            messageBus,
            aggregator,
            cancellationTokenSource,
            TestCaseDisplayName,
            SkipReason,
            explicitOption,
            constructorArguments!).ConfigureAwait(false);
    }
}
