using Xunit;

namespace Zaldaryon.Pharos.XUnit;

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
