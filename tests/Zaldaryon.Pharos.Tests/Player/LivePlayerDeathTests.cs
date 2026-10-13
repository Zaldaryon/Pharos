using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Player;

/// <summary>Damaging, killing and respawning a headless player on a live survival server.</summary>
[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "surviveandbuild", Isolation = WorldIsolation.Restart)]
public class LivePlayerDeathTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task DamageAsync_GoesThroughTheBehaviour()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        Assert.Equal(15, player.Vitals.Health);

        await player.DamageAsync(5, DamageInfo.Blunt);

        Assert.Equal(10, player.Vitals.Health);
        Assert.True(player.Entity!.Alive);
    }

    [ServerScenario]
    public async Task DamageAsync_RunsTheBehaviourWithTheDamageSource()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");

        // The health behaviour records the health from before the hit and takes the damage off itself,
        // and the entity's onHurt attribute is what the server sends the client, so all three show
        // that ReceiveDamage reached the game with what the test asked for.
        await player.DamageAsync(3, new DamageInfo(EnumDamageType.PiercingAttack, EnumDamageSource.Void, Tier: 2));

        Assert.Equal(12, player.Vitals.Health);
        Assert.Equal(3, player.Entity!.WatchedAttributes.GetFloat("onHurt"));
    }

    [ServerScenario]
    public async Task DamageAsync_OverTime_BecomesADamageOverTimeEffect()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");

        await player.DamageAsync(4, DamageInfo.Blunt.OverTime(TimeSpan.FromSeconds(4), 4, EnumDamageOverTimeEffectType.Poison), ticks: 0);

        // The health behaviour turns damage over a duration into an effect rather than taking it at
        // once, so health is untouched until the effect's ticks come.
        Assert.Equal(15, player.Vitals.Health);
        DamageOverTime effect = Assert.Single(player.Vitals.DamageOverTime);
        Assert.Equal(EnumDamageSource.Internal, effect.Source);
        Assert.Equal(4, effect.TicksLeft);

        Assert.True(await Host.TickUntilAsync(() => player.Vitals.Health < 15, maxTicks: 600));
        Assert.True(player.Vitals.Health < 15);
    }

    [ServerScenario]
    public async Task KillAsync_EndsThePlayerAndFiresTheDeath()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        List<string> deaths = [];
        Host!.RunOnGameThread(() => Api!.Event.OnEntityDeath += (Entity entity, DamageSource source) => deaths.Add(entity.GetName()));

        await player.KillAsync();

        Assert.False(player.Entity!.Alive);
        Assert.False(player.Vitals.IsAlive);
        Assert.Equal(0, player.Vitals.Health);
        Assert.Contains("Alice", deaths);

        await Assert.ThrowsAsync<InvalidOperationException>(() => player.KillAsync());
    }

    [ServerScenario]
    public async Task RespawnAsync_PutsThePlayerBackInTheWorld()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        BlockPos before = player.Entity!.Pos.AsBlockPos;

        await player.KillAsync();
        await player.RespawnAsync();

        Assert.True(player.Entity.Alive);
        Assert.True(player.Vitals.IsAlive);
        Assert.True(player.Vitals.Health > 0);
        Assert.True(before.DistanceTo(player.Entity.Pos.AsBlockPos) < 128, "the player came back near where it died");
    }

    [ServerScenario]
    public async Task RespawnAsync_RefusesALivingPlayer()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");

        InvalidOperationException alive = await Assert.ThrowsAsync<InvalidOperationException>(() => player.RespawnAsync());
        Assert.Contains("is not dead", alive.Message);
    }

    [ServerScenario]
    public async Task DamageAndDeath_NeedAPlayerThatHasJoined()
    {
        using ServerTestPlayer player = new("not-joined");

        await Assert.ThrowsAsync<InvalidOperationException>(() => player.DamageAsync(1));
        await Assert.ThrowsAsync<InvalidOperationException>(() => player.KillAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => player.RespawnAsync());
    }

    [Fact]
    public void DamageInfo_BuildsTheGameDamageSource()
    {
        DamageInfo info = DamageInfo.FromEntity(null!, EnumDamageType.SlashingAttack) with { Tier = 3, Knockback = 2.5f };
        DamageSource source = info.ToDamageSource();

        Assert.Equal(EnumDamageSource.Entity, source.Source);
        Assert.Equal(EnumDamageType.SlashingAttack, source.Type);
        Assert.Equal(3, source.DamageTier);
        Assert.Equal(2.5f, source.KnockbackStrength);
        Assert.Equal(TimeSpan.Zero, source.Duration);
        Assert.Equal(1, source.TicksPerDuration);

        DamageSource overTime = DamageInfo.Blunt.OverTime(TimeSpan.FromSeconds(6), 3, EnumDamageOverTimeEffectType.Bleeding).ToDamageSource();
        Assert.Equal(TimeSpan.FromSeconds(6), overTime.Duration);
        Assert.Equal(3, overTime.TicksPerDuration);
        Assert.Equal(EnumDamageOverTimeEffectType.Bleeding, overTime.DamageOverTimeTypeEnum);
    }
}