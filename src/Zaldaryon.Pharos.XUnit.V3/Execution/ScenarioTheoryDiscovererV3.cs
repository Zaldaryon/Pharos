using Xunit;
using Xunit.Sdk;
using Xunit.v3;

namespace Zaldaryon.Pharos.XUnit.V3.Execution;

/// <summary>
/// Discovers scenario theories for xUnit v3.
/// </summary>
public sealed class ScenarioTheoryDiscovererV3 : TheoryDiscoverer
{
    /// <inheritdoc />
    protected override async ValueTask<IReadOnlyCollection<IXunitTestCase>> CreateTestCasesForDataRow(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        ITheoryAttribute theoryAttribute,
        ITheoryDataRow dataRow,
        object?[]? testMethodArguments)
    {
        var standardCases = await base.CreateTestCasesForDataRow(discoveryOptions, testMethod, theoryAttribute, dataRow, testMethodArguments!).ConfigureAwait(false);
        return standardCases.Select(tc => tc is XunitTestCase xutc ? new ScenarioTestCaseV3(
            xutc.TestMethod,
            xutc.TestCaseDisplayName,
            xutc.UniqueID,
            xutc.Explicit,
            xutc.SkipExceptions,
            xutc.SkipReason,
            xutc.SkipType,
            xutc.SkipUnless,
            xutc.SkipWhen,
            xutc.Traits,
            xutc.TestMethodArguments,
            xutc.SourceFilePath,
            xutc.SourceLineNumber,
            xutc.Timeout) : tc).ToList();
    }
}
