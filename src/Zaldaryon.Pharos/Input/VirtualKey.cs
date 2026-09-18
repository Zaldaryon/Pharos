namespace Zaldaryon.Pharos.Input;

/// <summary>
/// Keyboard keys for deterministic virtual input injection.
/// Covers the main game interaction keys.
/// </summary>
public enum VirtualKey
{
    /// <summary>W key - forward movement.</summary>
    W = 0,

    /// <summary>A key - strafe left.</summary>
    A = 1,

    /// <summary>S key - backward movement.</summary>
    S = 2,

    /// <summary>D key - strafe right.</summary>
    D = 3,

    /// <summary>Space key - jump.</summary>
    Space = 4,

    /// <summary>Shift key - sprint/sneak modifier.</summary>
    Shift = 5,

    /// <summary>Control key - modifier.</summary>
    Control = 6,

    /// <summary>Alt key - modifier.</summary>
    Alt = 7,

    /// <summary>E key - inventory/interact.</summary>
    E = 8,

    /// <summary>F key - secondary interact.</summary>
    F = 9,

    /// <summary>Tab key - menu/player list.</summary>
    Tab = 10,

    /// <summary>Escape key - pause/cancel.</summary>
    Escape = 11,

    /// <summary>Enter key - confirm/chat.</summary>
    Enter = 12
}
