using Xunit;
using Zaldaryon.Pharos.Cli;

namespace Zaldaryon.Pharos.Tests.Cli;

/// <summary><c>pharos smoke</c>'s arguments, help and the checks it makes before booting anything.</summary>
public class SmokeCommandTests
{
    [Fact]
    public void Parse_ReadsEveryOption()
    {
        SmokeOptions options = SmokeCommand.Parse(
        [
            "--mod", "a.zip", "--mod", "b", "--frames", "120", "--command", "/time set day", "--strict",
            "--allow", "texture .* not found", "--allow-error", "known", "--timeout", "90", "--artifacts", "out",
            "--game", "/vs", "--seed", "7", "--world-type", "standard", "--play-style", "surviveandbuild", "-v",
        ])!;

        Assert.Equal(["a.zip", "b"], options.Mods);
        Assert.Equal(120, options.Frames);
        Assert.Equal(["/time set day"], options.Commands);
        Assert.True(options.Strict);
        Assert.Equal(["texture .* not found"], options.Allow);
        Assert.Equal(["known"], options.AllowErrors);
        Assert.Equal(90, options.TimeoutSeconds);
        Assert.Equal("out", options.ArtifactsDirectory);
        Assert.Equal("/vs", options.GamePath);
        Assert.Equal("7", options.Seed);
        Assert.Equal("standard", options.WorldType);
        Assert.Equal("surviveandbuild", options.PlayStyle);
        Assert.True(options.Verbose);
    }

    [Fact]
    public void Parse_HasSensibleDefaults()
    {
        SmokeOptions options = SmokeCommand.Parse(["--mod", "a.zip"])!;

        Assert.Equal(600, options.Frames);
        Assert.False(options.Strict);
        Assert.Null(options.TimeoutSeconds);
        Assert.Empty(options.Commands);
    }

    [Theory]
    [InlineData(new[] { "--frames", "10" }, "at least one mod")]
    [InlineData(new[] { "--mod" }, "needs a value")]
    [InlineData(new[] { "--mod", "a", "--frames", "0" }, "positive number")]
    [InlineData(new[] { "--mod", "a", "--frames", "many" }, "positive number")]
    [InlineData(new[] { "--mod", "a", "--bogus" }, "Unknown option")]
    public void Parse_RefusesBadArguments(string[] args, string message)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => SmokeCommand.Parse(args));

        Assert.Contains(message, error.Message);
    }

    [Fact]
    public void Help_IsNotAnError()
    {
        StringWriter stdout = new(), stderr = new();

        Assert.Null(SmokeCommand.Parse(["--help"]));
        Assert.Equal(SmokeCommand.ExitPassed, PhCliRunner.Run(["help", "smoke"], stdout, stderr));
        Assert.Contains("pharos smoke --mod", stdout.ToString());
        Assert.Contains("smoke", PhCliRunner.GetMainHelpText());
    }

    [Fact]
    public void BadArguments_ExitWithTwo_AndShowTheHelp()
    {
        StringWriter stdout = new(), stderr = new();

        int code = PhCliRunner.Run(["smoke", "--frames", "10"], stdout, stderr);

        Assert.Equal(SmokeCommand.ExitError, code);
        Assert.Contains("at least one mod", stderr.ToString());
        Assert.Contains("Usage: pharos smoke", stderr.ToString());
    }

    [Fact]
    public void Preflight_RefusesAMissingGameOrMod()
    {
        string game = Path.Combine(Path.GetTempPath(), "pharos-not-a-game-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(game);

        try
        {
            Assert.Contains("not a Vintage Story install", SmokeCommand.Preflight(new SmokeOptions { Mods = ["x"], GamePath = game }));

            string? vs = Environment.GetEnvironmentVariable("VINTAGE_STORY");
            if (vs != null && File.Exists(Path.Combine(vs, "VintagestoryAPI.dll")))
            {
                Assert.Contains("does not exist", SmokeCommand.Preflight(new SmokeOptions { Mods = ["/no/such/mod.zip"], GamePath = vs }));
                Assert.Contains("not a valid regular expression", SmokeCommand.Preflight(new SmokeOptions { Mods = [game], GamePath = vs, Allow = ["(unclosed"] }));
            }
        }
        finally
        {
            Directory.Delete(game);
        }
    }
}
