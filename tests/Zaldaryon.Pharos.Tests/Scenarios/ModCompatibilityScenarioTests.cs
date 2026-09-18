using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Mods;

namespace Zaldaryon.Pharos.Tests.Scenarios;

/// <summary>
/// Scenario tests for mod compatibility guard behavior (Issue #121).
/// Validates that conflicting ecosystem optimization mods are detected
/// and appropriate subsystems are disabled with diagnostic warnings.
/// Uses ModCompatibilitySimulator for headless-safe testing.
/// </summary>
[Collection("ModCompatibilitySimulator")]
public sealed class ModCompatibilityScenarioTests : IDisposable
{
    private readonly ModCompatibilitySimulator _simulator;

    public ModCompatibilityScenarioTests()
    {
        _simulator = new ModCompatibilitySimulator();
        _simulator.Enable();
    }

    public void Dispose()
    {
        _simulator.Disable();
    }

    // -------------------------------------------------------------------------
    // Komet Mod Detection Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ModCompat_Komet_Detected()
    {
        // Arrange & Act: Simulate Komet mod presence
        _simulator.SimulateModPresent(ModCompatibilitySimulator.KometModId);
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: Komet should be detected
        PharosAssert.ModDetected(snapshot, ModCompatibilitySimulator.KometModId);
        Assert.True(_simulator.IsModSimulated(ModCompatibilitySimulator.KometModId));
    }

    [Fact]
    public void ModCompat_Komet_DisablesConflictingSubsystems()
    {
        // Arrange & Act: Simulate Komet mod
        _simulator.SimulateModPresent(ModCompatibilitySimulator.KometModId);
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: Komet conflicts with TickOptimizer and ChunkCuller
        Assert.True(snapshot.HasConflicts);
        Assert.Contains("TickOptimizer", snapshot.DisabledSubsystems);
        Assert.Contains("ChunkCuller", snapshot.DisabledSubsystems);
        PharosAssert.SubsystemDisabled(_simulator.FeatureRegistry, "TickOptimizer");
        PharosAssert.SubsystemDisabled(_simulator.FeatureRegistry, "ChunkCuller");
    }

    [Fact]
    public void ModCompat_Komet_GeneratesDiagnosticWarnings()
    {
        // Arrange & Act: Simulate Komet mod
        _simulator.SimulateModPresent(ModCompatibilitySimulator.KometModId);

        // Assert: Diagnostic warnings should be present
        Assert.NotEmpty(_simulator.DiagnosticWarnings);
        Assert.True(_simulator.DiagnosticWarnings.Any(w =>
            w.Contains("TickOptimizer", StringComparison.OrdinalIgnoreCase) &&
            w.Contains("komet", StringComparison.OrdinalIgnoreCase)));
    }

    // -------------------------------------------------------------------------
    // OptiTime Mod Detection Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ModCompat_OptiTime_Detected()
    {
        // Arrange & Act: Simulate OptiTime mod presence
        _simulator.SimulateModPresent(ModCompatibilitySimulator.OptiTimeModId);
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: OptiTime should be detected
        PharosAssert.ModDetected(snapshot, ModCompatibilitySimulator.OptiTimeModId);
        Assert.True(_simulator.IsModSimulated(ModCompatibilitySimulator.OptiTimeModId));
    }

    [Fact]
    public void ModCompat_OptiTime_DisablesConflictingSubsystems()
    {
        // Arrange & Act: Simulate OptiTime mod
        _simulator.SimulateModPresent(ModCompatibilitySimulator.OptiTimeModId);
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: OptiTime conflicts with TickOptimizer and TimingHooks
        Assert.True(snapshot.HasConflicts);
        Assert.Contains("TickOptimizer", snapshot.DisabledSubsystems);
        Assert.Contains("TimingHooks", snapshot.DisabledSubsystems);
        PharosAssert.SubsystemDisabled(_simulator.FeatureRegistry, "TickOptimizer");
        PharosAssert.SubsystemDisabled(_simulator.FeatureRegistry, "TimingHooks");
    }

