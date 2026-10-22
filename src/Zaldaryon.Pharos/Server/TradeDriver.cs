using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>One thing a trader sells or wants.</summary>
/// <param name="Code">The code of the item.</param>
/// <param name="Price">What the trader charges for it, or pays for it.</param>
/// <param name="Stock">How many the trader has, or wants.</param>
public sealed record TradeStockEntry(string Code, int Price, int Stock);

/// <summary>What a trader offers and wants.</summary>
/// <param name="Selling">What the trader sells.</param>
/// <param name="Buying">What the trader wants to buy.</param>
/// <param name="TotalCost">What the cart of the player costs.</param>
/// <param name="TotalGain">What the cart of the player earns.</param>
/// <param name="PlayerAssets">What the player has, when a player is given.</param>
/// <param name="TraderAssets">What the trader has.</param>
/// <param name="NextRefreshDays">How many days until the trader restocks.</param>
public sealed record TraderView(
    IReadOnlyList<TradeStockEntry> Selling,
    IReadOnlyList<TradeStockEntry> Buying,
    int TotalCost,
    int TotalGain,
    int PlayerAssets,
    int TraderAssets,
    double NextRefreshDays);

/// <summary>What a trade did.</summary>
/// <param name="Result">The name of the game's own result, such as <c>Success</c>.</param>
/// <param name="Bought">The codes the player bought.</param>
/// <param name="Sold">The codes the player sold.</param>
public sealed record TradeResult(string Result, IReadOnlyList<string> Bought, IReadOnlyList<string> Sold);

/// <summary>
/// The traders of the server: what they offer, what they want, what a trade did and when they
/// restock.
/// </summary>
public sealed class TradeDriver
{
    private const string TradingHumanoid = "EntityTradingHumanoid";

    private readonly EmbeddedServerHost _host;

    internal TradeDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// Reads what a trader offers and wants.
    /// </summary>
    /// <param name="trader">The trader.</param>
    /// <param name="player">The player who trades with the trader, when there is one.</param>
    public TraderView Of(Entity trader, ServerTestPlayer? player = null)
    {
        ArgumentNullException.ThrowIfNull(trader);

        return _host.RunOnGameThread(() =>
        {
            object? inventory = InventoryOf(trader);
            if (inventory == null) return new TraderView([], [], 0, 0, 0, 0, 0);

            int assets = 0;
            if (player?.Player?.Entity is { } entity)
            {
                assets = (int)(GameMembers.Call(inventory, "GetPlayerAssets", entity) ?? 0);
            }

            return new TraderView(
                Stock(inventory, "SellingSlots"),
                Stock(inventory, "BuyingSlots"),
                (int)(GameMembers.Call(inventory, "GetTotalCost") ?? 0),
                (int)(GameMembers.Call(inventory, "GetTotalGain") ?? 0),
                assets,
                (int)(GameMembers.Call(inventory, "GetTraderAssets") ?? 0),
                (double)(GameMembers.Call(trader, "NextRefreshTotalDays") ?? 0d));
        });
    }

