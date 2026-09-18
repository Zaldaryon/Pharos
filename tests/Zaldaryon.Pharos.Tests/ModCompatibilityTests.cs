using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Mods;

namespace Zaldaryon.Pharos.Tests;

/// <summary>
/// Unit tests for <see cref="ModCompatibilitySimulator"/>, <see cref="SubsystemFeatureRegistry"/>,
/// <see cref="ModCompatibilitySnapshot"/>, and mod compatibility <see cref="PharosAssert"/> methods.
/// These tests use pure logic to validate ecosystem mod conflict detection and subsystem disabling.
/// </summary>
[Collection("ModCompatibility")]
public sealed class ModCompatibilityTests : IDisposable
{
    private readonly SubsystemFeatureRegistry _registry = new();
    private readonly ModCompatibilitySimulator _simulator;

    public ModCompatibilityTests()
    {
        _simulator = new ModCompatibilitySimulator(_registry);
    }

    public void Dispose()
    {
        _simulator.Disable();
    }

    // -------------------------------------------------------------------------
    // SubsystemFeatureRegistry tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Registry_StartsEmpty()
    {
        Assert.Equal(0, _registry.Count);
        Assert.Empty(_registry.RegisteredFeatures);
    }

    [Fact]
    public void RegisterFeature_AddsFeatureToRegistry()
    {
        _registry.RegisterFeature("TestFeature", true);

        Assert.True(_registry.IsFeatureRegistered("TestFeature"));
        Assert.True(_registry.IsFeatureEnabled("TestFeature"));
        Assert.Equal(1, _registry.Count);
    }

    [Fact]
    public void RegisterFeature_DisabledFeature_TracksState()
    {
        _registry.RegisterFeature("TestFeature", false, "Manual disable");

        Assert.True(_registry.IsFeatureRegistered("TestFeature"));
        Assert.False(_registry.IsFeatureEnabled("TestFeature"));
        Assert.Equal("Manual disable", _registry.GetDisableReason("TestFeature"));
    }

    [Fact]
    public void DisableForConflict_DisablesAndRecordsReason()
    {
        _registry.RegisterFeature("MeshBatcher", true);
        _registry.DisableForConflict("MeshBatcher", "tungsten");

        Assert.False(_registry.IsFeatureEnabled("MeshBatcher"));
        Assert.Contains("tungsten", _registry.GetDisableReason("MeshBatcher"));
    }

    [Fact]
    public void EnableFeature_ClearsDisableReason()
    {
        _registry.DisableForConflict("TestFeature", "testmod");
        _registry.EnableFeature("TestFeature");

        Assert.True(_registry.IsFeatureEnabled("TestFeature"));
        Assert.Null(_registry.GetDisableReason("TestFeature"));
    }

    [Fact]
    public void IsFeatureEnabled_CaseInsensitive()
    {
        _registry.RegisterFeature("TESTFEATURE", true);

        Assert.True(_registry.IsFeatureEnabled("testfeature"));
        Assert.True(_registry.IsFeatureEnabled("TestFeature"));
        Assert.True(_registry.IsFeatureEnabled("TESTFEATURE"));
    }

    [Fact]
    public void EnabledFeatures_ReturnsOnlyEnabled()
    {
        _registry.RegisterFeature("Enabled1", true);
        _registry.RegisterFeature("Disabled1", false);
        _registry.RegisterFeature("Enabled2", true);

        IReadOnlyList<string> enabled = _registry.EnabledFeatures;

        Assert.Equal(2, enabled.Count);
        Assert.Contains("Enabled1", enabled);
        Assert.Contains("Enabled2", enabled);
    }

    [Fact]
    public void DisabledFeatures_ReturnsOnlyDisabled()
    {
        _registry.RegisterFeature("Enabled1", true);
        _registry.RegisterFeature("Disabled1", false);
        _registry.RegisterFeature("Disabled2", false);

        IReadOnlyList<string> disabled = _registry.DisabledFeatures;

        Assert.Equal(2, disabled.Count);
        Assert.Contains("Disabled1", disabled);
        Assert.Contains("Disabled2", disabled);
    }

