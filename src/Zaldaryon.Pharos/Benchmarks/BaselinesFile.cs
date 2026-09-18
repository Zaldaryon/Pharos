using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zaldaryon.Pharos.Benchmarks;

/// <summary>
/// Reads and writes benchmark baseline values from/to JSON files.
/// Provides an external source of baseline values that can be updated
/// independently of hard-coded defaults.
/// </summary>
public static class BaselinesFile
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Reads baselines from a JSON file.
    /// </summary>
    /// <param name="path">Path to the baselines JSON file.</param>
    /// <returns>Dictionary mapping benchmark names to baseline values in milliseconds.</returns>
    /// <exception cref="ArgumentException">Thrown when path is null or empty.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    /// <exception cref="JsonException">Thrown when the file contains invalid JSON.</exception>
    public static Dictionary<string, float> ReadBaselines(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Baselines file not found: {path}", path);
        }

        var json = File.ReadAllText(path);
        var wrapper = JsonSerializer.Deserialize<BaselinesWrapper>(json, s_jsonOptions)
            ?? throw new JsonException("Failed to deserialize baselines file");

        return wrapper.Baselines ?? new Dictionary<string, float>();
    }

    /// <summary>
    /// Writes baselines to a JSON file.
    /// </summary>
    /// <param name="path">Path to write the baselines JSON file.</param>
    /// <param name="baselines">Dictionary mapping benchmark names to baseline values in milliseconds.</param>
    /// <exception cref="ArgumentException">Thrown when path is null or empty.</exception>
    /// <exception cref="ArgumentNullException">Thrown when baselines is null.</exception>
    public static void WriteBaselines(string path, Dictionary<string, float> baselines)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentNullException.ThrowIfNull(baselines);

        var wrapper = new BaselinesWrapper
        {
            Version = 1,
            GeneratedAt = DateTime.UtcNow.ToString("O"),
            Baselines = baselines
        };

        var json = JsonSerializer.Serialize(wrapper, s_jsonOptions);
        
        // Ensure directory exists
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Tries to get a baseline value from a dictionary.
    /// </summary>
    /// <param name="baselines">Dictionary of baseline values.</param>
    /// <param name="name">Benchmark name to look up.</param>
    /// <param name="baseline">The baseline value if found, otherwise 0.</param>
    /// <returns>True if the baseline was found, false otherwise.</returns>
    public static bool TryGetBaseline(
        IReadOnlyDictionary<string, float>? baselines,
        string name,
        out float baseline)
    {
        baseline = 0f;

        if (baselines is null || string.IsNullOrEmpty(name))
        {
            return false;
        }

        return baselines.TryGetValue(name, out baseline);
    }

    /// <summary>
    /// Tries to read baselines from a file, returning null if the file doesn't exist.
    /// </summary>
    /// <param name="path">Path to the baselines JSON file.</param>
    /// <returns>Dictionary of baselines if file exists and is valid, null otherwise.</returns>
    public static Dictionary<string, float>? TryReadBaselines(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return ReadBaselines(path);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// JSON wrapper for the baselines file format.
    /// </summary>
    private sealed class BaselinesWrapper
    {
        [JsonPropertyName("version")]
        public int Version { get; set; }

        [JsonPropertyName("generatedAt")]
        public string? GeneratedAt { get; set; }

        [JsonPropertyName("baselines")]
        public Dictionary<string, float>? Baselines { get; set; }
    }
}
