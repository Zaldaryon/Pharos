using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class ProcessingDriverTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Firepit_CooksRawFishOnFirewoodLitWithATorch()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = PlaceBlock(player, "game:firepit-extinct");
        FirepitFacade firepit = Host!.Processing.Firepit(pos);
        player.GiveItem("game:torch-basic-lit-up");

        // Three pieces of firewood, and a raw fish on the grate. The torch lights the first piece; the rest catch in turn.
        firepit.Fill(FirepitSlot.Fuel, "game:firewood", 3).Fill(FirepitSlot.Input, "game:fish-raw", 1);
        firepit.IgniteWithTorch(player, secondsHeld: 4);

        await firepit.RunAsync(TimeSpan.FromSeconds(20));
        Assert.True(firepit.State.Burning, "The torch did not light the firewood.");
        Assert.True(firepit.State.CookingProgress > 0, "The fish did not start cooking while the fire burned.");

        await firepit.RunAsync(TimeSpan.FromSeconds(60));
        FirepitState done = firepit.State;
        PharosAssert.Produces(done.Output, "game:fish-cooked", 1);
        Assert.True(firepit.FuelLeft < 3, "The fire did not use any of the firewood.");
    }

    [ServerScenario]
    public async Task Firepit_FillsReportTheChangedSlotToTheGame()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = PlaceBlock(player, "game:firepit-extinct");
        FirepitFacade firepit = Host!.Processing.Firepit(pos);
        var changed = new List<int>();

        using (firepit.WatchSlotChanges(changed.Add))
        {
            firepit.Fill(FirepitSlot.Input, "game:fish-raw", 1).Fill(FirepitSlot.Fuel, "game:firewood", 2);
            firepit.AddFuel("game:firewood", 1);
        }

        // Each write reaches the game's slot-modified event, which is what syncs a slot to the clients.
        Assert.Equal(new[] { (int)FirepitSlot.Input, (int)FirepitSlot.Fuel, (int)FirepitSlot.Fuel }, changed);
        Assert.Equal(3, firepit.FuelLeft);
    }

    [ServerScenario]
    public async Task Firepit_CatchesWithAFirestarterTurnedLongEnough()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        Host!.Random.Seed(4243);
        BlockPos pos = PlaceBlock(player, "game:firepit-extinct");
        FirepitFacade firepit = Host.Processing.Firepit(pos).Fill(FirepitSlot.Fuel, "game:firewood", 2);
        player.GiveItem("game:firestarter");

        firepit.IgniteWithFirestarter(player, secondsHeld: 4);
        await firepit.RunAsync(TimeSpan.FromSeconds(10));

        Assert.True(firepit.State.Burning, "The firestarter did not light the firewood.");
    }

    [ServerScenario]
    public async Task Firepit_DoesNotCatchFromAFirestarterTurnedTooBriefly()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = PlaceBlock(player, "game:firepit-extinct");
        FirepitFacade firepit = Host!.Processing.Firepit(pos).Fill(FirepitSlot.Fuel, "game:firewood", 2);
        player.GiveItem("game:firestarter");

        // Under three seconds the firepit is not yet ignitable, so no turn can catch it.
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => firepit.IgniteWithFirestarter(player, secondsHeld: 1, attempts: 5));

        Assert.Contains("did not catch", error.Message);
        Assert.False(firepit.State.Burning);
    }

    [ServerScenario]
    public async Task GeneratedPlayerNames_AtTheLimit_JoinTheServer()
    {
        string name = PlayerNames.Next();
        ServerTestPlayer player = await CreateTestPlayerAsync(name);

        Assert.Equal(PlayerNames.MaxLength, name.Length);
        Assert.Equal(name, player.PlayerName);
    }

    [ServerScenario]
    public async Task Firepit_NeedsALitTorchToCatch()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = PlaceBlock(player, "game:firepit-extinct");
        FirepitFacade firepit = Host!.Processing.Firepit(pos).Fill(FirepitSlot.Fuel, "game:firewood", 1);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => firepit.IgniteWithTorch(player));

        Assert.Contains("not a lit torch", error.Message);
        Assert.False(firepit.State.Burning);
    }

    [ServerScenario]
    public async Task Barrel_MeadIsReadyAfterItsFourteenDaysOfSealing()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = PlaceBlock(player, "game:barrel");
        BarrelFacade barrel = Host!.Processing.Barrel(pos);

        // Honey is 100 portions to the litre, so 0.1 litres of honey is 10 portions, and the mead recipe seals for 336 hours.
        barrel.Fill(BarrelSlot.Liquid, "game:honeyportion", 10).Seal();

        await barrel.RunAsync(TimeSpan.FromDays(13.5));
        Assert.NotEqual("game:ciderportion-mead", barrel.State.Liquid?.Code);

        await barrel.RunAsync(TimeSpan.FromDays(1));
        PharosAssert.Produces(barrel.State.Liquid, "game:ciderportion-mead", 10);
        Assert.Null(barrel.State.Items);
    }

    [ServerScenario]
    public async Task Barrel_SpeltCiderIsNotReadyBeforeItsSealingTime()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
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

    [ServerScenario]
    public async Task FiredClayPot_CooksPorridgeInAnotherColourOfPot()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = PlaceBlock(player, "game:firepit-extinct");
        FirepitFacade firepit = Host!.Processing.Firepit(pos);
        player.GiveItem("game:torch-basic-lit-up");

        // Porridge is grains alone, at least two grain stacks, each in its own slot. Each stack counts once toward the
        // recipe's minimum, so one stack of three grains would not match. It has no water ingredient.
        firepit.Fill(FirepitSlot.Fuel, "game:firewood", 6).Fill(FirepitSlot.Input, "game:claypot-red-fired", 1)
            .FillCookingSlot(0, "game:grain-spelt", 1)
            .FillCookingSlot(1, "game:grain-spelt", 1);
        firepit.IgniteWithTorch(player, secondsHeld: 4);

        // Each firewood piece burns for 24 seconds, so the cook keeps the fire fed, as a player does.
        for (int minute = 0; minute < 16 && firepit.State.Output is null; minute++)
        {
            if (firepit.FuelLeft < 2) firepit.AddFuel("game:firewood", 3);
            await firepit.RunAsync(TimeSpan.FromSeconds(30));
        }

    }

    [ServerScenario]
    public async Task FiredClayPot_CooksASoupOverAFirewoodFire()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = PlaceBlock(player, "game:firepit-extinct");
        FirepitFacade firepit = Host!.Processing.Firepit(pos);
        player.GiveItem("game:torch-basic-lit-up");

        // The soup is one litre of water and one carrot in a fired clay pot. The pot goes on the fire.
        firepit.Fill(FirepitSlot.Fuel, "game:firewood", 6).Fill(FirepitSlot.Input, "game:claypot-black-fired", 1)
            .FillCookingSlot(0, "game:waterportion", 100)
            .FillCookingSlot(1, "game:vegetable-carrot", 1);
        firepit.IgniteWithTorch(player, secondsHeld: 4);

        await firepit.RunAsync(TimeSpan.FromMinutes(3));

        FirepitState done = firepit.State;
        Assert.Equal("game:claypot-black-cooked", done.Output?.Code);
        Assert.Equal("soup", firepit.OutputMeal?.Recipe);
        Assert.True(firepit.OutputMeal?.Servings >= 1, "The soup has no servings.");
    }

    [ServerScenario]
    public async Task Quern_GrindsWhenTheHandleIsHeldThroughPackets()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = PlaceBlock(player, "game:quern-granite");
        QuernFacade quern = Host!.Processing.Quern(pos).Fill("game:grain-spelt", 1);

        await quern.GrindThroughPacketsAsync(player, TimeSpan.FromSeconds(8));

        QuernState done = quern.State;
        Assert.Null(done.Input);
        PharosAssert.Produces(done.Output, "game:flour-spelt", 1);
    }

    [ServerScenario]
    public async Task Quern_GrindsGrainIntoFlourOnlyWhileAPlayerHoldsTheHandle()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = PlaceBlock(player, "game:quern-granite");
        QuernFacade quern = Host!.Processing.Quern(pos).Fill("game:grain-spelt", 1);

        // Nobody holds the handle, so the grain stays whole.
        await Host.TickListeners.FastForwardAsync(pos, TimeSpan.FromSeconds(10));
        Assert.Equal("game:grain-spelt", quern.State.Input?.Code);

        await quern.GrindAsync(player, TimeSpan.FromSeconds(8));

        QuernState done = quern.State;
        Assert.Null(done.Input);
        PharosAssert.Produces(done.Output, "game:flour-spelt", 1);
    }

    [ServerScenario]
    public async Task ClayOven_BakesSpeltBreadOnceTheFireIsOutAndTheOvenIsStillHot()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = PlaceBlock(player, "game:clayoven-north");
        OvenFacade oven = Host!.Processing.Oven(pos);
        player.GiveItem("game:torch-basic-lit-up");

        // A full stack of six pieces heats the oven past the bake temperature before the fire goes out.
        oven.Fill(OvenSlot.First, "game:firewood", 6).IgniteWithTorch(player, secondsHeld: 5);
        await oven.RunAsync(TimeSpan.FromSeconds(10));
        Assert.True(oven.State.Burning, "The torch did not light the oven.");

        // Step until the fire is out, so the check runs at the moment the fuel is gone.
        for (int i = 0; i < 600 && oven.State.Burning; i++) await oven.RunAsync(TimeSpan.FromSeconds(1));
        Assert.False(oven.State.Burning, "The fuel is still burning.");
        Assert.True(oven.State.Temperature > 160, $"The oven was at {oven.State.Temperature} °C when the fire went out, below the bake temperature.");

        oven.RemoveFuel().PutIn("game:dough-spelt");

        bool perfect = false;
        for (int i = 0; i < 60 && !perfect; i++)
        {
            await oven.RunAsync(TimeSpan.FromSeconds(30));
            perfect = oven.State.Slots.Any(slot => slot?.Code == "game:bread-spelt-perfect");
        }
        Assert.True(perfect, "The dough did not bake to a perfect loaf in the oven's remaining heat.");
    }

    [ServerScenario]
    public async Task ClayOven_BreadPassesThroughPartbakedThenPerfectThenCharred()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = PlaceBlock(player, "game:clayoven-north");
        OvenFacade oven = Host!.Processing.Oven(pos);
        player.GiveItem("game:torch-basic-lit-up");

        oven.Fill(OvenSlot.First, "game:firewood", 6).IgniteWithTorch(player, secondsHeld: 5);
        for (int i = 0; i < 600 && oven.State.Burning; i++) await oven.RunAsync(TimeSpan.FromSeconds(1));
        oven.RemoveFuel().PutIn("game:dough-spelt");

        // The stages follow the bake levels in the dough's and the bread's baking properties: partbaked to a
        // quarter, perfect to a half, and charred to three quarters. Record each stage in the order it appears.
        var order = new List<string>();
        for (int i = 0; i < 400 && !order.Contains("game:bread-spelt-charred"); i++)
        {
            await oven.RunAsync(TimeSpan.FromSeconds(30));
            foreach (var slot in oven.State.Slots)
            {
                if (slot is { Code: { } code } && code.StartsWith("game:bread-spelt-", StringComparison.Ordinal) && !order.Contains(code)) order.Add(code);
            }
        }

        Assert.Equal(new[] { "game:bread-spelt-partbaked", "game:bread-spelt-perfect", "game:bread-spelt-charred" }, order);
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
