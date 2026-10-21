using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Items;

/// <summary>The contents of a bag, read and written the way the game writes them.</summary>
public sealed class BagContents
{
    private readonly ItemStack _stack;
    private readonly IHeldBag _bag;

    internal BagContents(ItemStack stack, IHeldBag bag, IReadOnlyList<ItemSlotBagContent> slots)
    {
        _stack = stack;
        _bag = bag;
        Slots = slots;
    }

    /// <summary>The stack of the bag itself.</summary>
    public ItemStack Stack { get; }

    /// <summary>The slots the bag holds, in the bag's order.</summary>
    public IReadOnlyList<ItemSlotBagContent> Slots { get; }

    /// <summary>The stacks the bag holds.</summary>
    public IReadOnlyList<ItemStack> Contents => [.. Slots.Where(slot => !slot.Empty).Select(slot => slot.Itemstack!)];

    /// <summary>
    /// Puts a stack in a slot of the bag, the way the game does, so the bag's attributes hold it.
    /// </summary>
    /// <param name="slot">The slot of the bag, counted from zero.</param>
    /// <param name="code">The code of the item, such as <c>game:flint</c>.</param>
    /// <param name="quantity">How many items to put in the slot.</param>
    /// <returns>Whether the slot took the stack.</returns>
    public bool Put(int slot, string code, int quantity = 1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (slot >= Slots.Count) return false;

        ItemSlotBagContent target = Slots[slot];
        CollectibleObject? collectible = target.Inventory?.Api?.World?.GetItem(new AssetLocation(code));
        if (collectible == null) return false;

        ItemStack stack = new(collectible, quantity);
        if (!target.CanHold(new DummySlot(stack))) return false;

        target.Itemstack = stack;
        _bag.Store(_stack, target);
        return true;
    }

    /// <summary>How many items of a code the bag holds.</summary>
    /// <param name="code">The code of the item, such as <c>game:flint</c>.</param>
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

/// <summary>The gear a player wears, and what it gives them.</summary>
public sealed class GearStats
{
    internal GearStats(float warmth, StatModifiers? statModifiers, ProtectionModifiers? protection)
    {
        Warmth = warmth;
        StatModifiers = statModifiers;
        Protection = protection;
    }

    /// <summary>How much warmth the worn items give.</summary>
    public float Warmth { get; }

    /// <summary>The stat modifiers the worn items give, or null when none is worn.</summary>
    public StatModifiers? StatModifiers { get; }

    /// <summary>The protection the worn armor gives, or null when none is worn.</summary>
    public ProtectionModifiers? Protection { get; }
}

/// <summary>The gear slots of a player, and the stats the worn items give.</summary>
public sealed class GearDriver(ServerTestPlayer player)
{
    /// <summary>The code of each gear slot, in the order the character's inventory holds them.</summary>
    private static readonly (string Code, EnumCharacterDressType DressType)[] s_slots =
    [
        ("boots", EnumCharacterDressType.Foot),
        ("gloves", EnumCharacterDressType.Hand),
        ("cape", EnumCharacterDressType.Shoulder),
        ("hat", EnumCharacterDressType.Head),
        ("trousers", EnumCharacterDressType.LowerBody),
        ("shirt", EnumCharacterDressType.UpperBody),
        ("pullover", EnumCharacterDressType.UpperBodyOver),
        ("necklace", EnumCharacterDressType.Neck),
        ("bracers", EnumCharacterDressType.Arm),
        ("belt", EnumCharacterDressType.Waist),
        ("medal", EnumCharacterDressType.Emblem),
        ("mask", EnumCharacterDressType.Face),
        ("armorhead", EnumCharacterDressType.ArmorHead),
        ("armorbody", EnumCharacterDressType.ArmorBody),
        ("armorlegs", EnumCharacterDressType.ArmorLegs),
    ];

    /// <summary>
    /// Puts an item into a gear slot on the server, the way the character dialog does.
    /// </summary>
    /// <param name="slotCode">The slot's code, such as <c>armorbody</c>.</param>
    /// <param name="code">The code of the item to wear, such as <c>game:armor-leather</c>.</param>
    /// <param name="quantity">How many items to put in the slot.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>Whether the slot took the item.</returns>
    public async Task<bool> EquipAsync(string slotCode, string code, int quantity = 1, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);

        EnumCharacterDressType? dressType = s_slots
            .Where(slot => string.Equals(slot.Code, slotCode, StringComparison.OrdinalIgnoreCase))
            .Select(slot => slot.DressType)
            .FirstOrDefault();
        if (dressType == null)
        {
            throw new ArgumentException(
                $"There is no gear slot named '{slotCode}'. The slots are: {string.Join(", ", s_slots.Select(slot => slot.Code))}.",
                nameof(slotCode));
        }

        bool equipped = player.RunBotOnGameThread(() =>
        {
            IPlayer? serverPlayer = player.Player;
            if (serverPlayer == null) return false;

            IInventory? character = serverPlayer.InventoryManager.GetOwnInventory("character");
            if (character == null) return false;

            int index = (int)dressType.Value;
            ItemSlot? slot = character[index];
            if (slot == null) return false;

            CollectibleObject? collectible = serverPlayer.Entity?.World?.GetItem(new AssetLocation(code));
            if (collectible == null) return false;

            ItemStack stack = new(collectible, quantity);
            if (!slot.CanHold(new DummySlot(stack))) return false;

            slot.Itemstack = stack;
            slot.MarkDirty();
            return true;
        });

        ct.ThrowIfCancellationRequested();
        return equipped;
    }

    /// <summary>What the worn items give the player.</summary>
    public GearStats Stats => player.RunBotOnGameThread(() =>
    {
        IPlayer? serverPlayer = player.Player;
        if (serverPlayer == null) return new GearStats(0, null, null);

        IInventory? character = serverPlayer.InventoryManager.GetOwnInventory("character");
        if (character == null) return new GearStats(0, null, null);

        float warmth = 0;
        StatModifiers? statModifiers = null;
        ProtectionModifiers? protection = null;
        foreach (ItemSlot slot in character)
        {
            if (slot?.Itemstack == null) continue;
            foreach (Vintagestory.API.Common.CollectibleBehavior behavior in slot.Itemstack.Collectible.CollectibleBehaviors)
            {
                if (behavior is IWearable wearable) warmth += wearable.GetWarmth(slot);
                if (behavior is IWearableStatsSupplier supplier)
                {
                    statModifiers ??= supplier.GetStatModifiers(slot);
                    protection ??= supplier.GetProtectionModifiers(slot);
                }
            }
        }

        return new GearStats(warmth, statModifiers, protection);
    });
}
