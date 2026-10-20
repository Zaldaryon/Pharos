using Vintagestory.Common;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Mods;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Mods;

/// <summary>The joined engine client loads its own mod list, separate from the server's.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharostestmod")]
[PharosMods("TestMods/pharostestmod", "TestMods/pharosloadmod")]
public class ClientModLoadingTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360
    };

    protected override string PlayerName => "ModPilot";

    [ClientServerScenario]
    public void JoinedClient_ReportsItsMods()
    {
        Assert.True(Client.IsJoined);

        LoadedMod serverMod = Assert.IsType<LoadedMod>(Server.Mods.Mod("pharostestmod"));
        Assert.Equal(ModStatus.Enabled, serverMod.Status);

        LoadedMod clientMod = Assert.IsType<LoadedMod>(Client.Mods.Mod("pharostestmod"));
        Assert.Equal(ModStatus.Enabled, clientMod.Status);
        Assert.Equal("1.0.0", clientMod.Version);
        Assert.Equal(Vintagestory.API.Common.EnumAppSide.Universal, clientMod.Side);

        PharosAssert.NoModErrors(Client);
        Assert.Contains(Client.Mods.Phases, phase => phase.Phase == ModRunPhase.Normal);

        Assert.True(
            Client.Mods.Loaded.ContainsKey("pharosloadmod"),
            "client mods: " + string.Join(", ", Client.Mods.Loaded.Keys));
        LoadedSystem hud = Assert.Single(Client.Mods.Systems.Where(system =>
            system.Key == "pharosloadmod:PharosClientOnlySystem"));
        Assert.True(hud.Ran);
        Assert.True(hud.LoadsOn(Vintagestory.API.Common.EnumAppSide.Client));
        Assert.False(hud.LoadsOn(Vintagestory.API.Common.EnumAppSide.Server));
    }
}
