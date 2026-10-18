using System.Runtime.CompilerServices;
using Xunit.v3;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.XUnit.V3;

/// <summary>
/// A plain xUnit fact that runs only when the installed game version is in the range.
/// </summary>
public sealed class GameVersionFactAttribute : Xunit.FactAttribute
{
    /// <summary>Skips the fact unless the installed game version is in range.</summary>
    public GameVersionFactAttribute(
        string range,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        Range = range;
        Skip ??= GameVersionGate.SkipReason(range);
    }

    /// <summary>The versions the test runs on.</summary>
    public string Range { get; }
}

/// <summary>
/// A plain xUnit theory that runs only when the installed game version is in the range.
/// </summary>
public sealed class GameVersionTheoryAttribute : Xunit.TheoryAttribute
{
    /// <summary>Skips the theory unless the installed game version is in range.</summary>
    public GameVersionTheoryAttribute(
        string range,
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        Range = range;
        Skip ??= GameVersionGate.SkipReason(range);
    }

    /// <summary>The versions the test runs on.</summary>
    public string Range { get; }
}
