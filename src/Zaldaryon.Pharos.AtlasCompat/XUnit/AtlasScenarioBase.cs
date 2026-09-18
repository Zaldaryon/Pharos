using Zaldaryon.Pharos.XUnit;

namespace Atlas.XUnit;

/// <summary>
/// Compatibility shim for Atlas.XUnit.AtlasScenarioBase.
/// Inherits from <see cref="ServerScenarioBase"/> in Pharos.
/// </summary>
/// <remarks>
/// <para>
/// This base class provides source compatibility for existing Atlas test suites.
/// For new code, inherit from <see cref="ServerScenarioBase"/> directly.
/// </para>
/// <para>
/// Atlas naming conventions are preserved: AtlasScenarioBase maps to ServerScenarioBase.
/// </para>
/// </remarks>
[Obsolete("Inherit from ServerScenarioBase in Zaldaryon.Pharos.XUnit instead. This shim exists only for migration.")]
public abstract class AtlasScenarioBase : ServerScenarioBase
{
}
