using Xunit;
using Zaldaryon.Pharos.Input;

namespace Zaldaryon.Pharos.Tests.Input;

/// <summary>
/// Pure-logic unit tests for VirtualInputController.
/// No live client or native libraries required.
/// </summary>
public sealed class VirtualInputControllerTests : IDisposable
{
    private readonly VirtualInputController _controller = new();

    public void Dispose() => _controller.Reset();

    // -------------------------------------------------------------------------
    // Mouse position tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GetMousePosition_InitiallyZero()
    {
        var (x, y) = _controller.GetMousePosition();

        Assert.Equal(0, x);
        Assert.Equal(0, y);
    }

    [Fact]
    public void InjectMouseMove_UpdatesPosition()
    {
        _controller.InjectMouseMove(100, 200);

        var (x, y) = _controller.GetMousePosition();

        Assert.Equal(100, x);
        Assert.Equal(200, y);
    }

    [Fact]
    public void InjectMouseMove_SupportsNegativeCoordinates()
    {
        _controller.InjectMouseMove(-50, -75);

        var (x, y) = _controller.GetMousePosition();

        Assert.Equal(-50, x);
        Assert.Equal(-75, y);
    }

    // -------------------------------------------------------------------------
    // Mouse button tests
    // -------------------------------------------------------------------------

    [Fact]
    public void IsMouseButtonDown_InitiallyFalse()
    {
        Assert.False(_controller.IsMouseButtonDown(VirtualMouseButton.Left));
        Assert.False(_controller.IsMouseButtonDown(VirtualMouseButton.Right));
        Assert.False(_controller.IsMouseButtonDown(VirtualMouseButton.Middle));
    }

    [Fact]
    public void InjectMouseButton_Press_SetsButtonDown()
    {
        _controller.InjectMouseButton(VirtualMouseButton.Left, pressed: true);

        Assert.True(_controller.IsMouseButtonDown(VirtualMouseButton.Left));
        Assert.False(_controller.IsMouseButtonDown(VirtualMouseButton.Right));
    }

    [Fact]
    public void InjectMouseButton_Release_ClearsButtonDown()
    {
        _controller.InjectMouseButton(VirtualMouseButton.Left, pressed: true);
        _controller.InjectMouseButton(VirtualMouseButton.Left, pressed: false);

        Assert.False(_controller.IsMouseButtonDown(VirtualMouseButton.Left));
    }

    [Fact]
    public void GetActiveButtons_ReturnsAllPressedButtons()
    {
        _controller.InjectMouseButton(VirtualMouseButton.Left, pressed: true);
        _controller.InjectMouseButton(VirtualMouseButton.Right, pressed: true);

        var buttons = _controller.GetActiveButtons();

        Assert.Equal(2, buttons.Count);
        Assert.Contains(VirtualMouseButton.Left, buttons);
        Assert.Contains(VirtualMouseButton.Right, buttons);
    }

    [Fact]
    public void GetActiveButtons_EmptyWhenNoButtonsPressed()
    {
        var buttons = _controller.GetActiveButtons();

        Assert.Empty(buttons);
    }

    // -------------------------------------------------------------------------
    // Keyboard tests
    // -------------------------------------------------------------------------

    [Fact]
    public void IsKeyDown_InitiallyFalse()
    {
        Assert.False(_controller.IsKeyDown(VirtualKey.W));
        Assert.False(_controller.IsKeyDown(VirtualKey.Space));
    }

    [Fact]
    public void InjectKey_Press_SetsKeyDown()
    {
        _controller.InjectKey(VirtualKey.W, pressed: true);

        Assert.True(_controller.IsKeyDown(VirtualKey.W));
        Assert.False(_controller.IsKeyDown(VirtualKey.S));
    }

    [Fact]
    public void InjectKey_Release_ClearsKeyDown()
    {
        _controller.InjectKey(VirtualKey.W, pressed: true);
        _controller.InjectKey(VirtualKey.W, pressed: false);

        Assert.False(_controller.IsKeyDown(VirtualKey.W));
    }

