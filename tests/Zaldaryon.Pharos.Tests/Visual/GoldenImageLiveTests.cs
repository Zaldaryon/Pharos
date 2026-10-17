using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Visual;

[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public sealed class GoldenImageLiveTests : ClientScenarioBase
{
    [ClientScenario]
    public void CapturedFramebufferMatchesPngGolden()
    {
        FrameController!.StepFrames(1);
        FramebufferSnapshot frame = Client!.CaptureFrame();
        string directory = Path.Combine(Path.GetTempPath(), "pharos-golden-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            frame.SaveToPng(Path.Combine(directory, "captured.png"));
            PharosAssert.MatchesGolden(frame, "captured", new() { GoldenDirectory = directory, LocalTolerance = 0, GlobalTolerance = 0 });
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
