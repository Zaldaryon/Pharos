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

        // A new trader's countdown starts anywhere up to 7 days, and a restock resets it to about 6,
        // so comparing countdowns misses a restock. The trader writes down when it last restocked,
        // and only a restock moves that day: start it 6.5 days before the next restock and watch it.
        double last = Server.RunOnGameThread(() => trader.World.Calendar.TotalDays) - 0.5;
        Server.RunOnGameThread(() => trader.WatchedAttributes.SetDouble("lastRefreshTotalDays", last));
        await Server.Trade.RestockAsync(trader);

        double restocked = Server.RunOnGameThread(() => trader.WatchedAttributes.GetDouble("lastRefreshTotalDays"));
        Assert.True(restocked > last, "The trader did not restock.");
        double next = Server.Trade.Of(trader).NextRefreshDays;
        Assert.True(next > 0, $"The trader restocked, but the next restock is {next} days away.");
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