    [Fact]
    public void GetActiveKeys_ReturnsAllPressedKeys()
    {
        _controller.InjectKey(VirtualKey.W, pressed: true);
        _controller.InjectKey(VirtualKey.Shift, pressed: true);

        var keys = _controller.GetActiveKeys();

        Assert.Equal(2, keys.Count);
        Assert.Contains(VirtualKey.W, keys);
        Assert.Contains(VirtualKey.Shift, keys);
    }

    [Fact]
    public void GetActiveKeys_EmptyWhenNoKeysPressed()
    {
        var keys = _controller.GetActiveKeys();

        Assert.Empty(keys);
    }

    // -------------------------------------------------------------------------
    // Scroll tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GetScrollDelta_InitiallyZero()
    {
        Assert.Equal(0, _controller.GetScrollDelta());
    }

    [Fact]
    public void InjectScroll_AccumulatesDelta()
    {
        _controller.InjectScroll(3);
        _controller.InjectScroll(2);

        Assert.Equal(5, _controller.GetScrollDelta());
    }

    [Fact]
    public void InjectScroll_SupportsNegativeDelta()
    {
        _controller.InjectScroll(-5);

        Assert.Equal(-5, _controller.GetScrollDelta());
    }

    [Fact]
    public void ResetScrollDelta_ClearsDelta()
    {
        _controller.InjectScroll(10);
        _controller.ResetScrollDelta();

        Assert.Equal(0, _controller.GetScrollDelta());
    }

    // -------------------------------------------------------------------------
    // Snapshot tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Snapshot_CapturesCurrentState()
    {
        _controller.InjectMouseMove(150, 250);
        _controller.InjectMouseButton(VirtualMouseButton.Left, pressed: true);
        _controller.InjectKey(VirtualKey.W, pressed: true);
        _controller.InjectScroll(7);

        var snap = _controller.Snapshot();

        Assert.Equal(150, snap.MouseX);
        Assert.Equal(250, snap.MouseY);
        Assert.Single(snap.PressedMouseButtons);
        Assert.Contains(VirtualMouseButton.Left, snap.PressedMouseButtons);
        Assert.Single(snap.PressedKeys);
        Assert.Contains(VirtualKey.W, snap.PressedKeys);
        Assert.Equal(7, snap.ScrollDelta);
    }

    [Fact]
    public void Snapshot_IsImmutable_AfterCapture()
    {
        _controller.InjectKey(VirtualKey.W, pressed: true);
        var snap1 = _controller.Snapshot();

        _controller.InjectKey(VirtualKey.W, pressed: false);
        _controller.InjectKey(VirtualKey.S, pressed: true);
        var snap2 = _controller.Snapshot();

        // snap1 should still show W pressed, not S
        Assert.Contains(VirtualKey.W, snap1.PressedKeys);
        Assert.DoesNotContain(VirtualKey.S, snap1.PressedKeys);

        // snap2 should show S pressed, not W
        Assert.Contains(VirtualKey.S, snap2.PressedKeys);
        Assert.DoesNotContain(VirtualKey.W, snap2.PressedKeys);
    }

    // -------------------------------------------------------------------------
    // Reset tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Reset_ClearsAllState()
    {
        _controller.InjectMouseMove(100, 200);
        _controller.InjectMouseButton(VirtualMouseButton.Left, pressed: true);
        _controller.InjectKey(VirtualKey.W, pressed: true);
        _controller.InjectScroll(5);

        _controller.Reset();

        var (x, y) = _controller.GetMousePosition();
        Assert.Equal(0, x);
        Assert.Equal(0, y);
        Assert.Empty(_controller.GetActiveButtons());
        Assert.Empty(_controller.GetActiveKeys());
        Assert.Equal(0, _controller.GetScrollDelta());
    }

