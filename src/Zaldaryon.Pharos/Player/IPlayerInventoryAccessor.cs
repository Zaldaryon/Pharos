using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// Provides programmatic access and manipulation for player inventory slots, hotbars, and active selections.
/// </summary>
public interface IPlayerInventoryAccessor
{
    /// <summary>
    /// Gets the player's hotbar inventory if available.
    /// </summary>
    IInventory? Hotbar { get; }

    /// <summary>
    /// Gets the player's backpack inventory if available.
    /// </summary>
    IInventory? Backpack { get; }

    /// <summary>
    /// Gets the player's crafting grid inventory if available.
    /// </summary>
    IInventory? Crafting { get; }

    /// <summary>
    /// Gets or sets the 0-indexed active hotbar slot.
    /// </summary>
    int ActiveHotbarSlotIndex { get; set; }

    /// <summary>
    /// Gets the currently selected hotbar slot.
    /// </summary>
    ItemSlot? ActiveHotbarSlot { get; }

    /// <summary>
    /// Gets the item stack currently held in the active hotbar slot, or null if empty.
    /// </summary>
    ItemStack? ActiveHotbarItem { get; }

    /// <summary>
    /// Retrieves a slot by inventory class name and slot index.
    /// </summary>
    ItemSlot? GetSlot(string inventoryName, int slotId);

    /// <summary>
    /// Selects an active hotbar slot by index. Returns true if valid and selected.
    /// </summary>
    bool SelectHotbarSlot(int slotIndex);

    /// <summary>
    /// Clears all items from the player's hotbar.
    /// </summary>
    void ClearHotbar();

    /// <summary>
    /// Clears all items across all inventories belonging to the player.
    /// </summary>
    void ClearAll();
}
