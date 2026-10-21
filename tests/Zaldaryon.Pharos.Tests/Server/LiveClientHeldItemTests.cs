using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Items;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The holds an engine-mode client makes with the item in its hand.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveClientHeldItemTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task TheClient_HoldsTheUseButton_AndTheItemRunsItsHooks()
    {
        await Session.StepFramesAsync(60);
        // The command takes the item code, the quantity and then the target.
        await ExecuteCommandAsync($"/giveitem game:bread-spelt-partbaked 1 {PlayerName}");
        await Session.StepFramesAsync(5);

        Assert.True(Client.TestPlayer.Inventory.SelectHotbarSlot(0));

        // The client's own mouse handling only acts on a button when the mouse is in the world.
        Vintagestory.API.Common.Entities.EntityPos pos = Client.Client.EntityPlayer.Pos;
        Vintagestory.API.MathTools.BlockPos ground = new(
            (int)Math.Floor(pos.X) + 2,
            (int)Math.Floor(pos.Y) - 1,
            (int)Math.Floor(pos.Z));
        Assert.True(await Session.Blocks.AimAtAsync(ground));

        using HeldTraceScope scope = Session.Held.Trace();
        await Session.Held.UseAsync(TimeSpan.FromSeconds(0.5));

        Assert.Contains(scope.Trace.Calls, call => call.Side == EnumAppSide.Client);
        Assert.Contains(scope.Trace.Calls, call => call.Hook == HeldHook.InteractStart);
    }
}
