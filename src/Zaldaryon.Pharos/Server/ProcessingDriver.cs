using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
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
            CollectibleObject collectible = ProcessingDriver.CollectibleNamed(ServerApi.World, code);
            target.Itemstack = new ItemStack(collectible, quantity);
            ProcessingDriver.MarkSlot((BlockEntity)Block(), target);
        });
        return this;
    }

    /// <summary>
    /// Lights the firepit with a lit torch the player holds, as the game does: the player holds the torch against
    /// the firepit for a few seconds, and the firepit then allows its fuel to catch by itself. Once allowed, each
    /// new piece of firewood lights when the one before it burns out, so the fire only needs fuel to keep going.
    /// </summary>
    /// <param name="player">The joined player who holds the lit torch.</param>
    /// <param name="secondsHeld">How long the torch is held against the firepit, in seconds.</param>
    /// <returns>This facade.</returns>
    /// <exception cref="InvalidOperationException">The player does not hold a lit torch, or the torch is held too briefly.</exception>
    public FirepitFacade IgniteWithTorch(ServerTestPlayer player, double secondsHeld = 4)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(secondsHeld);
        _host.RunOnGameThread(() =>
        {
            ItemSlot hand = player.Entity?.RightHandItemSlot
                ?? throw new InvalidOperationException($"{player.PlayerUID} has not joined the server.");
            string held = hand.Itemstack?.Collectible.Code.ToString() ?? "nothing";
            if (!held.Contains("torch", StringComparison.Ordinal) || !held.Contains("lit", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The player holds {held}, not a lit torch.");
            }

            object be = Block();
            object state = be.GetType().GetMethod("GetIgnitableState")!.Invoke(be, [(float)secondsHeld])!;
            if (!string.Equals(state.ToString(), "IgniteNow", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The firepit is not ignitable yet: {state} after {secondsHeld} s.");
            }

            Block firepit = ServerApi.World.BlockAccessor.GetBlock(_pos);
            object[] args = [player.Entity!, _pos.Copy(), (float)secondsHeld, EnumHandling.PassThrough];
            firepit.GetType().GetMethod("OnTryIgniteBlockOver")!.Invoke(firepit, args);
        });
        return this;
    }

    /// <summary>
    /// Puts an ingredient into a slot of the cooking container in the input slot, such as a fired clay pot: the
    /// water and vegetables of a soup, for example. The container's slots are the game's <c>CookingSlots</c>.
    /// </summary>
    /// <param name="index">The container slot, from 0.</param>
    /// <param name="code">The item code, such as <c>game:waterportion</c> or <c>game:vegetable-carrot</c>.</param>
    /// <param name="quantity">The stack size. A liquid portion is a hundredth of a litre.</param>
    /// <returns>This facade, so fills can chain.</returns>
    /// <exception cref="InvalidOperationException">The input slot holds no cooking container.</exception>
    public FirepitFacade FillCookingSlot(int index, string code, int quantity = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        _host.RunOnGameThread(() =>
        {
            ItemSlot[] slots = (ItemSlot[])(ProcessingDriver.Member(Block(), "otherCookingSlots")
                ?? throw new InvalidOperationException("The firepit exposes no cooking slots."));
            if (slots.Length == 0) throw new InvalidOperationException("The input slot holds no cooking container, so there are no cooking slots.");
            if (index < 0 || index >= slots.Length) throw new ArgumentOutOfRangeException(nameof(index), $"The container has {slots.Length} cooking slots.");
            CollectibleObject collectible = ProcessingDriver.CollectibleNamed(ServerApi.World, code);
            slots[index].Itemstack = new ItemStack(collectible, quantity);
            slots[index].MarkDirty();
            ProcessingDriver.SyncBlockEntity((BlockEntity)Block());
        });
        return this;
    }

    /// <summary>
    /// The recipe a cooked container holds and how many servings it has, from the output stack's attributes, or null
    /// when the output is not a cooked container.
    /// </summary>
    public (string Recipe, int Servings)? OutputMeal => _host.RunOnGameThread<(string Recipe, int Servings)?>(() =>
    {
        ItemStack? output = Inventory()[(int)FirepitSlot.Output].Itemstack;
        string? recipe = output?.Attributes.GetString("recipeCode");
        if (recipe is null) return null;
        return (recipe, output!.Attributes.GetInt("quantityServings", 1));
    });

    /// <summary>
    /// Listens for the firepit's inventory slots changing, as the game's sync does. The listener gets the slot index.
    /// Dispose the returned handle to stop listening.
    /// </summary>
    /// <param name="onChanged">Called with the index of each changed slot.</param>
    public IDisposable WatchSlotChanges(Action<int> onChanged)
    {
        ArgumentNullException.ThrowIfNull(onChanged);
        InventoryBase inventory = _host.RunOnGameThread(() => Inventory());
        return _host.RunOnGameThread(() => new SlotWatch(inventory, onChanged));
    }

    private sealed class SlotWatch : IDisposable
    {
        private readonly InventoryBase _inventory;
        private readonly Action<int> _onChanged;

        public SlotWatch(InventoryBase inventory, Action<int> onChanged)
        {
            _inventory = inventory;
            _onChanged = onChanged;
            _inventory.SlotModified += _onChanged;
        }

        public void Dispose() => _inventory.SlotModified -= _onChanged;
    }

    /// <summary>The pieces of fuel left in the fuel slot.</summary>
    public int FuelLeft => _host.RunOnGameThread(() => Inventory()[(int)FirepitSlot.Fuel].Itemstack?.StackSize ?? 0);

    /// <summary>
    /// Lights the firepit with a firestarter, the hand drill the player turns against it. The drill turns for
    /// the given seconds, and each finished turn has a one-in-four chance of catching, as the game's firestarter does.
    /// The turns are repeated until the firepit catches, or until the attempts run out.
    /// </summary>
    /// <param name="player">The joined player who holds the firestarter.</param>
    /// <param name="secondsHeld">How long each turn lasts, in seconds. The firepit lights only after more than three.</param>
    /// <param name="attempts">The most turns to try.</param>
    /// <returns>This facade.</returns>
    /// <exception cref="InvalidOperationException">The player does not hold a firestarter, the turn is too short, or the firepit did not catch.</exception>
    public FirepitFacade IgniteWithFirestarter(ServerTestPlayer player, double secondsHeld = 4, int attempts = 40)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(secondsHeld);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(attempts);
        _host.RunOnGameThread(() =>
        {
            EntityAgent entity = player.Entity ?? throw new InvalidOperationException($"{player.PlayerUID} has not joined the server.");
            ItemSlot hand = entity.RightHandItemSlot;
            string held = hand.Itemstack?.Collectible.Code.ToString() ?? "nothing";
            if (!string.Equals(held, "game:firestarter", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The player holds {held}, not a firestarter.");
            }

            Block firepit = ServerApi.World.BlockAccessor.GetBlock(_pos);
            BlockSelection selection = new(_pos.Copy(), BlockFacing.UP, firepit);
            CollectibleObject drill = hand.Itemstack.Collectible;
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                if (hand.Empty) throw new InvalidOperationException("The firestarter broke before the firepit caught.");
                drill.GetType().GetMethod("OnHeldInteractStop")!.Invoke(drill, [(float)secondsHeld, hand, entity, selection, null]);
                if ((bool)(ProcessingDriver.Member(Block(), "canIgniteFuel") ?? false)) return;
            }

            throw new InvalidOperationException($"The firepit did not catch after {attempts} turns of {secondsHeld} s.");
        });
        return this;
    }

    /// <summary>Adds firewood to the fuel slot as a player does while the fire burns. Stacks merge, as they do in the game.</summary>
    /// <param name="code">The fuel item code, such as <c>game:firewood</c>.</param>
    /// <param name="quantity">How many pieces to add.</param>
    /// <returns>This facade.</returns>
    public FirepitFacade AddFuel(string code, int quantity = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        _host.RunOnGameThread(() =>
        {
            ItemSlot fuel = Inventory()[(int)FirepitSlot.Fuel];
            CollectibleObject collectible = ProcessingDriver.CollectibleNamed(ServerApi.World, code);
            if (fuel.Empty)
            {
                fuel.Itemstack = new ItemStack(collectible, quantity);
            }
            else if (fuel.Itemstack.Collectible.Code.Equals(collectible.Code))
            {
                fuel.Itemstack.StackSize += quantity;
            }
            else
            {
                throw new InvalidOperationException($"The fuel slot holds {fuel.Itemstack.Collectible.Code}, not {code}.");
            }
            ProcessingDriver.MarkSlot((BlockEntity)Block(), Inventory()[(int)FirepitSlot.Fuel]);
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
            CollectibleObject collectible = ProcessingDriver.CollectibleNamed(ServerApi.World, code);
            target.Itemstack = new ItemStack(collectible, quantity);
            ProcessingDriver.MarkSlot((BlockEntity)Block(), target);
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

/// <summary>The state of a quern.</summary>
/// <param name="Input">The code and quantity of the grain in the input slot, or null when it is empty.</param>
/// <param name="Output">The code and quantity of the ground product, or null when it is empty.</param>
/// <param name="GrindProgress">The grinding progress of the current item, from 0 to 1.</param>
public sealed record QuernState((string Code, int Quantity)? Input, (string Code, int Quantity)? Output, float GrindProgress);

/// <summary>Fills and reads a quern, and grinds by a player holding the quern's handle for a span of game time.</summary>
public sealed class QuernFacade
{
    /// <summary>How often the holding player's interaction is renewed in simulated time. The server drops a grinder after one second without one.</summary>
    private static readonly TimeSpan RenewEvery = TimeSpan.FromMilliseconds(200);

    private readonly EmbeddedServerHost _host;
    private readonly BlockPos _pos;

    internal QuernFacade(EmbeddedServerHost host, BlockPos pos)
    {
        _host = host;
        _pos = pos;
    }

    /// <summary>Puts grain into the quern's input slot, through the block entity's inventory so it syncs.</summary>
    /// <param name="code">The grain item code, such as <c>game:grain-spelt</c>.</param>
    /// <param name="quantity">The stack size.</param>
    /// <returns>This facade, so fills can chain.</returns>
    public QuernFacade Fill(string code, int quantity = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        _host.RunOnGameThread(() =>
        {
            ItemSlot target = (ItemSlot)ProcessingDriver.Member(Block(), "InputSlot")!;
            CollectibleObject collectible = ProcessingDriver.CollectibleNamed(ServerApi.World, code);
            target.Itemstack = new ItemStack(collectible, quantity);
            ProcessingDriver.MarkSlot((BlockEntity)Block(), target);
        });
        return this;
    }

    /// <summary>Reads the quern's input, output and grinding progress.</summary>
    public QuernState State => _host.RunOnGameThread(() =>
    {
        object be = Block();
        float progress = (float)ProcessingDriver.Member(be, "inputGrindTime")!;
        float max = Convert.ToSingle(be.GetType().GetMethod("maxGrindingTime")!.Invoke(be, null));
        return new QuernState(
            FirepitFacade.Describe((ItemSlot)ProcessingDriver.Member(be, "InputSlot")!),
            FirepitFacade.Describe((ItemSlot)ProcessingDriver.Member(be, "OutputSlot")!),
            max > 0 ? Math.Clamp(progress / max, 0, 1) : 0);
    });

    /// <summary>
    /// Grinds for a span of game time while a player holds the quern's handle. The game grinds only while a
    /// player's interaction is renewed, so the span renews it every 200 simulated milliseconds, as a player
    /// holding the mouse button does, and ends the interaction when the span is over.
    /// </summary>
    /// <param name="player">The joined player who grinds.</param>
    /// <param name="span">The game time to hold the handle for.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task GrindAsync(ServerTestPlayer player, TimeSpan span, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentOutOfRangeException.ThrowIfLessThan(span, TimeSpan.Zero);
        IPlayer? byPlayer = _host.RunOnGameThread(() => player.Player)
            ?? throw new InvalidOperationException($"{player.PlayerUID} has not joined the server.");
        BlockSelection handle = _host.RunOnGameThread(() => new BlockSelection(_pos.Copy(), BlockFacing.UP, ((BlockEntity)Block()).Block)
        {
            SelectionBoxIndex = 1,
        });

        bool started = _host.RunOnGameThread(() => (bool)Interact(byPlayer, handle, "OnBlockInteractStart"));
        if (!started) throw new InvalidOperationException($"The quern at {_pos} did not start grinding for {player.PlayerUID}.");
        try
        {
            TimeSpan played = TimeSpan.Zero;
            while (played < span)
            {
                ct.ThrowIfCancellationRequested();
                TimeSpan slice = span - played < RenewEvery ? span - played : RenewEvery;
                _host.RunOnGameThread(() => Interact(byPlayer, handle, "OnBlockInteractStep", (float)slice.TotalSeconds));
                await _host.TickListeners.FastForwardAsync(_pos, slice, ct: ct);
                played += slice;
            }
        }
        finally
        {
            _host.RunOnGameThread(() => Interact(byPlayer, handle, "OnBlockInteractStop", (float)span.TotalSeconds));
        }
    }

    private object Interact(IPlayer byPlayer, BlockSelection handle, string method, float seconds = 0)
    {
        object block = ServerApi.World.BlockAccessor.GetBlock(_pos);
        MethodInfo info = block.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"The quern block has no {method} method.");
        object[] args = method == "OnBlockInteractStart"
            ? [ServerApi.World, byPlayer, handle]
            : method == "OnBlockInteractStep"
                ? [seconds, ServerApi.World, byPlayer, handle]
                : [seconds, ServerApi.World, byPlayer, handle];
        return info.Invoke(block, args) ?? false;
    }

    private ICoreServerAPI ServerApi => (ICoreServerAPI)_host.Server.Api;

    private object Block() => ServerApi.World.BlockAccessor.GetBlockEntity(_pos)
        ?? throw new InvalidOperationException($"There is no block entity at {_pos}.");
}

/// <summary>The slots of a clay oven, in the order the game stores them. Slot 0 holds fuel or the first bakeable.</summary>
public enum OvenSlot
{
    /// <summary>The first slot, which holds fuel while the oven is unlit.</summary>
    First = 0,

    /// <summary>The second bakeable slot.</summary>
    Second = 1,

    /// <summary>The third bakeable slot.</summary>
    Third = 2,

    /// <summary>The fourth bakeable slot.</summary>
    Fourth = 3,
}

/// <summary>The state of a clay oven.</summary>
/// <param name="Temperature">The oven temperature, in degrees Celsius.</param>
/// <param name="Burning">Whether the oven is lit.</param>
/// <param name="Slots">The code and quantity in each slot, or null for an empty slot.</param>
public sealed record OvenState(float Temperature, bool Burning, IReadOnlyList<(string Code, int Quantity)?> Slots);

/// <summary>Fills, lights and reads a clay oven, and bakes for a span of game time.</summary>
public sealed class OvenFacade
{
    private readonly EmbeddedServerHost _host;
    private readonly BlockPos _pos;

    internal OvenFacade(EmbeddedServerHost host, BlockPos pos)
    {
        _host = host;
        _pos = pos;
    }

    /// <summary>Puts a stack into an oven slot, through the block entity's inventory so it syncs.</summary>
    /// <param name="slot">The slot to fill.</param>
    /// <param name="code">The item code, such as <c>game:firewood</c> or <c>game:dough-spelt</c>.</param>
    /// <param name="quantity">The stack size.</param>
    /// <returns>This facade, so fills can chain.</returns>
    public OvenFacade Fill(OvenSlot slot, string code, int quantity = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        _host.RunOnGameThread(() =>
        {
            ItemSlot target = ProcessingDriver.InventoryOf(Block())[(int)slot];
            CollectibleObject collectible = ProcessingDriver.CollectibleNamed(ServerApi.World, code);
            target.Itemstack = new ItemStack(collectible, quantity);
            ProcessingDriver.MarkSlot((BlockEntity)Block(), target);
        });
        return this;
    }

    /// <summary>
    /// Lights the oven with a lit torch the player holds against it for a few seconds, as the game does. The oven
    /// then burns the fuel in its first slot.
    /// </summary>
    /// <param name="player">The joined player who holds the lit torch.</param>
    /// <param name="secondsHeld">How long the torch is held against the oven, in seconds.</param>
    /// <returns>This facade.</returns>
    /// <exception cref="InvalidOperationException">The player does not hold a lit torch, or the oven does not light.</exception>
    public OvenFacade IgniteWithTorch(ServerTestPlayer player, double secondsHeld = 5)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(secondsHeld);
        _host.RunOnGameThread(() =>
        {
            EntityAgent entity = player.Entity ?? throw new InvalidOperationException($"{player.PlayerUID} has not joined the server.");
            string held = entity.RightHandItemSlot.Itemstack?.Collectible.Code.ToString() ?? "nothing";
            if (!held.Contains("torch", StringComparison.Ordinal) || !held.Contains("lit", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The player holds {held}, not a lit torch.");
            }

            Block oven = ServerApi.World.BlockAccessor.GetBlock(_pos);
            object state = oven.GetType().GetMethod("OnTryIgniteBlock")!.Invoke(oven, [entity, _pos.Copy(), (float)secondsHeld])!;
            if (!string.Equals(state.ToString(), "IgniteNow", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"The oven is not ignitable yet: {state} after {secondsHeld} s.");
            }

            object[] args = [entity, _pos.Copy(), (float)secondsHeld, EnumHandling.PassThrough];
            oven.GetType().GetMethod("OnTryIgniteBlockOver")!.Invoke(oven, args);
            if (!(bool)ProcessingDriver.Member(Block(), "IsBurning")!)
            {
                throw new InvalidOperationException("The oven did not light. It needs fuel in its first slot.");
            }
        });
        return this;
    }

    /// <summary>
    /// Puts a bakeable item in the oven through the oven's own rules: it refuses while the oven burns or holds fuel,
    /// and takes the item into the first free bakeable slot. The bread is only baked while the oven is hot.
    /// </summary>
    /// <param name="code">The item code, such as <c>game:dough-spelt</c>.</param>
    /// <returns>This facade.</returns>
    /// <exception cref="InvalidOperationException">The oven refused the item.</exception>
    public OvenFacade PutIn(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _host.RunOnGameThread(() =>
        {
            CollectibleObject collectible = ProcessingDriver.CollectibleNamed(ServerApi.World, code);
            object be = Block();
            var source = new DummySlot(new ItemStack(collectible, 1));
            bool put = (bool)(be.GetType().GetMethod("TryPut", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)!
                .Invoke(be, [source]) ?? false);
            if (!put) throw new InvalidOperationException($"The oven refused {code}. It cannot take bakeables while it burns or holds fuel.");
        });
        return this;
    }

    /// <summary>Removes the fuel from the first slot, as a player does once the oven is out. The oven refuses while it burns.</summary>
    /// <returns>This facade.</returns>
    /// <exception cref="InvalidOperationException">The oven is still burning.</exception>
    public OvenFacade RemoveFuel()
    {
        _host.RunOnGameThread(() =>
        {
            if ((bool)ProcessingDriver.Member(Block(), "IsBurning")!) throw new InvalidOperationException("The oven is still burning, so its fuel cannot be removed.");
            ItemSlot first = ProcessingDriver.InventoryOf(Block())[0];
            first.Itemstack = null;
            ProcessingDriver.MarkSlot((BlockEntity)Block(), first);
        });
        return this;
    }

    /// <summary>Reads the oven's temperature, whether it burns, and every slot.</summary>
    public OvenState State => _host.RunOnGameThread(() =>
    {
        object be = Block();
        InventoryBase inventory = ProcessingDriver.InventoryOf(be);
        var slots = new List<(string Code, int Quantity)?>();
        for (int i = 0; i < 4; i++) slots.Add(FirepitFacade.Describe(inventory[i]));
        return new OvenState(
            (float)ProcessingDriver.Member(be, "ovenTemperature")!,
            (bool)ProcessingDriver.Member(be, "IsBurning")!,
            slots);
    });

    /// <summary>
    /// Plays game time forward for the oven. Its burn and bake update runs on a tick listener measured in real
    /// seconds, so the span is simulated through that listener with fixed steps.
    /// </summary>
    /// <param name="span">The game time to play.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<TimeSpan> RunAsync(TimeSpan span, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(span, TimeSpan.Zero);
        FastForwardResult result = await _host.TickListeners.FastForwardAsync(_pos, span, ct: ct);
        return result.Simulated;
    }

    private ICoreServerAPI ServerApi => (ICoreServerAPI)_host.Server.Api;

    private object Block() => ServerApi.World.BlockAccessor.GetBlockEntity(_pos)
        ?? throw new InvalidOperationException($"There is no block entity at {_pos}.");
}

