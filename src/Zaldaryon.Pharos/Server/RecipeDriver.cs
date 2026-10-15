using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>The result of matching a crafting grid against the game's grid recipes.</summary>
/// <param name="Recipe">The name of the recipe the game would craft from the grid, or null when no recipe matches.</param>
/// <param name="Output">The code and quantity of the output the game would give, or null when no recipe matches.</param>
/// <param name="Matches">
/// The name of every recipe that matches, once each, in the order the game tries them: shaped recipes first, then shapeless.
/// A recipe with variants matches under one name. More than one name means the grid is ambiguous, and the first is the one the game crafts.
/// </param>
/// <param name="NearMisses">For each recipe that does not match, the first cell where the grid differs from its best placement.</param>
public sealed record GridMatch(
    string? Recipe,
    (string Code, int Quantity)? Output,
    IReadOnlyList<string> Matches,
    IReadOnlyList<GridMiss> NearMisses);

/// <summary>Why a recipe does not match a grid: the cell where the grid first differs from the recipe, and what each side holds.</summary>
/// <param name="Recipe">The name of the recipe.</param>
/// <param name="Row">The grid row of the first difference, from the top.</param>
/// <param name="Column">The grid column of the first difference, from the left.</param>
/// <param name="Expected">The ingredient the recipe wants at that cell, or "nothing" for an empty cell.</param>
/// <param name="Found">The item code the grid holds at that cell, or "nothing" for an empty cell.</param>
public sealed record GridMiss(string Recipe, int Row, int Column, string Expected, string Found);

/// <summary>Matches crafting grids against the game's recipes on the server, and explains a grid that matches nothing.</summary>
public sealed class RecipeDriver
{
    // The game's crafting grid is 3 by 3 (InventoryCraftingGrid).
    private const int GridSize = 3;
    private readonly EmbeddedServerHost _host;

    internal RecipeDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// Matches a 3 by 3 grid of item codes, one item per cell, against the game's grid recipes, as the game does when a player
    /// fills its crafting grid. Nothing is consumed.
    /// </summary>
    /// <param name="player">A connected player. The game's recipe checks read the player, so a player is required.</param>
    /// <param name="grid">The grid, indexed [row, column]. A null cell is empty. The grid may be smaller than 3 by 3.</param>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> or <paramref name="grid"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The player is not connected, or the grid is larger than 3 by 3.</exception>
    /// <exception cref="ArgumentException">A cell names an item or block the game does not have.</exception>
    public GridMatch Match(ServerTestPlayer player, string?[,] grid)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(grid);
        var stacks = new ItemStack?[grid.GetLength(0), grid.GetLength(1)];
        for (int row = 0; row < grid.GetLength(0); row++)
        {
            for (int column = 0; column < grid.GetLength(1); column++)
            {
                string? code = grid[row, column];
                stacks[row, column] = code is null ? null : new ItemStack(ProcessingDriver.CollectibleNamed(ServerApi.World, code));
            }
        }

