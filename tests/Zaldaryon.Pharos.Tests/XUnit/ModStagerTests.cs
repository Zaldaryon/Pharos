using System;
using System.IO;
using System.Linq;
using Xunit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>
/// Tests for PharosModsAttribute, ModStager, and ModStagingResult.
/// </summary>
public class ModStagerTests : IDisposable
{
    private readonly string _testTempDir;

    public ModStagerTests()
    {
        _testTempDir = Path.Combine(Path.GetTempPath(), $"pharos-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testTempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testTempDir))
        {
            Directory.Delete(_testTempDir, recursive: true);
        }
    }

    [Fact]
    public void PharosModsAttribute_StoresModPaths()
    {
        var attr = new PharosModsAttribute("path/to/mod1", "path/to/mod2");

        Assert.Equal(2, attr.ModPaths.Count);
        Assert.Equal("path/to/mod1", attr.ModPaths[0]);
        Assert.Equal("path/to/mod2", attr.ModPaths[1]);
    }

    [Fact]
    public void ModStager_CreatesAndCleansStagingDirectory()
    {
        string stagingPath;

        using (var stager = new ModStager(_testTempDir))
        {
            stagingPath = stager.StagingDirectory;
            Assert.True(Directory.Exists(stagingPath), "Staging directory should exist after creation");
        }

        Assert.False(Directory.Exists(stagingPath), "Staging directory should be deleted after Dispose");
    }

    [Fact]
    public void ModStager_StageFile_CopiesFileToStaging()
    {
        var sourceFile = Path.Combine(_testTempDir, "testmod.dll");
        File.WriteAllText(sourceFile, "test content");

        using var stager = new ModStager(_testTempDir);
        stager.StageFile(sourceFile);

        var expectedPath = Path.Combine(stager.StagingDirectory, "testmod.dll");
        Assert.True(File.Exists(expectedPath), "File should be copied to staging directory");
        Assert.Single(stager.StagedPaths);
        Assert.Equal(expectedPath, stager.StagedPaths[0]);
    }

    [Fact]
    public void ModStager_StageFile_ThrowsOnMissingFile()
    {
        using var stager = new ModStager(_testTempDir);
        var nonExistentPath = Path.Combine(_testTempDir, "does-not-exist.dll");

        var ex = Assert.Throws<FileNotFoundException>(() => stager.StageFile(nonExistentPath));
        Assert.Contains("does-not-exist.dll", ex.Message);
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void ModStager_StageDirectory_CopiesContents()
    {
        var sourceDir = Path.Combine(_testTempDir, "testmod");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "modinfo.json"), "{}");

        using var stager = new ModStager(_testTempDir);
        stager.StageDirectory(sourceDir);

        var expectedDir = Path.Combine(stager.StagingDirectory, "testmod");
        var expectedFile = Path.Combine(expectedDir, "modinfo.json");
        Assert.True(Directory.Exists(expectedDir), "Directory should be copied to staging");
        Assert.True(File.Exists(expectedFile), "File inside directory should be copied");
        Assert.Single(stager.StagedPaths);
    }

    [Fact]
    public void ModStagingResult_Failure_HasErrorMessage()
    {
        var result = ModStagingResult.Failure("Something went wrong");

        Assert.False(result.Success);
        Assert.Equal("Something went wrong", result.ErrorMessage);
        Assert.Empty(result.StagedFiles);
    }

    [Fact]
    public void ModStagingResult_Of_HasSuccess()
    {
        var files = new[] { "file1.dll", "file2.dll" };
        var result = ModStagingResult.Of(files);

        Assert.True(result.Success);
        Assert.Null(result.ErrorMessage);
        Assert.Equal(2, result.StagedFiles.Count);
        Assert.Equal("file1.dll", result.StagedFiles[0]);
        Assert.Equal("file2.dll", result.StagedFiles[1]);
    }

    [Fact]
    public void ModStager_ResolvePath_Absolute_ReturnsSame()
    {
        var absolutePath = OperatingSystem.IsWindows() ? @"C:\Mods\MyMod" : "/tmp/Mods/MyMod";
        var baseDir = OperatingSystem.IsWindows() ? @"C:\Some\Other\Dir" : "/var/Some/Other/Dir";
        var result = ModStager.ResolvePath(absolutePath, baseDir);

        Assert.Equal(absolutePath, result);
    }

    [Fact]
    public void ModStager_ResolvePath_Relative_ResolvedAgainstBase()
    {
        var relativePath = "mods/mymod";
        var baseDir = OperatingSystem.IsWindows() ? @"C:\TestProject\bin" : "/TestProject/bin";

        var result = ModStager.ResolvePath(relativePath, baseDir);

        var expected = Path.GetFullPath(Path.Combine(baseDir, relativePath));
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ModStager_CollectFromType_ReturnsEmptyForTypeWithNoAttribute()
    {
        var attributes = ModStager.CollectFromType(typeof(TypeWithNoModsAttribute));

        Assert.Empty(attributes);
    }

    [Fact]
    public void ModStager_DoubleDispose_IsSafe()
    {
        var stager = new ModStager(_testTempDir);
        var stagingPath = stager.StagingDirectory;
        Assert.True(Directory.Exists(stagingPath));

        stager.Dispose();
        Assert.False(Directory.Exists(stagingPath));

        var ex = Record.Exception(() => stager.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void ModStager_StageZip_CopiesZipToStaging()
    {
        var zipPath = Path.Combine(_testTempDir, "mymod.zip");
        File.WriteAllBytes(zipPath, new byte[] { 0x50, 0x4B, 0x03, 0x04 }); // minimal zip header

        using var stager = new ModStager(_testTempDir);
        stager.StageZip(zipPath);

        var expectedPath = Path.Combine(stager.StagingDirectory, "mymod.zip");
        Assert.True(File.Exists(expectedPath), "Zip file should be copied to staging");
        Assert.Single(stager.StagedPaths);
        Assert.Equal(expectedPath, stager.StagedPaths[0]);
    }

    [Fact]
    public void PharosModsAttribute_EmptyPaths_ReturnsEmptyList()
    {
        var attr = new PharosModsAttribute();

        Assert.Empty(attr.ModPaths);
    }

    // Helper class with no PharosModsAttribute
    private class TypeWithNoModsAttribute { }
}
