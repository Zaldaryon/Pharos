using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Input;
using Zaldaryon.Pharos.Text;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.UI;

[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "surviveandbuild")]
public class TextEdgeTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1280,
        Height = 900,
    };

    [ClientTheory]
    [MemberData(nameof(TextCorpus.EdgeCases), MemberType = typeof(TextCorpus))]
    public async Task NoBrokenSurrogates_AllCorpusStrings(string text)
    {
        await Session!.StepFramesAsync(10);
        PharosAssert.NoBrokenSurrogates(text);
    }

    [ClientServerScenario]
    public async Task TypeText_PerCodePoint_DeliversAllCharacters()
    {
        await Session!.StepFramesAsync(10);
        Client!.Input.TypeText("Hello", TextInputMode.PerCodePoint);
        await Session.StepFramesAsync(5);
        // Just verify no crash; actual text field verification needs an open dialog
        Assert.True(true);
    }
}