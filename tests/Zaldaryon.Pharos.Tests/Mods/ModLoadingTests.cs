using Vintagestory.Common;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.Mods;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Mods;

/// <summary>The game's mod loading, exposed as mods, systems, run order, patches and compatibility assets.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, Isolation = Isolation.Restart)]
[ServerMods(
    "TestMods/pharosloadmod",
    "TestMods/pharoscompatmod",
    "TestMods/pharosdependentmod")]
public class ModLoadingTests : ServerScenarioBase
{
    [ServerScenario]
    public void Loaded_ReportsStatesDependenciesAndOrder()
    {
        LoadedMod load = Assert.IsType<LoadedMod>(Server.Mods.Mod("pharosloadmod"));
        Assert.Equal(ModStatus.Enabled, load.Status);
        Assert.Null(load.Error);
        Assert.Equal("1.0.0", load.Version);
        Assert.Empty(load.MissingDependencies);

        LoadedMod dependent = Assert.IsType<LoadedMod>(Server.Mods.Mod("pharosdependentmod"));
        Assert.Equal(ModStatus.Enabled, dependent.Status);
        Assert.Contains(dependent.Dependencies, dependency => dependency.ModId == "pharosloadmod");

        List<string> order = Server.Mods.Loaded.Keys.ToList();
        Assert.True(
            order.IndexOf("pharosloadmod") < order.IndexOf("pharosdependentmod"),
            "The dependency was not loaded before the mod that needs it: " + string.Join(", ", order));
        Assert.DoesNotContain("pharosmissingdependency", Server.Mods.MissingDependencies);
    }

    [ServerScenario]
    public void Systems_ReportOrderSidesAndPhases()
    {
        Assert.True(
            Server.Mods.Systems.Count > 0,
            $"systems={Server.Mods.Systems.Count} runlog={Server.Mods.RunLog.Count} phases={Server.Mods.Phases.Count} notes=[{string.Join("; ", Server.Mods.Notes)}]");
        List<LoadedSystem> matches = Server.Mods.Systems.Where(system =>
            system.Key == "pharosloadmod:PharosLoadSystem").ToList();
        Assert.True(
            matches.Count == 1,
            "systems: " + string.Join(", ", Server.Mods.Systems.Select(system => system.Key)));
        LoadedSystem load = matches[0];
        Assert.True(load.Ran);
        Assert.True(load.Order >= 0);
        Assert.Equal(0.20, load.ExecuteOrder, 3);
        Assert.True(load.LoadsOn(Vintagestory.API.Common.EnumAppSide.Server));
        Assert.True(load.LoadsOn(Vintagestory.API.Common.EnumAppSide.Client));
        Assert.True(load.PhaseMs.ContainsKey(ModRunPhase.Start));
        Assert.True(load.PhaseMs[ModRunPhase.Start] >= 0);

        List<LoadedSystem> clientMatches = Server.Mods.Systems.Where(system =>
            system.Key == "pharosloadmod:PharosClientOnlySystem").ToList();
        Assert.True(
            clientMatches.Count == 1,
            "systems: " + string.Join(", ", Server.Mods.Systems.Select(system => system.Key)));
        LoadedSystem clientOnly = clientMatches[0];
        Assert.False(clientOnly.Ran);
        Assert.Equal(-1, clientOnly.Order);
        Assert.True(clientOnly.LoadsOn(Vintagestory.API.Common.EnumAppSide.Client));
        Assert.False(clientOnly.LoadsOn(Vintagestory.API.Common.EnumAppSide.Server));

        PharosAssert.LoadsBefore(
            Server,
            "pharosloadmod:PharosLoadSystem",
            "pharosloadmod:PharosLateSystem",
            ModRunPhase.Start);
        PharosAssert.LoadsBefore(Server, "pharosloadmod:PharosLoadSystem", "pharosloadmod:PharosLateSystem");
        PharosAssert.LoadsBefore(Server, load.Type, "pharosloadmod:PharosLateSystem", ModRunPhase.Start);
        Assert.Contains(Server.Mods.RunLog, run => run.Key == "pharosloadmod:PharosLoadSystem" && run.Phase == ModRunPhase.AssetsLoaded);
    }

    [ServerScenario]
    public void Patches_ReportEveryOutcome()
    {
        IReadOnlyList<PatchResult> patches = Server.Mods.PatchesOf("pharosloadmod");
        Assert.Contains(patches, patch => patch.Outcome == PatchOutcome.Applied && patch.Target == "pharosloadmod:config/base.json");
        Assert.Contains(patches, patch => patch.Outcome == PatchOutcome.Applied && patch.Target == "pharosloadmod:config/second.json");
        Assert.Equal(3, patches.Count(patch =>
            patch.Source.EndsWith("patches/wildcard.json", StringComparison.Ordinal)
            && patch.Outcome == PatchOutcome.Applied));
        Assert.DoesNotContain(patches, patch => patch.Target == "pharosloadmod:config/*");
        Assert.Contains(patches, patch => patch.Outcome == PatchOutcome.Disabled);
        Assert.Contains(patches, patch => patch.Outcome == PatchOutcome.ConditionUnmet);
        Assert.Contains(patches, patch => patch.Outcome == PatchOutcome.DependencyMissing);
        PharosAssert.AllPatchesApplied(Server, "pharosloadmod");
    }

    [ServerScenario]
    public void CompatibilityAssets_ReportAddedAndReplaced()
    {
        CompatibilityAsset replaced = Assert.Single(Server.Mods.CompatibilityAssets.Where(asset =>
            asset.Path == "pharosloadmod:config/base.json"));
        Assert.Equal("pharosloadmod", replaced.TargetMod);
        Assert.Equal("pharoscompatmod", replaced.ProviderMod);
        Assert.True(replaced.Replaced);

        CompatibilityAsset added = Assert.Single(Server.Mods.CompatibilityAssets.Where(asset =>
            asset.Path == "pharosloadmod:config/extra.json"));
        Assert.False(added.Replaced);

        AssetSource source = Server.Assets.Origin("pharosloadmod:config/base.json");
        Assert.Equal("pharosloadmod", source.Domain);
        Assert.Equal("pharoscompatmod", source.ModId);
        Assert.True(source.IsCompatibility);
        Assert.True(source.Patched);

        AssetSource own = Server.Assets.Origin("pharosloadmod:config/second.json");
        Assert.Equal("pharosloadmod", own.Domain);
        Assert.Equal("pharosloadmod", own.ModId);
        Assert.False(own.IsCompatibility);
        Assert.True(own.Patched);

        AssetSource patchedGame = Server.Assets.Origin("game:lang/en.json");
        Assert.Equal("game", patchedGame.Domain);
        Assert.Equal("pharosloadmod", patchedGame.ModId);
        Assert.False(patchedGame.IsCompatibility);
        Assert.True(patchedGame.Patched);
        Assert.Null(Server.Assets.TryOrigin("pharosloadmod:config/never.json"));
    }

    [ServerScenario]
    public void Metrics_PublishLoadingCountsAndPhaseTimes()
    {
        IReadOnlyList<Zaldaryon.Pharos.Metrics.MetricReading> readings = Server.Mods.Metrics();
        Assert.Contains(readings, reading => reading.Name == "mods.loaded" && reading.Value >= 3);
        Assert.Contains(readings, reading => reading.Name == "mods.patches.applied" && reading.Value >= 1);
        Assert.Contains(readings, reading => reading.Name.StartsWith("mods.phase.start.", StringComparison.Ordinal));
    }
}
