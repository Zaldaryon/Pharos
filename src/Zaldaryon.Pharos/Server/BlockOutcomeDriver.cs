using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>How often a code dropped and how much of it.</summary>
/// <param name="Code">The code of the drop.</param>
/// <param name="Rolls">How many of the rolls dropped it.</param>
/// <param name="Amount">How much of it dropped, in total.</param>
public sealed record DropEntry(string Code, int Rolls, double Amount)
{
    /// <summary>The mean quantity per roll.</summary>
    public double Mean => Rolls == 0 ? 0 : Amount / Rolls;
}

/// <summary>The drops of a block over many rolls.</summary>
public sealed class DropDistribution
{
    private readonly Dictionary<string, DropEntry> _entries;

    internal DropDistribution(int rolls, Dictionary<string, DropEntry> entries)
    {
        Rolls = rolls;
        _entries = entries;
    }

    /// <summary>How many rolls were made.</summary>
    public int Rolls { get; }

    /// <summary>The codes that dropped, with how often and how much.</summary>
    public IReadOnlyList<DropEntry> Entries =>
        [.. _entries.Values.OrderBy(entry => entry.Code, StringComparer.Ordinal)];

    /// <summary>The mean quantity of a code per roll.</summary>
    public double Mean(string code) => _entries.TryGetValue(code, out DropEntry? entry) ? entry.Mean : 0;

    /// <summary>The total quantity of a code over all the rolls.</summary>
    public double Total(string code) => _entries.TryGetValue(code, out DropEntry? entry) ? entry.Amount : 0;

    /// <summary>How many of the rolls dropped the code.</summary>
    public int Hits(string code) => _entries.TryGetValue(code, out DropEntry? entry) ? entry.Rolls : 0;
}

/// <summary>The block hooks a trace records.</summary>
public enum ContactHook
{
    /// <summary>An entity touched the block.</summary>
    EntityCollide,

    /// <summary>An entity is inside the block.</summary>
    EntityInside,

    /// <summary>An entity fell onto the block.</summary>
    FallOnto,
}

/// <summary>One contact between an entity and a block.</summary>
/// <param name="Hook">Which hook was called.</param>
/// <param name="EntityId">The entity that touched the block, or zero when the hook has none.</param>
/// <param name="EntityCode">The entity's code, or empty when the hook has none.</param>
/// <param name="Position">Where the block is.</param>
/// <param name="Tick">The server tick the call happened at.</param>
public sealed record ContactEvent(
    ContactHook Hook,
    long EntityId,
    string EntityCode,
    BlockPos Position,
    int Tick);

/// <summary>The contacts between entities and blocks, while the trace is open.</summary>
public sealed class ContactTrace : IDisposable
{
    private readonly BlockOutcomeDriver _driver;
    private readonly List<ContactEvent> _events = [];
    private bool _disposed;

    internal ContactTrace(BlockOutcomeDriver driver, BlockPos center, int radius)
    {
        _driver = driver;
        Center = center;
        Radius = radius;
    }

    /// <summary>The middle of the region the trace watches.</summary>
    public BlockPos Center { get; }

    /// <summary>How many blocks around the middle the trace watches.</summary>
    public int Radius { get; }

    /// <summary>The contacts, in the order they happened.</summary>
    public IReadOnlyList<ContactEvent> Events
    {
        get
        {
            lock (_events) return [.. _events];
        }
    }

