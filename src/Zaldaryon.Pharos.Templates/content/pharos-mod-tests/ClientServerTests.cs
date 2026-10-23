using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Mods;
using Zaldaryon.Pharos.XUnit;
#if (framework == "xunit3")
using Zaldaryon.Pharos.XUnit.V3;
#endif

namespace PharosModTests;

/// <summary>Tests in which an engine-mode client joins the server. See docs/writing-client-server-scenarios.md.</summary>
[Trait(PharosTraits.Category, PharosTraits.Live)]
public sealed class ClientServerTests : ClientServerScenarioBase
{
    [ClientServerScenario]
    public void TheClient_JoinsWithTheMod()
    {
        Assert.True(Client.Mods.Loaded.TryGetValue(ModUnderTest.Id, out LoadedMod? mod), $"The client did not see {ModUnderTest.Id}.");
        Assert.True(mod.Enabled, mod.ToString());
        PharosAssert.NoModErrors(Client);
        Assert.Single(Server.AllOnlinePlayers);
    }
}
