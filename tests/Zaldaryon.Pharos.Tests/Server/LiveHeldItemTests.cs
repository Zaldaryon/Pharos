using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Items;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The held-item hooks, the mining times and the durability the game makes of an item.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveHeldItemTests : ServerScenarioBase
{
    private const string Bread = "game:bread-spelt-partbaked";
    private const string Pickaxe = "game:pickaxe-iron";
    private const string CopperPickaxe = "game:pickaxe-copper";
    private const string Ilmenite = "game:ore-medium-ilmenite-granite";

    [ServerScenario]
    public async Task TheHeldUse_RunsTheItemsHooks()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem(Bread, 1);
        Assert.True((await player.Bot.SelectSlotAsync(SlotOf(player, "game:bread"))).Accepted);

        BlockPos below = player.Entity!.Pos.AsBlockPos;
        using HeldTraceScope scope = Server.Held.Trace();
        BotResult result = await player.Bot.UseHeldAsync(40, below);

        Assert.True(result.Accepted, result.Reason ?? result.Error?.Code);
        Assert.Contains(scope.Trace.Calls, call => call.Hook == HeldHook.InteractStart);
        Assert.Contains(scope.Trace.Calls, call => call.Hook == HeldHook.InteractStep);
        Assert.All(scope.Trace.Calls, call => Assert.Equal(EnumAppSide.Server, call.Side));
        Assert.Contains(scope.Trace.Calls, call => call.ItemCode.StartsWith("game:bread", StringComparison.Ordinal));
    }

    [ServerScenario]
    public void ACopperPickaxe_CannotMineTheIlmenite()
    {
        int required = Server.RunOnGameThread(() => Block(Ilmenite).RequiredMiningTier);
        Assert.True(required > 2, $"The ilmenite ore needs tier {required}, so a copper pickaxe cannot mine it.");

        PharosAssert.CannotMine(Server, CopperPickaxe, Ilmenite);
    }

    [ServerScenario]
    public async Task AnIronPickaxe_MinesStoneInASecondOrSo()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        ItemStack pickaxe = Server.RunOnGameThread(() => new ItemStack(Server.Api.World.GetItem(new AssetLocation(Pickaxe))!, 1));
        BlockPos stone = PlaceBlock("game:rock-granite");

        float time = Server.Items.MiningTime(pickaxe, stone, player.Player);

        Assert.InRange(time, 0.1f, 60f);
    }

    [ServerScenario]
    public async Task BreakingABlock_CostsTheToolItsDurability()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem(Pickaxe, 1);
        int slot = SlotOf(player, Pickaxe);
        Assert.True((await player.Bot.SelectSlotAsync(slot)).Accepted);

        BlockPos stone = PlaceBlock("game:rock-granite");
        int before = Server.Items.Durability(slot, player.Player);

        BotResult broken = await player.Bot.BreakBlockAsync(stone);

        Assert.True(broken.Accepted, broken.Reason ?? broken.Error?.Code);
        PharosAssert.DurabilityLost(Server, slot, 1, before, player.Player);
    }

    private Block Block(string code) => Server.Api.World.Blocks
        .First(candidate => candidate.Code.ToString() == code);

    private BlockPos PlaceBlock(string code)
    {
        EntityPos spawn = Server.DefaultSpawnPosition;
        BlockPos position = new((int)spawn.X, (int)spawn.Y - 1, (int)spawn.Z);
        int id = Server.RunOnGameThread(() => Server.Api.World.GetBlock(new AssetLocation(code))!.BlockId);
        Server.RunOnGameThread(() => Server.Api.World.BlockAccessor.SetBlock(id, position));
        return position;
    }

    private int SlotOf(ServerTestPlayer player, string code) => Server.RunOnGameThread(() =>
    {
        IInventory hotbar = player.Player!.InventoryManager.GetHotbarInventory();
        return Enumerable.Range(0, hotbar.Count)
            .First(index => hotbar[index]?.Itemstack?.Collectible?.Code.ToString().StartsWith(code, StringComparison.Ordinal) == true);
    });
}
