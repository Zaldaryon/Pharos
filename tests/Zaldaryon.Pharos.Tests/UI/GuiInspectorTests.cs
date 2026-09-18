using Xunit;
using Zaldaryon.Pharos.UI;

namespace Zaldaryon.Pharos.Tests.UI;

/// <summary>
/// Pure-logic unit tests for GuiInspector using mock mode.
/// No live client or native libraries required.
/// </summary>
public sealed class GuiInspectorTests : IDisposable
{
    private readonly GuiInspector _inspector = new();

    public void Dispose() => _inspector.ClearMockState();

    // -------------------------------------------------------------------------
    // Dialog tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GetOpenDialogs_InitiallyEmpty()
    {
        var dialogs = _inspector.GetOpenDialogs();

        Assert.Empty(dialogs);
    }

    [Fact]
    public void AddMockDialog_MakesDialogVisible()
    {
        _inspector.AddMockDialog("InventoryDialog");

        var dialogs = _inspector.GetOpenDialogs();

        Assert.Single(dialogs);
        Assert.Equal("InventoryDialog", dialogs[0]);
    }

    [Fact]
    public void AddMockDialog_MultipleDifferentDialogs()
    {
        _inspector.AddMockDialog("InventoryDialog");
        _inspector.AddMockDialog("ChatDialog");
        _inspector.AddMockDialog("MapDialog");

        var dialogs = _inspector.GetOpenDialogs();

        Assert.Equal(3, dialogs.Count);
        Assert.Contains("InventoryDialog", dialogs);
        Assert.Contains("ChatDialog", dialogs);
        Assert.Contains("MapDialog", dialogs);
    }

    [Fact]
    public void AddMockDialog_DuplicatesIgnored()
    {
        _inspector.AddMockDialog("InventoryDialog");
        _inspector.AddMockDialog("InventoryDialog");

        var dialogs = _inspector.GetOpenDialogs();

        Assert.Single(dialogs);
    }

    [Fact]
    public void RemoveMockDialog_RemovesDialog()
    {
        _inspector.AddMockDialog("InventoryDialog");
        _inspector.AddMockDialog("ChatDialog");

        _inspector.RemoveMockDialog("InventoryDialog");

        var dialogs = _inspector.GetOpenDialogs();
        Assert.Single(dialogs);
        Assert.Equal("ChatDialog", dialogs[0]);
    }

    [Fact]
    public void IsDialogOpen_ReturnsTrueForOpenDialog()
    {
        _inspector.AddMockDialog("InventoryDialog");

        Assert.True(_inspector.IsDialogOpen("InventoryDialog"));
        Assert.True(_inspector.IsDialogOpen("inventorydialog")); // case insensitive
    }

    [Fact]
    public void IsDialogOpen_ReturnsFalseForClosedDialog()
    {
        _inspector.AddMockDialog("InventoryDialog");

        Assert.False(_inspector.IsDialogOpen("ChatDialog"));
    }

    [Fact]
    public void IsDialogOpen_ReturnsFalseForEmptyOrNullName()
    {
        Assert.False(_inspector.IsDialogOpen(""));
        Assert.False(_inspector.IsDialogOpen(null!));
    }

    // -------------------------------------------------------------------------
    // HUD element tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GetHudElements_InitiallyEmpty()
    {
        var huds = _inspector.GetHudElements();

        Assert.Empty(huds);
    }

    [Fact]
    public void AddMockHudElement_MakesHudElementVisible()
    {
        _inspector.AddMockHudElement("HudHealthBar");

        var huds = _inspector.GetHudElements();

        Assert.Single(huds);
        Assert.Equal("HudHealthBar", huds[0]);
    }

    [Fact]
    public void AddMockHudElement_MultipleElements()
    {
        _inspector.AddMockHudElement("HudHealthBar");
        _inspector.AddMockHudElement("HudHotbar");
        _inspector.AddMockHudElement("HudCrosshair");

        var huds = _inspector.GetHudElements();

        Assert.Equal(3, huds.Count);
    }

    [Fact]
    public void RemoveMockHudElement_RemovesElement()
    {
        _inspector.AddMockHudElement("HudHealthBar");
        _inspector.AddMockHudElement("HudHotbar");

        _inspector.RemoveMockHudElement("HudHealthBar");

        var huds = _inspector.GetHudElements();
        Assert.Single(huds);
        Assert.Equal("HudHotbar", huds[0]);
    }

    // -------------------------------------------------------------------------
    // Modal tests
    // -------------------------------------------------------------------------

    [Fact]
    public void HasModalDialog_InitiallyFalse()
    {
        Assert.False(_inspector.HasModalDialog());
    }

    [Fact]
    public void SetMockModal_SetsModalState()
    {
        _inspector.SetMockModal(true);

        Assert.True(_inspector.HasModalDialog());
    }

    [Fact]
    public void SetMockModal_CanBeCleared()
    {
        _inspector.SetMockModal(true);
        _inspector.SetMockModal(false);

        Assert.False(_inspector.HasModalDialog());
    }

    // -------------------------------------------------------------------------
    // Button click simulation tests
    // -------------------------------------------------------------------------

