using System.Globalization;
using System.Reflection;

namespace Zaldaryon.Pharos.Scenarios;

/// <summary>The watchdog timeouts of scenario attributes.</summary>
public static class ScenarioTimeouts
{
    /// <summary>Multiplies every scenario timeout, for slow machines: <c>PHAROS_TIMEOUT_SCALE=2</c>.</summary>
    public const string ScaleVariable = "PHAROS_TIMEOUT_SCALE";

    /// <summary>How long an aborted body gets to stop before its host is given up.</summary>
    public static TimeSpan Grace { get; set; } = TimeSpan.FromSeconds(15);

    private static readonly AsyncLocal<int?> s_override = new();

    /// <summary>Replaces the attribute's timeout on this flow, in milliseconds: the smoke runner's <c>--timeout</c>.</summary>
    public static int? Override
    {
        get => s_override.Value;
        set => s_override.Value = value;
    }

    /// <summary>
    /// The timeout of a scenario test in milliseconds, 0 for none: the attribute's
    /// <c>Timeout</c> when set, else its <c>TimeoutMs</c>, scaled by <c>PHAROS_TIMEOUT_SCALE</c>.
    /// </summary>
    public static int Of(MethodInfo method)
    {
        if (Override is { } overridden) return overridden;

        Attribute? attribute = method.GetCustomAttributes().FirstOrDefault(a =>
            a.GetType().Name.Contains("Scenario") || a.GetType().Name.Contains("Theory") || a.GetType().Name.Contains("Fact"));
        if (attribute == null) return 0;

        int timeout = Named(attribute, "Timeout");
        if (timeout <= 0) timeout = Named(attribute, "TimeoutMs");
        if (timeout <= 0) return 0;

        return (int)Math.Clamp(timeout * Scale, 1, int.MaxValue);
    }

    /// <summary>The factor <c>PHAROS_TIMEOUT_SCALE</c> sets, 1 when unset.</summary>
    public static double Scale =>
        double.TryParse(Environment.GetEnvironmentVariable(ScaleVariable), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) && scale > 0
            ? scale
            : 1;

    private static int Named(Attribute attribute, string name)
    {
        PropertyInfo? prop = attribute.GetType().GetProperty(name);
        if (prop != null && prop.PropertyType == typeof(int) && prop.GetValue(attribute) is int val)
            return val;
        return 0;
    }
}
