using Xunit;
using Zaldaryon.Pharos.Player;

namespace Zaldaryon.Pharos.Tests.Player;

/// <summary>
/// Pure-logic unit tests for InventoryAutomation.
/// Tests use mock mode - no live client or native libraries required.
/// </summary>
public sealed class InventoryAutomationTests : IDisposable
{
    private readonly InventoryAutomation _automation = new();

    public void Dispose() => _automation.ResetMockState();

    // -------------------------------------------------------------------------
    // Mock mode tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_StartsInMockMode()
    {
        Assert.True(_automation.IsMockMode);
    }

    [Fact]
    public void EnableMockMode_SetsFlag()
    {
        _automation.DisableMockMode();
        _automation.EnableMockMode();

        Assert.True(_automation.IsMockMode);
    }

    [Fact]
    public void DisableMockMode_ClearsFlag()
    {
        _automation.DisableMockMode();

        Assert.False(_automation.IsMockMode);
    }

    // -------------------------------------------------------------------------
    // Slot selection tests
    // -------------------------------------------------------------------------

    [Fact]
    public void SelectSlot_ValidIndex_ReturnsTrue()
    {
        bool result = _automation.SelectSlot(5);

        Assert.True(result);
        Assert.Equal(5, _automation.SelectedSlotIndex);
    }

    [Fact]
    public void SelectSlot_NegativeIndex_ReturnsFalse()
    {
        bool result = _automation.SelectSlot(-1);

        Assert.False(result);
    }

    [Fact]
    public void SelectedSlotIndex_InitiallyZero()
    {
        Assert.Equal(0, _automation.SelectedSlotIndex);
    }

    [Fact]
    public void SelectSlot_UpdatesSelectedSlotIndex()
    {
        _automation.SelectSlot(3);

        Assert.Equal(3, _automation.SelectedSlotIndex);
    }

    // -------------------------------------------------------------------------
    // Slot contents tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GetSlotContents_EmptySlot_ReturnsEmptySnapshot()
    {
        SlotSnapshot snapshot = _automation.GetSlotContents(0);

        Assert.Equal(0, snapshot.SlotIndex);
        Assert.Null(snapshot.ItemCode);
        Assert.Equal(0, snapshot.StackSize);
        Assert.True(snapshot.IsEmpty);
    }

    [Fact]
    public void GetSlotContents_NegativeIndex_ReturnsEmptySnapshot()
    {
        SlotSnapshot snapshot = _automation.GetSlotContents(-1);

        Assert.True(snapshot.IsEmpty);
    }

    [Fact]
    public void SetMockSlotContents_CreatesPopulatedSlot()
    {
        _automation.SetMockSlotContents(0, "game:pickaxe-copper", 1);

        SlotSnapshot snapshot = _automation.GetSlotContents(0);

        Assert.Equal(0, snapshot.SlotIndex);
        Assert.Equal("game:pickaxe-copper", snapshot.ItemCode);
        Assert.Equal(1, snapshot.StackSize);
        Assert.False(snapshot.IsEmpty);
    }

    [Fact]
    public void SetMockSlotContents_NullItemCode_ClearsSlot()
    {
        _automation.SetMockSlotContents(0, "game:pickaxe-copper", 1);
        _automation.SetMockSlotContents(0, null, 0);

        SlotSnapshot snapshot = _automation.GetSlotContents(0);

        Assert.True(snapshot.IsEmpty);
    }

    [Fact]
    public void SetMockSlotContents_ZeroStackSize_ClearsSlot()
    {
        _automation.SetMockSlotContents(0, "game:pickaxe-copper", 1);
        _automation.SetMockSlotContents(0, "game:pickaxe-copper", 0);

        SlotSnapshot snapshot = _automation.GetSlotContents(0);

        Assert.True(snapshot.IsEmpty);
    }