    [Fact]
    public void Clear_RemovesAllFeatures()
    {
        _registry.RegisterFeature("Feature1", true);
        _registry.RegisterFeature("Feature2", false);
        _registry.Clear();

        Assert.Equal(0, _registry.Count);
        Assert.Empty(_registry.RegisteredFeatures);
    }

    // -------------------------------------------------------------------------
    // ModCompatibilitySimulator tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Simulator_IsDisabledByDefault()
    {
        Assert.False(_simulator.IsEnabled);
    }

    [Fact]
    public void Enable_SetsIsEnabledTrue()
    {
        _simulator.Enable();

        Assert.True(_simulator.IsEnabled);
    }

    [Fact]
    public void Disable_AfterEnable_SetsIsEnabledFalse()
    {
        _simulator.Enable();
        _simulator.Disable();

        Assert.False(_simulator.IsEnabled);
    }

    [Fact]
    public void SimulateModPresent_AddsModToSimulated()
    {
        _simulator.Enable();
        bool added = _simulator.SimulateModPresent("komet");

        Assert.True(added);
        Assert.True(_simulator.IsModSimulated("komet"));
        Assert.Single(_simulator.SimulatedMods);
    }

    [Fact]
    public void SimulateModPresent_DuplicateMod_ReturnsFalse()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("komet");
        bool addedAgain = _simulator.SimulateModPresent("komet");

