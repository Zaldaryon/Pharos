using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Playing the server forward in game days, on the real server.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class ServerSimulationTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task OneDayAtSpeed1440_RunsTheTicksTheDayNeeds()
    {
        SoakReport report = await Host!.Simulate.DaysAsync(1, speed: 1440);

        // At speed 1440 a day is 2400 ticks of 50 ms: 86400 game seconds, 36 per tick. Opening the
        // clock costs the calendar its first 250 ms (5 ticks), measured at 2405 ticks for one day, so the
        // run ends on the calendar's day with those ticks added.
        Assert.True(report.Completed);
        Assert.InRange(report.Ticks, 2400, 2405);

        // The run ends on the first tick that reaches the day, so it overshoots by less than one tick (36 game seconds).
        Assert.InRange(report.CalendarDays, 1 - 1e-6, 1 + 1.0 / 2400);
        Assert.Equal((long)report.TickTime.Count, report.Ticks);
    }

    [ServerScenario]
    public async Task ASampleEachGameHour_AndACheckpointEachDay()
    {
        SoakReport report = await Host!.Simulate.DaysAsync(1, speed: 1440);

        // One game hour at speed 1440 is 100 ticks: a sample per 100 ticks, the last one partial, and a
        // checkpoint each game day and at the end.
        Assert.Equal((int)Math.Ceiling(report.Ticks / 100.0), report.Samples.Count);
        // The end of the game day (sample 24) and the partial last sample are the two checkpoints.
        Assert.Equal(2, report.Samples.Count(s => s.Checkpoint));
        Assert.True(report.Samples[^1].Checkpoint);
        Assert.NotNull(report.Samples[^1].ManagedBytesAfterGc);
        Assert.True(report.Samples.Zip(report.Samples.Skip(1)).All(p => p.Second.Tick > p.First.Tick));
    }

    [ServerScenario]
    public async Task ARun_RestoresTheSpeedAndPassTimeWhenEmpty()
    {
        double rateBefore = Host!.Calendar.Pace().GameSecondsPerSecond;
        bool passTimeBefore = Host.RunOnGameThread(() => Host.Server.Config.PassTimeWhenEmpty);

        await Host.Simulate.DaysAsync(0.5, speed: 1440);

        Assert.Equal(rateBefore, Host.Calendar.Pace().GameSecondsPerSecond);
        Assert.Equal(passTimeBefore, Host.RunOnGameThread(() => Host.Server.Config.PassTimeWhenEmpty));
    }

    [ServerScenario]
    public async Task AClockThatDoesNotFollowTheCalendar_IsRefused()
    {
        using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50, followCalendar: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Host.Simulate.DaysAsync(1, speed: 1440));
    }

    [ServerScenario]
    public async Task AFrozenCalendar_IsRefused()
    {
        Host!.Calendar.Freeze();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Host.Simulate.DaysAsync(1, speed: 1440));
    }
}

/// <summary>The argument checks of <see cref="ServerSimulation.DaysAsync"/>, which run before any server work.</summary>
public class ServerSimulationArgumentTests
{
    [Fact]
    public async Task DaysAsync_RejectsBadArguments()
    {
        ServerSimulation simulation = new(null!);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => simulation.DaysAsync(0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => simulation.DaysAsync(double.NaN));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => simulation.DaysAsync(1, speed: 0));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => simulation.DaysAsync(1, speed: ServerSimulation.MaxSpeed + 1));
    }
}