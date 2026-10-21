using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Scenarios;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The configuration a scenario row selects, before anything boots.</summary>
public sealed class ScenarioConfigurationTests
{
    [Fact]
    public void WorldValues_MergeIntoTheRecipe_WithoutDroppingWhatItHas()
    {
        ScenarioConfiguration configuration = new(
            [new KeyValuePair<string, JToken>("creatureHostility", new JValue("off"))],
            []);

        ServerWorldOptions options = configuration.ApplyWorldValues(new ServerWorldOptions
        {
            WorldConfigurationJson = "{\"worldWidth\":\"512\",\"creatureHostility\":\"aggressive\"}",
        });

        JObject json = JObject.Parse(options.WorldConfigurationJson);
        Assert.Equal("512", json["worldWidth"]?.Value<string>());
        Assert.Equal("off", json["creatureHostility"]?.Value<string>());
    }

    [Fact]
    public void MatrixValues_KeepTheirJsonType_AndPlainTextStaysText()
    {
        Assert.Equal(JTokenType.String, ScenarioConfiguration.ParseMatrixScalar("off", "value").Type);
        Assert.Equal(JTokenType.Boolean, ScenarioConfiguration.ParseMatrixScalar("false", "value").Type);
        Assert.Equal(JTokenType.Integer, ScenarioConfiguration.ParseMatrixScalar("12", "value").Type);
        Assert.Equal(JTokenType.Float, ScenarioConfiguration.ParseMatrixScalar("1.5", "value").Type);
        Assert.Equal("off", ScenarioConfiguration.ParseMatrixScalar("off", "value").Value<string>());
    }

    [Fact]
    public void TheClassAndMethodValues_AreResolved_WithTheMethodWinning()
    {
        MethodInfo method = typeof(ConfiguredClass).GetMethod("MethodWithItsOwnValues")!;
        using (Row(typeof(ConfiguredClass), method, ["veryoften"]))
        {
            IReadOnlyDictionary<string, string> manifest = Manifest(ScenarioAttributes.Configuration(typeof(ConfiguredClass)));

            Assert.Equal("veryoften", manifest["world.temporalStorms"]);
            Assert.Equal("off", manifest["world.creatureHostility"]);
            Assert.Equal("false", manifest["server.AllowPvP"]);
        }
    }

    [Fact]
    public void AnUnknownServerConfigKey_IsReported()
    {
        MethodInfo method = typeof(BrokenClass).GetMethod("MethodWithoutValues")!;
        using (Row(typeof(BrokenClass), method, []))
        {
            ArgumentException failure = Assert.Throws<ArgumentException>(() => ScenarioAttributes.Configuration(typeof(BrokenClass)));
            Assert.Contains("NoSuchSetting", failure.Message);
        }
    }

    [Fact]
    public void TwoMatricesOnOneTheory_AreReported()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(() => ScenarioAttributes.SingleMatrix(
        [
            new WorldConfigMatrixAttribute("creatureHostility", "aggressive"),
            new ServerConfigMatrixAttribute("MaxChunkRadius", "4"),
        ]));

        Assert.Contains("only one Pharos configuration matrix", failure.Message);
    }

    [Fact]
    public void AMatrix_GivesOneRowPerValue()
    {
        MethodInfo method = Helper("MethodWithOneString");

        List<object[]> rows = [.. new WorldConfigMatrixAttribute("creatureHostility", "aggressive", "passive").GetData(method)];
        Assert.Equal(["aggressive", "passive"], rows.Select(row => (string)row[0]));

        Assert.Throws<ArgumentException>(() => new WorldConfigMatrixAttribute("creatureHostility").GetData(method).ToList());
        Assert.Throws<ArgumentException>(() => new WorldConfigMatrixAttribute("creatureHostility", "aggressive")
            .GetData(Helper("MethodWithoutValues")).ToList());
    }

    [Fact]
    public void TheModConfigMatrix_RejectsADestinationOutsideModConfig()
    {
        Assert.Throws<ArgumentException>(() => new ModConfigMatrixAttribute("../outside.json", "a.json"));
        Assert.Throws<ArgumentException>(() => new ModConfigMatrixAttribute("sub/dir.json", "a.json"));
    }

    [Fact]
    public void TheRunManifest_ListsTheWorldTheServerAndTheSeededFiles()
    {
        string folder = Path.Combine(Path.GetTempPath(), "pharos-config-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        try
        {
            string file = Path.Combine(folder, "seed.json");
            File.WriteAllText(file, "{\"Label\":\"seeded\"}");

            DataFileSet files = DataFileSet.Create(
                [new DataFilesAttribute(file) { To = "ModConfig/seed.json", Side = EnumAppSide.Server }],
                [],
                [EnumAppSide.Server]);
            ScenarioConfiguration configuration = new(
                [new KeyValuePair<string, JToken>("temporalStorms", new JValue("off"))],
                [new KeyValuePair<string, JToken>("AllowPvP", new JValue(false))]);

            IReadOnlyDictionary<string, string> manifest = configuration.ForRunManifest(
                new ServerWorldOptions { WorldConfigurationJson = "{\"temporalStorms\":\"off\"}" }, files)!;

            Assert.Equal("off", manifest["world.temporalStorms"]);
            Assert.Equal("false", manifest["server.AllowPvP"]);
            Assert.Contains("server.file.ModConfig/seed.json", manifest.Keys);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void TheBaseline_WatchesModConfig_AndPutsBackWhatATestChanged()
    {
        string folder = Path.Combine(Path.GetTempPath(), "pharos-config-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "ModConfig"));
        try
        {
            DataFileBaseline? baseline = DataFileBaseline.Capture(folder, DataFileSet.Empty, EnumAppSide.Server, includeModConfig: true);

            Assert.NotNull(baseline);
            File.WriteAllText(Path.Combine(folder, "ModConfig", "mymod.json"), "changed");
            Assert.Equal(1, baseline.Restore());
            Assert.False(File.Exists(Path.Combine(folder, "ModConfig", "mymod.json")));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static MethodInfo Helper(string name) =>
        typeof(Helpers).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
    private static IReadOnlyDictionary<string, string> Manifest(ScenarioConfiguration configuration) =>
        configuration.ForRunManifest(new ServerWorldOptions { WorldConfigurationJson = "{}" }, DataFileSet.Empty)!;

    private static IDisposable Row(Type testClass, MethodInfo method, object?[] arguments)
    {
        ScenarioTestInfo? previous = ScenarioTestInfo.Current;
        ScenarioTestInfo.Current = new ScenarioTestInfo("row", testClass, method.Name, method) { MethodArguments = arguments };
        return new Restore(() => ScenarioTestInfo.Current = previous);
    }

    private sealed class Restore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    [WorldConfig("creatureHostility", "off")]
    [ServerConfig(nameof(Vintagestory.Server.ServerConfig.AllowPvP), false)]
    private class ConfiguredClass
    {
        [WorldConfig("temporalStorms", "veryoften")]
        public void MethodWithItsOwnValues()
        {
        }
    }

    [ServerConfig("NoSuchSetting", true)]
    private class BrokenClass
    {
        public void MethodWithoutValues()
        {
        }
    }

    private sealed class Helpers
    {
        private void MethodWithoutValues()
        {
        }

        private void MethodWithOneString(string value)
        {
        }
    }
}
