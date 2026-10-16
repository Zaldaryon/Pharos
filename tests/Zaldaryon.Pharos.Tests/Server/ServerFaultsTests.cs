using System.Runtime.CompilerServices;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
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
    public async Task ThrowIn_FailsTickListenerOnceThenRecovers()
    {
        using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
        var ticker = new Ticker();
        Host.RunOnGameThread(() => Api!.Event.RegisterGameTickListener(ticker.OnTick, 20));

        using (ServerFault fault = Host.Faults.ThrowIn<Ticker>(nameof(Ticker.OnTick), times: 1))
        {
            // Four ticks: the first call to the listener throws, the others succeed.
            clock.Advance(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(50));

            Assert.Equal(1, fault.Hits);
            Assert.Equal(3, ticker.Calls);

            // The server logs the injected throw once as an error, and carries on.
            Assert.Single(Host.Logs.Errors.Where(e => e.Message.Contains("Injected fault in OnTick", StringComparison.Ordinal)));
        }

        // With the fault gone, the listener runs on every tick again.
        clock.Advance(TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50));
        Assert.Equal(5, ticker.Calls);
        await Task.CompletedTask;
    }

    [ServerScenario]
    public async Task FailSaves_FailsTheSaveAndTheServerStaysUsable()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos chest = player.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        await Host!.EnsureChunksLoadedAsync(chest, chest);
        this.SetBlock(chest, "game:chest-east");

        using (ServerFault fault = Host.Faults.FailSaves())
        {
            // The failure reaches the save one of two ways, depending on the pass of the save that writes first: the game logs a failed
            // write and goes on, or the write escapes and the save throws it.
            try
            {
                await Host.SaveWorldAsync();
            }
            catch (IOException error) when (error.Message == "disk full")
            {
            }

            Assert.True(fault.Hits > 0);
        }

        // The game clears a chunk's dirty mark before it writes the chunk, so a failed write leaves the chunk clean: a later save writes it
        // again only once it changes. Changing the chest again makes the next save store it, and a crash keeps it.
        this.SetBlock(chest, "game:rock-granite");
        await Host.SaveWorldAsync();
        await RestartWorldAsync(RestartMode.Crash);
        await Host!.EnsureChunksLoadedAsync(chest, chest);
        Assert.Equal("game:rock-granite", BlockCodeAt(chest));
    }

    private string BlockCodeAt(BlockPos pos) => Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(pos).Code.ToString());

    [ServerScenario]
    public async Task SlowTicks_NeedsAVirtualClock()
    {
        Assert.Throws<InvalidOperationException>(() => Host!.Faults.SlowTicks(every: 4, delayMs: 500));
        await Task.CompletedTask;
    }

    /// <summary>A tick listener a fault can make throw. Its method is not inlined, so the fault can patch it.</summary>
    private sealed class Ticker
    {
        public int Calls { get; private set; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnTick(float dt) => Calls++;
    }
}
