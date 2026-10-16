using Xunit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

[ServerWorld(seed: 250, playStyle: "creativebuilding", worldType: "superflat")]
public sealed class ServerScenarioSequenceLiveTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task PollingAndSequenceAdvanceTheEmbeddedServer()
    {
        int ticks = 0;
        Host!.RunOnGameThread(() => Api!.Event.RegisterGameTickListener(_ => Interlocked.Increment(ref ticks), 0));

        await SucceedWhenAsync(() => Assert.True(Volatile.Read(ref ticks) >= 3, $"Observed {ticks} server ticks."), maxTicks: 10);
        Assert.Equal(3, Volatile.Read(ref ticks));

        int actions = 0;
        await Sequence()
            .Then(() => Interlocked.Increment(ref actions))
            .Idle(2)
            .WaitUntil(() => Assert.True(Volatile.Read(ref ticks) >= 6, $"Observed {ticks} server ticks."))
            .FailIf(() => Assert.True(Volatile.Read(ref ticks) < 0, "Impossible tick count."))
            .RunAsync(maxTicks: 10);

        Assert.Equal(1, actions);
        Assert.True(Volatile.Read(ref ticks) >= 6);
    }
}
