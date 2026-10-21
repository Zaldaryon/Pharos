using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Server;

/// <summary>The inventory of a block entity, with the slots a test can fill and count.</summary>
public sealed class ContainerInventory
{
    private readonly IInventory _inventory;
    private readonly System.Func<string, CollectibleObject?> _resolve;

    internal ContainerInventory(BlockPos position, string inventoryClassName, IInventory inventory, System.Func<string, CollectibleObject?> resolve)
    {
        Position = position;
        InventoryClassName = inventoryClassName;
        _inventory = inventory;
        _resolve = resolve;
    }

    /// <summary>Where the container stands.</summary>
    public BlockPos Position { get; }

    /// <summary>The name of the inventory's class, such as <c>chest</c>.</summary>
    public string InventoryClassName { get; }

    /// <summary>The slots of the container, in order.</summary>
    public IReadOnlyList<ItemSlot> Slots => [.. _inventory.Where(slot => slot != null)];

    /// <summary>The stack in a slot, or null when the slot is empty.</summary>
    public ItemStack? ItemAt(int slot) => _inventory[slot]?.Itemstack;

    /// <summary>
    /// Puts a stack in a slot, the way the game does, so the change is marked dirty and the
    /// players are told about it.
    /// </summary>
    /// <param name="slot">The slot, counted from zero.</param>
    /// <param name="code">The code of the item, such as <c>game:ingot-copper</c>.</param>
    /// <param name="quantity">How many items to put in the slot.</param>
    /// <returns>Whether the slot took the stack.</returns>
    public bool Fill(int slot, string code, int quantity = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        ItemSlot? target = _inventory[slot];
        if (target == null) return false;

        CollectibleObject? collectible = _resolve(code);
        if (collectible == null) return false;

        ItemStack stack = new(collectible, quantity);
        if (!target.CanHold(new DummySlot(stack))) return false;

        target.Itemstack = stack;
        target.MarkDirty();
        return true;
    }

    /// <summary>Empties every slot of the container.</summary>
    public void Clear()
    {
        foreach (ItemSlot slot in Slots)
        {
            if (slot.Empty) continue;
            slot.Itemstack = null;
            slot.MarkDirty();
        }
    }

    /// <summary>How many items of a code the container holds.</summary>
    /// <param name="code">The code of the item, such as <c>game:ingot-copper</c>.</param>
    public int Count(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        int total = 0;
        foreach (ItemSlot slot in Slots)
        {
            ItemStack? stack = slot.Itemstack;
            if (stack?.Collectible != null && stack.Collectible.Code.ToString() == code) total += stack.StackSize;
        }

        return total;
    }
}

/// <summary>How many items of each code a region holds.</summary>
public sealed class ItemTotals
{
    /// <summary>The totals, by item code.</summary>
    public IReadOnlyDictionary<string, int> ByCode { get; }

    /// <summary>The totals of the items the region lost, by item code.</summary>
    public IReadOnlyDictionary<string, int> Lost { get; }

    /// <summary>The totals of the items the region gained, by item code.</summary>
    public IReadOnlyDictionary<string, int> Gained { get; }

    internal ItemTotals(IReadOnlyDictionary<string, int> byCode, IReadOnlyDictionary<string, int> lost, IReadOnlyDictionary<string, int> gained)
    {
        ByCode = byCode;
        Lost = lost;
        Gained = gained;
    }

    /// <summary>How many items of a code the region holds.</summary>
    /// <param name="code">The code of the item, such as <c>game:ingot-copper</c>.</param>
    public int Count(string code) => ByCode.TryGetValue(code, out int count) ? count : 0;

    /// <summary>How many items the region holds, of every code.</summary>
    public int Total => ByCode.Values.Sum();
}

/// <summary>One move the item flow made.</summary>
/// <param name="From">Where the items came from, such as <c>block:12,64,-3</c>.</param>
/// <param name="To">Where the items went, such as <c>block:12,65,-3</c>.</param>
/// <param name="Code">The code of the item that moved.</param>
/// <param name="Quantity">How many items moved.</param>
public sealed record ItemFlowMove(string From, string To, string Code, int Quantity);

