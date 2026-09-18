namespace Zaldaryon.Pharos.Mods;

/// <summary>
/// Immutable snapshot of mod compatibility state for ecosystem testing.
/// Captures active mock mods, disabled subsystems, and conflict status.
/// </summary>
public sealed record ModCompatibilitySnapshot
{
    /// <summary>List of mod IDs currently simulated as active (e.g., "komet", "optitime", "tungsten", "synergy").</summary>
    public IReadOnlyList<string> ActiveMockMods { get; init; } = [];

    /// <summary>List of subsystem names that have been disabled due to conflicts.</summary>
    public IReadOnlyList<string> DisabledSubsystems { get; init; } = [];

    /// <summary>List of diagnostic warnings generated during conflict detection.</summary>
    public IReadOnlyList<string> DiagnosticWarnings { get; init; } = [];

    /// <summary>Whether any conflicts were detected between mods and subsystems.</summary>
    public bool HasConflicts { get; init; }

    /// <summary>Number of active mock mods.</summary>
    public int ActiveModCount => ActiveMockMods.Count;

    /// <summary>Number of disabled subsystems.</summary>
    public int DisabledSubsystemCount => DisabledSubsystems.Count;

    /// <summary>True if no conflicts exist and all subsystems are operational.</summary>
    public bool IsClean => !HasConflicts && DisabledSubsystems.Count == 0;

    /// <summary>Empty snapshot sentinel for clean/uninitialized state.</summary>
    public static readonly ModCompatibilitySnapshot Empty = new()
    {
        HasConflicts = false,
    };

    /// <summary>
    /// Creates a synthetic snapshot for testing without client context.
    /// </summary>
    /// <param name="activeMods">List of active mock mod IDs.</param>
    /// <param name="disabledSubsystems">List of disabled subsystem names.</param>
    /// <param name="hasConflicts">Whether conflicts were detected.</param>
    /// <param name="warnings">Diagnostic warnings.</param>
    /// <returns>A valid ModCompatibilitySnapshot for testing.</returns>
    public static ModCompatibilitySnapshot CreateSynthetic(
        IReadOnlyList<string>? activeMods = null,
        IReadOnlyList<string>? disabledSubsystems = null,
        bool hasConflicts = false,
        IReadOnlyList<string>? warnings = null)
    {
        return new ModCompatibilitySnapshot
        {
            ActiveMockMods = activeMods ?? [],
            DisabledSubsystems = disabledSubsystems ?? [],
            HasConflicts = hasConflicts,
            DiagnosticWarnings = warnings ?? [],
        };
    }
}
