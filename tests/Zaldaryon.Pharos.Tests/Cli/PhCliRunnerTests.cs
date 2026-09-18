using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.Cli;

namespace Zaldaryon.Pharos.Tests.Cli;

/// <summary>
/// Tests for the Pharos CLI runner components.
/// All tests are pure logic tests that don't require native libraries or GPU.
/// </summary>
public class PhCliRunnerTests
{
    #region CliRunOptions.Parse Tests

    [Fact]
    public void Parse_EmptyArgs_ReturnsDefault()
    {
        var options = CliRunOptions.Parse([]);

        Assert.NotNull(options);
        Assert.Null(options.AssemblyPath);
        Assert.Null(options.Filter);
        Assert.Null(options.OutputDir);
        Assert.False(options.Verbose);
        Assert.Equal(1, options.MaxParallel);
    }

    [Fact]
    public void Parse_HelpFlag_ReturnsNull()
    {
        Assert.Null(CliRunOptions.Parse(["--help"]));
        Assert.Null(CliRunOptions.Parse(["-h"]));
        Assert.Null(CliRunOptions.Parse(["-?"]));
    }

    [Fact]
    public void Parse_VerboseFlag_SetsVerbose()
    {
        var options = CliRunOptions.Parse(["--verbose"]);
        Assert.True(options?.Verbose);

        options = CliRunOptions.Parse(["-v"]);
        Assert.True(options?.Verbose);
    }

    [Fact]
    public void Parse_AssemblyPath_SetsPath()
    {
        var options = CliRunOptions.Parse(["--assembly", "test.dll"]);
        Assert.Equal("test.dll", options?.AssemblyPath);

        options = CliRunOptions.Parse(["-a", "other.dll"]);
        Assert.Equal("other.dll", options?.AssemblyPath);
    }

    [Fact]
    public void Parse_FilterArg_SetsFilter()
    {
        var options = CliRunOptions.Parse(["--filter", "Inventory"]);
        Assert.Equal("Inventory", options?.Filter);

        options = CliRunOptions.Parse(["-f", "Memory"]);
        Assert.Equal("Memory", options?.Filter);
    }

    [Fact]
    public void Parse_OutputDirArg_SetsOutputDir()
    {
        var options = CliRunOptions.Parse(["--output-dir", "./results"]);
        Assert.Equal("./results", options?.OutputDir);

        options = CliRunOptions.Parse(["-o", "/tmp/out"]);
        Assert.Equal("/tmp/out", options?.OutputDir);
    }

    [Fact]
    public void Parse_MaxParallelArg_SetsMaxParallel()
    {
        var options = CliRunOptions.Parse(["--max-parallel", "4"]);
        Assert.Equal(4, options?.MaxParallel);

        options = CliRunOptions.Parse(["-p", "8"]);
        Assert.Equal(8, options?.MaxParallel);
    }

    [Fact]
    public void Parse_PositionalArg_TreatedAsAssemblyPath()
    {
        var options = CliRunOptions.Parse(["MyTests.dll"]);
        Assert.Equal("MyTests.dll", options?.AssemblyPath);
    }

    [Fact]
    public void Parse_CombinedArgs_ParsesAll()
    {
        var options = CliRunOptions.Parse([
            "-a", "tests.dll",
            "-f", "Scenario",
            "-o", "./out",
            "-v",
            "-p", "2"
        ]);

        Assert.NotNull(options);
        Assert.Equal("tests.dll", options.AssemblyPath);
        Assert.Equal("Scenario", options.Filter);
        Assert.Equal("./out", options.OutputDir);
        Assert.True(options.Verbose);
        Assert.Equal(2, options.MaxParallel);
    }

