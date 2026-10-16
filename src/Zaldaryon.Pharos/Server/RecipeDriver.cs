using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
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

/// <summary>A recipe that cannot be used, and the reason, for <see cref="RecipeDriver.Unresolved"/>.</summary>
/// <param name="Recipe">The recipe's name, or the location of the file that holds it.</param>
/// <param name="Reason">What is wrong: an ingredient or output that names no item or block, or a wildcard that matches none.</param>
public sealed record UnresolvedRecipe(string Recipe, string Reason);

/// <summary>Matches crafting grids against the game's recipes on the server, and explains a grid that matches nothing.</summary>
public sealed class RecipeDriver
{
    // The game's crafting grid is 3 by 3 (InventoryCraftingGrid).
    private const int GridSize = 3;
    private const string KnappingSurface = "knappingsurface";
    private const string KnappingEntity = "BlockEntityKnappingSurface";
    private const string ClayFormBlock = "clayform";
    private const string ClayFormEntity = "BlockEntityClayForm";
    private const string ClayItem = "ItemClay";
    private const string RecipeRegistryType = "Vintagestory.GameContent.RecipeRegistrySystem";

    // The packets a knapping surface reads from the client: select a recipe, then remove a voxel (BEKnappingSurface.cs).
    private const int SelectRecipePacket = 1001;
    private const int UserOverPacket = 1002;

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

    /// <summary>
    /// Places a knapping surface at a position the way the game's flint item does: the surface gets the material as its base
    /// and the starting work item, a 10 by 10 block of voxels.
    /// </summary>
    /// <param name="pos">Where the surface goes. The block there is replaced.</param>
    /// <param name="materialCode">The item code of the base material, such as <c>game:flint</c>.</param>
    /// <exception cref="InvalidOperationException">Called on the game thread, or the game has no knapping surface.</exception>
    public void PlaceKnappingSurface(BlockPos pos, string materialCode)
    {
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentNullException.ThrowIfNull(materialCode);
        if (_host.IsOnGameThread) throw new InvalidOperationException("PlaceKnappingSurface waits for the game thread and cannot run on it.");
        _host.RunOnGameThread(() =>
        {
            ICoreServerAPI api = ServerApi;
            var material = new ItemStack(ProcessingDriver.CollectibleNamed(api.World, materialCode), 1);
            Block surface = api.World.GetBlock(new AssetLocation(KnappingSurface))
                ?? throw new InvalidOperationException($"The game has no {KnappingSurface} block.");
            api.World.BlockAccessor.SetBlock(surface.BlockId, pos);
            BlockEntity be = api.World.BlockAccessor.GetBlockEntity(pos)
                ?? throw new InvalidOperationException($"The knapping surface at {pos} has no block entity.");
            be.GetType().GetField("BaseMaterial")!.SetValue(be, material);
            be.GetType().GetMethod("CreateInitialWorkItem")!.Invoke(be, null);
            be.MarkDirty();
        });
    }

