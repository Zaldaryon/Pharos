using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Matching crafting grids against the game's grid recipes on the server.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class RecipeDriverTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Match_FindsTheFirestarterFromDryGrassAndSticks()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        GridMatch match = Host!.Recipes.Match(player, new string?[,]
        {
            { "game:drygrass", "game:stick", null },
            { "game:stick", null, null },
            { null, null, null },
        });

        Assert.Contains("firestarter", match.Recipe, StringComparison.Ordinal);
        Assert.Equal(("game:firestarter", 1), match.Output);
        Assert.Single(match.Matches);
        Assert.DoesNotContain(match.NearMisses, miss => miss.Recipe == match.Recipe);
    }

    [ServerScenario]
    public async Task Match_ExplainsTheCellWhereTheGridDiffers()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        // The firestarter needs a stick below the dry grass. This grid has the stick beside it instead, so the game
        // finds nothing, and the explanation points at the empty cell under the grass.
        GridMatch match = Host!.Recipes.Match(player, new string?[,]
        {
            { "game:drygrass", "game:stick", null },
            { null, "game:stick", null },
            { null, null, null },
        });

        Assert.Null(match.Recipe);
        Assert.Null(match.Output);
        GridMiss miss = Assert.Single(match.NearMisses, m => m.Recipe.Contains("firestarter", StringComparison.Ordinal));
        Assert.Equal((1, 0), (miss.Row, miss.Column));
        Assert.Equal("game:stick x1", miss.Expected);
        Assert.Equal("nothing", miss.Found);
    }

    [ServerScenario]
    public async Task Match_NamesTheStackThatIsSmallerThanTheRecipeNeeds()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        // The crude bellows wants four sticks in each stick cell. A single stick in those cells is not enough.
        GridMatch match = Host!.Recipes.Match(player, new ItemStack?[,]
        {
            { null, new ItemStack(Api!.World.GetItem(new AssetLocation("game:stick"))), null },
            { new ItemStack(Api.World.GetItem(new AssetLocation("game:rope"))), null, null },
            { null, new ItemStack(Api.World.GetItem(new AssetLocation("game:stick"))), null },
        });

        Assert.Null(match.Recipe);
        GridMiss miss = match.NearMisses.First(m => m.Recipe.Contains("bellows", StringComparison.Ordinal));
        Assert.Equal((0, 1), (miss.Row, miss.Column));
        Assert.Equal("game:stick x4", miss.Expected);
        Assert.Equal("game:stick x1", miss.Found);
    }

    [ServerScenario]
    public async Task Match_GivesTheOutputQuantityTheRecipeSays()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        // A knife and a piece of linen, in either order, make four clean bandages.
        GridMatch match = Host!.Recipes.Match(player, new string?[,]
        {
            { "game:knife-generic-flint", "game:linen-normal-up", null },
            { null, null, null },
            { null, null, null },
        });

        Assert.Equal("game:bandage-clean", match.Output?.Code);
        Assert.Equal(4, match.Output?.Quantity);
    }

    [ServerScenario]
    public async Task Match_ListsEveryRecipeTheGridFitsAndCraftsTheFirst()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        // A saw above two logs fits the bow stave recipe and the plank recipe. The game crafts the first one it tries.
        GridMatch match = Host!.Recipes.Match(player, new string?[,]
        {
            { "game:saw-bismuthbronze", null, null },
            { "game:log-placed-oak-ud", null, null },
            { "game:log-placed-oak-ud", null, null },
        });

        Assert.Contains(match.Matches, name => name.Contains("bowstave", StringComparison.Ordinal));
        Assert.Contains(match.Matches, name => name.Contains("plank", StringComparison.Ordinal));
        Assert.Equal(match.Matches[0], match.Recipe);
        Assert.Equal("game:bowstave-long-raw", match.Output?.Code);
    }

    [ServerScenario]
    public async Task Match_CountsEachCellsStackAgainstTheRecipe()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        ItemStack Reeds(int size) => new(Api!.World.GetItem(new AssetLocation("game:cattailtops")), size);

        // The basket needs two cattail tops in each of its five cells. Two in each cell crafts it.
        GridMatch enough = Host!.Recipes.Match(player, new ItemStack?[,]
        {
            { Reeds(2), null, Reeds(2) },
            { Reeds(2), Reeds(2), Reeds(2) },
            { null, null, null },
        });
        Assert.Contains("basket", enough.Recipe, StringComparison.Ordinal);
        Assert.Equal("game:basket-normal-reed", enough.Output?.Code);

        // One in each cell is not enough, and the explanation names the first cell that is short.
        GridMatch short_ = Host.Recipes.Match(player, new ItemStack?[,]
        {
            { Reeds(1), null, Reeds(1) },
            { Reeds(1), Reeds(1), Reeds(1) },
            { null, null, null },
        });
        Assert.Null(short_.Recipe);
        // Two basket recipes take cattail tops, so the near misses are not in a fixed order: the one for the matched recipe is named by its name.
        GridMiss miss = Assert.Single(short_.NearMisses, m => m.Recipe == enough.Recipe);
        Assert.Equal("game:cattailtops x2", miss.Expected);
        Assert.Equal("game:cattailtops x1", miss.Found);
    }

    [ServerScenario]
    public async Task Match_NeedsAConnectedPlayer()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.Disconnect();

        Assert.Throws<InvalidOperationException>(() => Host!.Recipes.Match(player, new string?[,] { { null, null, null }, { null, null, null }, { null, null, null } }));
    }

    [ServerScenario]
    public async Task Match_RejectsAGridLargerThanTheGame()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        Assert.Throws<InvalidOperationException>(() => Host!.Recipes.Match(player, new ItemStack?[4, 1]));
    }

    [ServerScenario]
    public async Task Match_NamesAnItemTheGameDoesNotHave()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        ArgumentException error = Assert.Throws<ArgumentException>(() => Host!.Recipes.Match(player, new string?[,] { { "game:no-such-item", null, null }, { null, null, null }, { null, null, null } }));
        Assert.Contains("game:no-such-item", error.Message, StringComparison.Ordinal);
    }
}
