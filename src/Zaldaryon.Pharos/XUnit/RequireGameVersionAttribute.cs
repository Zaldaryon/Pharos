using System.Reflection;
using Xunit;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Skips a scenario, or every scenario of a class, unless the installed game's version is in
/// <see cref="Range"/>, such as <c>"&gt;=1.22.0"</c> or <c>"1.22.x"</c> (see
/// <see cref="GameVersionRange"/>). The skip reason names the range and the installed version.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>It applies to the Pharos scenario attributes (<c>[ClientScenario]</c>, <c>[ClientTheory]</c> and the others). A plain xUnit test uses <see cref="GameVersionFactAttribute"/> or <see cref="GameVersionTheoryAttribute"/>.</item>
/// <item>Every attribute on the method, its class and the base classes must hold.</item>
/// <item>A range that cannot be read fails the test rather than skipping it, so a typo cannot hide a test.</item>
/// <item>When the version cannot be read, the test runs.</item>
/// <item>xUnit still creates the class and collection fixtures of a class whose tests are all skipped.</item>
/// </list>
/// </remarks>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RequireGameVersionAttribute(string range) : Attribute
{
    /// <summary>The versions the test runs on.</summary>
    public string Range { get; } = range;

    /// <summary>Why, added to the skip reason.</summary>
    public string? Reason { get; set; }

    /// <summary>
    /// Why <paramref name="method"/> of <paramref name="testClass"/> is skipped on
    /// <paramref name="version"/>, or null when it runs.
    /// </summary>
    /// <exception cref="FormatException">A range cannot be read.</exception>
    internal static string? SkipReasonFor(Type testClass, MethodInfo method, PharosGameVersion? version)
    {
        IEnumerable<RequireGameVersionAttribute> required = method.GetCustomAttributes<RequireGameVersionAttribute>(inherit: true)
            .Concat(testClass.GetCustomAttributes<RequireGameVersionAttribute>(inherit: true));
        foreach (RequireGameVersionAttribute attribute in required)
        {
            GameVersionRange range = GameVersionRange.Parse(attribute.Range);
            if (version is not { } installed || range.Includes(installed)) continue;
            return SkipMessage(attribute.Range, installed.ToString(), attribute.Reason);
        }

        return null;
    }

    internal static string SkipMessage(string range, string installed, string? reason)
    {
        string where;
        try
        {
            where = $" ({Platform.HeadlessPlatformResolver.ResolveGamePath()})";
        }
        catch
        {
            where = "";
        }

        return $"Requires Vintage Story {range}; this install is {installed}{where}." + (reason == null ? "" : " " + reason);
    }
}

/// <summary>
/// A plain xUnit fact that runs only when the installed game's version is in the range. See
/// <see cref="RequireGameVersionAttribute"/>.
/// </summary>
public sealed class GameVersionFactAttribute : FactAttribute
{
    /// <summary>
    /// Skips the fact unless the installed game's version is in <paramref name="range"/>. A range
    /// that cannot be read skips it too, saying so.
    /// </summary>
    public GameVersionFactAttribute(string range)
    {
        Range = range;
        Skip ??= GameVersionGate.SkipReason(range);
    }

    /// <summary>The versions the test runs on.</summary>
    public string Range { get; }
}

/// <summary>
/// A plain xUnit theory that runs only when the installed game's version is in the range. See
/// <see cref="RequireGameVersionAttribute"/>.
/// </summary>
public sealed class GameVersionTheoryAttribute : TheoryAttribute
{
    /// <summary>
    /// Skips the theory unless the installed game's version is in <paramref name="range"/>. A
    /// range that cannot be read skips it too, saying so.
    /// </summary>
    public GameVersionTheoryAttribute(string range)
    {
        Range = range;
        Skip ??= GameVersionGate.SkipReason(range);
    }

    /// <summary>The versions the test runs on.</summary>
    public string Range { get; }
}

internal static class GameVersionGate
{
    // An attribute that throws drops its tests from discovery without a word, so a range that
    // cannot be read skips them with the reason instead.
    public static string? SkipReason(string range)
    {
        GameVersionRange parsed;
        try
        {
            parsed = GameVersionRange.Parse(range);
        }
        catch (FormatException ex)
        {
            return "Invalid game version range: " + ex.Message;
        }

        return InstalledGame.ParsedVersion is { } installed && !parsed.Includes(installed)
            ? RequireGameVersionAttribute.SkipMessage(range, installed.ToString(), null)
            : null;
    }
}