    /// <summary>Stops recording.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _driver.Stop(this);
    }

    internal bool Watches(BlockPos position)
    {
        return Math.Abs(position.X - Center.X) <= Radius
            && Math.Abs(position.Y - Center.Y) <= Radius
            && Math.Abs(position.Z - Center.Z) <= Radius;
    }

    internal void Record(ContactEvent contact)
    {
        lock (_events) _events.Add(contact);
    }

    /// <summary>
    /// Waits until any hook is called, and returns the call.
    /// </summary>
    /// <param name="maxTicks">The most ticks to wait.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="TimeoutException">Thrown when no hook was called in time.</exception>
    public async Task<ContactEvent> WaitForAnyAsync(int maxTicks = 200, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);

        for (int tick = 0; tick < maxTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            ContactEvent? found = Events.FirstOrDefault();
            if (found != null) return found;
            _driver.Tick();
        }

        throw new TimeoutException($"No contact came within {maxTicks} ticks.");
    }

    /// <summary>
    /// Waits until a hook is called at a position, and returns the call.
    /// </summary>
    /// <param name="hook">The hook to wait for.</param>
    /// <param name="position">The block that has to be called.</param>
    /// <param name="maxTicks">The most ticks to wait.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="TimeoutException">Thrown when the hook was not called in time.</exception>
    public async Task<ContactEvent> WaitForAsync(
        ContactHook hook,
        BlockPos position,
        int maxTicks = 200,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);

        for (int tick = 0; tick < maxTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            ContactEvent? found = Events.FirstOrDefault(e => e.Hook == hook && e.Position.Equals(position));
            if (found != null) return found;
            _driver.Tick();
        }

        throw new TimeoutException($"The hook {hook} was not called at {position} within {maxTicks} ticks.");
    }
}

/// <summary>One neighbour update.</summary>
/// <param name="Position">The block that was told.</param>
/// <param name="Neighbour">The block that changed.</param>
/// <param name="Tick">The server tick the call happened at.</param>
public sealed record NeighbourUpdate(BlockPos Position, BlockPos Neighbour, int Tick);

/// <summary>The neighbour updates, while the trace is open.</summary>
public sealed class NeighbourUpdateTrace : IDisposable
{
    private readonly BlockOutcomeDriver _driver;
    private readonly List<NeighbourUpdate> _updates = [];
    private bool _disposed;

    internal NeighbourUpdateTrace(BlockOutcomeDriver driver, BlockPos center, int radius)
    {
        _driver = driver;
        Center = center;
        Radius = radius;
    }

    /// <summary>The middle of the region the trace watches.</summary>
    public BlockPos Center { get; }

    /// <summary>How many blocks around the middle the trace watches.</summary>
    public int Radius { get; }

    /// <summary>The updates, in the order they happened.</summary>
    public IReadOnlyList<NeighbourUpdate> Updates
    {
        get
        {
            lock (_updates) return [.. _updates];
        }
    }

    /// <summary>Stops recording.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _driver.Stop(this);
    }

    internal bool Watches(BlockPos position)
    {
        return Math.Abs(position.X - Center.X) <= Radius
            && Math.Abs(position.Y - Center.Y) <= Radius
            && Math.Abs(position.Z - Center.Z) <= Radius;
    }

    internal void Record(NeighbourUpdate update)
    {
        lock (_updates) _updates.Add(update);
    }

    /// <summary>
    /// Waits until any block is told about a neighbour, and returns the update.
    /// </summary>
    /// <param name="maxTicks">The most ticks to wait.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="TimeoutException">Thrown when no update came in time.</exception>
    public async Task<NeighbourUpdate> WaitForAnyAsync(int maxTicks = 200, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);

        for (int tick = 0; tick < maxTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            NeighbourUpdate? found = Updates.FirstOrDefault();
            if (found != null) return found;
            _driver.Tick();
        }

        throw new TimeoutException($"No neighbour update came within {maxTicks} ticks.");
    }

    /// <summary>
    /// Waits until a block is told about a neighbour, and returns the update.
    /// </summary>
    /// <param name="position">The block that has to be told.</param>
    /// <param name="maxTicks">The most ticks to wait.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="TimeoutException">Thrown when no update came in time.</exception>
    public async Task<NeighbourUpdate> WaitForAsync(BlockPos position, int maxTicks = 200, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);

        for (int tick = 0; tick < maxTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            NeighbourUpdate? found = Updates.FirstOrDefault(u => u.Position.Equals(position));
            if (found != null) return found;
            _driver.Tick();
        }

        throw new TimeoutException($"No neighbour update came for {position} within {maxTicks} ticks.");
    }
}