    [Fact]
    public void ModCompat_OptiTime_GeneratesDiagnosticWarnings()
    {
        // Arrange & Act: Simulate OptiTime mod
        _simulator.SimulateModPresent(ModCompatibilitySimulator.OptiTimeModId);

        // Assert: Diagnostic warnings should be present
        Assert.NotEmpty(_simulator.DiagnosticWarnings);
        Assert.True(_simulator.DiagnosticWarnings.Any(w =>
            w.Contains("TickOptimizer", StringComparison.OrdinalIgnoreCase) &&
            w.Contains("optitime", StringComparison.OrdinalIgnoreCase)));
    }

    // -------------------------------------------------------------------------
    // Tungsten Mod Detection Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ModCompat_Tungsten_Detected()
    {
        // Arrange & Act: Simulate Tungsten mod presence
        _simulator.SimulateModPresent(ModCompatibilitySimulator.TungstenModId);
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: Tungsten should be detected
        PharosAssert.ModDetected(snapshot, ModCompatibilitySimulator.TungstenModId);
        Assert.True(_simulator.IsModSimulated(ModCompatibilitySimulator.TungstenModId));
    }

    [Fact]
    public void ModCompat_Tungsten_DisablesConflictingSubsystems()
    {
        // Arrange & Act: Simulate Tungsten mod
        _simulator.SimulateModPresent(ModCompatibilitySimulator.TungstenModId);
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: Tungsten conflicts with MeshBatcher and IndirectRenderer
        Assert.True(snapshot.HasConflicts);
        Assert.Contains("MeshBatcher", snapshot.DisabledSubsystems);
        Assert.Contains("IndirectRenderer", snapshot.DisabledSubsystems);
        PharosAssert.SubsystemDisabled(_simulator.FeatureRegistry, "MeshBatcher");
        PharosAssert.SubsystemDisabled(_simulator.FeatureRegistry, "IndirectRenderer");
    }

    [Fact]
    public void ModCompat_Tungsten_GeneratesDiagnosticWarnings()
    {
        // Arrange & Act: Simulate Tungsten mod
        _simulator.SimulateModPresent(ModCompatibilitySimulator.TungstenModId);

        // Assert: Diagnostic warnings should be present
        Assert.NotEmpty(_simulator.DiagnosticWarnings);
        Assert.True(_simulator.DiagnosticWarnings.Any(w =>
            w.Contains("MeshBatcher", StringComparison.OrdinalIgnoreCase) &&
            w.Contains("tungsten", StringComparison.OrdinalIgnoreCase)));
    }

    // -------------------------------------------------------------------------
    // Synergy Mod Detection Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ModCompat_Synergy_Detected()
    {
        // Arrange & Act: Simulate Synergy mod presence
        _simulator.SimulateModPresent(ModCompatibilitySimulator.SynergyModId);
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: Synergy should be detected
        PharosAssert.ModDetected(snapshot, ModCompatibilitySimulator.SynergyModId);
        Assert.True(_simulator.IsModSimulated(ModCompatibilitySimulator.SynergyModId));
    }

    [Fact]
    public void ModCompat_Synergy_DisablesConflictingSubsystems()
    {
        // Arrange & Act: Simulate Synergy mod
        _simulator.SimulateModPresent(ModCompatibilitySimulator.SynergyModId);
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: Synergy conflicts with NetworkOptimizer and PacketBatcher
        Assert.True(snapshot.HasConflicts);
        Assert.Contains("NetworkOptimizer", snapshot.DisabledSubsystems);
        Assert.Contains("PacketBatcher", snapshot.DisabledSubsystems);
        PharosAssert.SubsystemDisabled(_simulator.FeatureRegistry, "NetworkOptimizer");
        PharosAssert.SubsystemDisabled(_simulator.FeatureRegistry, "PacketBatcher");
    }

