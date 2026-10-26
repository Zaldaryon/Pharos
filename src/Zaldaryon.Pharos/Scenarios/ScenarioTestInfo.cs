using System.Reflection;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Scenarios;

/// <summary>The test a scenario runs, as the failure capture names it.</summary>
/// <param name="DisplayName">The display name, theory arguments included.</param>
/// <param name="TestClass">The test class.</param>
/// <param name="MethodName">The test method's name.</param>
/// <param name="Method">The test method, when the scenario pipeline runs it.</param>
public sealed record ScenarioTestInfo(string DisplayName, Type TestClass, string MethodName, MethodInfo? Method = null)
{
    /// <summary>The arguments of this theory row, available while its scenario is initialized.</summary>
    public IReadOnlyList<object?> MethodArguments { get; init; } = [];

    /// <summary>The effective configuration recorded for this scenario row.</summary>
    public IReadOnlyDictionary<string, string>? Configuration { get; internal set; }

    /// <summary>The seed the test runs with, from <see cref="SeededAttribute"/>, or null when it is not seeded.</summary>
    public ScenarioRandomSeed? RandomSeed { get; init; }

    /// <summary>
    /// The cancellation token for this test, combining the test framework's token with any scenario-specific cancellation.
    /// Use this token in all async waits and long-running operations to support cooperative cancellation.
    /// </summary>
    public CancellationToken CancellationToken { get; init; } = default;

    /// <summary>Reason this running scenario requested a dynamic skip.</summary>
    public string? DynamicSkipReason { get; set; }

    private static readonly AsyncLocal<ScenarioTestInfo?> s_current = new();

    /// <summary>The scenario test running on this flow, set before its class is created.</summary>
    public static ScenarioTestInfo? Current
    {
        get => s_current.Value;
        internal set => s_current.Value = value;
    }
}
