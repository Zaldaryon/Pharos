using System.Collections.Generic;

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
    private readonly HashSet<VirtualMouseButton> _pressedButtons = new();

    /// <summary>
    /// Injects a mouse move event to the specified position.
    /// </summary>
    /// <param name="x">Target X coordinate.</param>
    /// <param name="y">Target Y coordinate.</param>
    public void InjectMouseMove(int x, int y)
    {
        lock (_lock)
        {
            _mouseX = x;
            _mouseY = y;
        }
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
    }

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
