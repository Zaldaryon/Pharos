using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The temporal systems of the world: the stability, the storms, the rifts and the wards.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[WorldConfig("temporalStability", true)]
[WorldConfig("temporalStorms", "sometimes")]
[WorldConfig("temporalRifts", "visible")]
public class LiveTemporalTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task TheStability_IsRead()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos;

        float stability = Server.Temporal.StabilityAt(pos);

        Assert.InRange(stability, 0f, 2f);
    }

    [ServerScenario]
    public async Task TheStorm_StartsAndEnds()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        await Server.Temporal.StartStormAsync(TemporalStormStrength.Heavy);
        TemporalStorm storm = Server.Temporal.Storm;

        Assert.True(storm.Active, "The storm did not start.");
        Assert.Equal(2, storm.Strength);

        await Server.Temporal.EndStormAsync();
        Assert.False(Server.Temporal.Storm.Active, "The storm did not end.");
    }

    [ServerScenario]
    public async Task TheRifts_AreRead()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        Assert.NotNull(Server.Temporal.Rifts);
    }

    [ServerScenario]
    public async Task TheRift_OpensAndCloses()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.AddCopy(3, 0, 0);

        TemporalRift rift = Server.Temporal.SpawnRift(pos, hours: 2);
        Assert.Contains(Server.Temporal.Rifts, r => r.RiftId == rift.RiftId);

        Server.Temporal.ClearRifts();
        Assert.DoesNotContain(Server.Temporal.Rifts, r => r.RiftId == rift.RiftId);
    }

    [ServerScenario]
    public async Task ThePlayerStability_IsRead()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        Assert.InRange(player.Vitals.TemporalStability ?? -1, 0d, 1d);
    }

    [ServerScenario]
    public async Task TheGear_SetsTheSpawn()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:gear-temporal");
        BlockPos at = player.Entity!.Pos.AsBlockPos;

        BotResult result = await Server.Temporal.UseGearAsync(player, ticks: 90, target: at);

        Assert.True(result.Accepted, "The gear use was refused.");
        FuzzyEntityPos spawn = player.Player!.GetSpawnPosition(false);
        Assert.True(Math.Abs(spawn.X - at.X) <= 2 && Math.Abs(spawn.Z - at.Z) <= 2, "The gear did not set the spawn point.");
    }

    [ServerScenario]
    public async Task TheWard_IsRead()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.AddCopy(2, -1, 0);
        Server.SetBlock(pos, "game:riftward");

        Assert.True(Server.Temporal.SetWard(pos, true), "The ward was not there.");
        Assert.True(Server.Temporal.Ward(pos).On, "The ward did not hold the rifts back.");
    }
}
