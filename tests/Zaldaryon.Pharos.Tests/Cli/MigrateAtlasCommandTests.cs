using Xunit;
using Zaldaryon.Pharos.Cli;

namespace Zaldaryon.Pharos.Tests.Cli;

/// <summary>
/// Tests for the migrate-atlas CLI command.
/// </summary>
public sealed class MigrateAtlasCommandTests : IDisposable
{
    private readonly string _tempDir;

    public MigrateAtlasCommandTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pharos-migrate-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void ParseArgs_ReturnsNull_WhenNoDirectory()
    {
        var result = MigrateAtlasCommand.ParseArgs(new[] { "migrate-atlas" });
        Assert.Null(result);
    }

    [Fact]
    public void ParseArgs_ParsesDirectory()
    {
        var result = MigrateAtlasCommand.ParseArgs(new[] { "migrate-atlas", "/some/path" });
        Assert.NotNull(result);
        Assert.Equal("/some/path", result.Directory);
        Assert.False(result.DryRun);
        Assert.False(result.Verbose);
        Assert.True(result.Backup);
    }

    [Fact]
    public void ParseArgs_ParsesDryRun()
    {
        var result = MigrateAtlasCommand.ParseArgs(new[] { "migrate-atlas", "/some/path", "--dry-run" });
        Assert.NotNull(result);
        Assert.True(result.DryRun);
    }

    [Fact]
    public void ParseArgs_ParsesVerbose()
    {
        var result = MigrateAtlasCommand.ParseArgs(new[] { "migrate-atlas", "/some/path", "-v" });
        Assert.NotNull(result);
        Assert.True(result.Verbose);
    }

    [Fact]
    public void ParseArgs_ParsesNoBackup()
    {
        var result = MigrateAtlasCommand.ParseArgs(new[] { "migrate-atlas", "/some/path", "--no-backup" });
        Assert.NotNull(result);
        Assert.False(result.Backup);
    }

    [Fact]
    public void ParseArgs_ThrowsOnUnknownOption()
    {
        Assert.Throws<ArgumentException>(() =>
            MigrateAtlasCommand.ParseArgs(new[] { "migrate-atlas", "/some/path", "--unknown" }));
    }