/// <summary>Firepits and barrels: filling them, reading their state, and running their processing for a span of game time.</summary>
public sealed class ProcessingDriver
{
    private readonly EmbeddedServerHost _host;

    internal ProcessingDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>Marks a block entity dirty with a sync, so its inventory reaches the clients as the game sends it.</summary>
    internal static void SyncBlockEntity(BlockEntity blockEntity) => blockEntity.MarkDirty(true);

    /// <summary>
    /// Reports a changed slot the way the game does: the slot's own <c>MarkDirty</c> tells its inventory, which fires the
    /// slot-modified event that the block entity listens to for syncing. The block entity is then marked dirty with a sync.
    /// </summary>
    internal static void MarkSlot(BlockEntity blockEntity, ItemSlot slot)
    {
        slot.MarkDirty();
        SyncBlockEntity(blockEntity);
    }

    /// <summary>The item or block with a code. Containers such as the clay pot are blocks, so both are searched.</summary>
    internal static CollectibleObject CollectibleNamed(IWorldAccessor world, string code)
    {
        var location = new AssetLocation(code);
        return (CollectibleObject?)world.GetItem(location)
            ?? world.GetBlock(location)
            ?? throw new ArgumentException($"The game has no item or block named {code}.", nameof(code));
    }

    /// <summary>A public field or property of a game object, read by name.</summary>
    internal static object? Member(object target, string name)
    {
        BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        return target.GetType().GetField(name, flags)?.GetValue(target)
            ?? target.GetType().GetProperty(name, flags)?.GetValue(target)
            ?? throw new InvalidOperationException($"{target.GetType().Name} has no public member {name}.");
    }

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

    /// <summary>A quern at a position.</summary>
    /// <param name="pos">The quern's position.</param>
    public QuernFacade Quern(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return new QuernFacade(_host, pos.Copy());
    }

    /// <summary>A clay oven at a position.</summary>
    /// <param name="pos">The oven's position.</param>
    public OvenFacade Oven(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return new OvenFacade(_host, pos.Copy());
    }

    /// <summary>A barrel at a position.</summary>
    /// <param name="pos">The barrel's position.</param>
    public BarrelFacade Barrel(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return new BarrelFacade(_host, pos.Copy());
    }
}
