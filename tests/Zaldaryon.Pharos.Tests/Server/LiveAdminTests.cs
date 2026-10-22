using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The administration of the server: the whitelist, the bans and the player data.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveAdminTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task TheWhitelist_LetsAPlayerIn()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        Assert.NotNull(player);

        Server.Admin.Whitelist(player.PlayerName!, "a friend");
        Assert.True(Server.Admin.IsWhitelisted(player.PlayerName!));

        Server.Admin.Unwhitelist(player.PlayerName!);
        Assert.False(Server.Admin.IsWhitelisted(player.PlayerName!));

        Server.Admin.WhitelistMode = EnumWhitelistMode.On;
        Assert.Equal(EnumWhitelistMode.On, Server.Admin.WhitelistMode);
    }

    [ServerScenario]
    public async Task TheKick_RemovesThePlayer()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        Server.Admin.Kick(player, "a test kick");

        await SucceedWhenAsync(() => Assert.False(player.IsConnected), maxTicks: 200);
        Assert.False(player.IsConnected);
    }

    [ServerScenario]
    public async Task TheBan_RefusesTheJoin()
    {
        Server.Admin.Ban("Mallory", reason: "griefing");

        JoinOutcome outcome = await Server.Admin.TryJoinAsync("Mallory", maxTicks: 60);

        Assert.False(outcome.Accepted);
        Assert.Contains("griefing", outcome.DisconnectReason);
    }

    [ServerScenario]
    public async Task ThePlayerData_KeepsWhatAModStored()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        Server.PlayerData.Of(player).CustomPlayerData["mymod:rank"] = "elder";

        Assert.Equal("elder", Server.PlayerData.Of(player.PlayerUID).CustomPlayerData["mymod:rank"]);

        player.Sever();
        await SucceedWhenAsync(() => Assert.False(player.IsConnected), maxTicks: 200);

        await player.RejoinAsync();
        Assert.Equal("elder", Server.PlayerData.Of(player).CustomPlayerData["mymod:rank"]);
    }
}
