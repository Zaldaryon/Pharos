using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class PvpDriverTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task PvpOff_AttackDoesNotDamageAnotherPlayer()
    {
        (ServerTestPlayer attacker, ServerTestPlayer victim) = await JoinSurvivalPairAsync("PvpAttackerOff", "PvpVictimOff");
        Host!.Pvp.Enabled = false;
        float before = victim.Vitals.Health ?? throw new InvalidOperationException("The victim has no health reading.");

        BotResult result = await attacker.Bot.AttackEntityAsync(victim.Entity!);
        Host.Ticks(200);

        Assert.False(result.Accepted, "The server let an attack on another player through with PvP off.");
        Assert.Equal(before, victim.Vitals.Health);
    }

    [ServerScenario]
    public async Task PvpOn_AttackDamagesAnotherPlayer()
    {
        (ServerTestPlayer attacker, ServerTestPlayer victim) = await JoinSurvivalPairAsync("PvpAttackerOn", "PvpVictimOn");
        Host!.Pvp.Enabled = true;
        float before = victim.Vitals.Health ?? throw new InvalidOperationException("The victim has no health reading.");

        BotResult result = await attacker.Bot.AttackEntityAsync(victim.Entity!);
        Host.Ticks(200);

        Assert.True(result.Accepted, result.Reason ?? result.Error?.Code);
        Assert.True(victim.Vitals.Health < before, $"The victim kept {victim.Vitals.Health} of {before} health after an attack with PvP on.");
    }

    [ServerScenario]
    public void Baseline_RestoresTheSettingItRecorded()
    {
        bool original = Host!.Pvp.Enabled;
        PvpDriver.Baseline baseline = Host.Pvp.Capture();
        Host.Pvp.Enabled = !original;

        baseline.Restore();

        Assert.Equal(original, Host.Pvp.Enabled);
    }

    /// <summary>Joins two players in survival, since players in creative mode take no damage, and stands them one block apart.</summary>
    private async Task<(ServerTestPlayer Attacker, ServerTestPlayer Victim)> JoinSurvivalPairAsync(string attackerName, string victimName)
    {
        ServerTestPlayer attacker = await CreateTestPlayerAsync(attackerName);
        ServerTestPlayer victim = await CreateTestPlayerAsync(victimName);
        Host!.RunOnGameThread(() =>
        {
            attacker.Player!.WorldData.CurrentGameMode = EnumGameMode.Survival;
            victim.Player!.WorldData.CurrentGameMode = EnumGameMode.Survival;
        });
        Entity entity = attacker.Entity!;
        await victim.TeleportTo(entity.Pos.X + 1.5, entity.Pos.Y, entity.Pos.Z);
        return (attacker, victim);
    }
}
