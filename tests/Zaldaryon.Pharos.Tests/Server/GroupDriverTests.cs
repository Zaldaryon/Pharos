using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class GroupDriverTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Create_MakesTheGroupWithItsOwnerAndMembers()
    {
        ServerTestPlayer owner = await CreateTestPlayerAsync("GroupOwner");
        ServerTestPlayer member = await CreateTestPlayerAsync("GroupMember");
        ServerTestPlayer outsider = await CreateTestPlayerAsync("GroupOutsider");

        PlayerGroup group = Host!.Groups.Create("traders", owner, member);

        Assert.Same(group, Host.Groups.Find("traders"));
        Assert.Equal(owner.PlayerUID, group.OwnerUID);
        Assert.Equal(EnumPlayerGroupMemberShip.Owner, Host.Groups.Membership(owner, group));
        Assert.Equal(EnumPlayerGroupMemberShip.Member, Host.Groups.Membership(member, group));
        Assert.Equal(EnumPlayerGroupMemberShip.None, Host.Groups.Membership(outsider, group));
    }

    [ServerScenario]
    public async Task Create_RejectsAGroupNameThatIsTaken()
    {
        ServerTestPlayer owner = await CreateTestPlayerAsync("GroupDupOwner");
        Host!.Groups.Create("dupes", owner);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => Host.Groups.Create("dupes", owner));

        Assert.Contains("dupes", error.Message);
    }

    [ServerScenario]
    public async Task Baseline_RemovesTheGroupsCreatedAfterIt()
    {
        ServerTestPlayer owner = await CreateTestPlayerAsync("GroupBase");
        GroupDriver.Baseline baseline = Host!.Groups.Capture();
        Host.Groups.Create("transient", owner);
        Assert.NotNull(Host.Groups.Find("transient"));

        baseline.Restore();

        Assert.Null(Host.Groups.Find("transient"));
    }

    [ServerScenario]
    public async Task GroupMember_CanBreakInsideAClaimThatPermitsTheGroup()
    {
        ServerTestPlayer owner = await CreateTestPlayerAsync("ClaimGroupOwner");
        ServerTestPlayer member = await CreateTestPlayerAsync("ClaimGroupMember");
        ServerTestPlayer stranger = await CreateTestPlayerAsync("ClaimGroupFoe");
        Host!.RunOnGameThread(() =>
        {
            foreach (ServerTestPlayer player in new[] { owner, member, stranger }) player.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative;
        });
        foreach (ServerTestPlayer player in new[] { member, stranger }) Host.Roles.Assign(player, "suplayer");
        PlayerGroup group = Host.Groups.Create("crew", owner, member);

        Entity entity = owner.Entity!;
        BlockPos pos = new((int)Math.Floor(entity.Pos.X) + 1, (int)Math.Floor(entity.Pos.Y), (int)Math.Floor(entity.Pos.Z));
        int stone = Host.RunOnGameThread(() =>
        {
            Block rock = Api!.World.GetBlock(new AssetLocation("game:rock-granite"))!;
            Api.World.BlockAccessor.SetBlock(rock.BlockId, pos);
            return rock.BlockId;
        });
        LandClaim claim = Host.LandClaims.Add(owner, new Cuboidi(pos.X - 4, pos.Y - 2, pos.Z - 4, pos.X + 4, pos.Y + 4, pos.Z + 4));
        Host.RunOnGameThread(() => claim.PermittedPlayerGroupIds[group.Uid] = EnumBlockAccessFlags.BuildOrBreak);

        await member.TeleportTo(pos.X + 0.5, pos.Y, pos.Z + 2.5);
        BotResult memberBreak = await member.Bot.BreakBlockAsync(pos);
        Assert.True(memberBreak.Accepted, memberBreak.Reason ?? memberBreak.Error?.Code);
        Host.RunOnGameThread(() => Api!.World.BlockAccessor.SetBlock(stone, pos));

        await stranger.TeleportTo(pos.X + 0.5, pos.Y, pos.Z + 2.5);
        BotResult strangerBreak = await stranger.Bot.BreakBlockAsync(pos);
        PharosAssert.Denied(strangerBreak, "noprivilege-buildbreak-landclaimed");
    }
}
