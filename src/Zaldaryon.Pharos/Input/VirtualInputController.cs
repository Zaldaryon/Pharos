using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.Input;

/// <summary>
/// Deterministic virtual input controller for headless mouse and keyboard simulation.
/// Thread-safe using lock/snapshot pattern for concurrent access.
/// Input events are tick-aligned and do not leak across frame boundaries.
/// </summary>
public sealed class VirtualInputController
{
    private readonly object _lock = new();

    // Current state
    private int _mouseX;
    private int _mouseY;
    private int _scrollDelta;
    private readonly HashSet<VirtualKey> _pressedKeys = new();
    private readonly HashSet<GlKeys> _pressedGlKeys = new();
    private readonly HashSet<VirtualMouseButton> _pressedButtons = new();

    /// <summary>
    /// Where injected input goes besides this controller's own state. An engine-mode client sets
    /// it so every injected event reaches the game the way real input does; without one, the
    /// controller only records state.
    /// </summary>
    internal IVirtualInputSink? Sink { get; set; }

    /// <summary>
    /// Injects a mouse move event to the specified position.
    /// </summary>
    /// <param name="x">Target X coordinate.</param>
    /// <param name="y">Target Y coordinate.</param>
    public void InjectMouseMove(int x, int y)
    {
        int deltaX, deltaY;
        lock (_lock)
        {
            deltaX = x - _mouseX;
            deltaY = y - _mouseY;
            _mouseX = x;
            _mouseY = y;
        }

        Sink?.MouseMove(x, y, deltaX, deltaY);
    }

    /// <summary>
    /// Moves the mouse by a relative amount, the way a grabbed mouse turns the camera in game.
    /// </summary>
    /// <param name="deltaX">Horizontal movement in pixels; positive turns right.</param>
    /// <param name="deltaY">Vertical movement in pixels; positive looks down.</param>
    public void InjectMouseDelta(int deltaX, int deltaY)
    {
        int x, y;
        lock (_lock)
        {
            x = _mouseX;
            y = _mouseY;
        }

        Sink?.MouseMove(x, y, deltaX, deltaY);
    }

    /// <summary>
    /// Injects a mouse button press or release event.
    /// </summary>
    /// <param name="button">The mouse button.</param>
    /// <param name="pressed">True to press, false to release.</param>
    public void InjectMouseButton(VirtualMouseButton button, bool pressed)
    {
        lock (_lock)
        {
            if (pressed)
            {
                _pressedButtons.Add(button);
            }
            else
            {
                _pressedButtons.Remove(button);
            }
        }

        (int x, int y) = GetMousePosition();
        Sink?.MouseButton(ToEngineButton(button), pressed, x, y, Modifiers());
    }

    /// <summary>
    /// Injects a press or release of any mouse button the engine knows, the side buttons included.
    /// Held Shift, Ctrl and Alt keys go with it, as for a real click.
    /// </summary>
    /// <param name="button">The engine's mouse button.</param>
    /// <param name="pressed">True to press, false to release.</param>
    public void InjectMouseButton(EnumMouseButton button, bool pressed)
    {
        VirtualMouseButton? virtualButton = button switch
        {
            EnumMouseButton.Left => VirtualMouseButton.Left,
            EnumMouseButton.Middle => VirtualMouseButton.Middle,
            EnumMouseButton.Right => VirtualMouseButton.Right,
            _ => null,
        };

        if (virtualButton is { } tracked)
        {
            InjectMouseButton(tracked, pressed);
            return;
        }

        (int x, int y) = GetMousePosition();
        Sink?.MouseButton(button, pressed, x, y, Modifiers());
    }

    // The engine's modifier bits on a mouse event: 1 Shift, 2 Ctrl, 4 Alt.
    private int Modifiers()
    {
        (bool shift, bool ctrl, bool alt) = HeldModifiers();
        return (shift ? 1 : 0) | (ctrl ? 2 : 0) | (alt ? 4 : 0);
    }

