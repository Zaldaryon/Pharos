using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class ReinforcementDriverTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Reinforce_GivesTheBlockStrengthThatIsReadBack()
    {
        ServerTestPlayer owner = await CreateTestPlayerAsync("ReinforceOwner");
        BlockPos pos = PlaceStone(owner);

        bool accepted = Host!.Reinforcement.Reinforce(pos, 10, owner);

        Assert.True(accepted, "The survival system refused the reinforcement.");
        Assert.Equal(10, Host.Reinforcement.Strength(pos));
    }

    [ServerScenario]
    public async Task Baseline_ClearsTheReinforcementsPlacedAfterIt()
    {
        ServerTestPlayer owner = await CreateTestPlayerAsync("ReinforceBase");
        BlockPos pos = PlaceStone(owner);
        ReinforcementDriver.Baseline baseline = Host!.Reinforcement.Capture();
        Assert.True(Host.Reinforcement.Reinforce(pos, 10, owner));

        baseline.Restore();

        Assert.Null(Host.Reinforcement.Strength(pos));
    }

    [ServerScenario]
    public async Task StrangerCannotBreakAReinforcedBlock()
    {
        ServerTestPlayer owner = await CreateTestPlayerAsync("ReinforceOwner2");
        ServerTestPlayer stranger = await CreateTestPlayerAsync("ReinforceFoe");
        // Creative breaks finish in one packet. Survival breaks need held time, which the bot does not simulate.
        Host!.RunOnGameThread(() =>
        {
            owner.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative;
            stranger.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative;
        });
        Host.Roles.Assign(owner, "suplayer");
        Host.Roles.Assign(stranger, "suplayer");
        BlockPos guarded = PlaceStone(owner);
        BlockPos plain = PlaceStone(owner, dz: 1);
        Assert.True(Host.Reinforcement.Reinforce(guarded, 10, owner), "The owner could not reinforce the guarded block.");
        int stone = Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(guarded).BlockId);
        await stranger.TeleportTo(guarded.X + 0.5, guarded.Y, guarded.Z + 2.5);

        // The unreinforced control block is the same stone, so a break that succeeds here shows the bot can break it.
        BotResult control = await stranger.Bot.BreakBlockAsync(plain);
        Assert.True(control.Accepted, control.Reason ?? control.Error?.Code);

        BotResult refused = await stranger.Bot.BreakBlockAsync(guarded);

        Assert.False(refused.Accepted, "The stranger broke a reinforced block.");
        Assert.Null(refused.Error);
        Assert.Equal(stone, Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(guarded).BlockId));
    }

    private BlockPos PlaceStone(ServerTestPlayer near, int dz = 0)
    {
        Entity entity = near.Entity!;
        BlockPos pos = new((int)Math.Floor(entity.Pos.X) + 1, (int)Math.Floor(entity.Pos.Y), (int)Math.Floor(entity.Pos.Z) + dz);
        Host!.RunOnGameThread(() =>
        {
            Block rock = Api!.World.GetBlock(new AssetLocation("game:rock-granite"))!;
            Api.World.BlockAccessor.SetBlock(rock.BlockId, pos);
        });
        return pos;
    }
}
