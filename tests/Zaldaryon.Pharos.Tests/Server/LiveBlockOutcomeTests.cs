using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The block outcomes of the server: the drops, the explosions and the block hooks.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveBlockOutcomeTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task TheDrops_AreCountedOverManyRolls()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.DownCopy();

        DropDistribution drops = Server.Blocks.Drops(pos, player.Player, rolls: 200);

        Assert.Equal(200, drops.Rolls);
        Assert.Single(drops.Entries);
        Assert.Equal(200, drops.Hits(drops.Entries[0].Code));
        Assert.Equal(200, drops.Total(drops.Entries[0].Code));
    }

    [ServerScenario]
    public async Task TheDrops_FollowTheHeldTool()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        BlockPos pos = player.Entity!.Pos.AsBlockPos.DownCopy();
        Server.SetBlock(pos, "game:tallgrass-medium-free");

        DropDistribution bareHanded = Server.Blocks.Drops(pos, player.Player, rolls: 200);

        player.GiveItem("game:knife-generic-copper", 1);
        ItemStack knife = new(Server.Api.World.GetItem(new AssetLocation("game", "knife-generic-copper")), 1);
        DropDistribution withKnife = Server.Blocks.Drops(pos, player.Player, tool: knife, rolls: 200);

        Assert.Equal(0, bareHanded.Total("game:drygrass"));
        Assert.True(withKnife.Total("game:drygrass") > 0, "The knife did not cut the grass.");
    }

    [ServerScenario]
    public async Task TheExplosion_BreaksBlocksAndHurtsEntities()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos center = player.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        for (int x = -2; x <= 2; x++)
        {
            for (int y = 0; y <= 2; y++)
            {
                for (int z = -2; z <= 2; z++)
                {
                    Server.SetBlock(center.AddCopy(x, y, z), "game:rock-granite");
                }
            }
        }

        Entity dummy = await Server.Entities.SpawnAsync("game:strawdummy", center);

        ExplosionReport report = await Server.World.ExplodeAsync(
            center,
            EnumBlastType.RockBlast,
            destructionRadius: 4,
            injureRadius: 6);

        Assert.True(report.Destroyed.Count > 0, "The explosion broke nothing.");
        Assert.True(report.Damaged.Count > 0, "The explosion hurt nobody.");
        Assert.Equal(dummy.EntityId, report.Damaged[0].EntityId);
    }

    [ServerScenario]
    public async Task TheContact_IsRecorded()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.DownCopy();

        using ContactTrace trace = Server.Blocks.TraceContacts(pos, radius: 8);
        Entity hen = await Server.Entities.SpawnAsync("game:chicken-hen", pos.UpCopy());

        Assert.NotNull(hen);

        ContactEvent contact;
        try
        {
            contact = await trace.WaitForAnyAsync(maxTicks: 200);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("No contact came at " + pos + ". Seen: " + Seen(trace));
        }

        Assert.True(contact.EntityId > 0, "The contact carried no entity.");
        Assert.Equal("game:chicken-hen", contact.EntityCode);
    }

    [ServerScenario]
    public async Task TheDroppedItem_LiesInsideTheBlockUnderIt()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        BlockPos pos = player.Entity!.Pos.AsBlockPos.AddCopy(2, -1, 0);
        ItemStack stack = new(Server.Api.World.GetItem(new AssetLocation("game", "pickaxe-copper")), 1);

        using ContactTrace trace = Server.Blocks.TraceContacts(pos, radius: 8);
        Entity? item = null;
        Server.RunOnGameThread(() => item = Server.Api.World.SpawnItemEntity(stack, pos.UpCopy().ToVec3d()));

        Assert.NotNull(item);

        ContactEvent contact;
        try
        {
            contact = await trace.WaitForAsync(ContactHook.EntityInside, pos.UpCopy(), maxTicks: 200);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("No contact came at " + pos.UpCopy() + ". Seen: " + Seen(trace));
        }

        Assert.Equal("game:item", contact.EntityCode);
    }

    [ServerScenario]
    public async Task TheNeighbourUpdate_IsRecorded()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.AddCopy(2, -1, 0);
        Server.SetBlock(pos, "game:soil-verylow-none");

        using NeighbourUpdateTrace trace = Server.Blocks.TraceNeighbourUpdates(pos, radius: 8);
        BotResult result = await player.Bot.BreakBlockAsync(pos);

        Assert.True(result.Accepted, "The block was not broken.");

        NeighbourUpdate update;
        try
        {
            update = await trace.WaitForAnyAsync(maxTicks: 100);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException("No neighbour update came. Seen: " + Seen(trace));
        }

        Assert.True(update.Neighbour.Equals(pos), "The update came from another block.");
    }

    private static string Seen(ContactTrace trace) =>
        string.Join(",", trace.Events.Take(5).Select(e => e.Hook + "@" + e.Position + "#" + e.EntityCode));

    private static string Seen(NeighbourUpdateTrace trace) =>
        string.Join(",", trace.Updates.Take(5).Select(u => u.Position + "<-" + u.Neighbour));
}
