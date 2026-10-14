using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>The slots of a firepit, in the order the game stores them.</summary>
public enum FirepitSlot
{
    /// <summary>The fuel slot.</summary>
    Fuel = 0,

    /// <summary>The input slot, where the food or material to cook goes.</summary>
    Input = 1,

    /// <summary>The output slot, where the cooked result appears.</summary>
    Output = 2,
}

/// <summary>The slots of a barrel, in the order the game stores them.</summary>
public enum BarrelSlot
{
    /// <summary>The item slot.</summary>
    Items = 0,

    /// <summary>The liquid slot, which also holds the output liquid.</summary>
    Liquid = 1,
}

/// <summary>The state of a firepit.</summary>
/// <param name="Temperature">The furnace temperature, in degrees Celsius.</param>
/// <param name="FuelBurnTimeLeft">The seconds of fuel left.</param>
/// <param name="CookingProgress">The input's cooking progress, from 0 to 1.</param>
/// <param name="Burning">Whether the fire is lit.</param>
/// <param name="Output">The code and quantity of the output stack, or null when there is none.</param>
public sealed record FirepitState(float Temperature, float FuelBurnTimeLeft, float CookingProgress, bool Burning, (string Code, int Quantity)? Output);

/// <summary>The state of a barrel.</summary>
/// <param name="Sealed">Whether the barrel is sealed.</param>
/// <param name="SealedSinceTotalHours">The calendar hour the barrel was sealed at.</param>
/// <param name="Items">The code and quantity of the item slot's stack, or null when it is empty.</param>
/// <param name="Liquid">The code and quantity of the liquid slot's stack, or null when it is empty.</param>
public sealed record BarrelState(bool Sealed, double SealedSinceTotalHours, (string Code, int Quantity)? Items, (string Code, int Quantity)? Liquid);

/// <summary>Fills, lights and reads a firepit, and runs its cooking for a span of game time.</summary>
public sealed class FirepitFacade
{
    private readonly EmbeddedServerHost _host;
    private readonly BlockPos _pos;

    internal FirepitFacade(EmbeddedServerHost host, BlockPos pos)
    {
        _host = host;
        _pos = pos;
    }

    /// <summary>Puts a stack into a firepit slot, through the block entity's inventory so it syncs.</summary>
    /// <param name="slot">The slot to fill.</param>
    /// <param name="code">The item or block code, such as <c>game:firewood</c>.</param>
    /// <param name="quantity">The stack size.</param>
    /// <returns>This facade, so fills can chain.</returns>
    public FirepitFacade Fill(FirepitSlot slot, string code, int quantity = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        _host.RunOnGameThread(() =>
        {
            ItemSlot target = Inventory()[(int)slot];
            Item item = ServerApi.World.GetItem(new AssetLocation(code))
                ?? throw new ArgumentException($"The game has no item named {code}.", nameof(code));
            target.Itemstack = new ItemStack(item, quantity);
            target.MarkDirty();
        });
        return this;
    }

    /// <summary>Lights the fire with the fuel in the fuel slot, as the game does when fuel catches.</summary>
    /// <returns>This facade.</returns>
    /// <exception cref="InvalidOperationException">The fuel slot is empty, or there is no firepit at the position.</exception>
    public FirepitFacade Ignite()
    {
        _host.RunOnGameThread(() =>
        {
            if (Inventory()[(int)FirepitSlot.Fuel].Empty) throw new InvalidOperationException("The fuel slot is empty, so there is nothing to light.");
            Block().GetType().GetMethod("igniteFuel")!.Invoke(Block(), null);
        });
        return this;
    }

    /// <summary>Reads the firepit's temperature, fuel, cooking progress and output.</summary>
    public FirepitState State => _host.RunOnGameThread(() =>
    {
        object be = Block();
        float temperature = (float)Field(be, "furnaceTemperature");
        float fuel = (float)Field(be, "fuelBurnTime");
        float cooking = (float)Field(be, "inputStackCookingTime");
        float maxCooking = Convert.ToSingle(be.GetType().GetMethod("maxCookingTime")!.Invoke(be, null));
        ItemSlot output = Inventory()[(int)FirepitSlot.Output];
        return new FirepitState(
            temperature,
            fuel,
            maxCooking > 0 && Inventory()[(int)FirepitSlot.Input].Itemstack is not null ? Math.Clamp(cooking / maxCooking, 0, 1) : 0,
            fuel > 0,
            Describe(output));
    });

    /// <summary>
    /// Plays game time forward for the firepit. Its burn and cooking update runs on a tick listener measured in
    /// real seconds, so the span is simulated through that listener with fixed steps.
    /// </summary>
    /// <param name="span">The game time to play, in simulated seconds.</param>
    /// <returns>The game time the listener simulated.</returns>
    public async Task<TimeSpan> RunAsync(TimeSpan span, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(span, TimeSpan.Zero);
        FastForwardResult result = await _host.TickListeners.FastForwardAsync(_pos, span, ct: ct);
        return result.Simulated;
    }

