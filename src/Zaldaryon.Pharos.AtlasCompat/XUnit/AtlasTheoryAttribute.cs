using Zaldaryon.Pharos.XUnit;

namespace Atlas.XUnit;

/// <summary>
/// Compatibility shim for Atlas.XUnit.AtlasTheoryAttribute.
/// Provides the same xUnit semantics as <see cref="ServerTheoryAttribute"/> in Pharos.
/// </summary>
/// <remarks>
/// <para>
/// This attribute provides source compatibility for existing Atlas test suites.
/// For new code, use <see cref="ServerTheoryAttribute"/> directly.
/// </para>
/// <para>
/// Atlas naming conventions are preserved: [AtlasTheory] maps to [ServerTheory] semantically.
/// Both inherit from xUnit's TheoryAttribute for Theory-based data-driven test execution.
/// </para>
/// </remarks>
[Obsolete("Use [ServerTheory] from Zaldaryon.Pharos.XUnit instead. This shim exists only for migration.")]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AtlasTheoryAttribute : Xunit.TheoryAttribute
{
    /// <summary>
    /// The default timeout in milliseconds for theory tests (120 seconds).
    /// </summary>
    public const int DefaultTimeoutMs = 120_000;

    /// <summary>
    /// Gets or sets the world isolation mode for this test.
    /// When not set, uses <see cref="WorldIsolation.Rollback"/>.
    /// </summary>
    public WorldIsolation Isolation { get; set; } = WorldIsolation.Rollback;

    /// <summary>
    /// Gets or sets the watchdog timeout in milliseconds.
    /// When not set, uses <see cref="DefaultTimeoutMs"/> (120 seconds).
    /// </summary>
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    /// <summary>
    /// Initializes a new instance of the <see cref="AtlasTheoryAttribute"/> class.
    /// </summary>
    public AtlasTheoryAttribute()
    {
    }
}