/// <summary>What the item flow moved while the ticks ran.</summary>
/// <param name="Moves">The moves, in the order they were read.</param>
/// <param name="Ticks">How many ticks ran.</param>
public sealed record ItemFlowReport(IReadOnlyList<ItemFlowMove> Moves, int Ticks);

/// <summary>The server's inventories: the containers, the items on the ground and what moves between them.</summary>
public sealed class InventoryDriver
{
    private readonly EmbeddedServerHost _host;

    internal InventoryDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>The inventory of the block entity at a position.</summary>
    /// <param name="position">Where the container stands.</param>
    /// <exception cref="InvalidOperationException">The block there has no inventory.</exception>
    public ContainerInventory At(BlockPos position)
    {
        ArgumentNullException.ThrowIfNull(position);
        return _host.RunOnGameThread(() =>
        {
            BlockEntity? blockEntity = _host.Api.World.BlockAccessor.GetBlockEntity(position);
            if (blockEntity is not IBlockEntityContainer container)
            {
                throw new InvalidOperationException($"The block at {position} has no inventory to read.");
            }

            return new ContainerInventory(position, container.InventoryClassName, container.Inventory, Resolve);
        });
    }

    /// <summary>Whether the block at a position has an inventory.</summary>
    /// <param name="position">Where the container stands.</param>
    public bool HasInventory(BlockPos position) => _host.RunOnGameThread(() =>
        _host.Api.World.BlockAccessor.GetBlockEntity(position) is IBlockEntityContainer);

