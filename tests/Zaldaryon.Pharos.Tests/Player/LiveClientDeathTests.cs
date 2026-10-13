using Vintagestory.API.Common;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Player;

/// <summary>The client's death screen and the Respawn button on it, on a real engine-mode client.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
// A creative player takes no damage but healing: EntityPlayer.ShouldReceiveDamage refuses it.
[ServerWorld(seed: 4242, playStyle: "surviveandbuild")]
public class LiveClientDeathTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    protected override string PlayerName => "DyingPlayer";

    [ClientServerScenario]
    public async Task Death_OpensTheScreenAndRespawn_ClosesIt()
    {
        Assert.False(Player!.Death.IsDialogOpen);

        await ServerPlayer.DamageAsync(100, DamageInfo.Blunt);

        // The game waits two and a half game seconds after the player stops being alive before it
        // puts the screen up.
        Assert.True(await StepUntilAsync(() => !ServerPlayer.Entity!.Alive, maxFrames: 600), "the server did not kill the player");
        Assert.True(await StepUntilAsync(() => Player.Death.IsDialogOpen, maxFrames: 600), "the death screen never opened");

        Assert.NotNull(Player.Death.Dialog);

        Assert.True(Player.Death.Respawn());
        Assert.True(await StepUntilAsync(() => ServerPlayer.Entity!.Alive, maxFrames: 600), "the server never brought the player back");

        Assert.True(await StepUntilAsync(() => !Player.Death.IsDialogOpen, maxFrames: 600), "the death screen stayed up");
        Assert.True(ServerPlayer.Vitals.Health > 0);
    }

    [ClientServerScenario]
    public async Task Respawn_RefusesALivingPlayer()
    {
        Assert.True(ServerPlayer.Vitals.IsAlive);
        Assert.False(Player!.Death.Respawn());
    }
}