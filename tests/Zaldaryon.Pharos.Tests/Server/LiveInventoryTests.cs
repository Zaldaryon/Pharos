using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The containers of the server, what they hold, and what moves between them.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveInventoryTests : ServerScenarioBase
{
    [ServerScenario]
    public void TheChest_HoldsWhatWasPutInIt()
    {
        BlockPos chest = Place("game:chest-east");
        ContainerInventory container = Server.Inventories.At(chest);

        Assert.Equal("chest", container.InventoryClassName);
        Assert.True(container.Fill(0, "game:ingot-copper", 64));
        Assert.Equal(64, container.Count("game:ingot-copper"));
        Assert.Equal(64, container.ItemAt(0)?.StackSize);

        container.Clear();
        Assert.Equal(0, container.Count("game:ingot-copper"));
    }

    [ServerScenario]
    public void TheTotals_CountTheContainersAndTheItemsOnTheGround()
    {
        BlockPos chest = Place("game:chest-east");
        Server.Inventories.At(chest).Fill(0, "game:ingot-copper", 32);
        Drop("game:ingot-copper", 8, chest.AddCopy(2, 0, 0));

        ItemTotals totals = Server.Inventories.Totals(chest, radius: 4);

        Assert.Equal(40, totals.Count("game:ingot-copper"));
        Assert.Equal(40, totals.Total);
    }

    [ServerScenario]
    public async Task TheHopperMovesTheItemsDown()
    {
        BlockPos bottom = Place("game:chest-east");
        BlockPos hopper = Place("game:hopper", bottom.AddCopy(0, 1, 0));

        Server.Inventories.At(hopper).Fill(0, "game:ingot-copper", 64);
        ItemTotals before = Server.Inventories.Totals(bottom, radius: 3);

        ItemFlowReport report = await Server.Inventories.RunFlowAsync(bottom, ticks: 600, radius: 3);

        Assert.True(report.Moves.Count > 0, "The flow moved nothing.");
        Assert.Contains(report.Moves, move => move.Code == "game:ingot-copper" && move.Quantity > 0);
        Assert.Equal(64, Server.Inventories.At(bottom).Count("game:ingot-copper"));
        PharosAssert.ItemsConserved(before, Server.Inventories.Totals(bottom, radius: 3));
    }

    [ServerScenario]
    public void AnItemOnTheGround_CountsInTheTotals()
    {
        BlockPos ground = Place("game:rock-granite");
        Drop("game:ingot-copper", 5, ground.AddCopy(1, 0, 0));

        Assert.Equal(5, Server.Inventories.Totals(ground, radius: 3).Count("game:ingot-copper"));
    }

    private BlockPos Place(string code, BlockPos? position = null)
    {
        BlockPos target = position ?? Origin();
        int id = Server.RunOnGameThread(() => Server.Api.World.GetBlock(new AssetLocation(code))!.BlockId);
        Server.RunOnGameThread(() => Server.Api.World.BlockAccessor.SetBlock(id, target));
        return target;
    }

    private void Drop(string code, int quantity, BlockPos position) => Server.RunOnGameThread(() =>
    {
        Item item = Server.Api.World.GetItem(new AssetLocation(code))!;
        EntityItem entity = EntityItem.FromItemstack(new ItemStack(item, quantity), position.ToVec3d(), null, Server.Api.World);
        Server.Api.World.SpawnEntity(entity);
    });

    private BlockPos Origin()
    {
        EntityPos spawn = Server.DefaultSpawnPosition;
        return new BlockPos((int)spawn.X, (int)spawn.Y - 1, (int)spawn.Z);
    }
}

/// <summary>The copy of an open container the client has.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveInventorySyncTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task TheClientSeesTheSameChestAsTheServer()
    {
        await Session.StepFramesAsync(60);
        EntityPos spawn = Client.Client.EntityPlayer.Pos;
        BlockPos chest = new((int)Math.Floor(spawn.X) + 2, (int)Math.Floor(spawn.Y) - 1, (int)Math.Floor(spawn.Z));

        int id = ServerHost!.RunOnGameThread(() => ServerApi.World.GetBlock(new AssetLocation("game:chest-east"))!.BlockId);
        ServerHost.RunOnGameThread(() => ServerApi.World.BlockAccessor.SetBlock(id, chest));
        ServerHost.RunOnGameThread(() => ServerApi.World.BlockAccessor.SetBlock(id, chest.AddCopy(0, 1, 0)));

        Assert.True(await Session.Blocks.UseAsync(chest), "The client never acted on the use click");

        PharosAssert.InventorySynced(Session, chest);
    }

    private ICoreServerAPI ServerApi => (ICoreServerAPI)Server!.Api;
}
