using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.XUnit;

public sealed partial class HeadlessPlayerBot
{
    /// <summary>
    /// Queues a real shift-click (packet 7) without ticking. The container must already be open for this player.
    /// Use inside Host.SameTick to contend for one stack.
    /// </summary>
    public void QueueTakeSlot(BlockPos position, int slot)
    {
        ArgumentNullException.ThrowIfNull(position);
        _owner.RunBotOnGameThread(() =>
        {
            if (_owner.RequireBotHost().Api.World.BlockAccessor.GetBlockEntity(position) is not IBlockEntityContainer container)
                throw new ArgumentException("The block has no inventory.", nameof(position));
            var inventory = RequireOpenInventory(container.Inventory.InventoryID, slot);
            QueueInventoryPacket(new Packet_Client
            {
                Id = 7,
                ActivateInventorySlot = new Packet_ActivateInventorySlot
                {
                    TargetInventoryId = inventory.InventoryID,
                    TargetSlot = slot,
                    TargetLastChanged = inventory.LastChanged,
                    MouseButton = (int)EnumMouseButton.Left,
                    Modifiers = (int)EnumModifierKey.SHIFT,
                    Priority = (int)EnumMergePriority.AutoMerge,
                },
            });
        });
    }

    /// <summary>Queues packet 8 between two open inventories without ticking; quantity zero means the whole stack.</summary>
    public void QueueMoveSlot(string sourceInventoryId, int sourceSlot, string targetInventoryId, int targetSlot, int quantity = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        _owner.RunBotOnGameThread(() =>
        {
            var source = RequireOpenInventory(sourceInventoryId, sourceSlot);
            var target = RequireOpenInventory(targetInventoryId, targetSlot);
            QueueInventoryPacket(new Packet_Client
            {
                Id = 8,
                MoveItemstack = new Packet_MoveItemstack
                {
                    SourceInventoryId = sourceInventoryId,
                    TargetInventoryId = targetInventoryId,
                    SourceSlot = sourceSlot,
                    TargetSlot = targetSlot,
                    Quantity = quantity,
                    SourceLastChanged = source.LastChanged,
                    TargetLastChanged = target.LastChanged,
                    MouseButton = (int)EnumMouseButton.Left,
                    Priority = (int)EnumMergePriority.AutoMerge,
                },
            });
        });
    }

    private InventoryBase RequireOpenInventory(string id, int slot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentOutOfRangeException.ThrowIfNegative(slot);
        if (!RequirePlayer().InventoryManager.GetInventory(id, out var inventory) || inventory == null)
            throw new ArgumentException($"Inventory '{id}' is not open for this bot.", nameof(id));
        if (slot >= inventory.Count) throw new ArgumentOutOfRangeException(nameof(slot));
        return inventory;
    }

    private void QueueInventoryPacket(Packet_Client packet) => _owner.RequireBotConnection().SendClientPacket(packet);
}