    /// <summary>
    /// Trades with a trader, the way the dialog does: the goods go into the carts and the trader
    /// hands them over.
    /// </summary>
    /// <param name="player">The player who trades.</param>
    /// <param name="trader">The trader.</param>
    /// <param name="buy">The codes the player buys.</param>
    /// <param name="sell">The codes the player sells.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>What the trade did.</returns>
    public Task<TradeResult> TradeAsync(
        ServerTestPlayer player,
        Entity trader,
        IReadOnlyList<string> buy,
        IReadOnlyList<string>? sell = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(trader);
        ArgumentNullException.ThrowIfNull(buy);

        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Trade(player, trader, buy, sell ?? []));
    }

    /// <summary>
    /// Moves the clock past the day the trader restocks, and lets the trader restock.
    /// </summary>
    /// <param name="trader">The trader.</param>
    /// <param name="ct">Cancels the wait.</param>
    public async Task RestockAsync(Entity trader, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(trader);

        double days = _host.RunOnGameThread(() => (double)(GameMembers.Call(trader, "NextRefreshTotalDays") ?? 0d));
        if (days <= 0) return;

        for (double day = 0; day < days + 1; day += 0.25)
        {
            ct.ThrowIfCancellationRequested();
            _host.Calendar.Advance(TimeSpan.FromHours(6));
            _host.Tick();
            await Task.Yield();
        }

        // The trader looks at its own clock only every so often, so the driver waits for it.
        for (int tick = 0; tick < 260; tick++)
        {
            ct.ThrowIfCancellationRequested();
            _host.Tick();
            await Task.Yield();
        }
    }

    private TradeResult Trade(ServerTestPlayer player, Entity trader, IReadOnlyList<string> buy, IReadOnlyList<string> sell)
    {
        return _host.RunOnGameThread(() =>
        {
            object? inventory = InventoryOf(trader);
            if (inventory == null) return new TradeResult("NoTrader", [], []);

            // The trader only trades with the player it is talking to.
            trader.WatchedAttributes.SetString("tradingPlayerUID", player.Player!.PlayerUID);

            foreach (string code in buy)
            {
                object? slot = SellingSlotOf(inventory, code);
                if (slot == null) continue;
                GameMembers.Call(inventory, "AddToBuyingCart", slot);
            }

            foreach (string code in sell)
            {
                object? slot = SellingCartSlotOf(inventory, code);
                if (slot == null) continue;
                GameMembers.Set(slot, "Itemstack", StackOf(code, player));
            }

            object? result = GameMembers.Call(inventory, "TryBuySell", player.Player);
            string name = result?.ToString() ?? "Failure";

            List<string> bought = [];
            foreach (object slot in CartSlots(inventory, "GetBuyingCartSlot"))
            {
                string? code = CodeOf(slot);
                if (code != null) bought.Add(code);
            }

            List<string> sold = [];
            foreach (object slot in CartSlots(inventory, "GetSellingCartSlot"))
            {
                string? code = CodeOf(slot);
                if (code != null) sold.Add(code);
            }

            trader.WatchedAttributes.RemoveAttribute("tradingPlayerUID");
            return new TradeResult(name, bought, sold);
        });
    }

    private object? InventoryOf(Entity trader)
    {
        for (Type? current = trader.GetType(); current != null; current = current.BaseType)
        {
            if (current.Name == TradingHumanoid) return GameMembers.Get(trader, "Inventory");
        }

        return null;
    }

    private static List<TradeStockEntry> Stock(object inventory, string name)
    {
        List<TradeStockEntry> entries = [];
        foreach (object slot in Slots(inventory, name))
        {
            object? tradeItem = GameMembers.Get(slot, "TradeItem");
            if (tradeItem == null) continue;

            object? stack = GameMembers.Get(tradeItem, "Stack");
            string? code = stack is ItemStack item ? item.Collectible?.Code.ToString() : null;
            if (code == null) continue;

            entries.Add(new TradeStockEntry(
                code,
                (int)(GameMembers.Get(tradeItem, "Price") ?? 0),
                (int)(GameMembers.Get(tradeItem, "Stock") ?? 0)));
        }

        return entries;
    }

    private static IEnumerable<object> Slots(object inventory, string name)
    {
        if (GameMembers.Get(inventory, name) is not object[] slots) return [];
        return slots;
    }

    private static IEnumerable<object> CartSlots(object inventory, string getter)
    {
        for (int index = 0; index < 4; index++)
        {
            if (GameMembers.Call(inventory, getter, index) is object slot) yield return slot;
        }
    }

    private static object? SellingSlotOf(object inventory, string code)
    {
        foreach (object slot in Slots(inventory, "SellingSlots"))
        {
            if (CodeOf(slot) == code) return slot;
        }

        return null;
    }

    private static object? SellingCartSlotOf(object inventory, string code)
    {
        foreach (object slot in CartSlots(inventory, "GetSellingCartSlot"))
        {
            if (CodeOf(slot) == code) return slot;
        }

        return null;
    }

    private static string? CodeOf(object slot)
    {
        if (GameMembers.Get(slot, "Itemstack") is ItemStack stack) return stack.Collectible?.Code.ToString();

        object? tradeItem = GameMembers.TryGet(slot, "TradeItem");
        if (tradeItem != null && GameMembers.TryGet(tradeItem, "Stack") is ItemStack tradeStack)
        {
            return tradeStack.Collectible?.Code.ToString();
        }

        return null;
    }

    private static ItemStack? StackOf(string code, ServerTestPlayer player)
    {
        CollectibleObject? collectible = player.Entity?.Api.World.GetItem(new AssetLocation(code));
        return collectible == null ? null : new ItemStack(collectible, 1);
    }
}
