using Vintagestory.API.Client;
using Vintagestory.Client;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Input;

namespace Zaldaryon.Pharos.Player;

/// <summary>A player action bound to a key or mouse button in the controls settings.</summary>
public enum PlayerAction
{
    /// <summary>Walk forward (<c>walkforward</c>, W by default).</summary>
    Forward,

    /// <summary>Walk backward (<c>walkbackward</c>, S by default).</summary>
    Backward,

    /// <summary>Strafe left (<c>walkleft</c>, A by default).</summary>
    Left,

    /// <summary>Strafe right (<c>walkright</c>, D by default).</summary>
    Right,

    /// <summary>Jump (<c>jump</c>, Space by default).</summary>
    Jump,

    /// <summary>Sneak (<c>sneak</c>, left Shift by default).</summary>
    Sneak,

    /// <summary>Sprint (<c>sprint</c>, left Control by default).</summary>
    Sprint,

    /// <summary>Break or attack (<c>primarymouse</c>, the left mouse button by default).</summary>
    Attack,

    /// <summary>Place or use (<c>secondarymouse</c>, the right mouse button by default).</summary>
    Use,
}

/// <summary>
/// Holds and releases player actions on an engine-mode client through whatever key or button the
/// action is bound to, exactly as a player at the keyboard would.
/// </summary>
/// <remarks>
/// The engine's player control system reads the keyboard state every frame and writes the
/// player's movement controls from it, so movement can only be driven through input: a control
/// set directly on the entity is overwritten on the next frame. Bindings are read from the live
/// hotkey settings, so a remapped control still works.
/// </remarks>
public sealed class PlayerControls
{
    private static readonly Dictionary<PlayerAction, string> s_hotkeys = new()
    {
        [PlayerAction.Forward] = "walkforward",
        [PlayerAction.Backward] = "walkbackward",
        [PlayerAction.Left] = "walkleft",
        [PlayerAction.Right] = "walkright",
        [PlayerAction.Jump] = "jump",
        [PlayerAction.Sneak] = "sneak",
        [PlayerAction.Sprint] = "sprint",
        [PlayerAction.Attack] = "primarymouse",
        [PlayerAction.Use] = "secondarymouse",
    };

    private readonly HeadlessClient _client;
    private readonly HashSet<PlayerAction> _held = [];

    internal PlayerControls(HeadlessClient client)
    {
        _client = client;
    }

    /// <summary>The actions currently held.</summary>
    public IReadOnlyCollection<PlayerAction> Held => [.. _held];

    /// <summary>Starts holding <paramref name="action"/>.</summary>
    public void Press(PlayerAction action)
    {
        if (_held.Add(action)) Send(action, pressed: true);
    }

    /// <summary>Stops holding <paramref name="action"/>.</summary>
    public void Release(PlayerAction action)
    {
        if (_held.Remove(action)) Send(action, pressed: false);
    }

    /// <summary>Releases every held action.</summary>
    public void ReleaseAll()
    {
        foreach (PlayerAction action in _held.ToArray())
        {
            Release(action);
        }
    }

    private void Send(PlayerAction action, bool pressed)
    {
        int keyCode = _client.RunOnClientThread(() =>
            ScreenManager.hotkeyManager.HotKeys.TryGetValue(s_hotkeys[action], out HotKey? hotkey)
                ? hotkey.CurrentMapping.KeyCode
                : throw new InvalidOperationException($"The control '{s_hotkeys[action]}' is not registered."));

        // Key codes from 240 to 247 are mouse buttons.
        if (keyCode >= 240 && keyCode < 248)
        {
            VirtualMouseButton button = (keyCode - 240) switch
            {
                1 => VirtualMouseButton.Middle,
                2 => VirtualMouseButton.Right,
                _ => VirtualMouseButton.Left,
            };
            _client.Input.InjectMouseButton(button, pressed);
        }
        else
        {
            _client.Input.InjectKey((GlKeys)keyCode, pressed);
        }
    }
}
