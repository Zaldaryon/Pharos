using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Reflection;
using Xunit.Abstractions;

namespace Zaldaryon.Pharos.XUnit.Execution;

internal readonly record struct ScenarioAttemptPolicy(int Attempts, int RequiredSuccesses)
{
    public static ScenarioAttemptPolicy Read(MethodInfo method)
    {
        Attribute? attribute = method.GetCustomAttributes().FirstOrDefault(a =>
            a.GetType().GetProperty(nameof(ClientScenarioAttribute.Attempts))?.PropertyType == typeof(int) &&
            a.GetType().GetProperty(nameof(ClientScenarioAttribute.RequiredSuccesses))?.PropertyType == typeof(int));
        int attempts = (int?)attribute?.GetType().GetProperty(nameof(ClientScenarioAttribute.Attempts))?.GetValue(attribute) ?? 1;
        int required = (int?)attribute?.GetType().GetProperty(nameof(ClientScenarioAttribute.RequiredSuccesses))?.GetValue(attribute) ?? 1;
        if (attempts < 1 || required < 1 || required > attempts)
            throw new ArgumentOutOfRangeException(nameof(attempts), $"Scenario attempts must satisfy 1 <= RequiredSuccesses <= Attempts; got Attempts={attempts}, RequiredSuccesses={required}.");
        return new(attempts, required);
    }
}

internal sealed class ScenarioAttemptState
{
    private static readonly AsyncLocal<ScenarioAttemptState?> s_current = new();
    private readonly List<string> _artifactDirectories = [];

    public static ScenarioAttemptState? Current { get => s_current.Value; set => s_current.Value = value; }
    public bool BodyEntered { get; set; }
    public bool RetryableBodyFailure { get; set; }
    public Exception? RetryableFailure { get; set; }
    public bool NonRetryableFailure { get; private set; }
    public bool Skipped { get; set; }
    public string Phase { get; private set; } = "setup";
    public IReadOnlyList<string> ArtifactDirectories => _artifactDirectories;

    public void MarkNonRetryable(string phase = "setup")
    {
        NonRetryableFailure = true;
        Phase = phase;
    }
    public void EnterBody()
    {
        BodyEntered = true;
        Phase = "body";
    }
    public void AddArtifactDirectory(string directory) => _artifactDirectories.Add(directory);
}

internal sealed record ScenarioAttemptEntry(int Number, string Phase, string Outcome, double DurationSeconds, string? Failure, IReadOnlyList<string> ArtifactDirectories);

// One worker-written per-test fragment. The CLI folds these into the versioned run manifest
// alongside assembly/TRX/worker metadata; this is deliberately not run.json.
internal sealed record ScenarioAttemptFragment(
    int SchemaVersion,
    string RunId,
    string Assembly,
    string FullyQualifiedName,
    string DisplayName,
    int Attempts,
    int RequiredSuccesses,
    string RawOutcome,
    string FinalOutcome,
    bool Flaky,
    IReadOnlyList<ScenarioAttemptEntry> AttemptHistory);

internal static class ScenarioAttemptManifestWriter
{
    public const string DirectoryVariable = "PHAROS_ATTEMPT_MANIFEST_DIR";

    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static void Write(ITest test, ScenarioAttemptPolicy policy, IReadOnlyList<ScenarioAttemptEntry> attempts, string outcome, bool flaky)
    {
        string? directory = Environment.GetEnvironmentVariable(DirectoryVariable);
        if (string.IsNullOrWhiteSpace(directory)) return;
        try
        {
            WriteCore(test, policy, attempts, outcome, flaky, directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or
                                   JsonException or ArgumentException or NotSupportedException)
        {
            // Attempt metadata is diagnostic. An unwritable manifest must not convert a passing
            // or failing scenario into a different xUnit outcome.
            System.Diagnostics.Trace.TraceWarning($"Could not write Pharos attempt metadata: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void WriteCore(ITest test, ScenarioAttemptPolicy policy, IReadOnlyList<ScenarioAttemptEntry> attempts, string outcome, bool flaky, string directory)
    {
        Directory.CreateDirectory(directory);
        string fqn = $"{test.TestCase.TestMethod.TestClass.Class.Name}.{test.TestCase.TestMethod.Method.Name}";
        string identity = fqn + "\0" + test.DisplayName;
        string stem = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..16].ToLowerInvariant();
        ScenarioAttemptFragment record = new(1, Environment.GetEnvironmentVariable("PHAROS_RUN_ID") ?? "",
            test.TestCase.TestMethod.TestClass.TestCollection.TestAssembly.Assembly.Name,
            fqn, test.DisplayName,
            policy.Attempts, policy.RequiredSuccesses, outcome, outcome, flaky, attempts);
        string path = Path.Combine(directory, $"{stem}-{Guid.NewGuid():N}.json");
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(record, s_json));
            File.Move(temporary, path);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
