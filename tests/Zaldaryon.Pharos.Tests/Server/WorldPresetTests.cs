using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class WorldPresetTests
{
    [Fact]
    public void ResolveRejectsConflictingWorldOptionsBeforeCacheLookup()
    {
        ServerWorldOptions options = new() { Preset = WorldPreset.SurvivalFlat, PlayStyle = "survival" };
        ArgumentException error = Assert.Throws<ArgumentException>(() => WorldPresetCache.Resolve(options));
        Assert.Contains(nameof(ServerWorldOptions.PlayStyle), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveRejectsIncompatibleSaveAndLocation()
    {
        Assert.Throws<ArgumentException>(() => WorldPresetCache.Resolve(new ServerWorldOptions
        {
            Preset = WorldPreset.SurvivalFlat,
            SaveFile = "existing.vcdbs",
        }));
        Assert.Throws<ArgumentException>(() => WorldPresetCache.Resolve(new ServerWorldOptions
        {
            Preset = WorldPreset.SurvivalFlat,
            SaveFileLocation = "world.db",
        }));
    }

    [Fact]
    public void ResolveRejectsUnknownPresetValue()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => WorldPresetCache.Resolve(new ServerWorldOptions { Preset = (WorldPreset)99 }));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"Generation\":\"\"}")]
    [InlineData("{\"Generation\":null}")]
    [InlineData("{\"Generation\":\"../escape\"}")]
    [InlineData("{\"Generation\":\"bad\\u0000path\"}")]
    public void CacheLookupRejectsMalformedCurrentPointer(string json)
    {
        string root = Path.Combine(Path.GetTempPath(), "pharos-preset-pointer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "current.json"), json);
            Assert.Null(WorldPresetCache.TryCurrentSaveAt(root, WorldPreset.SurvivalFlat, "1.22.7"));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void CorruptExistingGenerationUsesAFreshImmutableDirectory()
    {
        string root = Path.Combine(Path.GetTempPath(), "pharos-preset-generation-" + Guid.NewGuid().ToString("N"));
        string contentHash = new('a', 64);
        string canonical = "generation-" + contentHash[..16];
        string corrupt = Path.Combine(root, canonical);
        Directory.CreateDirectory(corrupt);
        File.WriteAllText(Path.Combine(corrupt, "world.vcdbs"), "corrupt prior generation");

        try
        {
            string selected = WorldPresetCache.ChooseGenerationId(root, WorldPreset.SurvivalFlat, "1.22.7", contentHash);

            Assert.NotEqual(canonical, selected);
            Assert.StartsWith(canonical + "-", selected, StringComparison.Ordinal);
            Assert.Equal("corrupt prior generation", File.ReadAllText(Path.Combine(corrupt, "world.vcdbs")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void MethodServerWorldOverridesClassWorldForPoolOptions()
    {
        var method = typeof(WorldAttributePrecedence).GetMethod(nameof(WorldAttributePrecedence.Method))!;
        ScenarioTestInfo.Current = new ScenarioTestInfo("method precedence", typeof(WorldAttributePrecedence), method.Name, method);
        try
        {
            ServerWorldOptions options = ScenarioAttributes.WorldOptions(typeof(WorldAttributePrecedence));
            Assert.Equal("22", options.Seed);
            Assert.Equal("standard", options.WorldType);
        }
        finally { ScenarioTestInfo.Current = null; }
    }

    [ServerWorld(seed: 11, worldType: "superflat")]
    private sealed class WorldAttributePrecedence
    {
        [ServerWorld(seed: 22, worldType: "standard")]
        public void Method() { }
    }
}

[Collection("Sequential")]
[ServerWorld(Preset = WorldPreset.SurvivalFlat)]
public sealed class WorldPresetLiveScenarioTests : ServerScenarioBase
{
    [ServerScenario]
    public void SurvivalFlatLoadsFromTheVersionedPresetCache()
    {
        Assert.NotNull(Host);
        Assert.Equal("surviveandbuild", Host!.Options.PlayStyle);
        Assert.Equal("superflat", Host.Options.WorldType);
        Assert.Equal("pharos-survival-flat-v1", Host.Options.Seed);
    }
}
