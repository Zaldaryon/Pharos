using System.Reflection;
using Xunit;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Skips a scenario, or every scenario of a class, unless the tests run on an Optimum build of
/// the game. See <c>docs/optimum.md</c>. For a plain xUnit test, use <see cref="OptimumFactAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
public sealed class RequireOptimumAttribute : Attribute
{
    /// <summary>Why, added to the skip reason.</summary>
    public string? Reason { get; set; }
}

/// <summary>
/// Skips a scenario, or every scenario of a class, when the tests run on an Optimum build. For a
/// plain xUnit test, use <see cref="NotOptimumFactAttribute"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = true)]
public sealed class SkipOnOptimumAttribute : Attribute
{
    /// <summary>Why, added to the skip reason.</summary>
    public string? Reason { get; set; }
}

/// <summary>A test both requires Optimum and skips on it.</summary>
internal sealed class OptimumGateConflictException(string message) : InvalidOperationException(message);

internal static class OptimumGate
{
    /// <summary>
    /// Why the test is skipped on <paramref name="optimum"/> (null for vanilla), or null when it runs.
    /// </summary>
    /// <exception cref="InvalidOperationException">The test both requires Optimum and skips on it.</exception>
    public static string? SkipReasonFor(Type testClass, MethodInfo method, OptimumInfo? optimum)
    {
        RequireOptimumAttribute? require = method.GetCustomAttribute<RequireOptimumAttribute>(inherit: true) ?? testClass.GetCustomAttribute<RequireOptimumAttribute>(inherit: true);
        SkipOnOptimumAttribute? skip = method.GetCustomAttribute<SkipOnOptimumAttribute>(inherit: true) ?? testClass.GetCustomAttribute<SkipOnOptimumAttribute>(inherit: true);
        if (require != null && skip != null)
        {
            throw new OptimumGateConflictException("[RequireOptimum] and [SkipOnOptimum] both apply to this test, so it could never run.");
        }

        if (require != null && optimum == null) return Vanilla() + Suffix(require.Reason);
        if (skip != null && optimum != null) return $"Skipped on {OptimumInstall.Describe(optimum)}{Where()}." + Suffix(skip.Reason);
        return null;
    }

    public static string Vanilla() => $"Requires an Optimum build; this install is vanilla Vintage Story {InstalledGame.Version}{Where()}.";

    private static string Where()
    {
        try
        {
            return $" ({Platform.HeadlessPlatformResolver.ResolveGamePath()})";
        }
        catch
        {
            return "";
        }
    }

    private static string Suffix(string? reason) => reason == null ? "" : " " + reason;
}

/// <summary>A plain xUnit fact that runs only on an Optimum build.</summary>
public sealed class OptimumFactAttribute : FactAttribute
{
    /// <summary>Skips the fact on vanilla Vintage Story.</summary>
    public OptimumFactAttribute() => Skip ??= OptimumInstall.Loaded == null ? OptimumGate.Vanilla() : null;
}

/// <summary>A plain xUnit theory that runs only on an Optimum build.</summary>
public sealed class OptimumTheoryAttribute : TheoryAttribute
{
    /// <summary>Skips the theory on vanilla Vintage Story.</summary>
    public OptimumTheoryAttribute() => Skip ??= OptimumInstall.Loaded == null ? OptimumGate.Vanilla() : null;
}

/// <summary>A plain xUnit fact that runs only on vanilla Vintage Story, not on an Optimum build.</summary>
public sealed class NotOptimumFactAttribute : FactAttribute
{
    /// <summary>Skips the fact on an Optimum build.</summary>
    public NotOptimumFactAttribute() => Skip ??= OptimumInstall.Loaded is { } optimum ? $"Skipped on {OptimumInstall.Describe(optimum)}." : null;
}