/// <summary>
/// The block outcomes of the server: what a block drops, what an explosion breaks and the hooks
/// a block calls when an entity or a neighbour touches it.
/// </summary>
public sealed class BlockOutcomeDriver
{
    private static readonly List<ContactTrace> s_contacts = [];
    private static readonly List<NeighbourUpdateTrace> s_neighbours = [];
    private static bool s_installed;

    private readonly EmbeddedServerHost _host;

    internal BlockOutcomeDriver(EmbeddedServerHost host)
    {
        _host = host;
        RunningServer.Host = host;
    }

    /// <summary>
    /// Rolls the drops of a block many times and counts what came out.
    /// </summary>
    /// <param name="pos">The block to roll.</param>
    /// <param name="player">The player the block drops for, whose held tool and state the drops follow.</param>
    /// <param name="tool">The tool to hold while rolling, when the player has it in the inventory.</param>
    /// <param name="rolls">How many rolls to make.</param>
    /// <exception cref="ArgumentOutOfRangeException">The number of rolls is not positive.</exception>
    public DropDistribution Drops(BlockPos pos, IPlayer? player = null, ItemStack? tool = null, int rolls = 100)
    {
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rolls);

        return _host.RunOnGameThread(() =>
        {
            Block block = _host.Api.World.BlockAccessor.GetBlock(pos);
            Dictionary<string, DropEntry> tally = [];

            int activeSlot = player?.InventoryManager.ActiveHotbarSlotNumber ?? 0;
            int toolSlot = tool == null ? -1 : FindToolSlot(player, tool);

            if (toolSlot >= 0) player!.InventoryManager.ActiveHotbarSlotNumber = toolSlot;

            try
            {
                for (int roll = 0; roll < rolls; roll++)
                {
                    foreach (ItemStack stack in block.GetDrops(_host.Api.World, pos, player!, 1f))
                    {
                        if (stack == null) continue;

                        string code = stack.Collectible.Code.ToString();
                        tally.TryGetValue(code, out DropEntry? entry);
                        tally[code] = new DropEntry(code, (entry?.Rolls ?? 0) + 1, (entry?.Amount ?? 0) + stack.StackSize);
                    }
                }
            }
            finally
            {
                if (toolSlot >= 0) player!.InventoryManager.ActiveHotbarSlotNumber = activeSlot;
            }

            return new DropDistribution(rolls, tally);
        });
    }

    /// <summary>
    /// Breaks a block for real, the way a player does, and returns the items that dropped.
    /// </summary>
    /// <param name="player">The player who breaks the block.</param>
    /// <param name="pos">The block to break.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The item entities the block dropped.</returns>
    public async Task<IReadOnlyList<EntityItem>> BreakAndCollectAsync(
        ServerTestPlayer player,
        BlockPos pos,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(pos);

        HashSet<long> before = ItemEntityIdsAround(pos, radius: 8);

        BotResult result = await player.Bot.BreakBlockAsync(pos, ct: ct).ConfigureAwait(false);
        if (!result.Accepted) return [];

        await Task.Yield();
        _host.Tick();

        List<EntityItem> dropped = [];
        foreach (EntityItem item in ItemEntitiesAround(pos, radius: 8))
        {
            if (!before.Contains(item.EntityId)) dropped.Add(item);
        }

        return dropped;
    }

    /// <summary>
    /// Records the contacts between the entities and the blocks of a region.
    /// </summary>
    /// <param name="center">The middle of the region.</param>
    /// <param name="radius">How many blocks around the middle the region reaches.</param>
    public ContactTrace TraceContacts(BlockPos center, int radius = 16)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        Install();
        ContactTrace trace = new(this, center, radius);
        lock (s_contacts) s_contacts.Add(trace);
        return trace;
    }

    /// <summary>
    /// Records the neighbour updates of a region.
    /// </summary>
    /// <param name="center">The middle of the region.</param>
    /// <param name="radius">How many blocks around the middle the region reaches.</param>
    public NeighbourUpdateTrace TraceNeighbourUpdates(BlockPos center, int radius = 16)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        Install();
        NeighbourUpdateTrace trace = new(this, center, radius);
        lock (s_neighbours) s_neighbours.Add(trace);
        return trace;
    }

    internal void Stop(ContactTrace trace)
    {
        lock (s_contacts) s_contacts.Remove(trace);
    }

    internal void Stop(NeighbourUpdateTrace trace)
    {
        lock (s_neighbours) s_neighbours.Remove(trace);
    }

    internal void Tick()
    {
        _host.Tick();
    }

    private int FindToolSlot(IPlayer? player, ItemStack tool)
    {
        if (player == null) return -1;

        AssetLocation? wanted = tool.Collectible?.Code;
        if (wanted == null) return -1;

        int index = 0;
        foreach (ItemSlot slot in player.InventoryManager.GetHotbarInventory())
        {
            AssetLocation? code = slot?.Itemstack?.Collectible?.Code;
            if (code != null && code.Equals(wanted)) return index;
            index++;
        }

        return -1;
    }

    private HashSet<long> ItemEntityIdsAround(BlockPos pos, int radius)
    {
        HashSet<long> ids = [];
        foreach (EntityItem item in ItemEntitiesAround(pos, radius)) ids.Add(item.EntityId);

        return ids;
    }

    private IEnumerable<EntityItem> ItemEntitiesAround(BlockPos pos, int radius)
    {
        List<EntityItem> found = [];
        _host.RunOnGameThread(() =>
        {
            BlockPos from = pos.AddCopy(-radius, -radius, -radius);
            BlockPos to = pos.AddCopy(radius, radius, radius);
            foreach (Entity entity in _host.Api.World.GetEntitiesInsideCuboid(from, to))
            {
                if (entity is EntityItem item) found.Add(item);
            }
        });

        return found;
    }

    private static void Install()
    {
        if (s_installed) return;
        s_installed = true;

        Harmony harmony = new("zaldaryon.pharos.blockoutcomes");

        harmony.Patch(
            typeof(Block).GetMethod(nameof(Block.OnEntityCollide))!,
            postfix: new HarmonyMethod(typeof(BlockOutcomeDriver), nameof(AfterEntityCollide)));

        harmony.Patch(
            typeof(Block).GetMethod(nameof(Block.OnEntityInside))!,
            postfix: new HarmonyMethod(typeof(BlockOutcomeDriver), nameof(AfterEntityInside)));

        harmony.Patch(
            typeof(Block).GetMethod(nameof(Block.OnFallOnto))!,
            postfix: new HarmonyMethod(typeof(BlockOutcomeDriver), nameof(AfterFallOnto)));

        harmony.Patch(
            typeof(Block).GetMethod(nameof(Block.OnNeighbourBlockChange))!,
            postfix: new HarmonyMethod(typeof(BlockOutcomeDriver), nameof(AfterNeighbourBlockChange)));
    }

    private static void AfterEntityCollide(IWorldAccessor world, Entity entity, BlockPos pos)
    {
        Record(ContactHook.EntityCollide, entity, pos);
    }

    private static void AfterEntityInside(IWorldAccessor world, Entity entity, BlockPos pos)
    {
        Record(ContactHook.EntityInside, entity, pos);
    }

    private static void AfterFallOnto(IWorldAccessor world, BlockPos pos)
    {
        Record(ContactHook.FallOnto, null, pos);
    }

    private static void AfterNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        List<NeighbourUpdateTrace> traces;
        lock (s_neighbours) traces = [.. s_neighbours];
        if (traces.Count == 0) return;

        int tick = RunningServer.TickCount;
        foreach (NeighbourUpdateTrace trace in traces)
        {
            if (trace.Watches(pos)) trace.Record(new NeighbourUpdate(pos, neibpos, tick));
        }
    }

    private static void Record(ContactHook hook, Entity? entity, BlockPos pos)
    {
        List<ContactTrace> traces;
        lock (s_contacts) traces = [.. s_contacts];
        if (traces.Count == 0) return;

        int tick = RunningServer.TickCount;
        foreach (ContactTrace trace in traces)
        {
            if (trace.Watches(pos))
            {
                trace.Record(new ContactEvent(
                    hook,
                    entity?.EntityId ?? 0,
                    entity?.Code?.ToString() ?? "",
                    pos,
                    tick));
            }
        }
    }
}
