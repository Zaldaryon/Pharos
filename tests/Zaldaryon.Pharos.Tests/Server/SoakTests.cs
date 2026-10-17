using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>
/// Soak tests: game days on a real server with a player, in the nightly run only. Each test restarts
/// the server, so nothing a test leaves behind reaches the next one.
/// </summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat", Isolation = Isolation.Restart)]
public class SoakTests : ServerScenarioBase
{
    // Speed 120 is 86,400 ticks for three days. At speed 60 it is 172,800 ticks, and at the measured 33 ms
    // per tick that does not fit the nightly budget.
    [ServerScenario(TimeoutMs = 3_600_000), Soak]
    public async Task ThreeDays_NoLeaks()
    {
        // A player makes random block ticks and the creature spawner run, so the soak sees their work.
        await CreateTestPlayerAsync(PlayerNames.Next());

        SoakReport report = await Host!.Simulate.DaysAsync(3, speed: 120, monitor: m => m
            .TickMs.P99Below(50)
            .ManagedMemoryGrowthBelow(64_000_000)
            .EntitiesBelow(500)
            .TickListenersStable());

        report.SaveCsv(SoakReport.ArtifactPath("ThreeDays_NoLeaks.csv"));
        Assert.True(report.Completed);
        Assert.Equal(3, Math.Round(report.CalendarDays));
    }
}
