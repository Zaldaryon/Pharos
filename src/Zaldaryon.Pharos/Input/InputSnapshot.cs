using System.Collections.Generic;

namespace Zaldaryon.Pharos.Input;

/// <summary>
/// Immutable snapshot of virtual input state at a point in time.
/// Thread-safe capture of mouse position, pressed keys, mouse buttons, and scroll delta.
/// </summary>
/// <param name="MouseX">Current mouse X coordinate.</param>
/// <param name="MouseY">Current mouse Y coordinate.</param>
/// <param name="PressedKeys">Keys currently held down.</param>
/// <param name="PressedMouseButtons">Mouse buttons currently pressed.</param>
/// <param name="ScrollDelta">Accumulated scroll wheel delta since last reset.</param>
public sealed record InputSnapshot(
    int MouseX,
    int MouseY,
    IReadOnlyList<VirtualKey> PressedKeys,
    IReadOnlyList<VirtualMouseButton> PressedMouseButtons,
    int ScrollDelta)
{
    /// <summary>
    /// Empty snapshot with no input state.
    /// </summary>
    public static InputSnapshot Empty { get; } = new(
        0, 0,
        Array.Empty<VirtualKey>(),
        Array.Empty<VirtualMouseButton>(),
        0);

    /// <summary>
    /// Returns true if no keys are pressed and no mouse buttons are held.
    /// </summary>
    public bool IsIdle => PressedKeys.Count == 0 && PressedMouseButtons.Count == 0 && ScrollDelta == 0;

    /// <summary>
    /// Returns true if the specified key is currently pressed.
    /// </summary>
    public bool IsKeyDown(VirtualKey key)
    {
        for (int i = 0; i < PressedKeys.Count; i++)
        {
            if (PressedKeys[i] == key) return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the specified mouse button is currently pressed.
    /// </summary>
    public bool IsMouseButtonDown(VirtualMouseButton button)
    {
        for (int i = 0; i < PressedMouseButtons.Count; i++)
        {
            if (PressedMouseButtons[i] == button) return true;
        }
        return false;
    }
}
