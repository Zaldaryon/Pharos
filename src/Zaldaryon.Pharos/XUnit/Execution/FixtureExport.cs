using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit.Execution;

/// <summary>
/// The test side of <c>pharos fixture</c>: when the run asks for it, the world the named test
/// built is saved once the test has passed, before the class rolls it back.
/// </summary>
internal static class FixtureExport
{
    /// <summary>Where to write the world: set by <c>pharos fixture</c> for its run.</summary>
    public const string OutVariable = "PHAROS_FIXTURE_OUT";

    /// <summary>The fully qualified name of the test to save the world of: Namespace.Class.Method.</summary>
    public const string TestVariable = "PHAROS_FIXTURE_TEST";

    /// <summary>Where the world of <paramref name="test"/> is to be written, or null when this run does not ask for it.</summary>
    public static string? Requested(ScenarioTestInfo test)
    {
        string? destination = Environment.GetEnvironmentVariable(OutVariable);
        string? wanted = Environment.GetEnvironmentVariable(TestVariable);
        if (string.IsNullOrWhiteSpace(destination) || string.IsNullOrWhiteSpace(wanted)) return null;
        return Matches(test, wanted) ? destination : null;
    }

    public static bool Matches(ScenarioTestInfo test, string fullyQualifiedName) =>
        string.Equals(FullyQualifiedName(test), fullyQualifiedName, StringComparison.Ordinal);

    // As xUnit names it to the test platform: nested classes with '+'.
    public static string FullyQualifiedName(ScenarioTestInfo test) => test.TestClass.FullName + "." + test.MethodName;

    public static Task SaveAsync(EmbeddedServerHost? server, ScenarioTestInfo test, string destination)
    {
        if (server == null)
        {
            throw new InvalidOperationException(
                $"pharos fixture saves the world of a scenario's embedded server; {test.DisplayName} has none. " +
                "Build fixtures with a [ServerScenario] or a [ClientServerScenario] test.");
        }

        return server.SaveWorldAsync(destination);
    }
}
