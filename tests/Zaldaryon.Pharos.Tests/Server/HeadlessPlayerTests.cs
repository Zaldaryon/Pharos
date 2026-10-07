using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

[Collection("Sequential")]
[ServerWorld(seed: 4242, Isolation = WorldIsolation.Restart)]
public class HeadlessPlayerTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task CreateTestPlayerAsync_JoinsAPlayingPlayerWithEntityAndInventories()
    {
        List<string> joined = [];
        List<string> nowPlaying = [];
        Host!.RunOnGameThread(() =>
        {
            Api!.Event.PlayerJoin += p => joined.Add(p.PlayerName);
            Api.Event.PlayerNowPlaying += p => nowPlaying.Add(p.PlayerName);
        });

        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");

        Assert.True(player.IsConnected);
        Assert.Equal("Alice", player.PlayerName);
        Assert.NotNull(player.Entity);
        Assert.True(player.Player!.InventoryManager.Inventories.Count > 0);

        ConnectedClient client = Assert.Single(Server!.Clients.Values, c => c.PlayerName == "Alice");
        Assert.Equal(EnumClientState.Playing, client.State);
        Assert.Contains(Server.AllOnlinePlayers, p => p.PlayerName == "Alice");

        // The server ran its own join sequence, so mods see the player join and start playing.
        Assert.Contains("Alice", joined);
        Assert.Contains("Alice", nowPlaying);

        // The server streams the world to a playing client.
        Host.Ticks(20);
        Assert.True(player.ReceivedPacketCount > 0);
    }

    [ServerScenario]
    public async Task CreateTestPlayerAsync_JoinsSeveralPlayersSideBySide()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        ServerTestPlayer bob = await CreateTestPlayerAsync("Bob");
        ServerTestPlayer carol = await CreateTestPlayerAsync("Carol");

        Assert.All([alice, bob, carol], p => Assert.True(p.IsConnected));
        Assert.Equal(3, Server!.Clients.Values.Count(c => c.State == EnumClientState.Playing));
        Assert.Equal(3, Host!.TestPlayers.Count);
        Assert.Equal(3, new[] { alice, bob, carol }.Select(p => p.Entity!.EntityId).Distinct().Count());
    }

    [ServerScenario]
    public async Task GiveItem_PutsTheItemInTheRealInventory()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");

        player.GiveItem("game:stick", 5);

        Assert.True(player.HasItem("game:stick", 5));
        Assert.False(player.HasItem("game:stick", 6));
    }

    [ServerScenario]
    public async Task SayAsync_ReachesTheServerChatHandlerAsAPlayerMessage()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        List<(string Name, string Message)> chat = [];
        Host!.RunOnGameThread(() => Api!.Event.PlayerChat += (IServerPlayer byPlayer, int channelId, ref string message, ref string data, Vintagestory.API.Datastructures.BoolRef consumed) =>
            chat.Add((byPlayer.PlayerName, message)));

        await player.SayAsync("hello from a headless player");

        Assert.Contains(chat, c => c.Name == "Alice" && c.Message.Contains("hello from a headless player"));
    }

    [ServerScenario]
    public async Task Disconnect_LeavesTheServerAndTheNameCanRejoin()
    {
        ServerTestPlayer first = await CreateTestPlayerAsync("Alice");
        first.Disconnect();

        Assert.False(first.IsConnected);
        Assert.DoesNotContain(Server!.Clients.Values, c => c.PlayerName == "Alice");

        ServerTestPlayer again = await CreateTestPlayerAsync("Alice");
        Assert.True(again.IsConnected);
        Assert.Single(Server.Clients.Values, c => c.PlayerName == "Alice");
    }

    [ServerScenario]
    public async Task CreateTestPlayerAsync_RejectsADuplicateName()
    {
        await CreateTestPlayerAsync("Alice");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateTestPlayerAsync("Alice"));
    }
}

[Collection("Sequential")]
public class EngineClientWithHeadlessPlayerTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task HeadlessPlayer_IsAnotherPlayerForTheRenderingClient()
    {
        ServerTestPlayer other = await CreateTestPlayerAsync("Bob");

        // Bring Bob next to the rendering client so the server tracks him for it.
        var mine = Client!.Client.EntityPlayer.Pos;
        await other.TeleportTo(mine.X + 2, mine.Y, mine.Z + 2);

        bool seen = await StepUntilAsync(
            () => Client.Client.PlayersByUid.ContainsKey(other.PlayerUID)
                && Client.Client.PlayersByUid[other.PlayerUID].Entity != null,
            maxFrames: 1200);

        Assert.True(seen, "The rendering client never received the other player and its entity");
        Assert.Equal(2, Server!.Clients.Values.Count(c => c.State == EnumClientState.Playing));
    }
}
