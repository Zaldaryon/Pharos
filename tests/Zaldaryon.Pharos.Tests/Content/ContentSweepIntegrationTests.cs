using Xunit;
using Zaldaryon.Pharos.Content;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Content;

[Collection("Sequential")]
[ServerWorld(Preset = WorldPreset.SurvivalFlat)]
[ServerMods("TestMods/pharoscontentsweepmod")]
public sealed class ContentSweepIntegrationTests : ServerScenarioBase
{
    [ServerTheory]
    [ContentSweep("pharoscontentsweepmod", SkipCodes = ["pharoscontentsweepmod:sweepthrowingblock"])]
    public async Task ContentCode_CompletesEveryApplicableStep(ContentCode code)
    {
        SweepReport report = await ContentSweep.RunAsync(Server!, code);

        Assert.Empty(report.Failures);
        Assert.Empty(report.LeakedTickListeners);
        switch (code.Kind)
        {
            case ContentKind.Block:
                Assert.Contains(code, report.Blocks);
                Assert.Contains(code, report.Passed(SweepStep.BlockEntityRoundTrip));
                Assert.DoesNotContain(code, report.WithoutBlockEntity);
                break;
            case ContentKind.Item:
                Assert.Contains(code, report.Items);
                Assert.Contains(code, report.Passed(SweepStep.ItemHeldIdle));
                break;
            case ContentKind.Entity:
                Assert.Contains(code, report.Entities);
                Assert.Contains(code, report.Passed(SweepStep.EntityRoundTrip));
                break;
            default:
                Assert.Fail($"Unexpected content kind {code.Kind}.");
                break;
        }
    }

    [ServerScenario]
    public async Task DomainOverload_SweepsTheLoadedModContent()
    {
        SweepReport report = await ContentSweep.RunAsync(Server!, "pharoscontentsweepmod", new ContentSweepOptions
        {
            Skip = ["pharoscontentsweepmod:sweepthrowingblock"],
        });

        Assert.Empty(report.Failures);
        Assert.Empty(report.LeakedTickListeners);
        Assert.DoesNotContain(Server!.TestPlayers, player =>
            player.IsConnected && player.PlayerName?.StartsWith("SweepBot", StringComparison.Ordinal) == true);
        Assert.Single(report.Blocks);
        Assert.Single(report.Items);
        Assert.Single(report.Entities);
    }

    [ServerScenario]
    public async Task Sweep_RecordsStepExceptionsAndLoggedErrors()
    {
        ContentCode code = new(ContentKind.Block, "pharoscontentsweepmod:sweepthrowingblock");
        SweepReport report = await ContentSweep.RunAsync(Server!, code);

        SweepFailure failure = Assert.Single(report.Failures, item => item.Step == SweepStep.BlockNeighbourChange);
        Assert.IsType<InvalidOperationException>(failure.Exception);
        Assert.Contains(failure.LoggedErrors, entry => entry.Message.Contains("content-sweep-error", StringComparison.Ordinal));

        SweepFailure unresolvedDrop = Assert.Single(report.Failures, item => item.Step == SweepStep.BlockDrops);
        Assert.Contains("missingdrop", unresolvedDrop.Message, StringComparison.Ordinal);
    }

    [ServerScenario]
    public async Task Steps_RunOnlySelectedOperations()
    {
        ContentCode code = new(ContentKind.Block, "pharoscontentsweepmod:sweepblock", SweepSteps.BlockDrops);
        SweepReport report = await ContentSweep.RunAsync(Server!, code);

        Assert.Empty(report.Failures);
        Assert.Contains(code, report.Passed(SweepStep.BlockDrops));
        Assert.DoesNotContain(code, report.Passed(SweepStep.BlockPlace));
    }
}
