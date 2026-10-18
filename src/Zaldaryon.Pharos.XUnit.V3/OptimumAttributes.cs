using System.Runtime.CompilerServices;
using Xunit.v3;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.XUnit.V3;

/// <summary>A plain xUnit fact that runs only on an Optimum build.</summary>
public sealed class OptimumFactAttribute : Xunit.FactAttribute
{
    /// <summary>Skips the fact on vanilla Vintage Story.</summary>
    public OptimumFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip ??= OptimumInstall.Loaded == null ? OptimumGate.Vanilla() : null;
    }
}

/// <summary>A plain xUnit theory that runs only on an Optimum build.</summary>
public sealed class OptimumTheoryAttribute : Xunit.TheoryAttribute
{
    /// <summary>Skips the theory on vanilla Vintage Story.</summary>
    public OptimumTheoryAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip ??= OptimumInstall.Loaded == null ? OptimumGate.Vanilla() : null;
    }
}

/// <summary>A plain xUnit fact that runs only on vanilla Vintage Story, not on an Optimum build.</summary>
public sealed class NotOptimumFactAttribute : Xunit.FactAttribute
{
    /// <summary>Skips the fact on an Optimum build.</summary>
    public NotOptimumFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        Skip ??= OptimumInstall.Loaded is { } optimum ? $"Skipped on {OptimumInstall.Describe(optimum)}." : null;
    }
}