    private ICoreServerAPI ServerApi => (ICoreServerAPI)_host.Server.Api;

    private object Block() => ServerApi.World.BlockAccessor.GetBlockEntity(_pos)
        ?? throw new InvalidOperationException($"There is no block entity at {_pos}.");

    private InventoryBase Inventory() => ProcessingDriver.InventoryOf(Block());

    internal static (string, int)? Describe(ItemSlot slot) =>
        slot.Itemstack is { } stack ? (stack.Collectible.Code.ToString(), stack.StackSize) : null;

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)!.GetValue(target);
}

/// <summary>Fills and reads a barrel, and runs its sealing for a span of calendar time.</summary>
public sealed class BarrelFacade
{
    private readonly EmbeddedServerHost _host;
    private readonly BlockPos _pos;

    internal BarrelFacade(EmbeddedServerHost host, BlockPos pos)
    {
        _host = host;
        _pos = pos;
    }

    /// <summary>Puts a stack into a barrel slot, through the block entity's inventory so it syncs.</summary>
    /// <param name="slot">The slot to fill.</param>
    /// <param name="code">The item code, such as <c>game:waterportion</c>.</param>
    /// <param name="quantity">The stack size.</param>
    /// <returns>This facade, so fills can chain.</returns>
    public BarrelFacade Fill(BarrelSlot slot, string code, int quantity = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        _host.RunOnGameThread(() =>
        {
            ItemSlot target = Inventory()[(int)slot];
            Item item = ServerApi.World.GetItem(new AssetLocation(code))
                ?? throw new ArgumentException($"The game has no item named {code}.", nameof(code));
            target.Itemstack = new ItemStack(item, quantity);
            target.MarkDirty();
        });
        return this;
    }

    /// <summary>Seals the barrel, which starts the recipe's sealing time on the calendar.</summary>
    /// <returns>This facade.</returns>
    public BarrelFacade Seal()
    {
        _host.RunOnGameThread(() => Block().GetType().GetMethod("SealBarrel")!.Invoke(Block(), null));
        return this;
    }

    /// <summary>Reads whether the barrel is sealed, and what its slots hold.</summary>
    public BarrelState State => _host.RunOnGameThread(() =>
    {
        object be = Block();
        return new BarrelState(
            (bool)Field(be, "Sealed")!,
            (double)Field(be, "SealedSinceTotalHours")!,
            FirepitFacade.Describe(Inventory()[(int)BarrelSlot.Items]),
            FirepitFacade.Describe(Inventory()[(int)BarrelSlot.Liquid]));
    });

    /// <summary>
    /// Advances the calendar by the span, then runs the barrel's update on its tick listener. A recipe finishes
    /// once the calendar has passed its sealing time, so one step is enough when the span covers it.
    /// </summary>
    /// <param name="span">The game time to advance.</param>
    /// <returns>The game time the calendar advanced.</returns>
    public async Task<TimeSpan> RunAsync(TimeSpan span, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(span, TimeSpan.Zero);
        _host.Calendar.Advance(span);
        // A span of two listener periods guarantees the barrel's check fires once, whatever its phase.
        await _host.TickListeners.FastForwardAsync(_pos, TimeSpan.FromMilliseconds(6000), ct: ct);
        return span;
    }

    private ICoreServerAPI ServerApi => (ICoreServerAPI)_host.Server.Api;

    private object Block() => ServerApi.World.BlockAccessor.GetBlockEntity(_pos)
        ?? throw new InvalidOperationException($"There is no block entity at {_pos}.");

    private InventoryBase Inventory() => ProcessingDriver.InventoryOf(Block());

    private static object? Field(object target, string name) =>
        target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target)
        ?? target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(target);
}

/// <summary>Firepits and barrels: filling them, reading their state, and running their processing for a span of game time.</summary>
public sealed class ProcessingDriver
{
    private readonly EmbeddedServerHost _host;

    internal ProcessingDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>The inventory a block entity exposes, read through its public <c>Inventory</c> property.</summary>
    internal static InventoryBase InventoryOf(object blockEntity) =>
        (InventoryBase)(blockEntity.GetType().GetProperty("Inventory", BindingFlags.Public | BindingFlags.Instance)?.GetValue(blockEntity)
            ?? throw new InvalidOperationException($"{blockEntity.GetType().Name} exposes no inventory."));

    /// <summary>A firepit at a position.</summary>
    /// <param name="pos">The firepit's position.</param>
    public FirepitFacade Firepit(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return new FirepitFacade(_host, pos.Copy());
    }

    /// <summary>A barrel at a position.</summary>
    /// <param name="pos">The barrel's position.</param>
    public BarrelFacade Barrel(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return new BarrelFacade(_host, pos.Copy());
    }
}
