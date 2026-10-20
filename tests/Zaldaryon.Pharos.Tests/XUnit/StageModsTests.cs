using Xunit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

public class StageModsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pharos-stagemods-" + Guid.NewGuid().ToString("N")[..8]);

    public StageModsTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string name, string contents)
    {
        string path = Path.Combine(_folder, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    [Fact]
    public void StageMods_CopiesAFileUnderItsOwnName()
    {
        string mod = Write("mymod.zip", "candidate");
        string staged = Path.Combine(_folder, "mods");

        ScenarioAttributes.StageMods([mod], staged);

        Assert.Equal("candidate", File.ReadAllText(Path.Combine(staged, "mymod.zip")));
    }

    [Fact]
    public void StageMods_AContainingOverride_ReplacesTheDeclaredBuildByName()
    {
        string declared = Write("bin/mymod.zip", "candidate");
        string overrideRoot = Path.Combine(_folder, "baseline");
        Directory.CreateDirectory(overrideRoot);
        File.WriteAllText(Path.Combine(overrideRoot, "mymod.zip"), "baseline");
        string staged = Path.Combine(_folder, "mods");

        ScenarioAttributes.StageMods([declared], staged, overrideRoot);

        Assert.Equal("baseline", File.ReadAllText(Path.Combine(staged, "mymod.zip")));
    }

    [Fact]
    public void StageMods_AnOverrideWithoutThatName_LeavesTheDeclaredBuildAlone()
    {
        string declared = Write("bin/mymod.zip", "candidate");
        string overrideRoot = Path.Combine(_folder, "baseline");
        Directory.CreateDirectory(overrideRoot);
        File.WriteAllText(Path.Combine(overrideRoot, "othermod.zip"), "baseline");
        string staged = Path.Combine(_folder, "mods");

        ScenarioAttributes.StageMods([declared], staged, overrideRoot);

        Assert.Equal("candidate", File.ReadAllText(Path.Combine(staged, "mymod.zip")));
    }

    [Fact]
    public void StageMods_AnOverrideFolder_IsCopiedInFull()
    {
        string declared = Path.Combine(_folder, "bin", "mymod");
        Directory.CreateDirectory(declared);
        File.WriteAllText(Path.Combine(declared, "modinfo.json"), "candidate");
        string overrideRoot = Path.Combine(_folder, "baseline");
        Directory.CreateDirectory(Path.Combine(overrideRoot, "mymod"));
        File.WriteAllText(Path.Combine(overrideRoot, "mymod", "modinfo.json"), "baseline");
        File.WriteAllText(Path.Combine(overrideRoot, "mymod", "extra.txt"), "extra");
        string staged = Path.Combine(_folder, "mods");

        ScenarioAttributes.StageMods([declared], staged, overrideRoot);

        Assert.Equal("baseline", File.ReadAllText(Path.Combine(staged, "mymod", "modinfo.json")));
        Assert.Equal("extra", File.ReadAllText(Path.Combine(staged, "mymod", "extra.txt")));
    }

    [Fact]
    public void StageMods_AMissingPath_IsStillAnError()
    {
        string missing = Path.Combine(_folder, "nope.zip");

        FileNotFoundException error = Assert.Throws<FileNotFoundException>(() => ScenarioAttributes.StageMods([missing], Path.Combine(_folder, "mods")));
        Assert.Contains("nope.zip", error.Message);
    }
}