    [Fact]
    public void Parse_MissingAssemblyValue_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliRunOptions.Parse(["--assembly"]));
        Assert.Contains("--assembly requires", ex.Message);
    }

    [Fact]
    public void Parse_MissingFilterValue_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => CliRunOptions.Parse(["--filter"]));
        Assert.Contains("--filter requires", ex.Message);
    }

    [Fact]
    public void Parse_InvalidMaxParallel_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliRunOptions.Parse(["--max-parallel", "invalid"]));
        Assert.Contains("positive integer", ex.Message);

        ex = Assert.Throws<ArgumentException>(() =>
            CliRunOptions.Parse(["--max-parallel", "0"]));
        Assert.Contains("positive integer", ex.Message);
    }

    [Fact]
    public void Parse_UnknownFlag_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            CliRunOptions.Parse(["--unknown-flag"]));
        Assert.Contains("Unknown argument", ex.Message);
    }

    #endregion

    #region CliRunResult Tests

    [Fact]
    public void CliRunResult_TotalCount_SumsAllCategories()
    {
        var result = new CliRunResult(10, 2, 3, 1000, []);
        Assert.Equal(15, result.TotalCount);
    }

    [Fact]
    public void CliRunResult_IsSuccess_TrueWhenNoFailures()
    {
        var success = new CliRunResult(10, 0, 5, 1000, []);
        Assert.True(success.IsSuccess);

        var failure = new CliRunResult(10, 1, 0, 1000, []);
        Assert.False(failure.IsSuccess);
    }

    [Fact]
    public void CliRunResult_ExitCode_ZeroOnSuccess()
    {
        var success = new CliRunResult(10, 0, 0, 1000, []);
        Assert.Equal(0, success.ExitCode);

        var failure = new CliRunResult(10, 1, 0, 1000, []);
        Assert.Equal(1, failure.ExitCode);
    }

    [Fact]
    public void CliRunResult_PassRate_CalculatesCorrectly()
    {
        var result = new CliRunResult(8, 2, 0, 1000, []);
        Assert.Equal(80f, result.PassRate, precision: 1);

        var allPass = new CliRunResult(10, 0, 0, 1000, []);
        Assert.Equal(100f, allPass.PassRate);

        var empty = new CliRunResult(0, 0, 0, 1000, []);
        Assert.Equal(100f, empty.PassRate); // No tests = 100% pass
    }

    [Fact]
    public void CliRunResult_Empty_CreatesEmptyResult()
    {
        var result = CliRunResult.Empty(500);

        Assert.Equal(0, result.PassCount);
        Assert.Equal(0, result.FailCount);
        Assert.Equal(0, result.SkippedCount);
        Assert.Equal(500, result.DurationMs);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public void CliRunResult_Error_CreatesErrorResult()
    {
        var result = CliRunResult.Error("Something went wrong", 100);

        Assert.Equal(0, result.PassCount);
        Assert.Equal(1, result.FailCount);
        Assert.Equal(100, result.DurationMs);
        Assert.Single(result.Failures);
        Assert.Equal("CLI", result.Failures[0].TestName);
        Assert.Equal("Something went wrong", result.Failures[0].FailureMessage);
    }

    [Fact]
    public void CliRunResult_FormatSummary_IncludesAllData()
    {
        var failures = new List<CliTestFailure>
        {
            new("Test.Failure1", "Expected true", "at line 42")
        };
        var result = new CliRunResult(9, 1, 2, 1500, failures);

        var summary = result.FormatSummary(verbose: false);

        Assert.Contains("12", summary); // Total
        Assert.Contains("9", summary);  // Passed
        Assert.Contains("1", summary);  // Failed
        Assert.Contains("2", summary);  // Skipped
        Assert.Contains("1500", summary); // Duration
        Assert.Contains("Test.Failure1", summary);
    }

    [Fact]
    public void CliRunResult_FormatSummary_VerboseIncludesStackTrace()
    {
        var failures = new List<CliTestFailure>
        {
            new("Test.Failure1", "Expected true", "at SomeMethod line 42")
        };
        var result = new CliRunResult(0, 1, 0, 100, failures);

        var verbose = result.FormatSummary(verbose: true);
        var brief = result.FormatSummary(verbose: false);

        Assert.Contains("Stack Trace", verbose);
        Assert.Contains("at SomeMethod line 42", verbose);
        Assert.DoesNotContain("Stack Trace", brief);
    }

    #endregion

    #region CliTestFailure Tests

    [Fact]
    public void CliTestFailure_FromException_CapturesData()
    {
        var exception = new InvalidOperationException("Test failed");
        var failure = CliTestFailure.FromException("MyTest", exception);

        Assert.Equal("MyTest", failure.TestName);
        Assert.Equal("Test failed", failure.FailureMessage);
    }

    [Fact]
    public void CliTestFailure_Format_Brief_ShowsOneLine()
    {
        var failure = new CliTestFailure("Namespace.Class.Method", "Assertion failed", "stack trace");

        var brief = failure.Format(verbose: false);

        Assert.Contains("Namespace.Class.Method", brief);
        Assert.Contains("Assertion failed", brief);
        Assert.DoesNotContain("stack trace", brief);
    }

    [Fact]
    public void CliTestFailure_Format_Verbose_IncludesStackTrace()
    {
        var failure = new CliTestFailure("Namespace.Class.Method", "Assertion failed", "at line 42");

        var verbose = failure.Format(verbose: true);

        Assert.Contains("Namespace.Class.Method", verbose);
        Assert.Contains("Assertion failed", verbose);
        Assert.Contains("at line 42", verbose);
    }

    #endregion

    #region PhCliRunner Tests

    [Fact]
    public void ComputeExitCode_NoFailures_ReturnsZero()
    {
        Assert.Equal(0, PhCliRunner.ComputeExitCode(10, 0));
        Assert.Equal(0, PhCliRunner.ComputeExitCode(0, 0));
    }

    [Fact]
    public void ComputeExitCode_WithFailures_ReturnsOne()
    {
        Assert.Equal(1, PhCliRunner.ComputeExitCode(10, 1));
        Assert.Equal(1, PhCliRunner.ComputeExitCode(0, 5));
    }

    [Fact]
    public void MatchesFilter_NullFilter_AlwaysMatches()
    {
        Assert.True(PhCliRunner.MatchesFilter("Any.Test.Name", null));
        Assert.True(PhCliRunner.MatchesFilter("Another.Test", ""));
    }

    [Fact]
    public void MatchesFilter_CaseInsensitive()
    {
        Assert.True(PhCliRunner.MatchesFilter("MyNamespace.InventoryTests.AddItem", "inventory"));
        Assert.True(PhCliRunner.MatchesFilter("MyNamespace.InventoryTests.AddItem", "INVENTORY"));
        Assert.True(PhCliRunner.MatchesFilter("MyNamespace.InventoryTests.AddItem", "InVeNtOrY"));
    }

    [Fact]
    public void MatchesFilter_PartialMatch()
    {
        Assert.True(PhCliRunner.MatchesFilter("Namespace.Class.TestMethod", "Class"));
        Assert.True(PhCliRunner.MatchesFilter("Namespace.Class.TestMethod", "Method"));
        Assert.True(PhCliRunner.MatchesFilter("Namespace.Class.TestMethod", "space"));
    }

    [Fact]
    public void MatchesFilter_NoMatch()
    {
        Assert.False(PhCliRunner.MatchesFilter("Namespace.Class.TestMethod", "NotFound"));
        Assert.False(PhCliRunner.MatchesFilter("Namespace.Class.TestMethod", "xyz"));
    }

    [Fact]
    public void IsFactOrTheoryAttribute_RecognizesFactAttribute()
    {
        Assert.True(PhCliRunner.IsFactOrTheoryAttribute(typeof(FactAttribute)));
    }

    [Fact]
    public void IsFactOrTheoryAttribute_RecognizesTheoryAttribute()
    {
        Assert.True(PhCliRunner.IsFactOrTheoryAttribute(typeof(TheoryAttribute)));
    }

    [Fact]
    public void IsFactOrTheoryAttribute_RejectsOtherAttributes()
    {
        Assert.False(PhCliRunner.IsFactOrTheoryAttribute(typeof(ObsoleteAttribute)));
        Assert.False(PhCliRunner.IsFactOrTheoryAttribute(typeof(SerializableAttribute)));
    }

    [Fact]
    public void DiscoverTests_FindsFactMethods()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var tests = PhCliRunner.DiscoverTests(assembly, null);

        Assert.NotEmpty(tests);
        Assert.Contains(tests, m => m.Name == nameof(DiscoverTests_FindsFactMethods));
    }

    [Fact]
    public void DiscoverTests_AppliesFilter()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var filtered = PhCliRunner.DiscoverTests(assembly, "ComputeExitCode");

        Assert.NotEmpty(filtered);
        Assert.All(filtered, m => Assert.Contains("ComputeExitCode", m.Name));
    }

    [Fact]
    public void DiscoverTests_FilterExcludesNonMatching()
    {
        var assembly = Assembly.GetExecutingAssembly();

        var filtered = PhCliRunner.DiscoverTests(assembly, "NonExistentTestName12345");

        Assert.Empty(filtered);
    }

    [Fact]
    public void Run_HelpFlag_ReturnsZero()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = PhCliRunner.Run(["--help"], stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("Pharos CLI Runner", stdout.ToString());
    }

    [Fact]
    public void Run_InvalidArgs_ReturnsErrorCode()
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        var exitCode = PhCliRunner.Run(["--unknown-flag"], stdout, stderr);

        Assert.Equal(2, exitCode);
        Assert.Contains("Unknown argument", stderr.ToString());
    }

    [Fact]
    public void GetHelpText_ContainsAllOptions()
    {
        var help = CliRunOptions.GetHelpText();

        Assert.Contains("--assembly", help);
        Assert.Contains("--filter", help);
        Assert.Contains("--output-dir", help);
        Assert.Contains("--verbose", help);
        Assert.Contains("--max-parallel", help);
        Assert.Contains("--help", help);
        Assert.Contains("Exit codes", help);
    }

    #endregion
}
