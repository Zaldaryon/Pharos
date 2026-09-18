namespace Zaldaryon.Pharos.Mods;

/// <summary>
/// Simulates the presence of ecosystem optimization mods (Komet, OptiTime, Tungsten, Synergy)
/// for testing Optimum's conflict detection and subsystem disabling behavior.
/// </summary>
/// <remarks>
/// This simulator enables Pharos scenarios to verify that:
/// - Optimum detects conflicting mods and disables overlapping features cleanly
/// - Harmony patch registration succeeds without duplicate patch exceptions
/// - System reports clear diagnostic warnings when mutual exclusions are triggered
/// </remarks>
public sealed class ModCompatibilitySimulator
{
    /// <summary>Well-known mod ID for Komet optimization mod.</summary>
    public const string KometModId = "komet";

    /// <summary>Well-known mod ID for OptiTime optimization mod.</summary>
    public const string OptiTimeModId = "optitime";

    /// <summary>Well-known mod ID for Tungsten optimization mod.</summary>
    public const string TungstenModId = "tungsten";

    /// <summary>Well-known mod ID for Synergy optimization mod.</summary>
    public const string SynergyModId = "synergy";

    /// <summary>All known ecosystem optimization mod IDs.</summary>
    public static readonly IReadOnlyList<string> KnownOptimizationMods =
        [KometModId, OptiTimeModId, TungstenModId, SynergyModId];

    /// <summary>Known subsystem conflicts: mod ID -> list of subsystems to disable.</summary>
    private static readonly Dictionary<string, string[]> _knownConflicts = new(StringComparer.OrdinalIgnoreCase)
    {
        [KometModId] = ["TickOptimizer", "ChunkCuller"],
        [OptiTimeModId] = ["TickOptimizer", "TimingHooks"],
        [TungstenModId] = ["MeshBatcher", "IndirectRenderer"],
        [SynergyModId] = ["NetworkOptimizer", "PacketBatcher"],
    };

    private readonly HashSet<string> _simulatedMods = new(StringComparer.OrdinalIgnoreCase);
    private readonly SubsystemFeatureRegistry _featureRegistry;
    private readonly List<string> _diagnosticWarnings = [];

    private bool _isEnabled;

    /// <summary>
    /// Creates a new ModCompatibilitySimulator with the specified feature registry.
    /// </summary>
    /// <param name="featureRegistry">The subsystem feature registry to use. If null, creates a new one.</param>
    public ModCompatibilitySimulator(SubsystemFeatureRegistry? featureRegistry = null)
    {
        _featureRegistry = featureRegistry ?? new SubsystemFeatureRegistry();
    }

    /// <summary>Whether the simulator is currently enabled.</summary>
    public bool IsEnabled => _isEnabled;

    /// <summary>The subsystem feature registry used by this simulator.</summary>
    public SubsystemFeatureRegistry FeatureRegistry => _featureRegistry;

    /// <summary>Diagnostic warnings generated during simulation.</summary>
    public IReadOnlyList<string> DiagnosticWarnings => _diagnosticWarnings;

    /// <summary>
    /// Enables the simulator.
    /// </summary>
    public void Enable()
    {
        _isEnabled = true;
    }

    /// <summary>
    /// Disables the simulator and clears all simulated mods.
    /// </summary>
    public void Disable()
    {
        _isEnabled = false;
        _simulatedMods.Clear();
        _diagnosticWarnings.Clear();
    }

    /// <summary>
    /// Simulates the presence of a mod by its ID.
    /// Automatically detects conflicts and disables affected subsystems.
    /// </summary>
    /// <param name="modId">The mod ID to simulate (case-insensitive).</param>
    /// <returns>True if the mod was newly added, false if already simulated.</returns>
    public bool SimulateModPresent(string modId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        if (!_simulatedMods.Add(modId))
        {
            return false; // Already simulated
        }

        // Check for known conflicts and disable affected subsystems
        if (_knownConflicts.TryGetValue(modId, out string[]? conflictingSubsystems))
        {
            foreach (string subsystem in conflictingSubsystems)
            {
                _featureRegistry.DisableForConflict(subsystem, modId);
                _diagnosticWarnings.Add($"WARNING: Subsystem '{subsystem}' disabled due to conflict with mod '{modId}'");
            }
        }

        return true;
    }

    /// <summary>
    /// Removes a simulated mod by its ID.
    /// Note: Does not automatically re-enable subsystems (use FeatureRegistry.EnableFeature manually).
    /// </summary>
    /// <param name="modId">The mod ID to remove.</param>
    /// <returns>True if the mod was removed, false if not simulated.</returns>
    public bool RemoveSimulatedMod(string modId)
    {
        return _simulatedMods.Remove(modId);
    }

    /// <summary>
    /// Checks whether a mod is currently simulated as present.
    /// </summary>
    /// <param name="modId">The mod ID to check (case-insensitive).</param>
    /// <returns>True if the mod is simulated.</returns>
    public bool IsModSimulated(string modId)
    {
        return _simulatedMods.Contains(modId);
    }

    /// <summary>
    /// Gets the list of currently simulated mod IDs.
    /// </summary>
    public IReadOnlyList<string> SimulatedMods => _simulatedMods.ToList();

    /// <summary>
    /// Checks if any known optimization mods are simulated.
    /// </summary>
    public bool HasOptimizationModConflicts =>
        _simulatedMods.Any(m => KnownOptimizationMods.Contains(m, StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Gets the subsystems that would conflict with a given mod.
    /// </summary>
    /// <param name="modId">The mod ID to check.</param>
    /// <returns>List of conflicting subsystem names, or empty if no conflicts.</returns>
    public IReadOnlyList<string> GetConflictingSubsystems(string modId)
    {
        return _knownConflicts.TryGetValue(modId, out string[]? subsystems)
            ? subsystems
            : [];
    }

    /// <summary>
    /// Creates a snapshot of the current mod compatibility state.
    /// </summary>
    /// <returns>An immutable snapshot of the current state.</returns>
    public ModCompatibilitySnapshot Snapshot()
    {
        if (!_isEnabled)
        {
            return ModCompatibilitySnapshot.Empty;
        }

        return new ModCompatibilitySnapshot
        {
            ActiveMockMods = _simulatedMods.ToList(),
            DisabledSubsystems = _featureRegistry.DisabledFeatures,
            HasConflicts = _featureRegistry.DisabledCount > 0,
            DiagnosticWarnings = _diagnosticWarnings.ToList(),
        };
    }

    /// <summary>
    /// Resets the simulator to initial state without disabling it.
    /// Clears simulated mods and warnings but keeps enabled state.
    /// </summary>
    public void Reset()
    {
        _simulatedMods.Clear();
        _diagnosticWarnings.Clear();
        _featureRegistry.Clear();
    }

    /// <summary>
    /// Simulates all known optimization mods being present.
    /// Useful for testing worst-case conflict scenarios.
    /// </summary>
    public void SimulateAllOptimizationMods()
    {
        foreach (string modId in KnownOptimizationMods)
        {
            SimulateModPresent(modId);
        }
    }
}
