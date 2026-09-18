using System.Collections.Generic;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// Immutable snapshot of an inventory slot contents.
/// Used for deterministic slot inspection without live server state.
/// </summary>
/// <param name="SlotIndex">The 0-based index of the slot within its inventory.</param>
/// <param name="ItemCode">The item/block code string, or null if empty.</param>
/// <param name="StackSize">The number of items in the stack, 0 if empty.</param>
/// <param name="IsEmpty">True if the slot contains no items.</param>
public sealed record SlotSnapshot(
    int SlotIndex,
    string? ItemCode,
    int StackSize,
    bool IsEmpty)
{
    /// <summary>
    /// Creates an empty slot snapshot for the given index.
    /// </summary>
    public static SlotSnapshot Empty(int slotIndex) => new(slotIndex, null, 0, true);

    /// <summary>
    /// Creates a populated slot snapshot.
    /// </summary>
    public static SlotSnapshot WithItem(int slotIndex, string itemCode, int stackSize) =>
        new(slotIndex, itemCode, stackSize, false);
}
