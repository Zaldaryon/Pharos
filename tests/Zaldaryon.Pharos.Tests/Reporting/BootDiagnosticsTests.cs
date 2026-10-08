using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Tests.Reporting;

/// <summary>
/// Boot diagnostics without a game: what a <see cref="LogCapture"/> keeps from the boot, how
/// <see cref="LogEntry.Source"/> is read, and how allowances judge the entries.
/// </summary>
public class BootDiagnosticsTests
{
    [Fact]
    public void BootList_KeepsWarningsAndWorse_UntilTheBootEnds_AndSurvivesClear()
    {
        var (capture, logger) = Capture();
        logger.Notification("starting");
        logger.Warning("texture missing");
        logger.Error("recipe broken");
        logger.Audit("audit line");

        capture.CompleteBoot();
        logger.Warning("after boot");
        capture.Clear();

        BootDiagnostics boot = capture.BootDiagnostics;
        Assert.True(boot.IsComplete);
        Assert.False(boot.IsTruncated);
        Assert.Equal(["texture missing", "recipe broken"], boot.Select(e => e.Message));
        Assert.Empty(capture.Entries);
    }

    [Fact]
    public void BootList_IsCutShortAtItsCapacity_AndCannotPass()
    {
        var (capture, logger) = Capture();
        for (int i = 0; i <= LogCapture.BootCapacity; i++)
        {
            logger.Warning("noise " + i);
        }

        BootDiagnostics boot = capture.BootDiagnostics;
        Assert.True(boot.IsTruncated);
        Assert.Equal(LogCapture.BootCapacity, boot.Count);

        BootDiagnosticsResult result = boot.Check([new AllowBootDiagnosticAttribute("^noise")]);
        Assert.False(result.Passed);
        Assert.Contains(result.Unmet, u => u.Contains("cut short"));
    }

    [Theory]
    [InlineData("[mymod] Something happened", "mymod")]
    [InlineData("[my-mod.v2] x", "my-mod.v2")]
    [InlineData("[Mod API] Invalid default spawn position", "game")]
    [InlineData("Texture asset 'mymod:block/x' not found", "game")]
    [InlineData("[mymod]no space", "game")]
    [InlineData("[] empty", "game")]
    public void Source_IsTheModLoggerPrefix_OrGame(string message, string source)
    {
        Assert.Equal(source, new LogEntry(EnumAppSide.Server, EnumLogType.Warning, message).Source);
    }

    [Fact]
    public void LogEntry_EqualityIsUnchangedBySource()
    {
        LogEntry a = new(EnumAppSide.Server, EnumLogType.Warning, "[mymod] x");
        LogEntry b = new(EnumAppSide.Server, EnumLogType.Warning, "[mymod] x");
        _ = a.Source;

        Assert.Equal(a, b);
    }

    [Fact]
    public void Allowances_MatchByPatternLevelAndSource()
    {
        BootDiagnostics boot = Boot(
            Warning("Texture asset 'mymod:block/a' not found"),
            Error("[mymod] Could not load config"),
            Warning("[othermod] Could not load config"));

        BootDiagnosticsResult result = boot.Check(
        [
            new AllowBootDiagnosticAttribute(@"texture asset .* NOT found"),
            new AllowBootDiagnosticAttribute("Could not load config") { Level = EnumLogType.Error, Source = "MYMOD" },
        ]);

        LogEntry unexpected = Assert.Single(result.Unexpected);
        Assert.Equal("[othermod] Could not load config", unexpected.Message);
        Assert.Empty(result.Unmet);
        Assert.False(result.Passed);
    }

    [Fact]
    public void Count_IsExact_AndImpliesRequired()
    {
        BootDiagnostics boot = Boot(Warning("shape missing a"), Warning("shape missing b"));

        Assert.True(boot.Check([new AllowBootDiagnosticAttribute("shape missing") { Count = 2 }]).Passed);

        BootDiagnosticsResult tooFew = boot.Check([new AllowBootDiagnosticAttribute("shape missing") { Count = 3 }]);
        Assert.Contains(tooFew.Unmet, u => u.Contains("expected 3, saw 2"));

        BootDiagnosticsResult none = Boot().Check([new AllowBootDiagnosticAttribute("shape missing") { Count = 1 }]);
        Assert.Contains(none.Unmet, u => u.Contains("expected 1, saw 0"));
    }