    [Fact]
    public void ModCompat_Synergy_GeneratesDiagnosticWarnings()
    {
        // Arrange & Act: Simulate Synergy mod
        _simulator.SimulateModPresent(ModCompatibilitySimulator.SynergyModId);

        // Assert: Diagnostic warnings should be present
        Assert.NotEmpty(_simulator.DiagnosticWarnings);
        Assert.True(_simulator.DiagnosticWarnings.Any(w =>
            w.Contains("NetworkOptimizer", StringComparison.OrdinalIgnoreCase) &&
            w.Contains("synergy", StringComparison.OrdinalIgnoreCase)));
    }

    // -------------------------------------------------------------------------
    // Multiple Mods / Worst-Case Scenario Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ModCompat_AllOptimizationMods_DetectedAndConflict()
    {
        // Arrange & Act: Simulate all optimization mods
        _simulator.SimulateAllOptimizationMods();
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: All mods detected
        PharosAssert.ModDetected(snapshot, ModCompatibilitySimulator.KometModId);
        PharosAssert.ModDetected(snapshot, ModCompatibilitySimulator.OptiTimeModId);
        PharosAssert.ModDetected(snapshot, ModCompatibilitySimulator.TungstenModId);
        PharosAssert.ModDetected(snapshot, ModCompatibilitySimulator.SynergyModId);

        // Assert: Conflicts exist with multiple subsystems disabled
        Assert.True(snapshot.HasConflicts);
        PharosAssert.ModConflictsExist(snapshot, minDisabledSubsystems: 4);
    }

    [Fact]
    public void ModCompat_AllOptimizationMods_AllSubsystemsDisabled()
    {
        // Arrange & Act: Simulate all optimization mods
        _simulator.SimulateAllOptimizationMods();
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: All conflicting subsystems are disabled
        Assert.Contains("TickOptimizer", snapshot.DisabledSubsystems);
        Assert.Contains("ChunkCuller", snapshot.DisabledSubsystems);
        Assert.Contains("TimingHooks", snapshot.DisabledSubsystems);
        Assert.Contains("MeshBatcher", snapshot.DisabledSubsystems);
        Assert.Contains("IndirectRenderer", snapshot.DisabledSubsystems);
        Assert.Contains("NetworkOptimizer", snapshot.DisabledSubsystems);
        Assert.Contains("PacketBatcher", snapshot.DisabledSubsystems);
    }

    // -------------------------------------------------------------------------
    // Clean State Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ModCompat_NoMods_CleanState()
    {
        // Arrange & Act: Take snapshot without simulating any mods
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: Clean state with no conflicts
        PharosAssert.ModCompatibilityClean(snapshot);
        Assert.False(snapshot.HasConflicts);
        Assert.Empty(snapshot.DisabledSubsystems);
        Assert.Empty(snapshot.ActiveMockMods);
    }

    [Fact]
    public void ModCompat_Reset_ClearsState()
    {
        // Arrange: Simulate mods and create conflicts
        _simulator.SimulateModPresent(ModCompatibilitySimulator.KometModId);
        Assert.True(_simulator.Snapshot().HasConflicts);

        // Act: Reset the simulator
        _simulator.Reset();
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: State is clean after reset
        PharosAssert.ModCompatibilityClean(snapshot);
        Assert.Empty(_simulator.SimulatedMods);
        Assert.Empty(_simulator.DiagnosticWarnings);
    }

