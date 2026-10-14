using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class RoleDriverTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Assign_ReturnsTheRoleThePlayerNowHas()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("RoleAssigned");

        string role = Host!.Roles.Assign(player, "suplayer");

        Assert.Equal("suplayer", role);
        Assert.Equal("suplayer", Host.RunOnGameThread(() => player.Player!.Role.Code));
    }

    [ServerScenario]
    public async Task Assign_RejectsARoleTheServerDoesNotDefine()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("RoleUnknown");

        ArgumentException error = Assert.Throws<ArgumentException>(() => Host!.Roles.Assign(player, "wizard"));

        Assert.Contains("wizard", error.Message);
    }

    [ServerScenario]
    public async Task VisitorRole_CannotBreakBlocksWithoutBuildPrivilege()
    {
        ServerTestPlayer visitor = await CreateTestPlayerAsync("RoleVisitor");
        Host!.RunOnGameThread(() => visitor.Player!.WorldData.CurrentGameMode = EnumGameMode.Survival);
        Host.Roles.Assign(visitor, "suvisitor");
        Entity entity = visitor.Entity!;
        BlockPos pos = new((int)Math.Floor(entity.Pos.X) + 1, (int)Math.Floor(entity.Pos.Y), (int)Math.Floor(entity.Pos.Z));
        int stone = Host.RunOnGameThread(() =>
        {
            Block rock = Api!.World.GetBlock(new AssetLocation("game:rock-granite"))!;
            Api.World.BlockAccessor.SetBlock(rock.BlockId, pos);
            return rock.BlockId;
        });

        BotResult result = await visitor.Bot.BreakBlockAsync(pos);

        PharosAssert.Denied(result, "noprivilege-buildbreak-nobuildprivilege");
        Assert.Equal(stone, Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(pos).BlockId));
    }
}
