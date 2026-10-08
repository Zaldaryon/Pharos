using System.Collections.Concurrent;
using System.Reflection;
using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.XUnit.Execution;

/// <summary>
/// The strict boot check of the scenario base classes: <see cref="StrictBootAttribute"/> and
/// <see cref="AllowBootDiagnosticAttribute"/>.
/// </summary>
internal static class BootCheck
{
    /// <summary>
    /// What the harness itself may log at boot, whatever the class allows: the server's overload
    /// warning depends on how busy the machine is, not on the mods under test.
    /// </summary>
    internal static readonly IReadOnlyList<AllowBootDiagnosticAttribute> HarnessAllowances =
    [
        new(@"^Server overloaded\. A tick took") { Source = "game" },
    ];

    private static readonly ConcurrentDictionary<Type, BootDiagnosticsException> s_failed = new();

    /// <summary>Whether the class, its base classes or its assembly ask for a strict boot.</summary>
    public static bool IsStrict(Type testClass) =>
        testClass.GetCustomAttribute<StrictBootAttribute>(inherit: true) != null
        || testClass.Assembly.GetCustomAttribute<StrictBootAttribute>() != null;

    /// <summary>The allowances of the class, its base classes and its assembly, and the harness's own.</summary>
    public static IReadOnlyList<AllowBootDiagnosticAttribute> Allowances(Type testClass) =>
        [.. testClass.Assembly.GetCustomAttributes<AllowBootDiagnosticAttribute>(),
         .. testClass.GetCustomAttributes<AllowBootDiagnosticAttribute>(inherit: true),
         .. HarnessAllowances];

    /// <summary>Judges <paramref name="boots"/> against the class's allowances.</summary>
    public static BootDiagnosticsResult Evaluate(Type testClass, params BootDiagnostics?[] boots) =>
        BootDiagnosticsResult.Of(boots.OfType<BootDiagnostics>(), Allowances(testClass));

    /// <summary>
    /// Throws when an earlier test of the class failed its strict boot: the class is not booted
    /// again, and the test fails at once with the same list.
    /// </summary>
    public static void ThrowIfFailedBefore(Type testClass)
    {
        if (!s_failed.TryGetValue(testClass, out BootDiagnosticsException? first)) return;

        throw new ScenarioFailedException(
            new BootDiagnosticsException(first.Result),
            $"{testClass.Name} failed its strict boot in an earlier test, {first.Test ?? "of this class"}, and is not booted again. That test's failure names its artifacts.");
    }

    /// <summary>
    /// Checks a freshly booted host's diagnostics for a strict class, and throws when they fail.
    /// Returns whether the class judges boot diagnostics at all, so the caller can start the
    /// logged-error gate afresh: what was logged at boot is judged here, not by the gate.
    /// </summary>
    /// <exception cref="BootDiagnosticsException">The boot logged what the class does not allow.</exception>
    public static bool Enforce(Type testClass, params BootDiagnostics?[] boots)
    {
        if (!IsStrict(testClass)) return Allowances(testClass).Count > HarnessAllowances.Count;

        BootDiagnosticsResult result = Evaluate(testClass, boots);
        if (result.Passed) return true;

        BootDiagnosticsException failure = new(result) { Test = ScenarioTestInfo.Current?.DisplayName };
        s_failed.TryAdd(testClass, failure);
        throw failure;
    }

    /// <summary>Forgets remembered failures, for tests of Pharos itself.</summary>
    internal static void Forget(Type testClass) => s_failed.TryRemove(testClass, out _);
}