    private (bool Shift, bool Ctrl, bool Alt) HeldModifiers(GlKeys? except = null)
    {
        lock (_lock)
        {
            bool Held(GlKeys key) => key != except && _pressedGlKeys.Contains(key);
            return (
                (_pressedKeys.Contains(VirtualKey.Shift) && except is not (GlKeys.LShift or GlKeys.RShift)) || Held(GlKeys.LShift) || Held(GlKeys.RShift),
                (_pressedKeys.Contains(VirtualKey.Control) && except is not (GlKeys.LControl or GlKeys.RControl)) || Held(GlKeys.LControl) || Held(GlKeys.RControl),
                (_pressedKeys.Contains(VirtualKey.Alt) && except is not (GlKeys.LAlt or GlKeys.RAlt)) || Held(GlKeys.LAlt) || Held(GlKeys.RAlt));
        }
    }

    /// <summary>
    /// Moves the mouse to (<paramref name="x"/>, <paramref name="y"/>) and presses and releases
    /// <paramref name="button"/> there.
    /// </summary>
    public void Click(int x, int y, VirtualMouseButton button = VirtualMouseButton.Left)
    {
        InjectMouseMove(x, y);
        InjectMouseButton(button, pressed: true);
        InjectMouseButton(button, pressed: false);
    }

    /// <summary>
    /// Injects a keyboard key press or release event.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="pressed">True to press, false to release.</param>
    public void InjectKey(VirtualKey key, bool pressed)
    {
        lock (_lock)
        {
            if (pressed)
            {
                _pressedKeys.Add(key);
            }
            else
            {
                _pressedKeys.Remove(key);
            }
        }

        SendKey(ToGlKey(key), pressed);
    }

    /// <summary>
    /// Injects a press or release of any key, by its engine key code.
    /// </summary>
    /// <param name="key">The key.</param>
    /// <param name="pressed">True to press, false to release.</param>
    public void InjectKey(GlKeys key, bool pressed)
    {
        lock (_lock)
        {
            if (pressed) _pressedGlKeys.Add(key);
            else _pressedGlKeys.Remove(key);
        }

        SendKey(key, pressed);
    }

    /// <summary>
    /// Presses and releases <paramref name="key"/>.
    /// </summary>
    public void PressKey(GlKeys key)
    {
        InjectKey(key, pressed: true);
        InjectKey(key, pressed: false);
    }