        Assert.False(addedAgain);
        Assert.Single(_simulator.SimulatedMods);
    }

    [Fact]
    public void SimulateModPresent_KnownMod_DisablesConflictingSubsystems()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("komet");

        // Komet conflicts with TickOptimizer and ChunkCuller
        Assert.False(_registry.IsFeatureEnabled("TickOptimizer"));
        Assert.False(_registry.IsFeatureEnabled("ChunkCuller"));
    }

    [Fact]
    public void SimulateModPresent_KnownMod_GeneratesDiagnosticWarnings()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("tungsten");

        Assert.NotEmpty(_simulator.DiagnosticWarnings);
        Assert.Contains(_simulator.DiagnosticWarnings, w => w.Contains("MeshBatcher"));
        Assert.Contains(_simulator.DiagnosticWarnings, w => w.Contains("tungsten"));
    }

    [Fact]
    public void SimulateModPresent_UnknownMod_NoConflicts()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("unknownmod");

        Assert.True(_simulator.IsModSimulated("unknownmod"));
        Assert.Empty(_registry.DisabledFeatures);
        Assert.Empty(_simulator.DiagnosticWarnings);
    }

    [Fact]
    public void IsModSimulated_CaseInsensitive()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("KOMET");

        Assert.True(_simulator.IsModSimulated("komet"));
        Assert.True(_simulator.IsModSimulated("KOMET"));
        Assert.True(_simulator.IsModSimulated("Komet"));
    }

    [Fact]
    public void RemoveSimulatedMod_RemovesMod()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("komet");
        bool removed = _simulator.RemoveSimulatedMod("komet");

        Assert.True(removed);
        Assert.False(_simulator.IsModSimulated("komet"));
        Assert.Empty(_simulator.SimulatedMods);
    }

    [Fact]
    public void GetConflictingSubsystems_ReturnsKnownConflicts()
    {
        IReadOnlyList<string> conflicts = _simulator.GetConflictingSubsystems("optitime");

        Assert.Contains("TickOptimizer", conflicts);
        Assert.Contains("TimingHooks", conflicts);
    }

    [Fact]
    public void GetConflictingSubsystems_UnknownMod_ReturnsEmpty()
    {
        IReadOnlyList<string> conflicts = _simulator.GetConflictingSubsystems("unknownmod");

        Assert.Empty(conflicts);
    }

    [Fact]
    public void SimulateAllOptimizationMods_SimulatesAllKnownMods()
    {
        _simulator.Enable();
        _simulator.SimulateAllOptimizationMods();

        Assert.True(_simulator.IsModSimulated("komet"));
        Assert.True(_simulator.IsModSimulated("optitime"));
        Assert.True(_simulator.IsModSimulated("tungsten"));
        Assert.True(_simulator.IsModSimulated("synergy"));
        Assert.Equal(4, _simulator.SimulatedMods.Count);
    }

    [Fact]
    public void HasOptimizationModConflicts_TrueWhenKnownModPresent()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("synergy");

        Assert.True(_simulator.HasOptimizationModConflicts);
    }

    [Fact]
    public void HasOptimizationModConflicts_FalseWhenNoKnownMods()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("custommodname");

        Assert.False(_simulator.HasOptimizationModConflicts);
    }

    [Fact]
    public void Reset_ClearsModsAndWarningsButKeepsEnabled()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("komet");
        _simulator.Reset();

        Assert.True(_simulator.IsEnabled);
        Assert.Empty(_simulator.SimulatedMods);
        Assert.Empty(_simulator.DiagnosticWarnings);
        Assert.Equal(0, _registry.Count);
    }

    // -------------------------------------------------------------------------
    // ModCompatibilitySnapshot tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Snapshot_WhenDisabled_ReturnsEmpty()
    {
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        Assert.Empty(snapshot.ActiveMockMods);
        Assert.Empty(snapshot.DisabledSubsystems);
        Assert.False(snapshot.HasConflicts);
    }

    [Fact]
    public void Snapshot_WhenEnabled_CapturesState()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("tungsten");
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        Assert.Single(snapshot.ActiveMockMods);
        Assert.Contains("tungsten", snapshot.ActiveMockMods);
        Assert.True(snapshot.HasConflicts);
        Assert.Contains("MeshBatcher", snapshot.DisabledSubsystems);
    }

    [Fact]
    public void Snapshot_CreateSynthetic_CreatesValidSnapshot()
    {
        ModCompatibilitySnapshot snapshot = ModCompatibilitySnapshot.CreateSynthetic(
            activeMods: ["testmod1", "testmod2"],
            disabledSubsystems: ["Subsystem1"],
            hasConflicts: true,
            warnings: ["Warning message"]);

        Assert.Equal(2, snapshot.ActiveModCount);
        Assert.Single(snapshot.DisabledSubsystems);
        Assert.True(snapshot.HasConflicts);
        Assert.Single(snapshot.DiagnosticWarnings);
    }

    [Fact]
    public void Snapshot_IsClean_TrueWhenNoConflicts()
    {
        ModCompatibilitySnapshot snapshot = ModCompatibilitySnapshot.CreateSynthetic(
            activeMods: ["unknownmod"],
            hasConflicts: false);

        Assert.True(snapshot.IsClean);
    }

    [Fact]
    public void Snapshot_IsClean_FalseWhenDisabledSubsystems()
    {
        ModCompatibilitySnapshot snapshot = ModCompatibilitySnapshot.CreateSynthetic(
            disabledSubsystems: ["SomeFeature"],
            hasConflicts: false);

        Assert.False(snapshot.IsClean);
    }

    // -------------------------------------------------------------------------
    // PharosAssert mod compatibility assertions
    // -------------------------------------------------------------------------

    [Fact]
    public void SubsystemDisabled_Passes_WhenDisabled()
    {
        _registry.RegisterFeature("TestFeature", false);

        // Should not throw
        PharosAssert.SubsystemDisabled(_registry, "TestFeature");
    }

    [Fact]
    public void SubsystemDisabled_Throws_WhenEnabled()
    {
        _registry.RegisterFeature("TestFeature", true);

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.SubsystemDisabled(_registry, "TestFeature"));

        Assert.Contains("to be disabled", ex.Message);
        Assert.Contains("currently enabled", ex.Message);
    }

    [Fact]
    public void SubsystemDisabled_Throws_WhenNotRegistered()
    {
        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.SubsystemDisabled(_registry, "UnknownFeature"));

        Assert.Contains("not registered", ex.Message);
    }

    [Fact]
    public void SubsystemEnabled_Passes_WhenEnabled()
    {
        _registry.RegisterFeature("TestFeature", true);

        // Should not throw
        PharosAssert.SubsystemEnabled(_registry, "TestFeature");
    }

    [Fact]
    public void SubsystemEnabled_Throws_WhenDisabled()
    {
        _registry.DisableForConflict("TestFeature", "conflictmod");

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.SubsystemEnabled(_registry, "TestFeature"));

        Assert.Contains("to be enabled", ex.Message);
        Assert.Contains("conflictmod", ex.Message);
    }

    [Fact]
    public void ModCompatibilityClean_Passes_WhenNoConflicts()
    {
        ModCompatibilitySnapshot snapshot = ModCompatibilitySnapshot.CreateSynthetic(hasConflicts: false);

        // Should not throw
        PharosAssert.ModCompatibilityClean(snapshot);
    }

    [Fact]
    public void ModCompatibilityClean_Throws_WhenHasConflicts()
    {
        ModCompatibilitySnapshot snapshot = ModCompatibilitySnapshot.CreateSynthetic(
            activeMods: ["komet"],
            disabledSubsystems: ["TickOptimizer"],
            hasConflicts: true);

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.ModCompatibilityClean(snapshot));

        Assert.Contains("conflicts were detected", ex.Message);
    }

    [Fact]
    public void ModCompatibilityClean_Throws_WhenDisabledSubsystems()
    {
        ModCompatibilitySnapshot snapshot = ModCompatibilitySnapshot.CreateSynthetic(
            disabledSubsystems: ["SomeFeature"],
            hasConflicts: false);

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.ModCompatibilityClean(snapshot));

        Assert.Contains("subsystem(s) are disabled", ex.Message);
    }

    [Fact]
    public void ModDetected_Passes_WhenModPresent()
    {
        ModCompatibilitySnapshot snapshot = ModCompatibilitySnapshot.CreateSynthetic(
            activeMods: ["komet", "synergy"]);

        // Should not throw
        PharosAssert.ModDetected(snapshot, "komet");
        PharosAssert.ModDetected(snapshot, "synergy");
    }

    [Fact]
    public void ModDetected_Throws_WhenModNotPresent()
    {
        ModCompatibilitySnapshot snapshot = ModCompatibilitySnapshot.CreateSynthetic(
            activeMods: ["komet"]);

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.ModDetected(snapshot, "tungsten"));

        Assert.Contains("tungsten", ex.Message);
        Assert.Contains("not in ActiveMockMods", ex.Message);
    }

    [Fact]
    public void ModConflictsExist_Passes_WhenConflictsPresent()
    {
        ModCompatibilitySnapshot snapshot = ModCompatibilitySnapshot.CreateSynthetic(
            disabledSubsystems: ["Feature1", "Feature2"],
            hasConflicts: true);

        // Should not throw
        PharosAssert.ModConflictsExist(snapshot, 2);
    }

    [Fact]
    public void ModConflictsExist_Throws_WhenNoConflicts()
    {
        ModCompatibilitySnapshot snapshot = ModCompatibilitySnapshot.CreateSynthetic(hasConflicts: false);

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.ModConflictsExist(snapshot));

        Assert.Contains("HasConflicts=false", ex.Message);
    }

    [Fact]
    public void ModConflictsExist_Throws_WhenNotEnoughDisabledSubsystems()
    {
        ModCompatibilitySnapshot snapshot = ModCompatibilitySnapshot.CreateSynthetic(
            disabledSubsystems: ["Feature1"],
            hasConflicts: true);

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.ModConflictsExist(snapshot, minDisabledSubsystems: 3));

        Assert.Contains("at least 3", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Integration-style tests (simulator -> snapshot -> assertions)
    // -------------------------------------------------------------------------

    [Fact]
    public void Integration_KometConflict_DisablesCorrectSubsystems()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("komet");

        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Verify using assertions
        PharosAssert.ModDetected(snapshot, "komet");
        PharosAssert.ModConflictsExist(snapshot, 2);
        PharosAssert.SubsystemDisabled(_registry, "TickOptimizer");
        PharosAssert.SubsystemDisabled(_registry, "ChunkCuller");
    }

    [Fact]
    public void Integration_MultipleConflictingMods_AccumulatesDisabledSubsystems()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("komet");    // Disables: TickOptimizer, ChunkCuller
        _simulator.SimulateModPresent("tungsten"); // Disables: MeshBatcher, IndirectRenderer

        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        Assert.Equal(2, snapshot.ActiveModCount);
        Assert.True(snapshot.HasConflicts);
        Assert.Equal(4, snapshot.DisabledSubsystems.Count);
    }

    [Fact]
    public void Integration_NoKnownMods_CleanCompatibility()
    {
        _simulator.Enable();
        _simulator.SimulateModPresent("myunknownmod");

        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Unknown mods don't cause conflicts
        PharosAssert.ModCompatibilityClean(snapshot);
    }
}
