using Xunit;
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
}