    // -------------------------------------------------------------------------
    // Edge Cases and Duplicate Detection Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ModCompat_DuplicateModSimulation_Idempotent()
    {
        // Arrange & Act: Simulate the same mod twice
        bool first = _simulator.SimulateModPresent(ModCompatibilitySimulator.KometModId);
        bool second = _simulator.SimulateModPresent(ModCompatibilitySimulator.KometModId);

        // Assert: First returns true, second returns false (already present)
        Assert.True(first);
        Assert.False(second);
        Assert.Single(_simulator.SimulatedMods.Where(m =>
            m.Equals(ModCompatibilitySimulator.KometModId, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void ModCompat_RemoveMod_RemovesFromSimulation()
    {
        // Arrange: Simulate a mod
        _simulator.SimulateModPresent(ModCompatibilitySimulator.KometModId);
        Assert.True(_simulator.IsModSimulated(ModCompatibilitySimulator.KometModId));

        // Act: Remove the mod
        bool removed = _simulator.RemoveSimulatedMod(ModCompatibilitySimulator.KometModId);

        // Assert: Mod is removed
        Assert.True(removed);
        Assert.False(_simulator.IsModSimulated(ModCompatibilitySimulator.KometModId));
    }

    [Fact]
    public void ModCompat_CaseInsensitiveModIds()
    {
        // Arrange & Act: Simulate with different cases
        _simulator.SimulateModPresent("KOMET");
        _simulator.SimulateModPresent("Komet");
        _simulator.SimulateModPresent("komet");

        // Assert: Only one instance (case-insensitive)
        Assert.Single(_simulator.SimulatedMods);
    }

    [Fact]
    public void ModCompat_GetConflictingSubsystems_ReturnsCorrectList()
    {
        // Act: Get conflicting subsystems for each mod
        var kometConflicts = _simulator.GetConflictingSubsystems(ModCompatibilitySimulator.KometModId);
        var optiTimeConflicts = _simulator.GetConflictingSubsystems(ModCompatibilitySimulator.OptiTimeModId);
        var tungstenConflicts = _simulator.GetConflictingSubsystems(ModCompatibilitySimulator.TungstenModId);
        var synergyConflicts = _simulator.GetConflictingSubsystems(ModCompatibilitySimulator.SynergyModId);

        // Assert: Each mod has correct conflict list
        Assert.Contains("TickOptimizer", kometConflicts);
        Assert.Contains("ChunkCuller", kometConflicts);

        Assert.Contains("TickOptimizer", optiTimeConflicts);
        Assert.Contains("TimingHooks", optiTimeConflicts);

        Assert.Contains("MeshBatcher", tungstenConflicts);
        Assert.Contains("IndirectRenderer", tungstenConflicts);

        Assert.Contains("NetworkOptimizer", synergyConflicts);
        Assert.Contains("PacketBatcher", synergyConflicts);
    }

    [Fact]
    public void ModCompat_UnknownMod_NoConflicts()
    {
        // Arrange & Act: Simulate an unknown mod
        _simulator.SimulateModPresent("unknownmod");
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: Mod is detected but no subsystem conflicts
        Assert.Contains("unknownmod", snapshot.ActiveMockMods);
        Assert.False(snapshot.HasConflicts);
        Assert.Empty(snapshot.DisabledSubsystems);
    }

    // -------------------------------------------------------------------------
    // Snapshot Consistency Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ModCompat_Snapshot_IsConsistent()
    {
        // Arrange: Simulate multiple mods
        _simulator.SimulateModPresent(ModCompatibilitySimulator.KometModId);
        _simulator.SimulateModPresent(ModCompatibilitySimulator.SynergyModId);

        // Act: Take multiple snapshots
        ModCompatibilitySnapshot snap1 = _simulator.Snapshot();
        ModCompatibilitySnapshot snap2 = _simulator.Snapshot();

        // Assert: Snapshots are consistent
        Assert.Equal(snap1.ActiveMockMods.Count, snap2.ActiveMockMods.Count);
        Assert.Equal(snap1.DisabledSubsystems.Count, snap2.DisabledSubsystems.Count);
        Assert.Equal(snap1.HasConflicts, snap2.HasConflicts);
    }

    [Fact]
    public void ModCompat_DisabledSnapshot_ReturnsEmpty()
    {
        // Arrange: Disable the simulator
        _simulator.Disable();

        // Act: Take snapshot
        ModCompatibilitySnapshot snapshot = _simulator.Snapshot();

        // Assert: Empty snapshot returned when disabled
        Assert.Equal(ModCompatibilitySnapshot.Empty, snapshot);
        Assert.Empty(snapshot.ActiveMockMods);
        Assert.False(snapshot.HasConflicts);
    }
}
