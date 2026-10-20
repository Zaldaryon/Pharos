using Vintagestory.Common;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Mods;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Mods;

/// <summary>A mod with a missing dependency and patches with missing targets surface as errors, not silence.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, Isolation = Isolation.Restart)]
[ServerMods("TestMods/pharosbrokenmod", "TestMods/pharosbrokenpatchmod")]
[AllowBootDiagnostic("pharosbrokenmod", Required = true)]
[AllowBootDiagnostic("pharosbrokenpatchmod:patches/missing.json", Required = true)]
[AllowBootDiagnostic("pharosbrokenpatchmod:patches/error.json", Required = true)]
public class ModLoadingErrorsTests : ServerScenarioBase
{
    [ServerScenario]
    public void ErroredModAndMissingDependency_AreReported()
    {
        LoadedMod broken = Assert.IsType<LoadedMod>(Server.Mods.Mod("pharosbrokenmod"));
        Assert.Equal(ModStatus.Errored, broken.Status);
        Assert.Equal(ModError.Dependency, broken.Error);
        Assert.Contains(broken.MissingDependencies, dependency =>
            dependency.StartsWith("pharosmissingdependency", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(Server.Mods.MissingDependencies, dependency =>
            dependency.StartsWith("pharosmissingdependency", StringComparison.OrdinalIgnoreCase));

        PharosAssertException error = Assert.Throws<PharosAssertException>(() => PharosAssert.NoModErrors(Server));
        Assert.Contains("pharosbrokenmod", error.Message);
    }

    [ServerScenario]
    public void BrokenPatches_ReportNotFoundAndError()
    {
        IReadOnlyList<PatchResult> patches = Server.Mods.PatchesOf("pharosbrokenpatchmod");
        Assert.Contains(patches, patch => patch.Outcome == PatchOutcome.NotFound
            && patch.Target == "pharosbrokenpatchmod:config/missing.json");
        Assert.Contains(patches, patch => patch.Outcome == PatchOutcome.Error
            && patch.Target == "pharosbrokenpatchmod:config/base.json");

        PharosAssertException error = Assert.Throws<PharosAssertException>(
            () => PharosAssert.AllPatchesApplied(Server, "pharosbrokenpatchmod"));
        Assert.Contains("did not apply", error.Message);
    }
}
