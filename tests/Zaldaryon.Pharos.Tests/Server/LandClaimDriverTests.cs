using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class LandClaimDriverTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Add_ClaimsTheBox_AndRemoveTakesItBack()
    {
        ServerTestPlayer owner = await CreateTestPlayerAsync("ClaimAdder");
        BlockPos pos = NextTo(owner);

        LandClaim claim = Host!.LandClaims.Add(owner, Box(pos));

        Assert.Equal(owner.PlayerUID, claim.OwnedByPlayerUid);
        Assert.Contains(Host.LandClaims.At(pos), found => found.OwnedByPlayerUid == owner.PlayerUID);
        Assert.Contains(Host.LandClaims.All, found => found.OwnedByPlayerUid == owner.PlayerUID);

        Assert.True(Host.LandClaims.Remove(claim));
        Assert.Empty(Host.LandClaims.At(pos));
        Assert.False(Host.LandClaims.Remove(claim));
    }

    [ServerScenario]
    public async Task OwnerCanBreakBlockInsideTheirClaim()
    {
        ServerTestPlayer owner = await CreateTestPlayerAsync("ClaimBreaker");
        BlockPos pos = NextTo(owner);
        Host!.RunOnGameThread(() =>
        {
            Block stone = Api!.World.GetBlock(new AssetLocation("game:rock-granite"))!;
            Api.World.BlockAccessor.SetBlock(stone.BlockId, pos);
        });
        Host.LandClaims.Add(owner, Box(pos));

        BotResult result = await owner.Bot.BreakBlockAsync(pos);

        Assert.True(result.Accepted, result.Reason ?? result.Error?.Code);
    }

    [ServerScenario]
    public async Task Rollback_PutsBackTheClaimsTheWorldStartedWith()
    {
        ServerTestPlayer owner = await CreateTestPlayerAsync("ClaimRollback");
        BlockPos pos = NextTo(owner);
        int before = Host!.LandClaims.All.Count;
        LandClaimDriver.LandClaimBaseline baseline = Host.LandClaims.Capture();
        Host.LandClaims.Add(owner, Box(pos));
        Assert.Equal(before + 1, Host.LandClaims.All.Count);

        int restored = baseline.Restore();

        Assert.Equal(before, restored);
        Assert.Equal(before, Host.LandClaims.All.Count);
        Assert.Empty(Host.LandClaims.At(pos));
    }

    [ServerScenario]
    public async Task StrangerCannotBreakBlockInsideAClaim()
    {
        ServerTestPlayer owner = await CreateTestPlayerAsync("ClaimOwnerDenied");
        ServerTestPlayer stranger = await CreateTestPlayerAsync("ClaimStranger");
        BlockPos pos = NextTo(owner);
        int stone = Host!.RunOnGameThread(() =>
        {
            Block rock = Api!.World.GetBlock(new AssetLocation("game:rock-granite"))!;
            Api.World.BlockAccessor.SetBlock(rock.BlockId, pos);
            return rock.BlockId;
        });
        Host.LandClaims.Add(owner, Box(pos));
        // Test players join as admin, who may break anywhere. A survival player's role is what claims restrict.
        stranger.RoleCode = "suplayer";
        await stranger.TeleportTo(pos.X + 0.5, pos.Y, pos.Z + 2.5);

        BotResult result = await stranger.Bot.BreakBlockAsync(pos);

        PharosAssert.Denied(result, "noprivilege-buildbreak-landclaimed");
        Assert.Equal(stone, Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(pos).BlockId));
    }

    private static BlockPos NextTo(ServerTestPlayer player)
    {
        Entity entity = player.Entity!;
        return new BlockPos((int)Math.Floor(entity.Pos.X) + 1, (int)Math.Floor(entity.Pos.Y), (int)Math.Floor(entity.Pos.Z));
    }

    private static Cuboidi Box(BlockPos pos) =>
        new(pos.X - 4, pos.Y - 2, pos.Z - 4, pos.X + 4, pos.Y + 4, pos.Z + 4);
}
