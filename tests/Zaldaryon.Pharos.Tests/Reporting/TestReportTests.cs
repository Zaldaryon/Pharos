using Xunit;
using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.Tests.Reporting;

/// <summary>
/// Tests for the TestReport and TestReportBuilder classes.
/// All tests are pure-logic and headless-safe.
/// </summary>
public sealed class TestReportTests : IDisposable
{
    private readonly string _tempDir;

    public TestReportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pharos-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); }
        catch { /* ignore cleanup errors */ }
    }

    // -------------------------------------------------------------------------
    // FrameMetricsRecord tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FrameMetricsRecord_Empty_HasDefaultValues()
    {
        FrameMetricsRecord record = FrameMetricsRecord.Empty;

        Assert.Equal(0, record.FrameIndex);
        Assert.Equal(0f, record.FrameTimeMs);
        Assert.Equal(0, record.DrawCalls);
        Assert.Equal(0, record.IndirectDrawCalls);
        Assert.Equal(0, record.CulledChunks);
        Assert.Equal(0, record.VisibleChunks);
        Assert.Equal(0L, record.MemoryBytes);
        Assert.Equal(0, record.TotalChunks);
        Assert.Equal(0f, record.CullingEfficiency);
    }

    [Fact]
    public void FrameMetricsRecord_TotalChunks_SumsVisibleAndCulled()
    {
        FrameMetricsRecord record = new()
        {
            CulledChunks = 30,
            VisibleChunks = 70,
        };

        Assert.Equal(100, record.TotalChunks);
    }

    [Fact]
    public void FrameMetricsRecord_CullingEfficiency_CalculatesCorrectly()
    {
        FrameMetricsRecord record = new()
        {
            CulledChunks = 75,
            VisibleChunks = 25,
        };

        Assert.Equal(75f, record.CullingEfficiency, precision: 1);
    }

    [Fact]
    public void FrameMetricsRecord_CullingEfficiency_ZeroWhenNoChunks()
    {
        FrameMetricsRecord record = new();

        Assert.Equal(0f, record.CullingEfficiency);
    }

    // -------------------------------------------------------------------------
    // TestReportBuilder tests
    // -------------------------------------------------------------------------

    [Fact]
    public void TestReportBuilder_Create_SetsTitle()
    {
        TestReportBuilder builder = TestReportBuilder.Create("Test Report");
        TestReport report = builder.Build();

        Assert.Equal("Test Report", report.Title);
    }

    [Fact]
    public void TestReportBuilder_AddFrame_IncreasesFrameCount()
    {
        TestReportBuilder builder = new("Test");

        builder.AddFrame(new FrameMetricsRecord { FrameIndex = 0 });
        builder.AddFrame(new FrameMetricsRecord { FrameIndex = 1 });

        Assert.Equal(2, builder.FrameCount);
    }

    [Fact]
    public void TestReportBuilder_AddFrame_WithParameters_CreatesRecord()
    {
        TestReportBuilder builder = new("Test");

        builder.AddFrame(
            frameIndex: 5,
            frameTimeMs: 16.67f,
            drawCalls: 100,
            indirectDrawCalls: 10,
            culledChunks: 50,
            visibleChunks: 50,
            memoryBytes: 1024 * 1024);

        TestReport report = builder.Build();
        Assert.Single(report.Frames);
        Assert.Equal(5, report.Frames[0].FrameIndex);
        Assert.Equal(16.67f, report.Frames[0].FrameTimeMs, precision: 2);
        Assert.Equal(100, report.Frames[0].DrawCalls);
    }

    [Fact]
    public void TestReportBuilder_AddPassResult_IncreasesPassCount()
    {
        TestReportBuilder builder = new("Test");

        builder.AddPassResult("Test1");
        builder.AddPassResult("Test2");

        Assert.Equal(2, builder.PassCount);
    }

    [Fact]
    public void TestReportBuilder_AddFailResult_IncreasesFailCount()
    {
        TestReportBuilder builder = new("Test");

        builder.AddFailResult("FailedTest", "assertion failed");

        Assert.Equal(1, builder.FailCount);
    }

    [Fact]
    public void TestReportBuilder_FluentChaining_Works()
    {
        TestReport report = TestReportBuilder.Create("Fluent Test")
            .AddFrame(new FrameMetricsRecord { FrameIndex = 0, FrameTimeMs = 10f })
            .AddFrame(new FrameMetricsRecord { FrameIndex = 1, FrameTimeMs = 12f })
            .AddPassResult("PassedTest")
            .AddFailResult("FailedTest", "reason")
            .WithDuration(500)
            .Build();

        Assert.Equal("Fluent Test", report.Title);
        Assert.Equal(2, report.Frames.Count);
        Assert.Equal(1, report.PassCount);
        Assert.Equal(1, report.FailCount);
        Assert.Equal(500, report.DurationMs);
    }

    [Fact]
    public void TestReportBuilder_WithDuration_OverridesAutomatic()
    {
        TestReportBuilder builder = new("Test");
        builder.WithDuration(12345);

        TestReport report = builder.Build();

        Assert.Equal(12345, report.DurationMs);
    }

    // -------------------------------------------------------------------------
    // TestReport computed properties tests
    // -------------------------------------------------------------------------

    [Fact]
    public void TestReport_TotalTests_SumsPassAndFail()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddPassResult("Pass1")
            .AddPassResult("Pass2")
            .AddFailResult("Fail1", "reason")
            .Build();

        Assert.Equal(3, report.TotalTests);
    }

    [Fact]
    public void TestReport_PassRate_CalculatesCorrectly()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddPassResult("Pass1")
            .AddPassResult("Pass2")
            .AddPassResult("Pass3")
            .AddFailResult("Fail1", "reason")
            .Build();

        Assert.Equal(75f, report.PassRate, precision: 1);
    }

    [Fact]
    public void TestReport_PassRate_100WhenNoTests()
    {
        TestReport report = new TestReportBuilder("Test").Build();

        Assert.Equal(100f, report.PassRate);
    }

    [Fact]
    public void TestReport_AverageFrameTime_CalculatesCorrectly()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddFrame(new FrameMetricsRecord { FrameTimeMs = 10f })
            .AddFrame(new FrameMetricsRecord { FrameTimeMs = 20f })
            .AddFrame(new FrameMetricsRecord { FrameTimeMs = 30f })
            .Build();

        Assert.Equal(20f, report.AverageFrameTimeMs, precision: 1);
    }

    [Fact]
    public void TestReport_MaxFrameTime_FindsMaximum()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddFrame(new FrameMetricsRecord { FrameTimeMs = 10f })
            .AddFrame(new FrameMetricsRecord { FrameTimeMs = 50f })
            .AddFrame(new FrameMetricsRecord { FrameTimeMs = 30f })
            .Build();

        Assert.Equal(50f, report.MaxFrameTimeMs, precision: 1);
    }

    [Fact]
    public void TestReport_TotalDrawCalls_SumsAllFrames()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddFrame(new FrameMetricsRecord { DrawCalls = 100, IndirectDrawCalls = 10 })
            .AddFrame(new FrameMetricsRecord { DrawCalls = 150, IndirectDrawCalls = 15 })
            .Build();

        Assert.Equal(250, report.TotalDrawCalls);
        Assert.Equal(25, report.TotalIndirectDrawCalls);
    }

    // -------------------------------------------------------------------------
    // HTML generation tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GenerateHtml_ContainsTitle()
    {
        TestReport report = new TestReportBuilder("My Test Report").Build();

        string html = report.GenerateHtml();

        Assert.Contains("<title>My Test Report</title>", html);
        Assert.Contains("<h1>My Test Report</h1>", html);
    }

    [Fact]
    public void GenerateHtml_ContainsDoctype()
    {
        TestReport report = new TestReportBuilder("Test").Build();

        string html = report.GenerateHtml();

        Assert.StartsWith("<!DOCTYPE html>", html);
    }

    [Fact]
    public void GenerateHtml_IncludesFrameMetrics()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddFrame(new FrameMetricsRecord
            {
                FrameIndex = 0,
                FrameTimeMs = 16.67f,
                DrawCalls = 500,
                IndirectDrawCalls = 50,
                VisibleChunks = 100,
                CulledChunks = 200,
            })
            .Build();

        string html = report.GenerateHtml();

        Assert.Contains("Frame Metrics", html);
        Assert.Contains("16.67", html);
        Assert.Contains("500", html);
    }

    [Fact]
    public void GenerateHtml_IncludesTestResults()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddPassResult("PassedTest")
            .AddFailResult("FailedTest", "assertion failed")
            .Build();

        string html = report.GenerateHtml();

        Assert.Contains("Test Results", html);
        Assert.Contains("PassedTest", html);
        Assert.Contains("FailedTest", html);
        Assert.Contains("assertion failed", html);
    }

    [Fact]
    public void GenerateHtml_EscapesSpecialCharacters()
    {
        TestReport report = new TestReportBuilder("Test <script>alert('xss')</script>").Build();

        string html = report.GenerateHtml();

        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    // -------------------------------------------------------------------------
    // Markdown generation tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GenerateMarkdown_ContainsTitle()
    {
        TestReport report = new TestReportBuilder("My Test Report").Build();

        string md = report.GenerateMarkdown();

        Assert.StartsWith("# My Test Report", md);
    }

    [Fact]
    public void GenerateMarkdown_IncludesSummarySection()
    {
        TestReport report = new TestReportBuilder("Test")
            .WithDuration(1234)
            .Build();

        string md = report.GenerateMarkdown();

        Assert.Contains("## Summary", md);
        Assert.Contains("**Duration:** 1234 ms", md);
    }

    [Fact]
    public void GenerateMarkdown_IncludesFrameTable()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddFrame(new FrameMetricsRecord
            {
                FrameIndex = 0,
                FrameTimeMs = 16.67f,
                DrawCalls = 500,
            })
            .Build();

        string md = report.GenerateMarkdown();

        Assert.Contains("## Frame Metrics", md);
        Assert.Contains("| Frame | Time (ms) |", md);
        Assert.Contains("| 0 | 16.67 |", md);
    }

    [Fact]
    public void GenerateMarkdown_IncludesTestResultsTable()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddPassResult("TestA")
            .AddFailResult("TestB", "failed")
            .Build();

        string md = report.GenerateMarkdown();

        Assert.Contains("## Test Results", md);
        Assert.Contains("| TestA | ✅ PASS |", md);
        Assert.Contains("| TestB | ❌ FAIL | failed |", md);
    }

    [Fact]
    public void GenerateMarkdown_EscapesPipeCharacters()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddFailResult("Test|Name", "reason|with|pipes")
            .Build();

        string md = report.GenerateMarkdown();

        Assert.Contains("Test\\|Name", md);
        Assert.Contains("reason\\|with\\|pipes", md);
    }

    // -------------------------------------------------------------------------
    // File I/O tests
    // -------------------------------------------------------------------------

    [Fact]
    public void SaveHtml_WritesFile()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddPassResult("Test1")
            .Build();
        string path = Path.Combine(_tempDir, "report.html");

        report.SaveHtml(path);

        Assert.True(File.Exists(path));
        string content = File.ReadAllText(path);
        Assert.Contains("<!DOCTYPE html>", content);
        Assert.Contains("Test", content);
    }

    [Fact]
    public void SaveMarkdown_WritesFile()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddPassResult("Test1")
            .Build();
        string path = Path.Combine(_tempDir, "report.md");

        report.SaveMarkdown(path);

        Assert.True(File.Exists(path));
        string content = File.ReadAllText(path);
        Assert.StartsWith("# Test", content);
    }

    [Fact]
    public void SaveHtml_ThrowsOnNullPath()
    {
        TestReport report = new TestReportBuilder("Test").Build();

        Assert.Throws<ArgumentNullException>(() => report.SaveHtml(null!));
    }

    [Fact]
    public void SaveMarkdown_ThrowsOnEmptyPath()
    {
        TestReport report = new TestReportBuilder("Test").Build();

        Assert.Throws<ArgumentException>(() => report.SaveMarkdown(""));
    }

    // -------------------------------------------------------------------------
    // Memory formatting tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GenerateHtml_FormatsMemoryBytesCorrectly()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddFrame(new FrameMetricsRecord { MemoryBytes = 1024 * 1024 * 512 }) // 512 MB
            .Build();

        string html = report.GenerateHtml();

        Assert.Contains("512.00 MB", html);
    }

    [Fact]
    public void GenerateMarkdown_FormatsLargeMemory()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddFrame(new FrameMetricsRecord { MemoryBytes = 2L * 1024 * 1024 * 1024 }) // 2 GB
            .Build();

        string md = report.GenerateMarkdown();

        Assert.Contains("2.00 GB", md);
    }

    [Fact]
    public void GenerateHtml_FormatsSmallMemory()
    {
        TestReport report = new TestReportBuilder("Test")
            .AddFrame(new FrameMetricsRecord { MemoryBytes = 512 })
            .Build();

        string html = report.GenerateHtml();

        Assert.Contains("512 B", html);
    }
}
