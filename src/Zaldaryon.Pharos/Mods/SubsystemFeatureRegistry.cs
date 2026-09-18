namespace Zaldaryon.Pharos.Mods;

/// <summary>
/// Registry for tracking enabled/disabled subsystem features.
/// Used to simulate Optimum detecting conflicting mods and disabling overlapping features.
/// </summary>
public sealed class SubsystemFeatureRegistry
{
    private readonly Dictionary<string, bool> _features = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _disableReasons = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Registers a subsystem feature with its enabled state.
    /// </summary>
    /// <param name="name">Feature/subsystem name (case-insensitive).</param>
    /// <param name="enabled">Whether the feature is enabled.</param>
    /// <param name="disableReason">Optional reason if disabled (for diagnostics).</param>
    public void RegisterFeature(string name, bool enabled, string? disableReason = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _features[name] = enabled;

        if (!enabled && !string.IsNullOrWhiteSpace(disableReason))
        {
            _disableReasons[name] = disableReason;
        }
        else
        {
            _disableReasons.Remove(name);
        }
    }

    /// <summary>
    /// Disables a feature due to a conflicting mod.
    /// </summary>
    /// <param name="name">Feature name to disable.</param>
    /// <param name="conflictingModId">The mod ID that caused the conflict.</param>
    public void DisableForConflict(string name, string conflictingModId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(conflictingModId);

        _features[name] = false;
        _disableReasons[name] = $"Disabled due to conflict with '{conflictingModId}'";
    }

    /// <summary>
    /// Enables a feature (clears any disable reason).
    /// </summary>
    /// <param name="name">Feature name to enable.</param>
    public void EnableFeature(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _features[name] = true;
        _disableReasons.Remove(name);
    }

    /// <summary>
    /// Checks whether a feature is enabled.
    /// Returns false if the feature is not registered.
    /// </summary>
    /// <param name="name">Feature name to check (case-insensitive).</param>
    /// <returns>True if the feature is registered and enabled.</returns>
    public bool IsFeatureEnabled(string name)
    {
        return _features.TryGetValue(name, out bool enabled) && enabled;
    }

    /// <summary>
    /// Checks whether a feature is registered (regardless of state).
    /// </summary>
    /// <param name="name">Feature name to check.</param>
    /// <returns>True if the feature is registered.</returns>
    public bool IsFeatureRegistered(string name)
    {
        return _features.ContainsKey(name);
    }

    /// <summary>
    /// Gets the disable reason for a feature, if any.
    /// </summary>
    /// <param name="name">Feature name.</param>
    /// <returns>The disable reason, or null if enabled/not set.</returns>
    public string? GetDisableReason(string name)
    {
        return _disableReasons.TryGetValue(name, out string? reason) ? reason : null;
    }

    /// <summary>
    /// Gets all registered feature names.
    /// </summary>
    public IReadOnlyCollection<string> RegisteredFeatures => _features.Keys;

    /// <summary>
    /// Gets all enabled feature names.
    /// </summary>
    public IReadOnlyList<string> EnabledFeatures =>
        _features.Where(kvp => kvp.Value).Select(kvp => kvp.Key).ToList();

    /// <summary>
    /// Gets all disabled feature names.
    /// </summary>
    public IReadOnlyList<string> DisabledFeatures =>
        _features.Where(kvp => !kvp.Value).Select(kvp => kvp.Key).ToList();

    /// <summary>
    /// Number of registered features.
    /// </summary>
    public int Count => _features.Count;

    /// <summary>
    /// Number of enabled features.
    /// </summary>
    public int EnabledCount => _features.Count(kvp => kvp.Value);

    /// <summary>
    /// Number of disabled features.
    /// </summary>
    public int DisabledCount => _features.Count(kvp => !kvp.Value);

    /// <summary>
    /// Clears all registered features.
    /// </summary>
    public void Clear()
    {
        _features.Clear();
        _disableReasons.Clear();
    }
}
