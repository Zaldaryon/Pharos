using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Recipes that failed to load, found by the server for one domain.</summary>
[ServerMods("TestMods/pharosrecipemod")]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class RecipeLoadTests : ServerScenarioBase
{
    private const string Domain = "pharosrecipemod";

    [ServerScenario]
    public void Unresolved_NamesTheRecipeWithAnIngredientThatDoesNotExist()
    {
        UnresolvedRecipe broken = Host!.Recipes.Unresolved(Domain).Single(r => r.Recipe.Contains("broken.json", StringComparison.Ordinal));

        Assert.Contains("pharosrecipemod:missing-stuff", broken.Reason, StringComparison.Ordinal);
    }

    [ServerScenario]
    public void Unresolved_NamesTheWildcardThatMatchesNothing()
    {
        UnresolvedRecipe wildcard = Host!.Recipes.Unresolved(Domain).Single(r => r.Recipe.Contains("wildcard.json", StringComparison.Ordinal));

        Assert.Contains("matches no item or block", wildcard.Reason, StringComparison.Ordinal);
    }

    [ServerScenario]
    public void Unresolved_ParsesMultilineIngredientsAndEveryRecipeCategory()
    {
        UnresolvedRecipe[] unresolved = Host!.Recipes.Unresolved(Domain).ToArray();

        string report = string.Join(Environment.NewLine, unresolved.Select(recipe => $"{recipe.Recipe}: {recipe.Reason}"));
        Assert.True(unresolved.Any(recipe => recipe.Recipe.EndsWith("multiline.json", StringComparison.Ordinal)
            && recipe.Reason.Contains("matches no item or block", StringComparison.Ordinal)), report);
        Assert.True(unresolved.Any(recipe => recipe.Recipe.EndsWith("multiline.json", StringComparison.Ordinal)
            && recipe.Reason.Contains("game:clay-*", StringComparison.Ordinal)), report);
        foreach (string file in new[] { "bad-knapping.json", "bad-clayforming.json", "bad-smithing.json", "bad-alloy.json", "bad-cooking.json", "bad-barrel.json" })
        {
            Assert.True(unresolved.Any(recipe => recipe.Recipe.EndsWith(file, StringComparison.Ordinal)
                && recipe.Reason.Contains("matches no item or block", StringComparison.Ordinal)), report);
        }
    }

    [ServerScenario]
    public void Unresolved_LeavesTheRecipeThatLoads()
    {
        Assert.DoesNotContain(Host!.Recipes.Unresolved(Domain), r => r.Recipe.Contains("good.json", StringComparison.Ordinal));
    }

    [ServerScenario]
    public void RecipesLoaded_FailsAndNamesTheBrokenFile()
    {
        PharosAssertException error = Assert.Throws<PharosAssertException>(() => PharosAssert.RecipesLoaded(Host!.Recipes.Unresolved(Domain), Domain));

        Assert.Contains("broken.json", error.Message, StringComparison.Ordinal);
        Assert.Contains("wildcard.json", error.Message, StringComparison.Ordinal);
    }

    // The game's own 1.22.7 data has three grid recipes whose wildcard names no item or block: the game registers neither
    // recipe, so they can never be crafted. The check reports them, so the game is not expected to pass it.
    [ServerScenario]
    public void Unresolved_FindsTheThreeGameRecipesThatCanNeverBeCrafted()
    {
        string[] found = Host!.Recipes.Unresolved("game").Select(r => $"{Path.GetFileName(r.Recipe)}: {r.Reason}").OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[]
            {
                "punchset.json: the wildcard punchset-letters-cased-* matches no item or block",
                "punchset.json: the wildcard punchset-letters-loose-* matches no item or block",
                "torch.json: the wildcard torch-crude-*-up matches no item or block",
            },
            found);
    }
}
