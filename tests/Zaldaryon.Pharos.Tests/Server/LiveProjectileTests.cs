using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The projectiles of the server: how they fly and what they hit.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveProjectileTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task TheArrow_FliesAndHitsTheDummy()
    {
        ServerTestPlayer shooter = await CreateTestPlayerAsync(PlayerNames.Next());
        shooter.GiveItem("game:bow-long", 1);
        shooter.GiveItem("game:arrow-copper", 10);

        // A straw dummy a few steps away, so the arrow has room to fly.
        Vec3d start = shooter.Entity!.Pos.XYZ;
        Entity dummy = await Server.Entities.SpawnAsync("game:strawdummy", start.AddCopy(10, 0, 0));
        float healthBefore = Health(dummy);

        Vec3d aim = dummy.Pos.XYZ.Add(0, 1.2, 0);

        using ProjectileLog log = Server.Projectiles.Track(shooter.Entity!.Pos.AsBlockPos, radius: 24);
        ProjectileRecord? arrow = await Server.Projectiles.FireAsync(shooter, 0, aim, charge: 1.5f);

        Assert.NotNull(arrow);
        ProjectileHit hit = await Server.Projectiles.WaitForImpactAsync(arrow!, maxTicks: 200);

        Assert.NotNull(hit.Entity);
        Assert.Equal(dummy.EntityId, hit.Entity!.EntityId);
        Assert.True(hit.Damage > 0, $"The arrow dealt {hit.Damage} damage.");
        Assert.True(arrow!.Path.Count > 0, "The arrow's path was not recorded.");
        Assert.True(Health(dummy) < healthBefore, "The dummy took no damage.");
    }

    [ServerScenario]
    public async Task TheArrow_KeepsItsStackWhenItHits()
    {
        ServerTestPlayer shooter = await CreateTestPlayerAsync(PlayerNames.Next());
        shooter.GiveItem("game:bow-long", 1);
        shooter.GiveItem("game:arrow-copper", 10);

        Vec3d start = shooter.Entity!.Pos.XYZ;
        Entity dummy = await Server.Entities.SpawnAsync("game:strawdummy", start.AddCopy(10, 0, 0));

        Vec3d aim = dummy.Pos.XYZ.Add(0, 1.2, 0);

        using ProjectileLog log = Server.Projectiles.Track(shooter.Entity!.Pos.AsBlockPos, radius: 24);
        ProjectileRecord? arrow = await Server.Projectiles.FireAsync(shooter, 0, aim, charge: 1.5f);

        Assert.NotNull(arrow);
        ProjectileHit hit = await Server.Projectiles.WaitForImpactAsync(arrow!, maxTicks: 200);

        Assert.True(hit.StackCollectable, "The arrow cannot be picked up.");
        Assert.False(hit.StackBroken, "The arrow was broken.");
    }

    private static float Health(Entity entity)
    {
        return entity.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("currenthealth", 20) ?? 20;
    }
}