        return Match(player, stacks);
    }

    /// <summary>
    /// Matches a 3 by 3 grid of item stacks against the game's grid recipes. A stack's size is the number of items in its
    /// cell, so a recipe that needs two of an item needs a stack of two.
    /// </summary>
    /// <param name="player">A connected player. The game's recipe checks read the player, so a player is required.</param>
    /// <param name="grid">The grid, indexed [row, column]. A null cell is empty. The grid may be smaller than 3 by 3.</param>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> or <paramref name="grid"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The player is not connected, or the grid is larger than 3 by 3.</exception>
    public GridMatch Match(ServerTestPlayer player, ItemStack?[,] grid)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(grid);
        if (grid.GetLength(0) > GridSize || grid.GetLength(1) > GridSize)
        {
            throw new InvalidOperationException($"The crafting grid is {GridSize} by {GridSize}; the grid given is {grid.GetLength(0)} by {grid.GetLength(1)}.");
        }

        IPlayer forPlayer = player.Player ?? throw new InvalidOperationException("The player is not connected, so the game cannot check recipes for it.");
        return _host.RunOnGameThread(() => MatchOnGameThread(forPlayer, grid));
    }

    private GridMatch MatchOnGameThread(IPlayer forPlayer, ItemStack?[,] grid)
    {
        IWorldAccessor world = ServerApi.World;
        ItemSlot[] slots = new ItemSlot[GridSize * GridSize];
        for (int i = 0; i < slots.Length; i++)
        {
            int row = i / GridSize;
            int column = i % GridSize;
            ItemStack? stack = row < grid.GetLength(0) && column < grid.GetLength(1) ? grid[row, column]?.Clone() : null;
            slots[i] = new DummySlot(stack);
        }

        List<GridRecipe> candidates = Candidates(world, slots);
        List<GridRecipe> matches = candidates.Where(recipe => !recipe.Shapeless && recipe.Matches(forPlayer, world, slots, GridSize)).ToList();
        matches.AddRange(candidates.Where(recipe => recipe.Shapeless && recipe.Matches(forPlayer, world, slots, GridSize)));

        GridRecipe? chosen = matches.FirstOrDefault();
        (string, int)? output = null;
        if (chosen is not null)
        {
            var outputSlot = new DummySlot();
            chosen.GenerateOutputStack(slots, outputSlot);
            output = (outputSlot.Itemstack!.Collectible.Code.ToString(), outputSlot.Itemstack.StackSize);
        }

        var misses = candidates
            .Where(recipe => !matches.Contains(recipe))
            .Select(recipe => NearMiss(recipe, slots))
            .OfType<GridMiss>()
            .ToList();

        return new GridMatch(
            chosen?.Name?.ToString(),
            output,
            matches.Select(recipe => recipe.Name?.ToString() ?? "?").Distinct().ToList(),
            misses);
    }

    // The enabled recipes that use an item in the grid, in the order the game collects them (InventoryCraftingGrid.FindMatchingRecipe).
    private static List<GridRecipe> Candidates(IWorldAccessor world, ItemSlot[] slots)
    {
        var list = new List<GridRecipe>();
        var byIngredient = world.FastSearchRecipesByIngredient;
        foreach (ItemSlot slot in slots)
        {
            ItemStack? stack = slot.Itemstack;
            if (stack is null || stack.StackSize == 0) continue;
            foreach (KeyValuePair<IRecipeIngredientBase, List<IRecipeBase>> entry in byIngredient)
            {
                if (!entry.Key.SatisfiesAsIngredient(stack, checkStackSize: false)) continue;
                foreach (IRecipeBase recipe in entry.Value)
                {
                    if (recipe is GridRecipe { Enabled: not false } grid && !list.Contains(grid)) list.Add(grid);
                }
            }
        }

        return list;
    }

    // The first cell where the grid differs from the recipe at its best placement, or null for a shapeless recipe, which has no cells.
    private static GridMiss? NearMiss(GridRecipe recipe, ItemSlot[] slots)
    {
        if (recipe.Shapeless || recipe.ResolvedIngredients is null || recipe.Name is null) return null;
        int width = recipe.Width;
        int height = recipe.Height;
        GridMiss? best = null;
        int bestCount = int.MaxValue;
        for (int column = 0; column <= GridSize - width; column++)
        {
            for (int row = 0; row <= GridSize - height; row++)
            {
                int count = 0;
                GridMiss? first = null;
                for (int r = 0; r < GridSize; r++)
                {
                    for (int c = 0; c < GridSize; c++)
                    {
                        bool inside = r >= row && r < row + height && c >= column && c < column + width;
                        var ingredient = inside ? recipe.ResolvedIngredients[(r - row) * width + (c - column)] : null;
                        ItemStack? stack = slots[r * GridSize + c].Itemstack;
                        if (Satisfies(ingredient, stack)) continue;
                        count++;
                        first ??= new GridMiss(recipe.Name.ToString(), r, c, Describe(ingredient), DescribeStack(stack));
                    }
                }

                if (count < bestCount && first is not null)
                {
                    bestCount = count;
                    best = first;
                }
            }
        }

        return best;
    }

    private static bool Satisfies(CraftingRecipeIngredient? ingredient, ItemStack? stack) => (ingredient, stack) switch
    {
        (null, null) => true,
        (null, _) or (_, null) => false,
        _ => ingredient.SatisfiesAsIngredient(stack, checkStackSize: true),
    };

    // The ingredient with its quantity, such as "game:stick x4", so a stack that is too small is visible in the explanation.
    private static string Describe(CraftingRecipeIngredient? ingredient) =>
        ingredient is null ? "nothing" : $"{ingredient.Code?.ToString() ?? ingredient.Name ?? "?"} x{ingredient.Quantity}";

    private static string DescribeStack(ItemStack? stack) =>
        stack is null ? "nothing" : $"{stack.Collectible.Code} x{stack.StackSize}";

    private ICoreServerAPI ServerApi => (ICoreServerAPI)_host.Server.Api;
}