    [Fact]
    public void SimulateButtonClick_InvokesRegisteredHandler()
    {
        bool wasClicked = false;
        _inspector.AddMockDialog("ConfirmDialog");
        _inspector.RegisterMockButtonHandler("ConfirmDialog", "btnOk", () => wasClicked = true);

        bool result = _inspector.SimulateButtonClick("ConfirmDialog", "btnOk");

        Assert.True(result);
        Assert.True(wasClicked);
    }

    [Fact]
    public void SimulateButtonClick_ReturnsFalseForUnknownDialog()
    {
        bool result = _inspector.SimulateButtonClick("UnknownDialog", "btnOk");

        Assert.False(result);
    }

    [Fact]
    public void SimulateButtonClick_ReturnsFalseForUnknownButton()
    {
        _inspector.AddMockDialog("ConfirmDialog");

        bool result = _inspector.SimulateButtonClick("ConfirmDialog", "unknownButton");

        Assert.False(result);
    }

    [Fact]
    public void SimulateButtonClick_ReturnsFalseForEmptyParams()
    {
        Assert.False(_inspector.SimulateButtonClick("", "btnOk"));
        Assert.False(_inspector.SimulateButtonClick("Dialog", ""));
        Assert.False(_inspector.SimulateButtonClick(null!, "btnOk"));
        Assert.False(_inspector.SimulateButtonClick("Dialog", null!));
    }

    // -------------------------------------------------------------------------
    // Snapshot tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Snapshot_CapturesCurrentState()
    {
        _inspector.AddMockDialog("InventoryDialog");
        _inspector.AddMockDialog("ChatDialog");
        _inspector.AddMockHudElement("HudHealthBar");
        _inspector.SetMockModal(true);

        var snap = _inspector.Snapshot();

        Assert.Equal(2, snap.OpenDialogs.Count);
        Assert.Single(snap.HudElements);
        Assert.True(snap.HasModal);
    }

    [Fact]
    public void Snapshot_IsImmutableAfterCapture()
    {
        _inspector.AddMockDialog("InventoryDialog");
        var snap1 = _inspector.Snapshot();

        _inspector.AddMockDialog("ChatDialog");
        var snap2 = _inspector.Snapshot();

        Assert.Single(snap1.OpenDialogs);
        Assert.Equal(2, snap2.OpenDialogs.Count);
    }

    // -------------------------------------------------------------------------
    // GuiSnapshot record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GuiSnapshot_Empty_IsEmpty()
    {
        Assert.True(GuiSnapshot.Empty.IsEmpty);
        Assert.False(GuiSnapshot.Empty.HasModal);
        Assert.Equal(0, GuiSnapshot.Empty.TotalElementCount);
    }

    [Fact]
    public void GuiSnapshot_IsEmpty_FalseWhenHasDialogs()
    {
        var snap = new GuiSnapshot(new[] { "Dialog" }, Array.Empty<string>(), false);

        Assert.False(snap.IsEmpty);
    }

    [Fact]
    public void GuiSnapshot_IsEmpty_FalseWhenHasHudElements()
    {
        var snap = new GuiSnapshot(Array.Empty<string>(), new[] { "Hud" }, false);

        Assert.False(snap.IsEmpty);
    }

    [Fact]
    public void GuiSnapshot_TotalElementCount_SumsBothTypes()
    {
        var snap = new GuiSnapshot(new[] { "D1", "D2" }, new[] { "H1", "H2", "H3" }, false);

        Assert.Equal(5, snap.TotalElementCount);
    }

    [Fact]
    public void GuiSnapshot_IsDialogOpen_ReturnsTrueForMatch()
    {
        var snap = new GuiSnapshot(new[] { "InventoryDialog" }, Array.Empty<string>(), false);

        Assert.True(snap.IsDialogOpen("InventoryDialog"));
        Assert.True(snap.IsDialogOpen("inventorydialog")); // case insensitive
        Assert.False(snap.IsDialogOpen("ChatDialog"));
    }

    [Fact]
    public void GuiSnapshot_IsHudElementActive_ReturnsTrueForMatch()
    {
        var snap = new GuiSnapshot(Array.Empty<string>(), new[] { "HudHealthBar" }, false);

        Assert.True(snap.IsHudElementActive("HudHealthBar"));
        Assert.True(snap.IsHudElementActive("hudhealthbar")); // case insensitive
        Assert.False(snap.IsHudElementActive("HudMana"));
    }

    // -------------------------------------------------------------------------
    // ClearMockState tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ClearMockState_ClearsAllState()
    {
        _inspector.AddMockDialog("InventoryDialog");
        _inspector.AddMockHudElement("HudHealthBar");
        _inspector.SetMockModal(true);
        _inspector.RegisterMockButtonHandler("InventoryDialog", "btn", () => { });

        _inspector.ClearMockState();

        Assert.Empty(_inspector.GetOpenDialogs());
        Assert.Empty(_inspector.GetHudElements());
        Assert.False(_inspector.HasModalDialog());
        Assert.False(_inspector.SimulateButtonClick("InventoryDialog", "btn"));
    }

    // -------------------------------------------------------------------------
    // HeadlessClient property wiring
    // -------------------------------------------------------------------------

    [Fact]
    public void HeadlessClient_HasGuiProperty_OfCorrectType()
    {
        var prop = typeof(Core.HeadlessClient).GetProperty("Gui");

        Assert.NotNull(prop);
        Assert.Equal(typeof(GuiInspector), prop!.PropertyType);
    }
}