    [Fact]
    public void ScanCsprojFile_DetectsAtlasPackageReference()
    {
        var csprojPath = Path.Combine(_tempDir, "Test.csproj");
        File.WriteAllText(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pixnop.Atlas" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var result = MigrateAtlasCommand.ScanCsprojFile(csprojPath);

        Assert.NotNull(result);
        Assert.Single(result.LineChanges);
        Assert.Contains("Pixnop.Atlas", result.LineChanges[0].Before);
        Assert.Contains("Zaldaryon.Pharos.XUnit", result.LineChanges[0].After);
    }

    [Fact]
    public void ScanCsprojFile_DetectsAtlasXUnitPackageReference()
    {
        var csprojPath = Path.Combine(_tempDir, "Test.csproj");
        File.WriteAllText(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pixnop.Atlas.XUnit" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var result = MigrateAtlasCommand.ScanCsprojFile(csprojPath);

        Assert.NotNull(result);
        Assert.Single(result.LineChanges);
        Assert.Contains("Pixnop.Atlas.XUnit", result.LineChanges[0].Before);
        Assert.Contains("Zaldaryon.Pharos.XUnit", result.LineChanges[0].After);
    }

    [Fact]
    public void ScanCsprojFile_ReturnsNull_WhenNoAtlasReferences()
    {
        var csprojPath = Path.Combine(_tempDir, "Test.csproj");
        File.WriteAllText(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.3" />
              </ItemGroup>
            </Project>
            """);

        var result = MigrateAtlasCommand.ScanCsprojFile(csprojPath);

        Assert.Null(result);
    }

    [Fact]
    public void ScanCsFile_DetectsAtlasUsingStatements()
    {
        var csPath = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(csPath, """
            using Atlas.Api;
            using Atlas.XUnit;
            using Xunit;

            public class TestClass { }
            """);

        var result = MigrateAtlasCommand.ScanCsFile(csPath);

        Assert.NotNull(result);
        Assert.Equal(2, result.LineChanges.Count);
        Assert.Contains(result.LineChanges, c => c.Before.Contains("Atlas.Api") && c.After.Contains("Zaldaryon.Pharos.Server"));
        Assert.Contains(result.LineChanges, c => c.Before.Contains("Atlas.XUnit") && c.After.Contains("Zaldaryon.Pharos.XUnit"));
    }

    [Fact]
    public void ScanCsFile_DetectsAtlasTypeReferences()
    {
        var csPath = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(csPath, """
            public class MyTests : AtlasScenarioBase
            {
                [AtlasScenario]
                public void Test() { }
            }
            """);

        var result = MigrateAtlasCommand.ScanCsFile(csPath);

        Assert.NotNull(result);
        Assert.Contains(result.LineChanges, c => c.Before.Contains("AtlasScenarioBase") && c.After.Contains("ServerScenarioBase"));
        Assert.Contains(result.LineChanges, c => c.Before.Contains("[AtlasScenario]") && c.After.Contains("[ServerScenario]"));
    }

    [Fact]
    public void ScanCsFile_DetectsWorldSessionType()
    {
        var csPath = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(csPath, """
            private IWorldSession _session;
            private WorldOptions _options;
            """);

        var result = MigrateAtlasCommand.ScanCsFile(csPath);

        Assert.NotNull(result);
        Assert.Contains(result.LineChanges, c => c.Before.Contains("IWorldSession") && c.After.Contains("EmbeddedServerHost"));
        Assert.Contains(result.LineChanges, c => c.Before.Contains("WorldOptions") && c.After.Contains("ServerWorldOptions"));
    }

    [Fact]
    public void ScanCsFile_DetectsAllAttributes()
    {
        var csPath = Path.Combine(_tempDir, "Test.cs");
        File.WriteAllText(csPath, """
            [AtlasWorld(seed: 123)]
            [AtlasMods("mod1", "mod2")]
            public class MyTests
            {
                [AtlasScenario]
                public void Test1() { }

                [AtlasTheory]
                [InlineData(1)]
                public void Test2(int x) { }
            }
            """);

        var result = MigrateAtlasCommand.ScanCsFile(csPath);

        Assert.NotNull(result);
        Assert.Contains(result.LineChanges, c => c.After.Contains("[ServerWorld("));
        Assert.Contains(result.LineChanges, c => c.After.Contains("[ServerMods("));
        Assert.Contains(result.LineChanges, c => c.After.Contains("[ServerScenario]"));
        Assert.Contains(result.LineChanges, c => c.After.Contains("[ServerTheory]"));
    }

    [Fact]
    public void ScanDirectory_FindsAllFiles()
    {
        // Create project structure
        File.WriteAllText(Path.Combine(_tempDir, "Test.csproj"), """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pixnop.Atlas" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        File.WriteAllText(Path.Combine(_tempDir, "Tests.cs"), """
            using Atlas.Api;
            public class Tests { }
            """);

        var result = MigrateAtlasCommand.ScanDirectory(_tempDir, verbose: false);

        Assert.Equal(1, result.CsprojCount);
        Assert.Equal(1, result.CsFileCount);
        Assert.Equal(2, result.Changes.Count);
    }

    [Fact]
    public void Run_DryRunDoesNotModifyFiles()
    {
        var csprojPath = Path.Combine(_tempDir, "Test.csproj");
        var originalContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pixnop.Atlas" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(csprojPath, originalContent);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new MigrateAtlasCommand.MigrateOptions(_tempDir, DryRun: true, Verbose: false, Backup: true);

        var exitCode = MigrateAtlasCommand.Run(options, stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Equal(originalContent, File.ReadAllText(csprojPath));
        Assert.Contains("dry-run", stdout.ToString());
    }

    [Fact]
    public void Run_AppliesChangesWhenNotDryRun()
    {
        var csprojPath = Path.Combine(_tempDir, "Test.csproj");
        File.WriteAllText(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pixnop.Atlas" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new MigrateAtlasCommand.MigrateOptions(_tempDir, DryRun: false, Verbose: false, Backup: false);

        var exitCode = MigrateAtlasCommand.Run(options, stdout, stderr);

        Assert.Equal(0, exitCode);
        var newContent = File.ReadAllText(csprojPath);
        Assert.Contains("Zaldaryon.Pharos.XUnit", newContent);
        Assert.DoesNotContain("Pixnop.Atlas", newContent);
    }

    [Fact]
    public void Run_CreatesBackupFiles()
    {
        var csprojPath = Path.Combine(_tempDir, "Test.csproj");
        var originalContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="Pixnop.Atlas" Version="1.0.0" />
              </ItemGroup>
            </Project>
            """;
        File.WriteAllText(csprojPath, originalContent);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new MigrateAtlasCommand.MigrateOptions(_tempDir, DryRun: false, Verbose: false, Backup: true);

        MigrateAtlasCommand.Run(options, stdout, stderr);

        Assert.True(File.Exists(csprojPath + ".bak"));
        Assert.Equal(originalContent, File.ReadAllText(csprojPath + ".bak"));
    }

    [Fact]
    public void Run_ReturnsError_WhenDirectoryNotFound()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new MigrateAtlasCommand.MigrateOptions("/nonexistent/path", DryRun: true, Verbose: false, Backup: true);

        var exitCode = MigrateAtlasCommand.Run(options, stdout, stderr);

        Assert.Equal(2, exitCode);
        Assert.Contains("not found", stderr.ToString());
    }

    [Fact]
    public void Run_ReturnsSuccess_WhenNoChangesNeeded()
    {
        var csprojPath = Path.Combine(_tempDir, "Test.csproj");
        File.WriteAllText(csprojPath, """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageReference Include="xunit" Version="2.9.3" />
              </ItemGroup>
            </Project>
            """);

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new MigrateAtlasCommand.MigrateOptions(_tempDir, DryRun: true, Verbose: false, Backup: true);

        var exitCode = MigrateAtlasCommand.Run(options, stdout, stderr);

        Assert.Equal(0, exitCode);
        Assert.Contains("No changes needed", stdout.ToString());
    }

    [Fact]
    public void PhCliRunner_DispatchesToMigrateAtlas()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        // Just test that it dispatches correctly (will fail because dir doesn't exist)
        var exitCode = PhCliRunner.Run(new[] { "migrate-atlas", "/nonexistent" }, stdout, stderr);

        Assert.Equal(2, exitCode);
        Assert.Contains("not found", stderr.ToString());
    }
}
