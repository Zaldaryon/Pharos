using System.Text.Json;
using Xunit;
using Zaldaryon.Pharos.Cli;

namespace Zaldaryon.Pharos.Tests.Cli;

/// <summary>The <c>pharos doctor</c> and <c>pharos init</c> commands.</summary>
public sealed class DoctorCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-doctor-" + Guid.NewGuid().ToString("N"));

    public DoctorCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed test leaves its files behind for reading.
        }
    }

    [Fact]
    public void Inspect_WithAnInstall_FindsItAndTheNatives()
    {
        string game = FakeInstall();

        DoctorReport report = DoctorCommand.Inspect(new DoctorOptions(game, _root));

        Assert.Equal(Path.GetFullPath(game), report.GamePath);
        Assert.Null(report.GameVersion);
        DoctorCheck install = report.Checks.Single(check => check.Name == "Vintage Story");
        Assert.Equal(DoctorStatus.Ok, install.Status);
        Assert.Contains("unknown version", install.Message, StringComparison.Ordinal);
        Assert.Equal(DoctorStatus.Ok, report.Checks.Single(check => check.Name == "Natives").Status);
        Assert.Equal(DoctorStatus.Ok, report.Checks.Single(check => check.Name == "Ports").Status);
        Assert.True(report.AllGood);
        Assert.Equal(0, report.ExitCode);
    }

    [Fact]
    public void Inspect_WithoutAnInstall_Fails()
    {
        string empty = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;

        DoctorReport report = DoctorCommand.Inspect(new DoctorOptions(empty, _root));

        Assert.False(report.AllGood);
        Assert.Equal(1, report.ExitCode);
        Assert.Equal(DoctorStatus.Fail, report.Checks.Single(check => check.Name == "Vintage Story").Status);
        Assert.Contains("no install found", report.Checks.Single(check => check.Name == "Vintage Story").Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Run_Json_WritesTheChecks()
    {
        string game = FakeInstall();
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exit = DoctorCommand.Run(["--game", game, "--project", _root, "--json"], stdout, stderr);

        Assert.Equal(0, exit);
        using JsonDocument document = JsonDocument.Parse(stdout.ToString());
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.GetProperty("allGood").GetBoolean());
        Assert.Equal("ok", root.GetProperty("checks")[0].GetProperty("status").GetString());
        Assert.Equal("Vintage Story", root.GetProperty("checks")[0].GetProperty("name").GetString());
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Run_Help_PrintsTheUsage()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exit = DoctorCommand.Run(["--help"], stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Contains("pharos doctor", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Run_UnknownOption_ReturnsAnError()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exit = DoctorCommand.Run(["--nope"], stdout, stderr);

        Assert.Equal(2, exit);
        Assert.Contains("Unknown option", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PhCliRunner_DispatchesToDoctorAndInit()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        Assert.Equal(0, PhCliRunner.Run(["doctor", "--help"], stdout, stderr));
        Assert.Contains("check that this machine", stdout.ToString(), StringComparison.Ordinal);

        stdout.GetStringBuilder().Clear();
        Assert.Equal(0, PhCliRunner.Run(["init", "--help"], stdout, stderr));
        Assert.Contains("write a test project", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Init_WritesAProjectThatReferencesTheMod()
    {
        string mod = Path.Combine(_root, "MyMod", "MyMod.csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(mod)!);
        File.WriteAllText(mod, "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        string target = Path.Combine(_root, "MyMod.Tests");
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exit = InitCommand.Run(["--dir", target, "--mod-project", mod, "--name", "MyMod.Tests"], stdout, stderr);

        Assert.Equal(0, exit);
        string csproj = Path.Combine(target, "MyMod.Tests.csproj");
        Assert.True(File.Exists(csproj));
        string project = File.ReadAllText(csproj);
        Assert.Contains("Zaldaryon.Pharos.XUnit", project, StringComparison.Ordinal);
        Assert.Contains("<ProjectReference Include=\"../MyMod/MyMod.csproj\" />", project, StringComparison.Ordinal);
        Assert.Contains("namespace MyMod.Tests;", File.ReadAllText(Path.Combine(target, "SmokeTests.cs")), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(target, "xunit.runner.json")));
        Assert.Contains("Created", stdout.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Init_RefusesANonEmptyFolderWithoutForce()
    {
        string target = Directory.CreateDirectory(Path.Combine(_root, "taken")).FullName;
        File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exit = InitCommand.Run(["--dir", target], stdout, stderr);

        Assert.Equal(2, exit);
        Assert.Contains("not empty", stderr.ToString(), StringComparison.Ordinal);
        Assert.Equal("keep", File.ReadAllText(Path.Combine(target, "keep.txt")));

        stdout.GetStringBuilder().Clear();
        stderr.GetStringBuilder().Clear();
        Assert.Equal(0, InitCommand.Run(["--dir", target, "--force"], stdout, stderr));
        Assert.True(File.Exists(Path.Combine(target, "taken.csproj")));
    }

    private string FakeInstall()
    {
        string game = Directory.CreateDirectory(Path.Combine(_root, "game")).FullName;
        File.WriteAllText(Path.Combine(game, "VintagestoryAPI.dll"), "");
        File.WriteAllText(Path.Combine(game, "VintagestoryLib.dll"), "");
        string lib = Directory.CreateDirectory(Path.Combine(game, "Lib")).FullName;
        foreach (string name in new[]
        {
            "libopenal.so", "libcairo.so", "libglfw.so", "libSkiaSharp.so", "0Harmony.dll", "protobuf-net.dll",
        })
        {
            File.WriteAllText(Path.Combine(lib, name), "");
        }
        return game;
    }
}