    /// <summary>
    /// Types <paramref name="text"/> character by character, as text input into whatever has the
    /// keyboard focus, a chat line or a text field.
    /// </summary>
    public void TypeText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (char c in text)
        {
            Sink?.KeyPress(c);
        }
    }

    /// <summary>
    /// Checks if the specified key is currently pressed.
    /// </summary>
    public bool IsKeyDown(GlKeys key)
    {
        lock (_lock)
        {
            return _pressedGlKeys.Contains(key);
        }
    }

    private void SendKey(GlKeys key, bool pressed)
    {
        IVirtualInputSink? sink = Sink;
        if (sink == null) return;

        // A modifier key's own press does not count as that modifier being held, as on a real
        // keyboard: Ctrl then LShift is a press of LShift with Ctrl held, not with Shift.
        (bool shift, bool ctrl, bool alt) = HeldModifiers(except: key);

        if (pressed)
        {
            sink.KeyDown(key, shift, ctrl, alt);

            // A real keyboard reports a printable key twice: as the key going down, and as the
            // character it types. Hotkeys use the first, text fields the second, and the game
            // relies on both arriving: the chat hotkey swallows the character of its own key.
            if (!ctrl && !alt && ToCharacter(key, shift) is char character)
            {
                sink.KeyPress(character);
            }
        }
        else
        {
            sink.KeyUp(key, shift, ctrl, alt);
        }
    }

    internal static char? ToCharacter(GlKeys key, bool shift)
    {
        if (key >= GlKeys.A && key <= GlKeys.Z)
        {
            char letter = (char)('a' + (key - GlKeys.A));
            return shift ? char.ToUpperInvariant(letter) : letter;
        }

        if (key >= GlKeys.Number0 && key <= GlKeys.Number9 && !shift)
        {
            return (char)('0' + (key - GlKeys.Number0));
        }

        return key == GlKeys.Space ? ' ' : null;
    }

    private static GlKeys ToGlKey(VirtualKey key) => key switch
    {
        VirtualKey.W => GlKeys.W,
        VirtualKey.A => GlKeys.A,
        VirtualKey.S => GlKeys.S,
        VirtualKey.D => GlKeys.D,
        VirtualKey.Space => GlKeys.Space,
        VirtualKey.Shift => GlKeys.LShift,
        VirtualKey.Control => GlKeys.LControl,
        VirtualKey.Alt => GlKeys.LAlt,
        VirtualKey.E => GlKeys.E,
        VirtualKey.F => GlKeys.F,
        VirtualKey.Tab => GlKeys.Tab,
        VirtualKey.Escape => GlKeys.Escape,
        VirtualKey.Enter => GlKeys.Enter,
        _ => GlKeys.Unknown,
    };

    private static EnumMouseButton ToEngineButton(VirtualMouseButton button) => button switch
    {
        VirtualMouseButton.Right => EnumMouseButton.Right,
        VirtualMouseButton.Middle => EnumMouseButton.Middle,
        _ => EnumMouseButton.Left,
    };

    /// <summary>
    /// Injects a scroll wheel event.
    /// </summary>
    /// <param name="delta">Scroll delta (positive = up, negative = down).</param>
    public void InjectScroll(int delta)
    {
        lock (_lock)
        {
            _scrollDelta += delta;
        }

        Sink?.MouseWheel(delta);
    }

    /// <summary>
    /// Gets the current mouse position as a tuple.
    /// </summary>
    /// <returns>Tuple of (X, Y) coordinates.</returns>
    public (int X, int Y) GetMousePosition()
    {
        lock (_lock)
        {
            return (_mouseX, _mouseY);
        }
    }

    /// <summary>
    /// Checks if the specified key is currently pressed.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True if the key is held down.</returns>
    public bool IsKeyDown(VirtualKey key)
    {
        lock (_lock)
        {
            return _pressedKeys.Contains(key);
        }
    }

    /// <summary>
    /// Checks if the specified mouse button is currently pressed.
    /// </summary>
    /// <param name="button">The button to check.</param>
    /// <returns>True if the button is held down.</returns>
    public bool IsMouseButtonDown(VirtualMouseButton button)
    {
        lock (_lock)
        {
            return _pressedButtons.Contains(button);
        }
    }

    /// <summary>
    /// Gets all currently pressed mouse buttons.
    /// </summary>
    /// <returns>Read-only list of active mouse buttons.</returns>
    public IReadOnlyList<VirtualMouseButton> GetActiveButtons()
    {
        lock (_lock)
        {
            return _pressedButtons.Count == 0
                ? Array.Empty<VirtualMouseButton>()
                : _pressedButtons.ToArray();
        }
    }

    /// <summary>
    /// Gets all currently pressed keys.
    /// </summary>
    /// <returns>Read-only list of active keys.</returns>
    public IReadOnlyList<VirtualKey> GetActiveKeys()
    {
        lock (_lock)
        {
            return _pressedKeys.Count == 0
                ? Array.Empty<VirtualKey>()
                : _pressedKeys.ToArray();
        }
    }

    /// <summary>
    /// Gets the accumulated scroll delta since last reset.
    /// </summary>
    /// <returns>Scroll delta value.</returns>
    public int GetScrollDelta()
    {
        lock (_lock)
        {
            return _scrollDelta;
        }
    }

    /// <summary>
    /// Captures an immutable snapshot of the current input state.
    /// Thread-safe and deterministic.
    /// </summary>
    /// <returns>Immutable input snapshot.</returns>
    public InputSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new InputSnapshot(
                _mouseX,
                _mouseY,
                _pressedKeys.Count == 0 ? Array.Empty<VirtualKey>() : _pressedKeys.ToArray(),
                _pressedButtons.Count == 0 ? Array.Empty<VirtualMouseButton>() : _pressedButtons.ToArray(),
                _scrollDelta);
        }
    }

    /// <summary>
    /// Resets the scroll delta accumulator to zero.
    /// Call after processing scroll input each frame.
    /// </summary>
    public void ResetScrollDelta()
    {
        lock (_lock)
        {
            _scrollDelta = 0;
        }
    }

    /// <summary>
    /// Releases all pressed keys and mouse buttons, resets mouse position and scroll.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _mouseX = 0;
            _mouseY = 0;
            _scrollDelta = 0;
            _pressedKeys.Clear();
            _pressedButtons.Clear();
        }
    }
}
