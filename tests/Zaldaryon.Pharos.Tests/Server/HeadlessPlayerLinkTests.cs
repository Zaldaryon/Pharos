using Xunit;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>A headless player's connection can be lost, and the same player can join back with its data.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class HeadlessPlayerLinkTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Sever_DropsThePlayerWithoutALeavePacket()
    {
        ServerTestPlayer bob = await CreateTestPlayerAsync(PlayerNames.Next());
        ServerTestPlayer alice = await CreateTestPlayerAsync(PlayerNames.Next());

        bob.Sever();

        // The server reads the lost connection and drops the player; nobody else notices.
        Assert.True(await Host!.TickUntilAsync(() => !bob.IsConnected, maxTicks: 20));
        Assert.True(alice.IsConnected);
        Assert.Contains(Host.Logs.Entries, e => e.Message.Contains(" disconnected", StringComparison.Ordinal));
    }

    [ServerScenario]
    public async Task Rejoin_KeepsUidAndInventory()
    {
        ServerTestPlayer bob = await CreateTestPlayerAsync(PlayerNames.Next());
        string uid = bob.PlayerUID;
        bob.GiveItem("game:stick", 3);
        Assert.True(await Host!.TickUntilAsync(() => bob.HasItem("game:stick", 3), maxTicks: 60));

        bob.Sever();
        Assert.True(await Host.TickUntilAsync(() => !bob.IsConnected, maxTicks: 20));

        await bob.RejoinAsync(maxTicks: 600);

        Assert.True(bob.IsConnected);
        Assert.Equal(uid, bob.PlayerUID);
        Assert.True(bob.HasItem("game:stick", 3), "The player's inventory did not survive the lost connection.");
    }

    [ServerScenario]
    public async Task Configure_DelaysServerPacketsByLatency()
    {
        using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
        ServerTestPlayer bob = await CreateTestPlayerAsync(PlayerNames.Next());
        ServerTestPlayer alice = await CreateTestPlayerAsync(PlayerNames.Next());

        int baseline = await TicksUntilChatReaches(clock, bob, alice, "first");
        Assert.True(baseline <= 2, $"Without latency the chat took {baseline} ticks");

        bob.Network.Configure(new DegradedNetworkProfile(LatencyMs: 500));
        int delayed = await TicksUntilChatReaches(clock, bob, alice, "second");

        // 500 ms is ten ticks of 50 ms, and the server relays the chat on a tick of its own.
        Assert.InRange(delayed, 10, 12);
    }

    // Sends a chat from the sender and advances the virtual clock one 50 ms tick at a time until the receiver has it. The server
    // allows one chat line per rate limit, so the clock first moves past it.
    private async Task<int> TicksUntilChatReaches(VirtualClock clock, ServerTestPlayer receiver, ServerTestPlayer sender, string message)
    {
        clock.Advance(TimeSpan.FromSeconds(2));
        await sender.SayAsync(message, ticks: 0);

        int ticks = 0;
        while (!receiver.Received.ChatMessages.Any(m => m.Contains(message, StringComparison.Ordinal)) && ticks < 60)
        {
            clock.Advance(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
            ticks++;
        }

        Assert.True(ticks < 60, $"The chat '{message}' never reached {receiver.PlayerName}.");
        return ticks;
    }
}
