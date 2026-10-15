using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class QuernMechanicalTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task CreativeRotor_TurnsAQuernThroughAnAxleAndAnAngledGear()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        Entity entity = player.Entity!;
        BlockPos quern = new((int)Math.Floor(entity.Pos.X) + 3, (int)Math.Floor(entity.Pos.Y), (int)Math.Floor(entity.Pos.Z));

        // Support blocks: under the horizontal axle, beside the vertical axle.
        Stone(quern.AddCopy(-1, 1, 0));
        Stone(quern.AddCopy(1, 1, 0));

        // The quern connects to the axle above it, so that axle goes first, then the quern on top of the ground.
        // An axle placed with no connected neighbour takes the player's facing, so the vertical one is set in its
        // orientation and then reports its placement, as the game does once a neighbour is there.
        PlaceAxleVertical(quern.AddCopy(0, 1, 0));
        Assert.True(Place(player, "game:woodenaxle-we", quern.AddCopy(-1, 2, 0)), "The horizontal axle did not place.");
        Assert.True(Place(player, "game:angledgears-s", quern.AddCopy(0, 2, 0)), "The angled gear did not place.");
        Assert.True(Place(player, "game:creativerotor-north", quern.AddCopy(-2, 2, 0)), "The creative rotor did not place.");
        Assert.True(Place(player, "game:quern-granite", quern), "The quern did not place.");

        QuernFacade mill = Host!.Processing.Quern(quern).Fill("game:grain-spelt", 1);
        for (int i = 0; i < 40 && !IsAutomated(quern); i++)
        {
            Host.Ticks(5);
            await Task.Delay(1);
        }
        string[] names = ["rotor", "horizontal axle", "angled gear", "vertical axle", "quern"];
        BlockPos[] spots = [quern.AddCopy(-2, 2, 0), quern.AddCopy(-1, 2, 0), quern.AddCopy(0, 2, 0), quern.AddCopy(0, 1, 0), quern];
        string net = string.Join(" | ", names.Select((n, i) => n + "=" + Host!.RunOnGameThread(() =>
        {
            var world = ((Vintagestory.API.Server.ICoreServerAPI)Host.Server.Api).World;
            var be = world.BlockAccessor.GetBlockEntity(spots[i]);
            var block = world.BlockAccessor.GetBlock(spots[i]).Code.ToString();
            object mp = be?.Behaviors?.FirstOrDefault(b => b.GetType().BaseType?.Name == "BEBehaviorMPBase" || b.GetType().Name.Contains("MP"));
            object net = mp?.GetType().GetProperty("Network")?.GetValue(mp);
            return block + " behaviour=" + (mp?.GetType().Name ?? "none") + " net=" + (net == null ? "null" : "set");
        })));
        Assert.True(IsAutomated(quern), $"The quern did not connect to the power network. {net}");

        // The rotor spins up over a few seconds, then grinds the grain.
        await Host.TickListeners.FastForwardAsync(quern, TimeSpan.FromSeconds(30));
        for (int i = 0; i < 400 && mill.State.Input is not null; i++)
        {
            Host.Ticks(20);
            await Task.Delay(1);
            await Host.TickListeners.FastForwardAsync(quern, TimeSpan.FromMilliseconds(200));
        }

        PharosAssert.Produces(mill.State.Output, "game:flour-spelt", 1);
    }

    private bool IsAutomated(BlockPos quern) => Host!.RunOnGameThread(() =>
    {
        object be = ((Vintagestory.API.Server.ICoreServerAPI)Host.Server.Api).World.BlockAccessor.GetBlockEntity(quern)!;
        return (bool)be.GetType().GetField("automated", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(be)!;
    });

    private void Stone(BlockPos pos) => Host!.RunOnGameThread(() =>
    {
        var world = ((Vintagestory.API.Server.ICoreServerAPI)Host.Server.Api).World;
        world.BlockAccessor.SetBlock(world.GetBlock(new AssetLocation("game:rock-granite"))!.BlockId, pos);
    });

    private void PlaceAxleVertical(BlockPos pos) => Host!.RunOnGameThread(() =>
    {
        var world = ((Vintagestory.API.Server.ICoreServerAPI)Host.Server.Api).World;
        Block axle = world.GetBlock(new AssetLocation("game:woodenaxle-ud"))!;
        world.BlockAccessor.SetBlock(axle.BlockId, pos);
        axle.GetType().GetMethod("WasPlaced")!.Invoke(axle, [world, pos, null]);
    });

    private bool Place(ServerTestPlayer player, string code, BlockPos pos) => Host!.RunOnGameThread(() =>
    {
        var world = ((Vintagestory.API.Server.ICoreServerAPI)Host.Server.Api).World;
        Block block = world.GetBlock(new AssetLocation(code))
            ?? throw new InvalidOperationException($"The game has no block named {code}.");
        string failure = "";
        var selection = new BlockSelection(pos, BlockFacing.UP, block);
        bool placed = block.TryPlaceBlock(world, player.Player!, new ItemStack(block), selection, ref failure);
        if (!placed) throw new InvalidOperationException($"{code} did not place at {pos}: {failure}");
        return placed;
    });
}
