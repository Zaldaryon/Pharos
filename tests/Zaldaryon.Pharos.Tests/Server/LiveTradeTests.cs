using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The traders and the villagers of the server.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveTradeTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task TheTrader_OffersAndWants()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        Entity trader = await Server.Entities.SpawnAsync("game:trader-male-artisan-temperate", player.Entity!.Pos.XYZ.AddCopy(2, 0, 0));

        TraderView view = Server.Trade.Of(trader, player);

        Assert.NotEmpty(view.Selling);
        Assert.NotEmpty(view.Buying);
        Assert.True(view.NextRefreshDays > 0, "The trader does not restock.");
    }

    [ServerScenario]
    public async Task TheTrade_HandsTheGoodsOver()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:gear-rusty", 20);
        Entity trader = await Server.Entities.SpawnAsync("game:trader-male-artisan-temperate", player.Entity!.Pos.XYZ.AddCopy(2, 0, 0));

        TraderView view = Server.Trade.Of(trader, player);
        Assert.True(view.PlayerAssets > 0, "The player has no money.");

        string code = view.Selling[0].Code;
        TradeResult result = await Server.Trade.TradeAsync(player, trader, [code]);

        Assert.Equal("Success", result.Result);
        Assert.True(player.HasItem(code), "The player did not get the goods.");
    }

    [ServerScenario]
    public async Task TheTrader_Restocks()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        Entity trader = await Server.Entities.SpawnAsync("game:trader-male-artisan-temperate", player.Entity!.Pos.XYZ.AddCopy(2, 0, 0));

        double before = Server.Trade.Of(trader).NextRefreshDays;
        await Server.Trade.RestockAsync(trader);
        double after = Server.Trade.Of(trader).NextRefreshDays;

        Assert.True(after > before, "The trader did not restock.");
    }

    [ServerScenario]
    public async Task TheVillager_Talks()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        Entity villager = await Server.Entities.SpawnAsync("game:villager-nadiya-female-generic-generic", player.Entity!.Pos.XYZ.AddCopy(2, 0, 0));

        DialogueLines lines = await Server.Dialogue.StartAsync(player, villager);

        Assert.NotNull(lines);
        Assert.True(lines.Lines.Count > 0, "The villager said nothing.");
    }
}
