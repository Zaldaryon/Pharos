using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.XUnit;

#pragma warning disable xUnit1000 // These scenario fixtures are invoked only by ScenarioRunnerHarness.
internal sealed class ScenarioSkipRunnerFixtures
{
    [ClientScenario(Explicit = true)]
    public async Task DynamicSkip()
    {
        await Task.Yield();
        PharosAssert.Skip("mod absent");
    }

    [ClientScenario(Explicit = true)]
    public void ExplicitPasses() { }

    [ClientTheory]
    [InlineData(1)]
    [InlineData(2)]
    public void DynamicSkipRows(int row) => PharosAssert.Skip($"row {row} unavailable");

    public static Type TeardownFailureFixture => typeof(SkipThenDisposeFails);

    private sealed class SkipThenDisposeFails : IDisposable
    {
        [ClientScenario]
        public void Skip() => PharosAssert.Skip("body requested skip");

        public void Dispose() => throw new InvalidOperationException("teardown failed");
    }
}
#pragma warning restore xUnit1000
