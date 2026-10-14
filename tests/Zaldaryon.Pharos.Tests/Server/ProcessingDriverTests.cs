using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class ProcessingDriverTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Firepit_CooksRawFishOnOneFireOfFirewood()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("FirepitCook");
        BlockPos pos = PlaceBlock(player, "game:firepit-extinct");
        FirepitFacade firepit = Host!.Processing.Firepit(pos);

        // One firewood burns for 24 seconds. The fire lights only once, so the rest of the stack stays unburned.
        firepit.Fill(FirepitSlot.Fuel, "game:firewood", 1).Fill(FirepitSlot.Input, "game:fish-raw", 1).Ignite();
        Assert.True(firepit.State.Burning, "The fire did not light.");

        await firepit.RunAsync(TimeSpan.FromSeconds(20));
        Assert.True(firepit.State.CookingProgress > 0, "The fish did not start cooking while the fire burned.");

        await firepit.RunAsync(TimeSpan.FromSeconds(60));
        FirepitState done = firepit.State;
        Assert.Equal("game:fish-cooked", done.Output?.Code);
        // The fish's smelting ratio is 1, so one raw fish cooks into one.
        Assert.Equal(1, done.Output?.Quantity);
        Assert.False(done.Burning, "The one piece of firewood should have burned out by now.");
    }

    [ServerScenario]
    public async Task Barrel_SpeltCiderIsNotReadyBeforeItsSealingTime()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("BarrelCider");
        BlockPos pos = PlaceBlock(player, "game:barrel");
        BarrelFacade barrel = Host!.Processing.Barrel(pos);

        // A liquid portion is a hundredth of a litre, so one litre of water is 100 portions.
        barrel.Fill(BarrelSlot.Liquid, "game:waterportion", 100).Fill(BarrelSlot.Items, "game:flour-spelt", 1).Seal();
        Assert.True(barrel.State.Sealed);

        // The spelt recipe seals for 336 hours, which is 14 days.
        await barrel.RunAsync(TimeSpan.FromDays(13));
        Assert.NotNull(barrel.State.Items);
        Assert.NotEqual("game:ciderportion-spelt", barrel.State.Liquid?.Code);

        await barrel.RunAsync(TimeSpan.FromDays(1.5));
        Assert.Equal("game:ciderportion-spelt", barrel.State.Liquid?.Code);
        // The recipe yields 0.2 litres, which is 20 portions.
        Assert.Equal(20, barrel.State.Liquid?.Quantity);
        Assert.Null(barrel.State.Items);
    }

    private BlockPos PlaceBlock(ServerTestPlayer player, string blockCode)
    {
        Entity entity = player.Entity!;
        BlockPos pos = new((int)Math.Floor(entity.Pos.X) + 2, (int)Math.Floor(entity.Pos.Y) - 1, (int)Math.Floor(entity.Pos.Z));
        Host!.RunOnGameThread(() =>
        {
            Block block = Api!.World.GetBlock(new AssetLocation(blockCode))
                ?? throw new InvalidOperationException($"The game has no block named {blockCode}.");
            Api.World.BlockAccessor.SetBlock(block.BlockId, pos);
        });
        return pos;
    }
}
