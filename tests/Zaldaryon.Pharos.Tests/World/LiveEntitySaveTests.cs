using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>Entities through a save and reload: an entity keeps its id, and a standing entity keeps its state.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class LiveEntitySaveTests : ServerScenarioBase
{
    protected override bool FailOnLoggedErrors => true;

    [ServerScenario]
    public async Task AnEntity_KeepsItsId_AfterReload()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos spot = player.Entity!.Pos.AsBlockPos;
        Entity stand = await Host!.Entities.SpawnAsync("game:armorstand", new Vec3d(spot.X + 3.5, spot.Y, spot.Z + 0.5));
        long id = stand.EntityId;
        await Host.SaveWorldAsync();

        await ReloadWorldAsync();

        Entity? after = Host!.RunOnGameThread(() => Api!.World.GetEntityById(id));
        Assert.NotNull(after);
        Assert.Equal("game:armorstand", after!.Code.ToString());

        // A new entity must not take the id of one the save holds.
        Entity newer = await Host.Entities.SpawnAsync("game:armorstand", new Vec3d(spot.X + 3.5, spot.Y, spot.Z - 0.5));
        Assert.True(newer.EntityId > id, $"The new entity got id {newer.EntityId}, which is not after the saved id {id}.");
    }

    [ServerScenario]
    public async Task AStandingEntity_IsTheSameAfterReloadingTheWorld()
    {
        // The armor stand has no AI, so it stands where it was put. Capture after the save: saving serializes the entity,
        // which sets its head angles in its watched attributes.
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos spot = player.Entity!.Pos.AsBlockPos;
        await Host!.Entities.SpawnAsync("game:armorstand", new Vec3d(spot.X + 3.5, spot.Y, spot.Z + 0.5));
        BlockPos around = new(spot.X + 3, spot.Y, spot.Z);
        await Host.SaveWorldAsync();
        WorldState before = WorldState.Capture(Host, around, radius: 2);

        await ReloadWorldAsync();

        PharosAssert.SameWorldState(before, WorldState.Capture(Host!, around, radius: 2));
    }
}
