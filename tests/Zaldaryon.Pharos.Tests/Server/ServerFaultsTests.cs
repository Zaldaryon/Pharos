using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Faults a test injects into the embedded server: each acts while it is in place, and ends with its dispose.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class ServerFaultsTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task SlowTicks_StretchesEveryNthTick()
    {
        using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
        var steps = new List<long>();

        using (ServerFault slow = Host.Faults.SlowTicks(every: 4, delayMs: 500))
        {
            for (int i = 0; i < 12; i++)
            {
                long before = clock.ElapsedMilliseconds;
                clock.Advance(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
                steps.Add(clock.ElapsedMilliseconds - before);
            }

            // The 4th, 8th and 12th ticks take 500 ms longer.
            Assert.Equal(new long[] { 50, 50, 50, 550, 50, 50, 50, 550, 50, 50, 50, 550 }, steps);
            Assert.Equal(3, slow.Hits);
        }

        // With the fault gone, four ticks take the normal 200 ms.
        long afterFault = clock.ElapsedMilliseconds;
        clock.Advance(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(50));
        Assert.Equal(200, clock.ElapsedMilliseconds - afterFault);
        await Task.CompletedTask;
    }

    [ServerScenario]
    public async Task SlowTicks_NeedsAVirtualClock()
    {
        Assert.Throws<InvalidOperationException>(() => Host!.Faults.SlowTicks(every: 4, delayMs: 500));
        await Task.CompletedTask;
    }
}
