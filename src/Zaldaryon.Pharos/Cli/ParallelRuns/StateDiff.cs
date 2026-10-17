using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

/// <summary>
/// Compares two state hash recordings and names the first tick where they differ. Exit codes match
/// <see cref="DiffCommand"/>: 0 identical, 1 diverged, 2 bad input or not comparable.
/// </summary>
internal static class StateDiff
{
    /// <summary>Compares the recordings at <paramref name="before"/> and <paramref name="after"/> and prints the result.</summary>
    public static int Run(string before, string after, bool json, TextWriter stdout, TextWriter stderr)
    {
        StateHashFile first, second;
        try
        {
            first = StateHashFile.Load(before);
            second = StateHashFile.Load(after);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or FormatException or InvalidOperationException or KeyNotFoundException or NullReferenceException)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return DiffCommand.ExitError;
        }

        StateHashDivergence? divergence;
        try
        {
            divergence = StateHashFile.Compare(first, second);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return DiffCommand.ExitError;
        }

        if (json)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new StateDiffReport(first.Ticks.Count, second.Ticks.Count, divergence), s_json));
        }
        else if (divergence is null)
        {
            stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"State hashes are identical over {first.Ticks.Count} ticks."));
        }
        else
        {
            stdout.WriteLine(Describe(divergence, first.Ticks.Count, second.Ticks.Count));
        }

        return divergence is null ? DiffCommand.ExitNoRegressions : DiffCommand.ExitRegressions;
    }

    private static string Describe(StateHashDivergence divergence, int beforeTicks, int afterTicks)
    {
        string counts = string.Create(CultureInfo.InvariantCulture, $"run A: {beforeTicks} ticks, run B: {afterTicks}");
        if (divergence.Kind == "length")
        {
            return string.Create(CultureInfo.InvariantCulture, $"State hashes match for {divergence.Compared} ticks, then one run ends ({counts}).");
        }

        string parts = divergence.Parts.Count == 0 ? "" : string.Join(", ", divergence.Parts) + " differ";
        return string.Create(CultureInfo.InvariantCulture, $"State hashes diverge at tick {divergence.Tick} ({counts}): {parts}.");
    }

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>What <c>pharos diff --json</c> prints for two state hash recordings.</summary>
internal sealed record StateDiffReport(int BeforeTicks, int AfterTicks, StateHashDivergence? Divergence)
{
    /// <summary>Whether the recordings are the same.</summary>
    public bool Identical => Divergence is null;
}