    /// <summary>
    /// Every item in a region: the block entities' inventories, the items on the ground and the
    /// inventories of the players who stand in it.
    /// </summary>
    /// <param name="center">The middle of the region.</param>
    /// <param name="radius">How many blocks around the middle the region reaches.</param>
    public ItemTotals Totals(BlockPos center, int radius = 8)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        return _host.RunOnGameThread(() => TotalsCore(center, radius));
    }

    /// <summary>
    /// Steps the ticks the item flow needs and reports what it moved, read from the inventories
    /// before and after.
    /// </summary>
    /// <param name="center">The middle of the region the flow runs in.</param>
    /// <param name="ticks">How many server ticks to run.</param>
    /// <param name="radius">How many blocks around the middle the region reaches.</param>
    /// <param name="ct">Cancels the wait.</param>
    public async Task<ItemFlowReport> RunFlowAsync(BlockPos center, int ticks, int radius = 8, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        Dictionary<string, Dictionary<string, int>> before = _host.RunOnGameThread(() => Snapshot(center, radius));
        for (int tick = 0; tick < ticks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            _host.Tick();
        }

        Dictionary<string, Dictionary<string, int>> after = _host.RunOnGameThread(() => Snapshot(center, radius));
        return new ItemFlowReport(Moves(before, after), ticks);
    }

    /// <summary>The items a ground storage holds, the same way any container is read.</summary>
    /// <param name="position">Where the ground storage stands.</param>
    public ContainerInventory GroundStorage(BlockPos position) => At(position);

    /// <summary>The item a code names, or null when the server has no such item.</summary>
    private CollectibleObject? Resolve(string code)
    {
        AssetLocation location = new(code);
        return (CollectibleObject?)_host.Api.World.GetItem(location) ?? _host.Api.World.GetBlock(location);
    }

    private ItemTotals TotalsCore(BlockPos center, int radius)
    {
        Dictionary<string, int> totals = new(StringComparer.Ordinal);
        foreach (Dictionary<string, int> items in Snapshot(center, radius).Values)
        {
            foreach ((string code, int quantity) in items) totals[code] = totals.GetValueOrDefault(code) + quantity;
        }

        return new ItemTotals(totals, new Dictionary<string, int>(), new Dictionary<string, int>());
    }

    /// <summary>The items of every source in the region, by the source's key.</summary>
    private Dictionary<string, Dictionary<string, int>> Snapshot(BlockPos center, int radius)
    {
        Dictionary<string, Dictionary<string, int>> snapshot = new(StringComparer.Ordinal);
        BlockPos from = center.AddCopy(-radius, -radius, -radius);
        BlockPos to = center.AddCopy(radius, radius, radius);

        for (int x = from.X; x <= to.X; x++)
        {
            for (int y = from.Y; y <= to.Y; y++)
            {
                for (int z = from.Z; z <= to.Z; z++)
                {
                    BlockPos position = new(x, y, z);
                    if (_host.Api.World.BlockAccessor.GetBlockEntity(position) is not IBlockEntityContainer container) continue;
                    Dictionary<string, int> items = ItemsOf(container.Inventory);
                    if (items.Count > 0) snapshot[$"block:{x},{y},{z}"] = items;
                }
            }
        }

        foreach (Entity entity in _host.Api.World.GetEntitiesInsideCuboid(from, to))
        {
            if (entity is not EntityItem item || item.Itemstack == null) continue;
            string code = item.Itemstack.Collectible?.Code.ToString() ?? "";
            if (string.IsNullOrEmpty(code)) continue;
            Dictionary<string, int> items = new(StringComparer.Ordinal) { [code] = item.Itemstack.StackSize };
            snapshot[$"item:{entity.EntityId}"] = items;
        }

        foreach (IPlayer player in _host.AllOnlinePlayers)
        {
            if (player?.Entity == null) continue;
            BlockPos position = player.Entity.Pos.AsBlockPos;
            if (position.X < from.X || position.X > to.X || position.Y < from.Y || position.Y > to.Y || position.Z < from.Z || position.Z > to.Z) continue;

            Dictionary<string, int> items = [];
            foreach (IInventory inventory in new[] { player.InventoryManager.GetHotbarInventory(), player.InventoryManager.GetOwnInventory("main") })
            {
                if (inventory == null) continue;
                foreach ((string code, int quantity) in ItemsOf(inventory)) items[code] = items.GetValueOrDefault(code) + quantity;
            }

            if (items.Count > 0) snapshot[$"player:{player.PlayerName}"] = items;
        }

        return snapshot;
    }

    private static Dictionary<string, int> ItemsOf(IInventory? inventory)
    {
        Dictionary<string, int> items = new(StringComparer.Ordinal);
        if (inventory == null) return items;

        foreach (ItemSlot slot in inventory)
        {
            ItemStack? stack = slot?.Itemstack;
            if (stack?.Collectible == null) continue;
            string code = stack.Collectible.Code.ToString();
            items[code] = items.GetValueOrDefault(code) + stack.StackSize;
        }

        return items;
    }

    /// <summary>The moves between the sources, read from what they lost and gained.</summary>
    private static IReadOnlyList<ItemFlowMove> Moves(
        Dictionary<string, Dictionary<string, int>> before,
        Dictionary<string, Dictionary<string, int>> after)
    {
        List<ItemFlowMove> moves = [];
        HashSet<string> sources = [.. before.Keys, .. after.Keys];
        HashSet<string> codes = [.. before.Values.SelectMany(items => items.Keys), .. after.Values.SelectMany(items => items.Keys)];

        foreach (string code in codes)
        {
            List<(string Source, int Quantity)> lost = [];
            List<(string Source, int Quantity)> gained = [];
            foreach (string source in sources)
            {
                int had = before.TryGetValue(source, out Dictionary<string, int>? beforeItems) ? beforeItems.GetValueOrDefault(code) : 0;
                int has = after.TryGetValue(source, out Dictionary<string, int>? afterItems) ? afterItems.GetValueOrDefault(code) : 0;
                if (has < had) lost.Add((source, had - has));
                if (has > had) gained.Add((source, has - had));
            }

            foreach ((string from, int quantity) in lost)
            {
                int remaining = quantity;
                List<(string Source, int Quantity)> available = [.. gained];
                foreach ((string to, int gainedQuantity) in available)
                {
                    if (remaining <= 0) break;
                    int moved = Math.Min(remaining, gainedQuantity);
                    moves.Add(new ItemFlowMove(from, to, code, moved));
                    remaining -= moved;
                    gained = [.. gained.Select(entry => entry.Source == to ? (entry.Source, entry.Quantity - moved) : entry)];
                }
            }
        }

        return moves;
    }
}
