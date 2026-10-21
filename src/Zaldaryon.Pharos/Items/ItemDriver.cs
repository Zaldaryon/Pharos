using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Items;

/// <summary>The server's items: how long they take to mine with, what tier they have, and how much is left of them.</summary>
public sealed class ItemDriver
{
    private readonly EmbeddedServerHost _host;

    internal ItemDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// The seconds the game would take to mine a block with a stack, from the block's resistance
    /// and the stack's mining speed.
    /// </summary>
    /// <param name="stack">The stack that would do the mining.</param>
    /// <param name="position">The block that would be mined.</param>
    /// <param name="player">
    /// The player the mining is measured against. The game measures the mining speed against the
    /// player, so the first online player is used when none is given.
    /// </param>
    /// <exception cref="InvalidOperationException">No player is online to measure the mining against.</exception>
    public float MiningTime(ItemStack stack, BlockPos position, IPlayer? player = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        float speed = MiningSpeed(stack, position, player);
        if (speed <= 0) return float.PositiveInfinity;

        float resistance = _host.RunOnGameThread(() =>
        {
            IBlockAccessor blocks = _host.Api.World.BlockAccessor;
            Block block = blocks.GetBlock(position);
            return block.GetResistance(blocks, position);
        });

        return resistance / speed;
    }

    /// <summary>
    /// The resistance the stack takes off a block per second, the way the game measures it.
    /// </summary>
    /// <param name="stack">The stack that would do the mining.</param>
    /// <param name="position">The block that would be mined.</param>
    /// <param name="player">
    /// The player the mining is measured against. The first online player is used when none is given.
    /// </param>
    /// <exception cref="InvalidOperationException">No player is online to measure the mining against.</exception>
    public float MiningSpeed(ItemStack stack, BlockPos position, IPlayer? player = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        IPlayer forPlayer = player ?? RequirePlayer();
        return _host.RunOnGameThread(() =>
        {
            IBlockAccessor blocks = _host.Api.World.BlockAccessor;
            Block block = blocks.GetBlock(position);
            BlockSelection selection = new(position, BlockFacing.UP, block);
            return stack.Collectible.GetMiningSpeed(stack, selection, block, forPlayer);
        });
    }

    /// <summary>Whether a stack can mine a block: its tier and the material it is made for.</summary>
    /// <param name="stack">The stack that would do the mining.</param>
    /// <param name="position">The block that would be mined.</param>
    /// <param name="player">The player the mining is measured against.</param>
    public bool CanMine(ItemStack stack, BlockPos position, IPlayer? player = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        IPlayer forPlayer = player ?? RequirePlayer();
        return _host.RunOnGameThread(() =>
        {
            IBlockAccessor blocks = _host.Api.World.BlockAccessor;
            Block block = blocks.GetBlock(position);
            int requiredTier = block.GetRequiredMiningTier(_host.Api.World, position);
            if (requiredTier <= 0) return true;

            EnumBlockMaterial material = block.GetBlockMaterial(blocks, position);
            Dictionary<EnumBlockMaterial, float>? speeds = stack.Collectible.GetMiningSpeeds(new DummySlot(stack));
            if (stack.Collectible.GetToolTier(new DummySlot(stack)) < requiredTier) return false;
            return speeds != null && speeds.ContainsKey(material);
        });
    }

    /// <summary>The tier of a tool, the way the game reads it.</summary>
    /// <param name="stack">The stack to read.</param>
    public int ToolTier(ItemStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return stack.Collectible.GetToolTier(new DummySlot(stack));
    }

    /// <summary>How much durability a stack has left, zero when it has none.</summary>
    /// <param name="stack">The stack to read.</param>
    public int RemainingDurability(ItemStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return stack.Collectible.GetRemainingDurability(stack);
    }

    /// <summary>How much durability a stack has when it is new.</summary>
    /// <param name="stack">The stack to read.</param>
    public int MaxDurability(ItemStack stack)
    {
        ArgumentNullException.ThrowIfNull(stack);
        return stack.Collectible.GetMaxDurability(stack);
    }

    /// <summary>How much durability is left in a hotbar slot of a player.</summary>
    /// <param name="slot">The hotbar slot, counted from zero.</param>
    /// <param name="player">The player whose slot is read. The first online player is used when none is given.</param>
    /// <exception cref="InvalidOperationException">No player is online, or the slot is empty.</exception>
    public int Durability(int slot, IPlayer? player = null)
    {
        IPlayer forPlayer = player ?? RequirePlayer();
        return _host.RunOnGameThread(() =>
        {
            ItemSlot? hotbar = forPlayer.InventoryManager.GetHotbarInventory()[slot];
            if (hotbar?.Itemstack == null)
            {
                throw new InvalidOperationException($"The hotbar slot {slot} of '{forPlayer.PlayerName}' is empty.");
            }

            return hotbar.Itemstack.Collectible.GetRemainingDurability(hotbar.Itemstack);
        });
    }

    /// <summary>Sets the tool mode of a hotbar slot of a player, the way the game does.</summary>
    /// <param name="slot">The hotbar slot, counted from zero.</param>
    /// <param name="mode">The mode to set.</param>
    /// <param name="player">The player whose slot is changed. The first online player is used when none is given.</param>
    /// <exception cref="InvalidOperationException">No player is online, or the slot is empty.</exception>
    public void SetToolMode(int slot, int mode, IPlayer? player = null)
    {
        IPlayer forPlayer = player ?? RequirePlayer();
        _host.RunOnGameThread(() =>
        {
            ItemSlot? hotbar = forPlayer.InventoryManager.GetHotbarInventory()[slot];
            if (hotbar?.Itemstack == null)
            {
                throw new InvalidOperationException($"The hotbar slot {slot} of '{forPlayer.PlayerName}' is empty.");
            }

            BlockPos position = forPlayer.Entity?.Pos.AsBlockPos ?? new BlockPos(0, 0, 0);
            BlockSelection selection = new(position, BlockFacing.UP, _host.Api.World.BlockAccessor.GetBlock(position));
            hotbar.Itemstack.Collectible.SetToolMode(hotbar, forPlayer, selection, mode);
        });
    }

    /// <summary>The first player on the server, the one the game measures against when none is named.</summary>
    private IPlayer RequirePlayer()
    {
        IPlayer? player = _host.RunOnGameThread(() => _host.AllOnlinePlayers.FirstOrDefault());
        return player ?? throw new InvalidOperationException(
            "The server has no player online, so the item cannot be measured against one. Join a test player first.");
    }
}
