using Zaldaryon.Pharos.XUnit.V3;

namespace ExampleMod.Tests.V3;

/// <summary>
/// Boots a server and an engine-mode client with the mod, joins, walks and looks around, and fails
/// on a crash, a disconnect, a timeout or an error logged on either side. See docs/smoke-test.md.
/// </summary>
public sealed class Smoke : ModSmokeTest;
