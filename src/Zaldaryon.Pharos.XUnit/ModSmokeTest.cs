using Zaldaryon.Pharos.Scenarios;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// A ready-made smoke test for a mod: boots a server and an engine-mode client with the mods,
/// joins, runs the commands, walks and looks around, and fails on a crash, a disconnect, a
/// timeout, a failed command or an error logged on either side. See <c>docs/smoke-test.md</c>.
/// </summary>
/// <remarks>
/// <para>Inherit it with no body, and name the mods as for any scenario:</para>
/// <code>
/// [ServerMods("../../../../MyMod/bin/Release/mymod.zip")]
/// [StrictBoot]
/// public class Smoke : ModSmokeTest;
/// </code>
/// <para>
/// <see cref="StrictBootAttribute"/> also fails the boot on warnings. The test fails before booting
/// when no mod is named: a smoke test that boots nothing but vanilla would pass for every mod.
/// </para>
/// </remarks>
public abstract class ModSmokeTest : ModSmokeTestBase
{
    /// <summary>
    /// The smoke test: commands, then a walk, a turn, a jump and a look up and down, checking
    /// after each step that the client is still in the world and the server still running.
    /// </summary>
    [ClientServerScenario(TimeoutMs = SmokeTimeoutMs)]
    public new Task ModBootsJoinsAndPlays() => base.ModBootsJoinsAndPlays();
}
