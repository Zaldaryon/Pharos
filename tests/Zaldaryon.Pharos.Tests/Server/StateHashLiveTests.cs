using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The state hash recorder on the real server, on a virtual clock so its ticks are repeatable.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class StateHashLiveTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task ABlockChange_ChangesOnlyTheBlockHashFromThatTick()
    {
        using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
        BlockPos origin = Host.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos);
        Cuboidi region = new(origin.X, origin.Y, origin.Z, origin.X + 2, origin.Y + 2, origin.Z + 2);
        await Host.Simulate.DaysAsync(0.0001, speed: 1440);

        using StateHashRecording recording = Host.StateHash.Record(region, new StateHashOptions { Parts = StateHashParts.Blocks | StateHashParts.Entities });
        Host.Ticks(5);
        this.SetBlock(origin.AddCopy(1, 1, 1), "game:rock-granite");
        Host.Ticks(5);

        IReadOnlyList<StateHashTick> ticks = recording.Ticks;
        Assert.Equal(10, ticks.Count);
        Assert.Equal(ticks[0].Blocks, ticks[4].Blocks);
        Assert.NotEqual(ticks[4].Blocks, ticks[5].Blocks);
        Assert.All(ticks, t => Assert.Equal(ticks[0].Entities, t.Entities));
        Assert.Equal(0UL, ticks[0].Players);
        Assert.False(recording.Truncated);
    }

    [ServerScenario]
    public async Task ARecording_SavesAFileThatReadsBackTheSameSamples()
    {
        using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
        BlockPos origin = Host.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos);
        Cuboidi region = new(origin.X, origin.Y, origin.Z, origin.X + 1, origin.Y + 1, origin.Z + 1);
        await Host.Simulate.DaysAsync(0.0001, speed: 1440);

        using StateHashRecording recording = Host.StateHash.Record(region, new StateHashOptions { Parts = StateHashParts.Blocks });
        Host.Ticks(3);
        string path = Path.Combine(Path.GetTempPath(), "pharos-state-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            recording.Save(path);

            StateHashFile file = StateHashFile.Load(path);
            Assert.Equal(recording.Ticks, file.Ticks);
            Assert.Equal(50, file.TickMs);
            Assert.Null(StateHashFile.Compare(file, file));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [ServerScenario]
    public async Task ARegionLargerThanTheLimit_IsRefused()
    {
        using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
        await Host.Simulate.DaysAsync(0.0001, speed: 1440);

        Assert.Throws<ArgumentException>(() => Host.StateHash.Record(new Cuboidi(0, 0, 0, 99, 99, 99), new StateHashOptions { MaxBlocks = 1000 }));
    }
}
