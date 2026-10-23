using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Mods;
using Zaldaryon.Pharos.XUnit;

namespace ExampleMod.Tests;

/// <summary>Tests that need only a server.</summary>
[Trait(PharosTraits.Category, PharosTraits.Live)]
public sealed class ServerTests : ServerScenarioBase
{
    [ServerScenario]
    public void TheServer_LoadsTheMod()
    {
        Assert.True(Server.Mods.Loaded.TryGetValue(ModUnderTest.Id, out LoadedMod? mod), $"The server did not see {ModUnderTest.Id}.");
        Assert.True(mod.Enabled, mod.ToString());
        PharosAssert.NoModErrors(Server);
    }
}
