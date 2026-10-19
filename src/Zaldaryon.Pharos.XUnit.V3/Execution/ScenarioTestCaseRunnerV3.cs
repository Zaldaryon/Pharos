using Xunit.Sdk;
using Xunit.v3;

namespace Zaldaryon.Pharos.XUnit.V3.Execution;

/// <summary>
/// Runs a scenario test case in xUnit v3.
/// </summary>
public class ScenarioTestCaseRunnerV3 : XunitTestCaseRunner
{
    /// <summary>The singleton instance of the scenario test case runner.</summary>
    public static readonly new ScenarioTestCaseRunnerV3 Instance = new();

    /// <inheritdoc />
    protected override async ValueTask<RunSummary> RunTest(XunitTestCaseRunnerContext ctxt, IXunitTest test)
    {
        return await ScenarioTestRunnerV3.Instance.Run(
            test,
            ctxt.MessageBus,
            ctxt.ConstructorArguments,
            ctxt.ExplicitOption,
            ctxt.Aggregator.Clone(),
            ctxt.CancellationTokenSource,
            ctxt.BeforeAfterTestAttributes).ConfigureAwait(false);
    }
}