    [Fact]
    public void MockSlotCount_TracksPopulatedSlots()
    {
        _automation.SetMockSlotContents(0, "game:stone", 10);
        _automation.SetMockSlotContents(5, "game:log-oak", 64);

        Assert.Equal(2, _automation.MockSlotCount);
    }

    // -------------------------------------------------------------------------
    // Drag and drop tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DragSlot_SwapsContents()
    {
        _automation.SetMockSlotContents(0, "game:stone", 32);
        _automation.SetMockSlotContents(1, "game:log-oak", 16);

        bool result = _automation.DragSlot(0, 1);

        Assert.True(result);

        SlotSnapshot slot0 = _automation.GetSlotContents(0);
        SlotSnapshot slot1 = _automation.GetSlotContents(1);

        Assert.Equal("game:log-oak", slot0.ItemCode);
        Assert.Equal(16, slot0.StackSize);
        Assert.Equal("game:stone", slot1.ItemCode);
        Assert.Equal(32, slot1.StackSize);
    }

    [Fact]
    public void DragSlot_FromPopulatedToEmpty_MovesItem()
    {
        _automation.SetMockSlotContents(0, "game:stone", 32);

        bool result = _automation.DragSlot(0, 5);

        Assert.True(result);

        SlotSnapshot slot0 = _automation.GetSlotContents(0);
        SlotSnapshot slot5 = _automation.GetSlotContents(5);

        Assert.True(slot0.IsEmpty);
        Assert.Equal("game:stone", slot5.ItemCode);
        Assert.Equal(32, slot5.StackSize);
    }

    [Fact]
    public void DragSlot_FromEmptyToPopulated_MovesItem()
    {
        _automation.SetMockSlotContents(5, "game:stone", 32);

        bool result = _automation.DragSlot(0, 5);

        Assert.True(result);

        SlotSnapshot slot0 = _automation.GetSlotContents(0);
        SlotSnapshot slot5 = _automation.GetSlotContents(5);

        Assert.Equal("game:stone", slot0.ItemCode);
        Assert.True(slot5.IsEmpty);
    }

    [Fact]
    public void DragSlot_NegativeFromIndex_ReturnsFalse()
    {
        bool result = _automation.DragSlot(-1, 0);

        Assert.False(result);
    }