    [Fact]
    public void Required_ReportsAStaleAllowance()
    {
        BootDiagnosticsResult result = Boot().Check([new AllowBootDiagnosticAttribute("fixed long ago") { Required = true }]);

        Assert.Empty(result.Unexpected);
        Assert.Contains(result.Unmet, u => u.Contains("required but never logged"));
        Assert.Contains("Unmet allowance: [AllowBootDiagnostic(\"fixed long ago\", Required = true)]", result.Describe());
    }

    [Fact]
    public void AnEntryCountsTowardEveryAllowanceItMatches()
    {
        BootDiagnostics boot = Boot(Warning("shape missing"));

        BootDiagnosticsResult result = boot.Check(
        [
            new AllowBootDiagnosticAttribute("shape") { Count = 1 },
            new AllowBootDiagnosticAttribute("missing") { Count = 1 },
        ]);

        Assert.True(result.Passed);
    }

    [Fact]
    public void AnIncompleteBoot_IsNoted_ButDoesNotFail()
    {
        var (capture, _) = Capture();

        BootDiagnosticsResult result = capture.BootDiagnostics.Check([]);

        Assert.True(result.Passed);
        Assert.Contains(result.Incomplete, n => n.Contains("had not finished booting"));
    }

    [Fact]
    public void AnInvalidPattern_IsReportedWithTheAllowance()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => Boot(Warning("x")).Check([new AllowBootDiagnosticAttribute("(unclosed")]));

        Assert.Contains("(unclosed", error.Message);
    }

    [Fact]
    public void TheHarnessAllowsTheServerOverloadWarning()
    {
        BootDiagnosticsResult result = BootDiagnosticsResult_Of(Boot(Warning("Server overloaded. A tick took 812ms to complete.")));

        Assert.True(result.Passed);
    }

    [Fact]
    public void BootCheck_ReadsTheClassAndAssemblyAttributes()
    {
        Assert.True(BootCheck.IsStrict(typeof(StrictSample)));
        Assert.True(BootCheck.IsStrict(typeof(DerivedStrictSample)));
        Assert.False(BootCheck.IsStrict(typeof(BootDiagnosticsTests)));
        Assert.Contains(BootCheck.Allowances(typeof(DerivedStrictSample)), a => a.Pattern == "inherited");
        Assert.Contains(BootCheck.Allowances(typeof(DerivedStrictSample)), a => a.Pattern == "own");
    }

    [Fact]
    public void BootCheck_RemembersAFailedStrictBoot_AndReplaysItWithoutBooting()
    {
        BootCheck.Forget(typeof(StrictSample));
        try
        {
            BootDiagnosticsException first = Assert.Throws<BootDiagnosticsException>(() => BootCheck.Enforce(typeof(StrictSample), Boot(Warning("[mymod] unexpected"))));
            Assert.Contains("[mymod] unexpected", first.Message);

            ScenarioFailedException replay = Assert.Throws<ScenarioFailedException>(() => BootCheck.ThrowIfFailedBefore(typeof(StrictSample)));
            Assert.Contains("is not booted again", replay.Message);
            Assert.Contains("[mymod] unexpected", replay.Message);
        }
        finally
        {
            BootCheck.Forget(typeof(StrictSample));
        }
    }

    [Fact]
    public void BootCheck_PassesACleanStrictBoot()
    {
        Assert.True(BootCheck.Enforce(typeof(StrictSample), Boot(Warning("expected inherited warning"))));
        BootCheck.ThrowIfFailedBefore(typeof(StrictSample));
    }

    private static BootDiagnosticsResult BootDiagnosticsResult_Of(BootDiagnostics boot) => BootCheck.Evaluate(typeof(BootDiagnosticsTests), boot);

    private static LogEntry Warning(string message) => new(EnumAppSide.Server, EnumLogType.Warning, message);

    private static LogEntry Error(string message) => new(EnumAppSide.Server, EnumLogType.Error, message);

    private static BootDiagnostics Boot(params LogEntry[] entries)
    {
        var (capture, logger) = Capture();
        foreach (LogEntry entry in entries)
        {
            logger.Log(entry.Type, entry.Message);
        }

        capture.CompleteBoot();
        return capture.BootDiagnostics;
    }

    private static (LogCapture Capture, TestLogger Logger) Capture()
    {
        TestLogger logger = new();
        LogCapture capture = new(EnumAppSide.Server);
        capture.Attach(logger);
        return (capture, logger);
    }

    private sealed class TestLogger : LoggerBase
    {
        protected override void LogImpl(EnumLogType logType, string format, params object[] args)
        {
        }
    }

    [StrictBoot]
    [AllowBootDiagnostic("inherited")]
    private class StrictSample;

    [AllowBootDiagnostic("own")]
    private sealed class DerivedStrictSample : StrictSample;
}
