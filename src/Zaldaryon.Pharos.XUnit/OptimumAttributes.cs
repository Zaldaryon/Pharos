using Xunit;
using Zaldaryon.Pharos.Bootstrap;

namespace Zaldaryon.Pharos.XUnit;

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
