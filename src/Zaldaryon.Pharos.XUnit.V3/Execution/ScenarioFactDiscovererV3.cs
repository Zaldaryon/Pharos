using Xunit.Sdk;
using Xunit.v3;

namespace Zaldaryon.Pharos.XUnit.V3.Execution;

/// <summary>
/// Discovers scenario facts for xUnit v3.
/// </summary>
public sealed class ScenarioFactDiscovererV3 : FactDiscoverer
{
    /// <inheritdoc />
    protected override IXunitTestCase CreateTestCase(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        IXunitTestMethod testMethod,
        IFactAttribute factAttribute)
    {
        IXunitTestCase standard = base.CreateTestCase(discoveryOptions, testMethod, factAttribute);
        if (standard is XunitTestCase xutc)
        {
            return new ScenarioTestCaseV3(
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
                xutc.Timeout);
        }
        return standard;
    }
}
