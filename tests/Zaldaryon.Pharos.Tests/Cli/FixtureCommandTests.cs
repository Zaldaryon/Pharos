using Xunit;
using Zaldaryon.Pharos.Cli.ParallelRuns;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Tests.Cli;

public class FixtureCommandTests
{
    private static readonly string[] Listed =
    [
        "My.Tests.VillageBuilder.Build",
        "My.Tests.VillageBuilder.BuildBig",
        "My.Tests.Outer+FarmBuilder.Build",
        "My.Tests.Rows.Build",
        "My.Tests.Rows.Build",
    ];

    [Theory]
    [InlineData("VillageBuilder.Build", "My.Tests.VillageBuilder.Build")]
    [InlineData("My.Tests.VillageBuilder.BuildBig", "My.Tests.VillageBuilder.BuildBig")]
    [InlineData("Outer.FarmBuilder.Build", "My.Tests.Outer+FarmBuilder.Build")]
    [InlineData("Outer+FarmBuilder.Build", "My.Tests.Outer+FarmBuilder.Build")]
    public void Pick_FindsTheOneTestANameEndsWith(string scenario, string expected) =>
        Assert.Equal(expected, FixtureCommand.Pick(scenario, Listed));

    [Theory]
    [InlineData("Build", "names 3 tests")]
    [InlineData("Builder.Build", "No test is named")]
    [InlineData("Rows.Build", "theory with 2 rows")]
    [InlineData("Missing", "No test is named")]
    public void Pick_RefusesNoneSeveralAndTheoryRows(string scenario, string message)
    {
        Exception error = Assert.ThrowsAny<Exception>(() => FixtureCommand.Pick(scenario, Listed));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Parse_TakesTheScenarioAndTheOutput_AndPassesTheRestToTheRun()
    {
        FixtureOptions options = FixtureOptions.Parse(["tests/My.Tests", "--scenario", "A.B", "--out", "w.vcdbs", "--no-build", "-c", "Debug"])!;

        Assert.Equal("A.B", options.Scenario);
        Assert.Equal("w.vcdbs", options.Out);
        Assert.Equal("tests/My.Tests", options.Run.Target);
        Assert.True(options.Run.NoBuild);
        Assert.Equal("Debug", options.Run.Configuration);
        Assert.Equal("pharos-fixture", options.Run.ResultsDirectory);
    }

    [Theory]
    [InlineData(new[] { "tests/My.Tests", "--out", "w.vcdbs" }, "--scenario")]
    [InlineData(new[] { "tests/My.Tests", "--scenario", "A.B" }, "--out")]
    [InlineData(new[] { "tests/My.Tests", "--scenario", "A.B", "--out", "w", "--parallel", "2" }, "single test")]
    [InlineData(new[] { "--scenario", "A.B", "--out", "w" }, "Name the test assembly")]
    public void Parse_RefusesWhatDoesNotMakeAFixture(string[] args, string message)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => FixtureOptions.Parse(args));
        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Parse_Help_ReturnsNull() => Assert.Null(FixtureOptions.Parse(["--help"]));

    [Fact]
    public async Task AScenarioThatNamesNoTest_ExitsWithAnError_AndWritesNothing()
    {
        string results = Path.Combine(Path.GetTempPath(), "pharos-fixture-" + Guid.NewGuid().ToString("N")[..8]);
        string output = Path.Combine(results, "never.vcdbs");
        StringWriter stderr = new();
        try
        {
            FixtureOptions options = new("NoSuchBuilder.Build", output, new RunOptions { Target = typeof(FixtureCommandTests).Assembly.Location, NoBuild = true, ResultsDirectory = results });
            int exit = await FixtureCommand.RunAsync(options, new StringWriter(), stderr, CancellationToken.None);

            Assert.Equal(RunCommand.ExitError, exit);
            Assert.Contains("No test is named NoSuchBuilder.Build", stderr.ToString());
            Assert.False(File.Exists(output));
        }
        finally
        {
            if (Directory.Exists(results)) Directory.Delete(results, recursive: true);
        }
    }

    [Fact]
    public void Export_MatchesOnlyTheFullyQualifiedTest()
    {
        ScenarioTestInfo test = new("Build", typeof(FixtureCommandTests), "Build");
        Assert.True(FixtureExport.Matches(test, typeof(FixtureCommandTests).FullName + ".Build"));
        Assert.False(FixtureExport.Matches(test, "FixtureCommandTests.Build"));
        Assert.False(FixtureExport.Matches(test, typeof(FixtureCommandTests).FullName + ".BuildBig"));
    }
}