    [Fact]
    public void DragSlot_NegativeToIndex_ReturnsFalse()
    {
        bool result = _automation.DragSlot(0, -1);

        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // Crafting grid tests
    // -------------------------------------------------------------------------

    [Fact]
    public void SetCraftingSlot_ValidCoordinates_ReturnsTrue()
    {
        bool result = _automation.SetCraftingSlot(0, 0, "game:stone");

        Assert.True(result);
    }

    [Fact]
    public void SetCraftingSlot_SetsItem()
    {
        _automation.SetCraftingSlot(1, 1, "game:stick");

        string? code = _automation.GetCraftingSlot(1, 1);

        Assert.Equal("game:stick", code);
    }

    [Fact]
    public void SetCraftingSlot_NullItemCode_ClearsSlot()
    {
        _automation.SetCraftingSlot(0, 0, "game:stone");
        _automation.SetCraftingSlot(0, 0, null);

        string? code = _automation.GetCraftingSlot(0, 0);

        Assert.Null(code);
    }

    [Fact]
    public void SetCraftingSlot_InvalidXCoordinate_ReturnsFalse()
    {
        bool result = _automation.SetCraftingSlot(3, 0, "game:stone");

        Assert.False(result);
    }

    [Fact]
    public void SetCraftingSlot_InvalidYCoordinate_ReturnsFalse()
    {
        bool result = _automation.SetCraftingSlot(0, 3, "game:stone");

        Assert.False(result);
    }

    [Fact]
    public void SetCraftingSlot_NegativeCoordinates_ReturnsFalse()
    {
        bool resultX = _automation.SetCraftingSlot(-1, 0, "game:stone");
        bool resultY = _automation.SetCraftingSlot(0, -1, "game:stone");

        Assert.False(resultX);
        Assert.False(resultY);
    }

    [Fact]
    public void GetCraftingSlot_EmptySlot_ReturnsNull()
    {
        string? code = _automation.GetCraftingSlot(0, 0);

        Assert.Null(code);
    }

    [Fact]
    public void GetCraftingSlot_InvalidCoordinates_ReturnsNull()
    {
        string? code = _automation.GetCraftingSlot(5, 5);

        Assert.Null(code);
    }

    [Fact]
    public void ClearCraftingGrid_RemovesAllItems()
    {
        _automation.SetCraftingSlot(0, 0, "game:stone");
        _automation.SetCraftingSlot(1, 1, "game:stick");
        _automation.SetCraftingSlot(2, 2, "game:plank-oak");

        _automation.ClearCraftingGrid();

        Assert.Null(_automation.GetCraftingSlot(0, 0));
        Assert.Null(_automation.GetCraftingSlot(1, 1));
        Assert.Null(_automation.GetCraftingSlot(2, 2));
        Assert.Equal(0, _automation.MockCraftingGridCount);
    }

    [Fact]
    public void MockCraftingGridCount_TracksPopulatedSlots()
    {
        _automation.SetCraftingSlot(0, 0, "game:stone");
        _automation.SetCraftingSlot(1, 0, "game:stone");
        _automation.SetCraftingSlot(2, 0, "game:stone");

        Assert.Equal(3, _automation.MockCraftingGridCount);
    }

    // -------------------------------------------------------------------------
    // Reset tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ResetMockState_ClearsAllState()
    {
        _automation.SetMockSlotContents(0, "game:stone", 10);
        _automation.SetCraftingSlot(0, 0, "game:stick");
        _automation.SelectSlot(5);

        _automation.ResetMockState();

        Assert.Equal(0, _automation.MockSlotCount);
        Assert.Equal(0, _automation.MockCraftingGridCount);
        Assert.Equal(0, _automation.SelectedSlotIndex);
    }

    // -------------------------------------------------------------------------
    // SlotSnapshot record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void SlotSnapshot_Empty_CreatesEmptySnapshot()
    {
        SlotSnapshot snapshot = SlotSnapshot.Empty(3);

        Assert.Equal(3, snapshot.SlotIndex);
        Assert.Null(snapshot.ItemCode);
        Assert.Equal(0, snapshot.StackSize);
        Assert.True(snapshot.IsEmpty);
    }

    [Fact]
    public void SlotSnapshot_WithItem_CreatesPopulatedSnapshot()
    {
        SlotSnapshot snapshot = SlotSnapshot.WithItem(7, "game:sword-iron", 1);

        Assert.Equal(7, snapshot.SlotIndex);
        Assert.Equal("game:sword-iron", snapshot.ItemCode);
        Assert.Equal(1, snapshot.StackSize);
        Assert.False(snapshot.IsEmpty);
    }

    [Fact]
    public void SlotSnapshot_RecordEquality()
    {
        SlotSnapshot a = SlotSnapshot.WithItem(0, "game:stone", 64);
        SlotSnapshot b = SlotSnapshot.WithItem(0, "game:stone", 64);

        Assert.Equal(a, b);
    }

    // -------------------------------------------------------------------------
    // Crafting grid 3x3 boundary tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CraftingGrid_SupportsAll9Positions()
    {
        for (int x = 0; x < 3; x++)
        {
            for (int y = 0; y < 3; y++)
            {
                string code = $"game:item-{x}-{y}";
                bool result = _automation.SetCraftingSlot(x, y, code);

                Assert.True(result, $"Failed to set crafting slot ({x}, {y})");
                Assert.Equal(code, _automation.GetCraftingSlot(x, y));
            }
        }

        Assert.Equal(9, _automation.MockCraftingGridCount);
    }
}
