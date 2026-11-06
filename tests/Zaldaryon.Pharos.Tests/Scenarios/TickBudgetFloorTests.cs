using Xunit;
using Zaldaryon.Pharos.Scenarios;

namespace Zaldaryon.Pharos.Tests.Scenarios;

public class TickBudgetFloorTests
{
    [Fact]
    public void SmallBudgetsKeepASmallFloor()
    {
        // A 1-tick join must still time out: CrowdFailure_CleansOnlyNewPlayers relies on it.
        Assert.Equal(TimeSpan.FromMilliseconds(15 * ScenarioTimeouts.Scale), ScenarioTimeouts.TickBudgetFloor(1));
        Assert.Equal(TimeSpan.Zero, ScenarioTimeouts.TickBudgetFloor(0));
    }

    [Fact]
    public void LargeBudgetsAreCappedAtThirtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(30 * ScenarioTimeouts.Scale), ScenarioTimeouts.TickBudgetFloor(2000));
        Assert.Equal(TimeSpan.FromSeconds(30 * ScenarioTimeouts.Scale), ScenarioTimeouts.TickBudgetFloor(100_000));
    }
}