    /// <summary>
    /// Completes a knapping or clay forming recipe on the block at a position the way a player does: it selects the recipe, then
    /// changes one voxel per packet, through the block entity's own packets. The block finishes when its voxels match the recipe.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A knapping surface only removes voxels, so a recipe that needs a voxel the work item lacks cannot finish. That case throws and
    /// names the voxel. A clay form adds and removes voxels. Each addition takes from the form's voxel budget, and when the budget runs
    /// out the game takes one clay from the player's hand and adds 25 voxels (ItemClay); this driver does the same, and throws when the
    /// player holds no clay.
    /// </para>
    /// <para>The player must be in reach of the block and must hold the item the game requires: any item for knapping, clay for a clay form.</para>
    /// </remarks>
    /// <param name="player">The player who works the block. The block checks the player's reach and permissions.</param>
    /// <param name="pos">The knapping surface or clay form.</param>
    /// <param name="outputCode">The code of the recipe's output, such as <c>game:knifeblade-flint</c> or <c>game:bowl-blue-raw</c>.</param>
    /// <param name="outputQuantity">
    /// The output's stack size. A clay form has a recipe for one item and one for four under the same output code, so the size
    /// picks one; a knapping surface has one recipe per output and does not use it.
    /// </param>
    /// <exception cref="ArgumentNullException">A parameter is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// The player is not connected or cannot work, the block is neither a knapping surface nor a clay form, no recipe makes
    /// <paramref name="outputCode"/> from the block's material, or the block cannot finish the recipe.
    /// </exception>
    public Task CompleteVoxelRecipeAsync(ServerTestPlayer player, BlockPos pos, string outputCode, int outputQuantity = 1)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentNullException.ThrowIfNull(outputCode);
        IServerPlayer serverPlayer = player.Player ?? throw new InvalidOperationException("The player is not connected, so it cannot work the block.");
        if (_host.IsOnGameThread) throw new InvalidOperationException("CompleteVoxelRecipeAsync waits for the game thread and cannot run on it.");
        _host.RunOnGameThread(() =>
        {
            ICoreServerAPI api = ServerApi;
            BlockEntity be = api.World.BlockAccessor.GetBlockEntity(pos)
                ?? throw new InvalidOperationException($"There is no block entity at {pos}.");
            switch (be.GetType().Name)
            {
                case KnappingEntity:
                    CompleteKnappingOnGameThread(serverPlayer, pos, outputCode);
                    break;
                case ClayFormEntity:
                    CompleteClayformingOnGameThread(serverPlayer, pos, outputCode, outputQuantity);
                    break;
                default:
                    throw new InvalidOperationException($"The block at {pos} is a {be.GetType().Name}, which the game does not form from a recipe.");
            }
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Places a clay form at a position with the clay the game's clay item puts into it: the form gets the clay as its base material,
    /// the starting work item, and 25 voxels of budget.
    /// </summary>
    /// <param name="pos">Where the form goes. The block there is replaced.</param>
    /// <param name="clayCode">The item code of the clay, such as <c>game:clay-blue</c>.</param>
    /// <exception cref="InvalidOperationException">Called on the game thread, or the game has no clay form.</exception>
    public void PlaceClayForm(BlockPos pos, string clayCode)
    {
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentNullException.ThrowIfNull(clayCode);
        if (_host.IsOnGameThread) throw new InvalidOperationException("PlaceClayForm waits for the game thread and cannot run on it.");
        _host.RunOnGameThread(() =>
        {
            ICoreServerAPI api = ServerApi;
            Block form = api.World.GetBlock(new AssetLocation(ClayFormBlock))
                ?? throw new InvalidOperationException($"The game has no {ClayFormBlock} block.");
            api.World.BlockAccessor.SetBlock(form.BlockId, pos);
            BlockEntity be = api.World.BlockAccessor.GetBlockEntity(pos)
                ?? throw new InvalidOperationException($"The clay form at {pos} has no block entity.");
            var clay = new ItemStack(ProcessingDriver.CollectibleNamed(api.World, clayCode), 1);
            be.GetType().GetMethod("PutClay")!.Invoke(be, [new DummySlot(clay)]);
            be.MarkDirty();
        });
    }

    private void CompleteClayformingOnGameThread(IServerPlayer player, BlockPos pos, string outputCode, int outputQuantity)
    {
        ICoreServerAPI api = ServerApi;
        BlockEntity be = api.World.BlockAccessor.GetBlockEntity(pos)!;
        ItemSlot hand = player.InventoryManager.ActiveHotbarSlot;
        if (hand.Itemstack is null || hand.Itemstack.Collectible.GetType().Name != ClayItem)
        {
            throw new InvalidOperationException("The player must hold clay to work a clay form.");
        }

        // The clay tool's mode sets how many voxels one packet changes. Only a single voxel per packet is predictable here.
        int toolMode = hand.Itemstack.Collectible.GetToolMode(hand, player, new BlockSelection { Position = pos });
        if (toolMode != 0)
        {
            throw new InvalidOperationException($"The clay tool is in mode {toolMode}, which changes more than one voxel per packet. Use mode 0.");
        }

        ItemStack? material = (ItemStack?)ProcessingDriver.Member(be, "BaseMaterial");
        object registry = api.ModLoader.GetModSystem(RecipeRegistryType)
            ?? throw new InvalidOperationException($"The game's {RecipeRegistryType} is not loaded.");
        // The game lists the recipe for one item and the recipe for four under the same output code, so the size picks one.
        List<object> candidates = ((IEnumerable)ProcessingDriver.Member(registry, "ClayFormingRecipes")!).Cast<object>()
            .Where(r => OutputCodeOf(r) == outputCode && OutputQuantityOf(r) == outputQuantity && material is not null && IngredientOf(r).SatisfiesAsIngredient(material))
            .ToList();
        if (candidates.Count != 1)
        {
            throw new InvalidOperationException(
                $"{candidates.Count} clay forming recipes make {outputCode} x{outputQuantity} from {material?.Collectible.Code.ToString() ?? "nothing"}; expected one.");
        }

        object recipe = candidates[0];

        be.OnReceivedClientPacket(player, SelectRecipePacket, SerializerUtil.Serialize((int)ProcessingDriver.Member(recipe, "RecipeId")!));
        var required = (bool[,,])ProcessingDriver.Member(recipe, "Voxels")!;

        // Each packet changes the first voxel of the first layer that differs from the recipe. A packet that changes nothing stops the loop.
        const int MaxPackets = 16 * 16 * 16 * 2;
        for (int packet = 0; packet < MaxPackets; packet++)
        {
            if (api.World.BlockAccessor.GetBlock(pos).Code.Path != ClayFormBlock) return;

            // A copy: the block entity changes its own array when a packet lands, so the comparison below needs the old state.
            bool[,,] now = (bool[,,])Voxels3(be).Clone();
            int layer = -1;
            int x = -1;
            int z = -1;
            for (int y = 0; y < 16 && layer < 0; y++)
            {
                for (int i = 0; i < 16 && layer < 0; i++)
                {
                    for (int j = 0; j < 16; j++)
                    {
                        if (now[i, y, j] != required[i, y, j])
                        {
                            layer = y;
                            x = i;
                            z = j;
                            break;
                        }
                    }
                }
            }

            if (layer < 0) break;

            bool wanted = required[x, layer, z];
            if (wanted && AvailableVoxels(be) <= 0)
            {
                // ItemClay: when the form has no voxels left, one clay from the hand becomes 25 more.
                if (hand.Itemstack is null || hand.Itemstack.Collectible.GetType().Name != ClayItem)
                {
                    throw new InvalidOperationException($"The clay form at {pos} needs more clay to add the voxel ({x}, {layer}, {z}); the player holds none.");
                }

                hand.TakeOut(1);
                hand.MarkDirty();
                SetAvailableVoxels(be, AvailableVoxels(be) + 25);
            }

            be.OnReceivedClientPacket(player, UserOverPacket, UserOverData(x, layer, z, mouseMode: !wanted));
            if (Voxels3(be)[x, layer, z] == now[x, layer, z] && api.World.BlockAccessor.GetBlock(pos).Code.Path == ClayFormBlock)
            {
                throw new InvalidOperationException($"The clay form at {pos} did not change the voxel ({x}, {layer}, {z}) that {outputCode} needs.");
            }
        }

        if (api.World.BlockAccessor.GetBlock(pos).Code.Path == ClayFormBlock)
        {
            throw new InvalidOperationException($"The clay form at {pos} did not finish {outputCode}.");
        }
    }

    private static bool[,,] Voxels3(BlockEntity be) => (bool[,,])ProcessingDriver.Member(be, "Voxels")!;

    private static int AvailableVoxels(BlockEntity be) => (int)ProcessingDriver.Member(be, "AvailableVoxels")!;

    private static void SetAvailableVoxels(BlockEntity be, int value) => be.GetType().GetField("AvailableVoxels")!.SetValue(be, value);

    private void CompleteKnappingOnGameThread(IServerPlayer player, BlockPos pos, string outputCode)
    {
        ICoreServerAPI api = ServerApi;
        BlockEntity be = api.World.BlockAccessor.GetBlockEntity(pos)
            ?? throw new InvalidOperationException($"There is no block entity at {pos}.");
        if (be.GetType().Name != KnappingEntity)
        {
            throw new InvalidOperationException($"The block at {pos} is a {be.GetType().Name}, not a knapping surface.");
        }

        if (player.InventoryManager.ActiveHotbarSlot.Itemstack is null)
        {
            throw new InvalidOperationException("The player must hold an item to knap.");
        }

        ItemStack? material = (ItemStack?)ProcessingDriver.Member(be, "BaseMaterial");
        object registry = api.ModLoader.GetModSystem(RecipeRegistryType)
            ?? throw new InvalidOperationException($"The game's {RecipeRegistryType} is not loaded.");
        object recipe = ((IEnumerable)ProcessingDriver.Member(registry, "KnappingRecipes")!).Cast<object>()
            .FirstOrDefault(r => OutputCodeOf(r) == outputCode && material is not null && IngredientOf(r).SatisfiesAsIngredient(material))
            ?? throw new InvalidOperationException($"No knapping recipe makes {outputCode} from {material?.Collectible.Code.ToString() ?? "nothing"}.");

        // The game's packets: select the recipe, then remove one voxel per packet (BEKnappingSurface.OnReceivedClientPacket).
        be.OnReceivedClientPacket(player, SelectRecipePacket, SerializerUtil.Serialize((int)ProcessingDriver.Member(recipe, "RecipeId")!));
        var required = (bool[,,])ProcessingDriver.Member(recipe, "Voxels")!;

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                if (!IsKnappingSurface(api, pos)) return;
                if (!Voxels(be)[x, z] || required[x, 0, z]) continue;
                be.OnReceivedClientPacket(player, UserOverPacket, UserOverData(x, 0, z));
                if (Voxels(be)[x, z] && IsKnappingSurface(api, pos))
                {
                    throw new InvalidOperationException($"The knapping surface at {pos} did not take the voxel ({x}, {z}): the player is out of reach, or the block is claimed.");
                }
            }
        }

        if (!IsKnappingSurface(api, pos)) return;
        bool[,] left = Voxels(be);
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                if (required[x, 0, z] && !left[x, z])
                {
                    throw new InvalidOperationException($"The knapping surface at {pos} lacks the voxel ({x}, {z}) that {outputCode} needs. Knapping only removes voxels.");
                }
            }
        }

        throw new InvalidOperationException($"The knapping surface at {pos} did not finish {outputCode}.");
    }

    private bool IsKnappingSurface(ICoreServerAPI api, BlockPos pos) =>
        api.World.BlockAccessor.GetBlock(pos).Code.Path == KnappingSurface;

    private static bool[,] Voxels(BlockEntity be) => (bool[,])ProcessingDriver.Member(be, "Voxels")!;

    private static string? OutputCodeOf(object recipe) =>
        ((JsonItemStack?)ProcessingDriver.Member(recipe, "Output"))?.ResolvedItemstack?.Collectible.Code.ToString();

    private static int OutputQuantityOf(object recipe) =>
        ((JsonItemStack?)ProcessingDriver.Member(recipe, "Output"))?.ResolvedItemstack?.StackSize ?? 0;

    private static CraftingRecipeIngredient IngredientOf(object recipe) =>
        (CraftingRecipeIngredient)ProcessingDriver.Member(recipe, "Ingredient")!;

    // The same packet the game's clients send (BEKnappingSurface.SendUseOverPacket, BEClayForm.SendUseOverPacket): a voxel, the
    // mouse mode (true removes), and the face. The face is up, so the voxel is taken on its layer.
    private static byte[] UserOverData(int x, int y, int z, bool mouseMode = true)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(x);
            writer.Write(y);
            writer.Write(z);
            writer.Write(mouseMode);
            writer.Write((ushort)BlockFacing.UP.Index);
        }

        return stream.ToArray();
    }

    /// <summary>
    /// Lists the recipes of one domain that cannot be used: a recipe the game found while loading but did not register, and each
    /// ingredient, output or wildcard in the domain's recipe files that names no item or block.
    /// </summary>
    /// <param name="domain">The mod or game domain, such as <c>game</c> or <c>mymod</c>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="domain"/> is null.</exception>
    public IReadOnlyList<UnresolvedRecipe> Unresolved(string domain)
    {
        ArgumentNullException.ThrowIfNull(domain);
        return _host.RunOnGameThread(() => UnresolvedOnGameThread(domain));
    }

    private List<UnresolvedRecipe> UnresolvedOnGameThread(string domain)
    {
        ICoreServerAPI api = ServerApi;
        IWorldAccessor world = api.World;
        var result = new List<UnresolvedRecipe>();

        // Recipes the search holds but no registry does: the game gave them up while loading.
        var registered = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (GridRecipe grid in world.GridRecipes) registered.Add(grid);
        object registry = api.ModLoader.GetModSystem(RecipeRegistryType)
            ?? throw new InvalidOperationException($"The game's {RecipeRegistryType} is not loaded.");
        foreach (FieldInfo field in registry.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.GetValue(registry) is IEnumerable list)
            {
                foreach (object recipe in list) registered.Add(recipe);
            }
        }

        var given = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<IRecipeIngredientBase, List<IRecipeBase>> entry in world.FastSearchRecipesByIngredient)
        {
            foreach (IRecipeBase recipe in entry.Value)
            {
                if (recipe.Name?.Domain != domain || registered.Contains(recipe)) continue;
                if (given.Add(recipe.Name.ToString()))
                {
                    result.Add(new UnresolvedRecipe(recipe.Name.ToString(), "the game could not resolve this recipe"));
                }
            }
        }

        // The grid recipe files: each ingredient on its own line must name an item or block, and each wildcard must match one,
        // completed with the variants the line allows. Outputs are left out: an output that names nothing stops its recipe from
        // registering, which the search check reports.
        foreach (AssetLocation location in api.Assets.GetLocations("recipes/grid", domain))
        {
            string text = api.Assets.TryGet(location)?.ToText() ?? "";
            foreach (string line in text.Split('\n'))
            {
                if (line.Contains("output", StringComparison.Ordinal)) continue;
                Match ingredient = IngredientPattern.Match(line);
                if (!ingredient.Success) continue;
                string code = ingredient.Groups["code"].Value;
                if (code.Contains('{')) continue;
                Match allowed = AllowedVariantsPattern.Match(line);
                List<string> variants = allowed.Success ? QuotedPattern.Matches(allowed.Groups["list"].Value).Select(m => m.Groups[1].Value).ToList() : [];
                string? reason = CodeProblem(world, code, variants);
                if (reason is not null) result.Add(new UnresolvedRecipe(location.ToString(), reason));
            }
        }

        return result;
    }

    // An ingredient line of a grid recipe: its type and code, such as `"C": { type: "block", code: "torch-crude-*-up", ... }`.
    private static readonly Regex IngredientPattern = new(@"type:\s*""(?:item|block)""\s*,\s*code:\s*""(?<code>[^""]+)""", RegexOptions.Compiled);

    // The variants a wildcard ingredient may take, such as `allowedVariants: ["extinct", "lit"]`, and the quoted names in that list.
    private static readonly Regex AllowedVariantsPattern = new(@"allowedVariants:\s*\[(?<list>[^\]]*)\]", RegexOptions.Compiled);
    private static readonly Regex QuotedPattern = new(@"""([^""]+)""", RegexOptions.Compiled);

    // A code without a domain belongs to the game. A wildcard matches the codes the game matches it to, with the variants the line allows.
    private static string? CodeProblem(IWorldAccessor world, string code, List<string> variants)
    {
        if (!code.Contains('*'))
        {
            return Exists(world, code) ? null : $"{code} names no item or block";
        }

        AssetLocation location = code.Contains(':') ? AssetLocation.Create(code) : new AssetLocation("game", code);
        string[] allowed = variants.ToArray();
        bool matched = world.Items.Any(item => item.Code is not null && MatchesWildcard(location, item.Code, allowed))
            || world.Blocks.Any(block => block.Code is not null && MatchesWildcard(location, block.Code, allowed));
        return matched ? null : $"the wildcard {code} matches no item or block";
    }

    private static bool MatchesWildcard(AssetLocation wildcard, AssetLocation code, string[] allowed) =>
        allowed.Length == 0 ? WildcardUtil.Match(wildcard, code) : WildcardUtil.Match(wildcard, code, allowed);

    private static bool Exists(IWorldAccessor world, string code)
    {
        AssetLocation location = code.Contains(':') ? AssetLocation.Create(code) : new AssetLocation("game", code);
        return world.GetItem(location) is not null || world.GetBlock(location) is not null;
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
