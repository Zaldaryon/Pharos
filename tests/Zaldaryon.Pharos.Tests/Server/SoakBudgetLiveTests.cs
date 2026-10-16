using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>
/// The soak budgets on the real server: each one breaks on purpose, or passes, against a run that does
/// the thing it measures. Each test restarts the server, so the listeners a test adds do not carry over.
/// </summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat", Isolation = WorldIsolation.Restart)]
public class SoakBudgetLiveTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task EntitiesBelow_BreaksOnTheFirstSampleOverTheLimit()
    {
        BlockPos spawn = Host!.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos);
        for (int i = 0; i < 5; i++)
        {
            await Host.Entities.SpawnAsync("game:chicken-hen", new Vec3d(spawn.X + 0.5 + i, spawn.Y + 1, spawn.Z + 0.5));
        }

        SoakBudgetExceededException error = await Assert.ThrowsAsync<SoakBudgetExceededException>(
            () => Host.Simulate.DaysAsync(0.25, speed: 1440, monitor: m => m.EntitiesBelow(3)));

        Assert.Equal("entities", error.Breach.Budget);
        Assert.True(error.Breach.Gating);
        Assert.True(error.Breach.Value >= 3, $"value {error.Breach.Value}");
        Assert.False(error.Report.Completed);
        Assert.Single(error.Report.Samples);
    }

    [ServerScenario]
    public async Task TickListenersStable_BreaksWhenAListenerKeepsBeingAdded()
    {
        // Each game hour (5 seconds of virtual time) the listener adds another one.
        Host!.RunOnGameThread(() =>
        {
            ICoreServerAPI api = (ICoreServerAPI)Host.Server.Api;
            api.Event.RegisterGameTickListener(_ => api.Event.RegisterGameTickListener(_ => { }, 60_000), 5000);
        });

        SoakBudgetExceededException error = await Assert.ThrowsAsync<SoakBudgetExceededException>(() => Host.Simulate.DaysAsync(0.25, speed: 1440, monitor: m => m
            .TickListenersStable()
            .CheckpointEvery(TimeSpan.FromHours(2))
            .WarmUp(TimeSpan.FromHours(1))));

        Assert.Equal("tick listeners stable", error.Breach.Budget);
        Assert.True(error.Breach.Value >= 1, $"drift {error.Breach.Value}");
    }

    [ServerScenario]
    public async Task ManagedMemoryGrowth_BreaksWhenEachGameHourKeepsFourMegabytes()
    {
        // The listener keeps every array it allocates, so a forced collection cannot free them.
        List<byte[]> kept = [];
        Host!.RunOnGameThread(() =>
        {
            ICoreServerAPI api = (ICoreServerAPI)Host.Server.Api;
            api.Event.RegisterGameTickListener(_ => kept.Add(new byte[4 << 20]), 5000);
        });

        SoakBudgetExceededException error = await Assert.ThrowsAsync<SoakBudgetExceededException>(() => Host.Simulate.DaysAsync(0.25, speed: 1440, monitor: m => m
            .ManagedMemoryGrowthBelow(5_000_000)
            .CheckpointEvery(TimeSpan.FromHours(2))
            .WarmUp(TimeSpan.FromHours(1))));

        Assert.Equal("managed memory growth", error.Breach.Budget);
        Assert.True(error.Breach.Value >= 5_000_000, $"growth {error.Breach.Value}");
        Assert.NotEmpty(kept);
    }

    [ServerScenario]
    public async Task ATimingBudget_IsAdvisoryUnlessEnforced()
    {
        // No tick takes under a microsecond, so the budget breaks at the first sample with 100 ticks.
        SoakReport report = await Host!.Simulate.DaysAsync(0.25, speed: 1440, monitor: m => m.TickMs.P99Below(0.000001));

        Assert.True(report.Completed);
        Assert.NotEmpty(report.Breaches);
        Assert.All(report.Breaches, breach => Assert.False(breach.Gating));
    }

    [ServerScenario]
    public async Task ACleanRun_BreaksNothing()
    {
        SoakReport report = await Host!.Simulate.DaysAsync(0.25, speed: 1440, monitor: m => m.EntitiesBelow(100_000).TickMs.P99Below(100_000));

        Assert.True(report.Completed);
        Assert.Empty(report.Breaches);
        Assert.Equal((long)report.TickTime.Count, report.Ticks);
    }
}
