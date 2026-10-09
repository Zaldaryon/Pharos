using Vintagestory.API.Client;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Performance;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Performance;

public class TimingStatsTests
{
    [Fact]
    public void Percentiles_UseTheNearestRank()
    {
        TimingStats stats = TimingStats.From(Enumerable.Range(1, 60).Select(i => TimeSpan.FromMilliseconds(i)).Reverse());

        Assert.Equal(60, stats.Count);
        Assert.Equal(TimeSpan.FromMilliseconds(1), stats.Min);
        Assert.Equal(TimeSpan.FromMilliseconds(30), stats.Median);
        Assert.Equal(TimeSpan.FromMilliseconds(57), stats.P95);
        Assert.Equal(TimeSpan.FromMilliseconds(60), stats.P99);
        Assert.Equal(TimeSpan.FromMilliseconds(60), stats.Max);
        Assert.Equal(TimeSpan.FromMilliseconds(1830), stats.Total);
        Assert.Equal(TimeSpan.FromMilliseconds(30.5), stats.Mean);
        Assert.Equal(TimeSpan.FromMilliseconds(60), stats.Samples[0]);
    }

    [Fact]
    public void FewOrNoSamples_AreHandled()
    {
        TimingStats one = TimingStats.From([TimeSpan.FromMilliseconds(4)]);
        Assert.Equal(TimeSpan.FromMilliseconds(4), one.P99);
        Assert.Equal(one.Min, one.Median);

        TimingStats none = TimingStats.From([]);
        Assert.Equal(0, none.Count);
        Assert.Equal(TimeSpan.Zero, none.Max);
        Assert.Equal("no samples", none.ToString());
    }
}

public class FrameBaselineTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), "pharos-baselines-" + Guid.NewGuid().ToString("N")[..8] + ".json");

    public void Dispose() => File.Delete(_file);

    [Fact]
    public void AMeasurement_IsCheckedAgainstItsBaseline_AndUpdatesMerge()
    {
        FrameMeasurement measurement = Measurement(TimeSpan.FromMilliseconds(10), allocatedBytes: 6000, frames: 3);

        FrameBaselineResult missing = FrameBaseline.Check(measurement, _file, "scene");
        Assert.False(missing.Passed);
        Assert.Null(missing.Baseline);
        Assert.Throws<FrameBudgetExceededException>(() => FrameBaseline.Assert(measurement, _file, "scene"));

        Zaldaryon.Pharos.Benchmarks.BaselinesFile.WriteBaselines(_file, new Dictionary<string, float> { ["scene"] = 9f, ["other"] = 1f });
        Assert.True(FrameBaseline.Check(measurement, _file, "scene", tolerance: 0.25).Passed);
        FrameBaselineResult over = FrameBaseline.Check(measurement, _file, "scene", tolerance: 0.05);
        Assert.False(over.Passed);
        Assert.Contains("over", over.ToString());

        Environment.SetEnvironmentVariable(FrameBaseline.UpdateVariable, "1");
        try
        {
            Assert.True(FrameBaseline.Check(measurement, _file, "scene.alloc", FrameStat.AllocatedBytesPerFrame).Updated);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FrameBaseline.UpdateVariable, null);
        }

        Dictionary<string, float> written = Zaldaryon.Pharos.Benchmarks.BaselinesFile.ReadBaselines(_file);
        Assert.Equal(2000f, written["scene.alloc"]);
        Assert.Equal(1f, written["other"]);
        Assert.Equal(9f, written["scene"]);
    }

    private static FrameMeasurement Measurement(TimeSpan perFrame, long allocatedBytes, int frames)
    {
        TimingStats work = TimingStats.From(Enumerable.Repeat(perFrame, frames));
        TimingStats zero = TimingStats.From(Enumerable.Repeat(TimeSpan.Zero, frames));
        return new FrameMeasurement(work, allocatedBytes, allocatedBytes, 0, 0, 0, [], [], zero, zero, null);
    }
}

/// <summary>Frames of a real engine-mode client and its server, measured.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharosrendermod")]
public class LiveFrameMeasurementTests : ClientServerScenarioBase
{
    private static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(4.5);

    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task AWindow_IsMeasured_FrameByFrame()
    {
        // Warm up: the first frames compile code.
        await Session!.StepFramesAsync(30);

        FrameMeasurement measured = await Session.MeasureFramesAsync(60);

        Assert.Equal(60, measured.Frames);
        Assert.True(measured.Min > TimeSpan.Zero);
        Assert.Equal(measured.Work.Samples.Aggregate(TimeSpan.Zero, (a, b) => a + b), measured.Total);
        StageTiming opaque = measured.Stage(EnumRenderStage.Opaque)!;
        Assert.True(opaque.FramesRun == 60, $"Opaque ran in {opaque.FramesRun} frames:\n{measured}");
        Assert.True(opaque.PerFrame.Total <= measured.Total);
        RendererTiming mine = Assert.Single(measured.Of("pharosrendermod"), r => r.Renderer.Stage == EnumRenderStage.Opaque);
        Assert.True(mine.Calls == 60, $"the mod's renderer ran {mine.Calls} times:\n{measured}");
        Assert.Contains(measured.Renderers, r => r.Renderer.Mod == null);
        Assert.True(measured.AllocatedBytes > 0);
        Assert.True(measured.ProcessAllocatedBytes >= measured.AllocatedBytes);
        Assert.All(measured.Other.Samples, t => Assert.True(t >= TimeSpan.Zero));

        // The server's work, not its sleep: an idle superflat tick is far below its 33 ms.
        ServerTickTiming ticks = measured.ServerTicks!;
        Assert.True(ticks.Work.Count + ticks.SuspendedTicks == 60, $"{ticks.Work.Count} server ticks measured, {ticks.SuspendedTicks} suspended:\n{measured}");
        Assert.True(ticks.Work.Count > 0);
        Assert.True(ticks.Work.Median < TimeSpan.FromMilliseconds(25), $"server ticks: {ticks.Work}");
        Assert.Contains("frames:", measured.ToString());
    }

    [ClientServerScenario]
    public async Task ASlowRenderer_ShowsInItsStage_AndTheFrame()
    {
        await Session!.StepFramesAsync(10);
        await Client!.Commands.ExecuteSuccessAsync(".pharosfx slow true");
        try
        {
            FrameMeasurement measured = await Session.MeasureFramesAsync(20);

            RendererTiming mine = Assert.Single(measured.Of("pharosrendermod"), r => r.Renderer.Stage == EnumRenderStage.Opaque);
            Assert.True(mine.PerFrame.Min >= Slow, $"renderer: {mine.PerFrame}");
            Assert.True(measured.Stage(EnumRenderStage.Opaque)!.PerFrame.Min >= Slow);
            Assert.True(measured.Min >= Slow);
        }
        finally
        {
            await Client.Commands.ExecuteSuccessAsync(".pharosfx slow false");
        }
    }

    [ClientServerScenario]
    public async Task ServerTicks_FollowTheTicksPerFrame_AndAClientAloneHasNone()
    {
        FrameMeasurement twice = await Session!.MeasureFramesAsync(10, serverTicksPerFrame: 2);
        Assert.Equal(20, twice.ServerTicks!.Work.Count + twice.ServerTicks.SuspendedTicks);

        FrameMeasurement client = await Client!.MeasureFramesAsync(10);
        Assert.Equal(10, client.Frames);
        Assert.Null(client.ServerTicks);
    }
}
