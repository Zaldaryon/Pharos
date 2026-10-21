using System.Globalization;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>A server scenario configured before it boots, and the changes a test makes live.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[WorldConfig("temporalStorms", "off")]
[ServerConfig(nameof(Vintagestory.Server.ServerConfig.AllowPvP), false)]
public class LiveScenarioConfigurationTests : ServerScenarioBase
{
    [ServerScenario]
    public void TheServer_StartsWithTheConfiguredValues()
    {
        Assert.Equal("off", Server.Api.World.Config.GetString("temporalStorms"));
        Assert.False(Server.Config.AllowPvP);
    }

    [ServerScenario]
    public void LiveChanges_TakeEffectAtOnce()
    {
        Server.WorldConfig.Set("temporalStorms", "veryoften");
        Assert.Equal("veryoften", Server.Api.World.Config.GetString("temporalStorms"));

        Server.ServerConfig.Set(config => config.MaxChunkRadius = 4);
        Assert.Equal(4, Server.Config.MaxChunkRadius);
    }

    [ServerScenario]
    public void AModConfigFile_IsWritten_AndReadBack()
    {
        Server.ModConfig.Write("pharosconfig.json", new { Label = "written", Port = 7 });

        string path = Path.Combine(Server.DataPath, "ModConfig", "pharosconfig.json");
        Assert.Contains("written", File.ReadAllText(path), StringComparison.Ordinal);
    }

    /// <summary>The rollback puts the world's and the server's configuration back as it was at boot.</summary>
    protected override void OnRollbackRestored()
    {
        Assert.Equal("off", Server.Api.World.Config.GetString("temporalStorms"));
        Assert.False(Server.Config.AllowPvP);
        Assert.Equal(12, Server.Config.MaxChunkRadius);
    }
}

/// <summary>One server per configuration row.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharosconfigmod")]
public class LiveConfigurationMatrixTests : ServerScenarioBase
{
    [ServerTheory]
    [WorldConfigMatrix("creatureHostility", "aggressive", "passive")]
    public void EachWorldConfig_RunsWithItsValue(string value)
    {
        Assert.Equal(value, Server.Api.World.Config.GetString("creatureHostility"));
    }

    [ServerTheory]
    [ServerConfigMatrix(nameof(Vintagestory.Server.ServerConfig.MaxChunkRadius), "4", "8")]
    public void EachServerConfig_RunsWithItsValue(string value)
    {
        Assert.Equal(int.Parse(value, CultureInfo.InvariantCulture), Server.Config.MaxChunkRadius);
    }

    [ServerTheory]
    [ModConfigMatrix("pharosconfig.json", "Fixtures/DataFiles/pharosconfig-easy.json", "Fixtures/DataFiles/pharosconfig-hard.json")]
    public async Task EachModConfig_RunsWithItsFile(string fixture)
    {
        CommandResult result = await ExecuteCommandAsync("/pharosconfig");

        Assert.Equal($"label={(fixture.Contains("easy", StringComparison.Ordinal) ? "easy" : "hard")} port=0", result.StatusMessage);
    }
}
