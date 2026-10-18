using System.Reflection;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Scenarios;

/// <summary>The result of evaluating scenario gating attributes before execution.</summary>
public readonly record struct ScenarioGateResult(string? SkipReason, Exception? Exception)
{
    /// <summary>Whether the scenario passed all gates and should execute.</summary>
    public bool ShouldRun => SkipReason == null && Exception == null;

    /// <summary>Indicates the scenario should run.</summary>
    public static ScenarioGateResult Run() => new(null, null);

    /// <summary>Indicates the scenario should be skipped with the given reason.</summary>
    public static ScenarioGateResult Skip(string reason) => new(reason, null);

    /// <summary>Indicates the scenario failed a gate requirement (e.g. invalid syntax or conflict).</summary>
    public static ScenarioGateResult Fail(Exception exception) => new(null, exception);
}

/// <summary>
/// Framework-neutral gate evaluation for scenario tests.
/// Evaluates game version requirements, Optimum requirements, and explicit flags.
/// </summary>
public static class ScenarioGate
{
    /// <summary>Environment variable that forces explicit scenarios to execute.</summary>
    public const string RunExplicitVariable = "PHAROS_RUN_EXPLICIT";

    /// <summary>
    /// Evaluates all gates on the specified test class and method.
    /// </summary>
    public static ScenarioGateResult Evaluate(Type testClass, MethodInfo method)
    {
        // 1. Explicit gate
        if (IsExplicit(method) && Environment.GetEnvironmentVariable(RunExplicitVariable) != "1")
        {
            return ScenarioGateResult.Skip($"Explicit scenario. Select it by name with pharos run --filter or set {RunExplicitVariable}=1.");
        }

        // 2. Optimum and GameVersion gates
        try
        {
            // Optimum gate first, so conflicting attributes fail even on a version that skips.
            string? optimum = OptimumGate.SkipReasonFor(testClass, method, OptimumInstall.Loaded);
            if (optimum != null) return ScenarioGateResult.Skip(optimum);

            string? gameVersion = EvaluateGameVersion(testClass, method);
            if (gameVersion != null) return ScenarioGateResult.Skip(gameVersion);
        }
        catch (FormatException ex)
        {
            return ScenarioGateResult.Fail(new InvalidOperationException($"Invalid [RequireGameVersion] range: {ex.Message}", ex));
        }
        catch (OptimumGateConflictException ex)
        {
            return ScenarioGateResult.Fail(ex);
        }

        return ScenarioGateResult.Run();
    }

    private static bool IsExplicit(MethodInfo method)
    {
        foreach (Attribute attribute in method.GetCustomAttributes())
        {
            PropertyInfo? prop = attribute.GetType().GetProperty("Explicit");
            if (prop != null && prop.PropertyType == typeof(bool) && prop.GetValue(attribute) is true)
                return true;
        }

        return false;
    }

    private static string? EvaluateGameVersion(Type testClass, MethodInfo method)
    {
        IEnumerable<Attribute> attributes = method.GetCustomAttributes(inherit: true).OfType<Attribute>()
            .Concat(testClass.GetCustomAttributes(inherit: true).OfType<Attribute>())
            .Where(a => a.GetType().Name == "RequireGameVersionAttribute");

        PharosGameVersion? installed = InstalledGame.ParsedVersion;
        foreach (Attribute attr in attributes)
        {
            string? rangeText = attr.GetType().GetProperty("Range")?.GetValue(attr) as string;
            string? reason = attr.GetType().GetProperty("Reason")?.GetValue(attr) as string;
            if (rangeText == null) continue;

            GameVersionRange range = GameVersionRange.Parse(rangeText);
            if (installed is not { } v || range.Includes(v)) continue;

            string where;
            try { where = $" ({Platform.HeadlessPlatformResolver.ResolveGamePath()})"; }
            catch { where = ""; }

            return $"Requires Vintage Story {rangeText}; this install is {installed}{where}." + (reason == null ? "" : " " + reason);
        }

        return null;
    }
}
