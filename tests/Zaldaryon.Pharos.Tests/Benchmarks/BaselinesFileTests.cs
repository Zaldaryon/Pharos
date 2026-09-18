using System.Text.Json;
using Xunit;
using Zaldaryon.Pharos.Benchmarks;

namespace Zaldaryon.Pharos.Tests.Benchmarks;

/// <summary>
/// Tests for BaselinesFile read/write operations and BenchmarkSuite file integration.
/// All tests are pure logic tests that operate on temp files.
/// </summary>
public class BaselinesFileTests : IDisposable
{
    private readonly string _tempDir;

    public BaselinesFileTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pharos_baselines_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    #region ReadBaselines Tests

    [Fact]
    public void ReadBaselines_ValidFile_ReturnsDictionary()
    {
        var path = Path.Combine(_tempDir, "valid.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "baselines": {
                "TestBench": 10.5,
                "OtherBench": 20.0
              }
            }
            """);

        var result = BaselinesFile.ReadBaselines(path);

        Assert.Equal(2, result.Count);
        Assert.Equal(10.5f, result["TestBench"]);
        Assert.Equal(20.0f, result["OtherBench"]);
    }

    [Fact]
    public void ReadBaselines_MissingFile_ThrowsFileNotFound()
    {
        var path = Path.Combine(_tempDir, "missing.json");

        Assert.Throws<FileNotFoundException>(() => BaselinesFile.ReadBaselines(path));
    }

    [Fact]
    public void ReadBaselines_InvalidJson_ThrowsJsonException()
    {
        var path = Path.Combine(_tempDir, "invalid.json");
        File.WriteAllText(path, "{ not valid json }");

        Assert.Throws<JsonException>(() => BaselinesFile.ReadBaselines(path));
    }

    [Fact]
    public void ReadBaselines_NullPath_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentNullException>(() => BaselinesFile.ReadBaselines(null!));
        Assert.Throws<ArgumentException>(() => BaselinesFile.ReadBaselines(""));
    }

    [Fact]
    public void ReadBaselines_EmptyBaselines_ReturnsEmptyDictionary()
    {
        var path = Path.Combine(_tempDir, "empty.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "baselines": {}
            }
            """);

        var result = BaselinesFile.ReadBaselines(path);

        Assert.Empty(result);
    }

    #endregion

    #region WriteBaselines Tests

    [Fact]
    public void WriteBaselines_ValidData_WritesJsonFile()
    {
        var path = Path.Combine(_tempDir, "output.json");
        var baselines = new Dictionary<string, float>
        {
            ["IndirectDraw"] = 5.0f,
            ["SimdCulling"] = 10.0f
        };

        BaselinesFile.WriteBaselines(path, baselines);

        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("IndirectDraw", content);
        Assert.Contains("SimdCulling", content);
        Assert.Contains("version", content);
    }

    [Fact]
    public void WriteBaselines_CreatesDirectory_WhenNeeded()
    {
        var subDir = Path.Combine(_tempDir, "subdir", "nested");
        var path = Path.Combine(subDir, "baselines.json");
        var baselines = new Dictionary<string, float> { ["Test"] = 1.0f };

        BaselinesFile.WriteBaselines(path, baselines);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void WriteBaselines_NullPath_ThrowsArgumentException()
    {
        var baselines = new Dictionary<string, float>();
        Assert.Throws<ArgumentNullException>(() => BaselinesFile.WriteBaselines(null!, baselines));
        Assert.Throws<ArgumentException>(() => BaselinesFile.WriteBaselines("", baselines));
    }

    [Fact]
    public void WriteBaselines_NullBaselines_ThrowsArgumentNullException()
    {
        var path = Path.Combine(_tempDir, "null.json");
        Assert.Throws<ArgumentNullException>(() => BaselinesFile.WriteBaselines(path, null!));
    }

    #endregion

    #region Roundtrip Tests

    [Fact]
    public void WriteAndRead_Roundtrip_PreservesValues()
    {
        var path = Path.Combine(_tempDir, "roundtrip.json");
        var original = new Dictionary<string, float>
        {
            ["IndirectDraw"] = 5.0f,
            ["SimdCulling"] = 10.0f,
            ["MeshCompression"] = 15.0f,
            ["FsrPipeline"] = 2.0f,
            ["ModCompatibility"] = 1.0f
        };

        BaselinesFile.WriteBaselines(path, original);
        var loaded = BaselinesFile.ReadBaselines(path);

        Assert.Equal(original.Count, loaded.Count);
        foreach (var kvp in original)
        {
            Assert.Equal(kvp.Value, loaded[kvp.Key], precision: 3);
        }
    }

    [Fact]
    public void WriteAndRead_Roundtrip_PreservesFloatPrecision()
    {
        var path = Path.Combine(_tempDir, "precision.json");
        var original = new Dictionary<string, float>
        {
            ["Precise1"] = 1.2345f,
            ["Precise2"] = 0.0001f,
            ["Large"] = 1000000.5f
        };

        BaselinesFile.WriteBaselines(path, original);
        var loaded = BaselinesFile.ReadBaselines(path);

        Assert.Equal(original["Precise1"], loaded["Precise1"], precision: 3);
        Assert.Equal(original["Precise2"], loaded["Precise2"], precision: 4);
        Assert.Equal(original["Large"], loaded["Large"], precision: 0);
    }

    #endregion

    #region TryGetBaseline Tests

    [Fact]
    public void TryGetBaseline_ExistingKey_ReturnsTrue()
    {
        var baselines = new Dictionary<string, float> { ["Test"] = 5.0f };

        var result = BaselinesFile.TryGetBaseline(baselines, "Test", out var baseline);

        Assert.True(result);
        Assert.Equal(5.0f, baseline);
    }

    [Fact]
    public void TryGetBaseline_MissingKey_ReturnsFalse()
    {
        var baselines = new Dictionary<string, float> { ["Test"] = 5.0f };

        var result = BaselinesFile.TryGetBaseline(baselines, "NotFound", out var baseline);

        Assert.False(result);
        Assert.Equal(0f, baseline);
    }

    [Fact]
    public void TryGetBaseline_NullDictionary_ReturnsFalse()
    {
        var result = BaselinesFile.TryGetBaseline(null, "Test", out var baseline);

        Assert.False(result);
        Assert.Equal(0f, baseline);
    }

    [Fact]
    public void TryGetBaseline_NullOrEmptyName_ReturnsFalse()
    {
        var baselines = new Dictionary<string, float> { ["Test"] = 5.0f };

        Assert.False(BaselinesFile.TryGetBaseline(baselines, null!, out _));
        Assert.False(BaselinesFile.TryGetBaseline(baselines, "", out _));
    }

    #endregion

    #region TryReadBaselines Tests

    [Fact]
    public void TryReadBaselines_ExistingFile_ReturnsDictionary()
    {
        var path = Path.Combine(_tempDir, "try_read.json");
        File.WriteAllText(path, """
            {
              "version": 1,
              "baselines": { "Test": 5.0 }
            }
            """);

        var result = BaselinesFile.TryReadBaselines(path);

        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public void TryReadBaselines_MissingFile_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "not_exists.json");

        var result = BaselinesFile.TryReadBaselines(path);

        Assert.Null(result);
    }

    [Fact]
    public void TryReadBaselines_InvalidJson_ReturnsNull()
    {
        var path = Path.Combine(_tempDir, "invalid_try.json");
        File.WriteAllText(path, "not json");

        var result = BaselinesFile.TryReadBaselines(path);

        Assert.Null(result);
    }

    [Fact]
    public void TryReadBaselines_NullOrEmptyPath_ReturnsNull()
    {
        Assert.Null(BaselinesFile.TryReadBaselines(null!));
        Assert.Null(BaselinesFile.TryReadBaselines(""));
    }

    #endregion

    #region BenchmarkSuite Integration Tests

    [Fact]
    public void BenchmarkSuite_WithFilePath_LoadsBaselines()
    {
        var path = Path.Combine(_tempDir, "suite_baselines.json");
        var baselines = new Dictionary<string, float>
        {
            ["IndirectDraw"] = 100.0f,
            ["SimdCulling"] = 200.0f
        };
        BaselinesFile.WriteBaselines(path, baselines);

        var suite = new BenchmarkSuite(path);

        Assert.True(suite.HasFileBaselines);
        Assert.Equal(2, suite.FileBaselineCount);
    }

    [Fact]
    public void BenchmarkSuite_WithMissingFile_FallsBackToDefaults()
    {
        var suite = new BenchmarkSuite("/nonexistent/path.json");

        Assert.False(suite.HasFileBaselines);
        Assert.Equal(0, suite.FileBaselineCount);
    }

    [Fact]
    public void BenchmarkSuite_WithNullPath_UsesDefaults()
    {
        var suite = new BenchmarkSuite(baselineFilePath: null);

        Assert.False(suite.HasFileBaselines);
    }

    [Fact]
    public void BenchmarkSuite_RunWithFileBaselines_SourcedFromFileIsTrue()
    {
        var path = Path.Combine(_tempDir, "run_baselines.json");
        var baselines = new Dictionary<string, float>
        {
            ["IndirectDraw"] = 100.0f // Use a large value so test passes
        };
        BaselinesFile.WriteBaselines(path, baselines);

        var suite = new BenchmarkSuite(path);
        var benchmark = new IndirectDrawBenchmark();
        var results = suite.Run([benchmark]);

        Assert.Single(results);
        Assert.True(results[0].SourcedFromFile);
        Assert.Equal(100.0f, results[0].BaselineMs);
    }

    [Fact]
    public void BenchmarkSuite_RunWithoutFileBaselines_SourcedFromFileIsFalse()
    {
        var suite = new BenchmarkSuite();
        var benchmark = new IndirectDrawBenchmark();
        var results = suite.Run([benchmark]);

        Assert.Single(results);
        Assert.False(results[0].SourcedFromFile);
        Assert.Equal(benchmark.BaselineMs, results[0].BaselineMs);
    }

    [Fact]
    public void BenchmarkSummary_FromFileCount_TracksFileSourcedResults()
    {
        var results = new[]
        {
            BenchmarkResult.Create("A", 10f, 10f, sourcedFromFile: true),
            BenchmarkResult.Create("B", 10f, 10f, sourcedFromFile: false),
            BenchmarkResult.Create("C", 10f, 10f, sourcedFromFile: true)
        };

        var summary = BenchmarkSuite.Summarize(results);

        Assert.Equal(2, summary.FromFileCount);
    }

    [Fact]
    public void BenchmarkSummary_FormatSummary_IncludesFileCount()
    {
        var results = new[]
        {
            BenchmarkResult.Create("A", 10f, 10f, sourcedFromFile: true),
            BenchmarkResult.Create("B", 10f, 10f, sourcedFromFile: true)
        };

        var summary = BenchmarkSuite.Summarize(results);
        var formatted = summary.FormatSummary();

        Assert.Contains("2 from file", formatted);
    }

    #endregion

    #region BenchmarkResult SourcedFromFile Tests

    [Fact]
    public void BenchmarkResult_Create_DefaultSourcedFromFileIsFalse()
    {
        var result = BenchmarkResult.Create("Test", 10f, 10f);

        Assert.False(result.SourcedFromFile);
    }

    [Fact]
    public void BenchmarkResult_Create_SourcedFromFileCanBeTrue()
    {
        var result = BenchmarkResult.Create("Test", 10f, 10f, sourcedFromFile: true);

        Assert.True(result.SourcedFromFile);
    }

    [Fact]
    public void BenchmarkResult_FormatSummary_IncludesFileMarker()
    {
        var result = BenchmarkResult.Create("Test", 10f, 10f, sourcedFromFile: true);
        var summary = result.FormatSummary();

        Assert.Contains("[file]", summary);
    }

    [Fact]
    public void BenchmarkResult_FormatSummary_NoFileMarkerWhenDefault()
    {
        var result = BenchmarkResult.Create("Test", 10f, 10f, sourcedFromFile: false);
        var summary = result.FormatSummary();

        Assert.DoesNotContain("[file]", summary);
    }

    #endregion
}