    // -------------------------------------------------------------------------
    // InputSnapshot record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void InputSnapshot_Empty_IsIdle()
    {
        Assert.True(InputSnapshot.Empty.IsIdle);
    }

    [Fact]
    public void InputSnapshot_IsIdle_FalseWhenKeyPressed()
    {
        var snap = new InputSnapshot(0, 0, new[] { VirtualKey.W }, Array.Empty<VirtualMouseButton>(), 0);

        Assert.False(snap.IsIdle);
    }

    [Fact]
    public void InputSnapshot_IsIdle_FalseWhenMouseButtonPressed()
    {
        var snap = new InputSnapshot(0, 0, Array.Empty<VirtualKey>(), new[] { VirtualMouseButton.Left }, 0);

        Assert.False(snap.IsIdle);
    }

    [Fact]
    public void InputSnapshot_IsIdle_FalseWhenScrollDeltaNonZero()
    {
        var snap = new InputSnapshot(0, 0, Array.Empty<VirtualKey>(), Array.Empty<VirtualMouseButton>(), 5);

        Assert.False(snap.IsIdle);
    }

    [Fact]
    public void InputSnapshot_IsKeyDown_ReturnsTrueForPressedKey()
    {
        var snap = new InputSnapshot(0, 0, new[] { VirtualKey.W, VirtualKey.Shift }, Array.Empty<VirtualMouseButton>(), 0);

        Assert.True(snap.IsKeyDown(VirtualKey.W));
        Assert.True(snap.IsKeyDown(VirtualKey.Shift));
        Assert.False(snap.IsKeyDown(VirtualKey.S));
    }

    [Fact]
    public void InputSnapshot_IsMouseButtonDown_ReturnsTrueForPressedButton()
    {
        var snap = new InputSnapshot(0, 0, Array.Empty<VirtualKey>(), new[] { VirtualMouseButton.Left }, 0);

        Assert.True(snap.IsMouseButtonDown(VirtualMouseButton.Left));
        Assert.False(snap.IsMouseButtonDown(VirtualMouseButton.Right));
    }

    // -------------------------------------------------------------------------
    // HeadlessClient property wiring
    // -------------------------------------------------------------------------

    [Fact]
    public void HeadlessClient_HasInputProperty_OfCorrectType()
    {
        var prop = typeof(Core.HeadlessClient).GetProperty("Input");

        Assert.NotNull(prop);
        Assert.Equal(typeof(VirtualInputController), prop!.PropertyType);
    }

    // -------------------------------------------------------------------------
    // Enum coverage tests
    // -------------------------------------------------------------------------

    [Fact]
    public void VirtualMouseButton_HasExpectedValues()
    {
        Assert.Equal(0, (int)VirtualMouseButton.Left);
        Assert.Equal(1, (int)VirtualMouseButton.Right);
        Assert.Equal(2, (int)VirtualMouseButton.Middle);
    }

    [Fact]
    public void VirtualKey_HasExpectedGameKeys()
    {
        // Verify WASD movement keys exist
        Assert.True(Enum.IsDefined(typeof(VirtualKey), VirtualKey.W));
        Assert.True(Enum.IsDefined(typeof(VirtualKey), VirtualKey.A));
        Assert.True(Enum.IsDefined(typeof(VirtualKey), VirtualKey.S));
        Assert.True(Enum.IsDefined(typeof(VirtualKey), VirtualKey.D));

        // Verify modifiers
        Assert.True(Enum.IsDefined(typeof(VirtualKey), VirtualKey.Shift));
        Assert.True(Enum.IsDefined(typeof(VirtualKey), VirtualKey.Control));
        Assert.True(Enum.IsDefined(typeof(VirtualKey), VirtualKey.Alt));

        // Verify interaction keys
        Assert.True(Enum.IsDefined(typeof(VirtualKey), VirtualKey.Space));
        Assert.True(Enum.IsDefined(typeof(VirtualKey), VirtualKey.E));
        Assert.True(Enum.IsDefined(typeof(VirtualKey), VirtualKey.F));
    }
}
