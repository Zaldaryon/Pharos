using Xunit.Abstractions;
using Zaldaryon.Pharos.Scenarios;

namespace Zaldaryon.Pharos.XUnit.Execution;

internal static class ScenarioAttemptManifestWriterAdapter
{
    public static void Write(ITest test, ScenarioAttemptPolicy policy, IReadOnlyList<ScenarioAttemptEntry> attempts, string outcome, bool flaky)
    {
        string fqn = $"{test.TestCase.TestMethod.TestClass.Class.Name}.{test.TestCase.TestMethod.Method.Name}";
        ScenarioAttemptManifestWriter.Write(
            test.TestCase.TestMethod.TestClass.TestCollection.TestAssembly.Assembly.Name,
            fqn,
            test.DisplayName,
            policy,
            attempts,
            outcome,
            flaky);
    }
}
