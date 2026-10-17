using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

internal sealed record ScenarioRunManifest(
    int SchemaVersion,
    string RunId,
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string Assembly,
    string Trx,
    IReadOnlyList<ScenarioRunWorker> Workers,
    IReadOnlyList<string> LostTests,
    IReadOnlyList<string> MetadataWarnings,
    IReadOnlyList<ScenarioRunTest> Tests);

internal sealed record ScenarioRunWorker(int Index, string Group, int? ExitCode, bool TimedOut, string? Aborted,
    IReadOnlyList<string> LostTests);

internal sealed record ScenarioRunTest(string FullyQualifiedName, string DisplayName, string RawOutcome,
    string FinalOutcome, bool? Flaky, bool Quarantined, string? QuarantineOwner, string? QuarantineExpiresOn,
    string? FailurePhase, IReadOnlyList<ScenarioAttemptEntry>? Attempts);

internal static class ScenarioRunManifestWriter
{
    public static string PathFor(string trxPath) => Path.Combine(Path.GetDirectoryName(trxPath)!, "pharos-run.json");

    public static ScenarioRunManifest Write(string path, string runId, DateTimeOffset started, DateTimeOffset finished,
        string assembly, string trxPath, XDocument mergedTrx, IReadOnlyList<WorkerOutcome> outcomes,
        IEnumerable<TrxResult> notStartedTests, string attemptsDirectory, QuarantinePolicy? quarantinePolicy)
    {
        List<ScenarioAttemptFragment> fragments = [];
        List<string> warnings = [];
        string expectedAssembly = System.Reflection.AssemblyName.GetAssemblyName(assembly).FullName ?? "";
        if (Directory.Exists(attemptsDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(attemptsDirectory, "*.json", SearchOption.AllDirectories))
            {
                try
                {
                    if (JsonSerializer.Deserialize<ScenarioAttemptFragment>(File.ReadAllText(file), Options()) is not { } fragment ||
                        fragment.SchemaVersion != 1 || fragment.RunId != runId || fragment.Assembly != expectedAssembly ||
                        fragment.Attempts < 1 || fragment.RequiredSuccesses < 1 || fragment.RequiredSuccesses > fragment.Attempts ||
                        string.IsNullOrWhiteSpace(fragment.FullyQualifiedName) || string.IsNullOrWhiteSpace(fragment.DisplayName) ||
                        fragment.AttemptHistory == null || fragment.AttemptHistory.Count == 0 ||
                        fragment.AttemptHistory.Any(attempt => attempt == null || attempt.Number < 1 || string.IsNullOrWhiteSpace(attempt.Phase) || string.IsNullOrWhiteSpace(attempt.Outcome)))
                    {
                        warnings.Add($"Ignored invalid or mismatched attempt fragment: {Path.GetFileName(file)}");
                        continue;
                    }
                    fragments.Add(fragment);
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    // An invalid worker fragment is unknown metadata, never a test result.
                    warnings.Add($"Ignored unreadable attempt fragment {Path.GetFileName(file)}: {ex.GetType().Name}");
                }
            }
        }

        HashSet<string> duplicates = fragments.GroupBy(f => (f.FullyQualifiedName, f.DisplayName))
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.FullyQualifiedName}\0{group.Key.DisplayName}")
            .ToHashSet(StringComparer.Ordinal);
        foreach (string duplicate in duplicates) warnings.Add("Duplicate attempt metadata ignored for " + duplicate.Replace('\0', ' '));
        HashSet<string> infrastructureTests = outcomes
            .Where(outcome => outcome.Crashed || outcome.TimedOut)
            .SelectMany(outcome => outcome.Group.Tests)
            .Concat(outcomes.SelectMany(outcome => outcome.Lost.Select(test => test.FullyQualifiedName)))
            .Concat(notStartedTests.Select(test => test.FullyQualifiedName))
            .ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<ScenarioRunTest> tests = Trx.Results(mergedTrx).Select(result =>
        {
            string identity = result.FullyQualifiedName + "\0" + result.TestName;
            ScenarioAttemptFragment? fragment = duplicates.Contains(identity) ? null : fragments.FirstOrDefault(candidate =>
                candidate.FullyQualifiedName == result.FullyQualifiedName && candidate.DisplayName == result.TestName);
            string? phase = result.Failed ? fragment?.AttemptHistory.LastOrDefault()?.Phase : null;
            // Quarantine only a classified body failure. Missing metadata must fail closed: it
            // could represent setup, teardown, timeout, or a worker that never ran the body.
            bool canQuarantine = CanQuarantine(result.Failed, phase, infrastructureTests.Contains(result.FullyQualifiedName));
            QuarantineEntry? quarantine = canQuarantine
                ? quarantinePolicy?.Match(result.FullyQualifiedName, result.TestName)
                : null;
            return new ScenarioRunTest(result.FullyQualifiedName, result.TestName, result.Outcome,
                quarantine == null ? result.Outcome : "Quarantined", fragment?.Flaky, quarantine != null,
                quarantine?.Owner, quarantine?.ExpiresOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                phase, fragment?.AttemptHistory);
        }).ToList();
        ScenarioRunManifest manifest = new(1, runId, started.ToUniversalTime(), finished.ToUniversalTime(), assembly,
            Path.GetFullPath(trxPath), outcomes.Select(o => new ScenarioRunWorker(o.Index, o.Group.Name, o.ExitCode,
                o.TimedOut, o.Aborted, o.Lost.Select(test => test.TestName).ToArray())).ToArray(),
            outcomes.SelectMany(o => o.Lost.Select(test => test.TestName)).Concat(notStartedTests.Select(test => test.TestName)).ToArray(), warnings, tests);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(manifest, Options()));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        return manifest;
    }

    internal static bool CanQuarantine(bool rawFailure, string? failurePhase, bool infrastructureFailure) =>
        rawFailure && failurePhase == "body" && !infrastructureFailure;

    private static JsonSerializerOptions Options() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };
}
