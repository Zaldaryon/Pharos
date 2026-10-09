using System;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// Concrete inventory accessor providing inspection and manipulation of player inventory slots and hotbar selections.
/// </summary>
public sealed class PlayerInventoryAccessor : IPlayerInventoryAccessor
{
    // Read on each use, so the class follows the client into a new game session after a reconnect.
    private readonly Func<ClientMain> _game;

    private ClientMain _client => _game();
    private int _fallbackActiveHotbarSlotIndex;

    public PlayerInventoryAccessor(ClientMain client)
        : this(Fixed(client ?? throw new ArgumentNullException(nameof(client))))
    {
    }

    internal PlayerInventoryAccessor(Func<ClientMain> game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
    }

    private static Func<ClientMain> Fixed(ClientMain client) => () => client;

    private IPlayerInventoryManager? Manager => _client.player?.InventoryManager;

    public IInventory? Hotbar => Manager?.GetHotbarInventory();

    public IInventory? Backpack => Manager?.GetOwnInventory(GlobalConstants.backpackInvClassName);

    public IInventory? Crafting => Manager?.GetOwnInventory(GlobalConstants.craftingInvClassName);

    public int ActiveHotbarSlotIndex
    {
        get => Manager?.ActiveHotbarSlotNumber ?? _fallbackActiveHotbarSlotIndex;
        set => SelectHotbarSlot(value);
    }

    public ItemSlot? ActiveHotbarSlot => Manager?.ActiveHotbarSlot;

    public ItemStack? ActiveHotbarItem => ActiveHotbarSlot?.Itemstack;

    public ItemSlot? GetSlot(string inventoryName, int slotId)
    {
        if (Manager == null || string.IsNullOrEmpty(inventoryName))
        {
            return null;
        }

        IInventory? inv = Manager.GetInventory(inventoryName) ?? Manager.GetOwnInventory(inventoryName);
        if (inv != null && slotId >= 0 && slotId < inv.Count)
        {
            return inv[slotId];
        }

        return null;
    }

    public bool SelectHotbarSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= (Hotbar?.Count ?? 10))
        {
            return false;
        }

        _fallbackActiveHotbarSlotIndex = slotIndex;

        if (Manager != null)
        {
            Manager.ActiveHotbarSlotNumber = slotIndex;
            return true;
        }

        return true;
    }

    public void ClearHotbar()
    {
        IInventory? hotbar = Hotbar;
        if (hotbar == null)
        {
            return;
        }

        for (int i = 0; i < hotbar.Count; i++)
        {
            ItemSlot? slot = hotbar[i];
            if (slot != null)
            {
                slot.Itemstack = null;
            }
        }
    }

    public void ClearAll()
    {
        if (Manager == null)
        {
            return;
        }

        try
        {
            Manager.DiscardAll();
        }
        catch
        {
            // Fallback manually clearing opened inventories
            foreach (IInventory inv in Manager.OpenedInventories)
            {
                for (int i = 0; i < inv.Count; i++)
                {
                    ItemSlot? slot = inv[i];
                    if (slot != null)
                    {
                        slot.Itemstack = null;
                    }
                }
            }
        }
    }
}
