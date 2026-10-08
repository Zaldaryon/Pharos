using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Input;

/// <summary>A key combination a hotkey is bound to, copied from the game's <see cref="KeyCombination"/>.</summary>
/// <param name="KeyCode">The key, as a <see cref="GlKeys"/> code, or a mouse button from 240 on.</param>
/// <param name="SecondKeyCode">
/// The key released just before <paramref name="KeyCode"/> for a double tap, such as Space then
/// Space for the fly toggle, or null.
/// </param>
/// <param name="Ctrl">Whether Ctrl is held.</param>
/// <param name="Alt">Whether Alt is held.</param>
/// <param name="Shift">Whether Shift is held.</param>
public sealed record HotkeyBinding(int KeyCode, int? SecondKeyCode = null, bool Ctrl = false, bool Alt = false, bool Shift = false)
{
    /// <summary>The key, when the binding is a key.</summary>
    public GlKeys Key => (GlKeys)KeyCode;

    /// <summary>Whether the binding is a mouse button.</summary>
    public bool IsMouseButton => KeyCode >= KeyCombination.MouseStart;

    /// <summary>The mouse button, when the binding is one.</summary>
    public EnumMouseButton? MouseButton => IsMouseButton ? (EnumMouseButton)(KeyCode - KeyCombination.MouseStart) : null;

    /// <summary>A key with modifiers.</summary>
    public static HotkeyBinding Of(GlKeys key, bool ctrl = false, bool alt = false, bool shift = false) =>
        new((int)key, null, ctrl, alt, shift);

    /// <summary>A mouse button with modifiers.</summary>
    public static HotkeyBinding Of(EnumMouseButton button, bool ctrl = false, bool alt = false, bool shift = false) =>
        new(KeyCombination.MouseStart + (int)button, null, ctrl, alt, shift);

    /// <summary>A key pressed twice in quick succession.</summary>
    public static HotkeyBinding DoubleTap(GlKeys key) => new((int)key, (int)key);

    internal static HotkeyBinding From(KeyCombination combination) =>
        new(combination.KeyCode, combination.SecondKeyCode is null or 0 ? null : combination.SecondKeyCode, combination.Ctrl, combination.Alt, combination.Shift);

    internal KeyCombination ToKeyCombination() =>
        new() { KeyCode = KeyCode, SecondKeyCode = SecondKeyCode, Ctrl = Ctrl, Alt = Alt, Shift = Shift };

    /// <summary>The binding as the game's controls menu writes it, such as <c>CTRL + SHIFT + K</c>.</summary>
    public override string ToString() => ToKeyCombination().ToString();
}

/// <summary>A hotkey the client has registered.</summary>
/// <param name="Code">Its code, such as <c>inventorydialog</c>.</param>
/// <param name="Name">Its name in the controls menu.</param>
/// <param name="Type">Its kind, which decides when it may fire.</param>
/// <param name="Default">What it is bound to by default.</param>
/// <param name="Current">What it is bound to now.</param>
/// <param name="IsGlobal">Whether it fires even while a dialog has the keyboard.</param>
/// <param name="HasHandler">
/// Whether something handles it. Movement and other controls the game reads as held keys have no
/// handler: press them with <see cref="HotkeyDriver.PressAsync"/> or <see cref="Player.PlayerControls"/>.
/// </param>
/// <param name="TriggerOnUpAlso">Whether its handler also runs when the key is released.</param>
public sealed record HotkeyInfo(
    string Code, string Name, HotkeyType Type, HotkeyBinding Default, HotkeyBinding Current,
    bool IsGlobal, bool HasHandler, bool TriggerOnUpAlso);

/// <summary>Hotkeys bound to the same combination.</summary>
/// <param name="Binding">The combination they share.</param>
/// <param name="Codes">Their codes, in the order the game tries them.</param>
public sealed record HotkeyConflict(HotkeyBinding Binding, IReadOnlyList<string> Codes);

/// <summary>What happened when a hotkey's combination was sent.</summary>
/// <param name="Code">The hotkey whose combination was sent.</param>
/// <param name="FiredCode">
/// The hotkey whose handler took the key, or null when none did. Another hotkey than
/// <paramref name="Code"/> on a clash.
/// </param>
/// <param name="Consumed">
/// Whether anything took the key: a hotkey, a mod's key listener, a dialog that has the keyboard or
/// a dialog that closed on its own toggle key. Consumed with no <paramref name="FiredCode"/> means
/// the key was taken before any hotkey saw it.
/// </param>
public sealed record HotkeyTriggerResult(string Code, string? FiredCode, bool Consumed)
{
    /// <summary>Whether the hotkey's own handler took the key.</summary>
    public bool Fired => FiredCode == Code;
}

/// <summary>
/// Reads the hotkeys an engine-mode client has registered, its own and its mods', and fires them by
/// their code.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TriggerAsync"/> sends the combination a hotkey is bound to as one key event, its
/// modifiers as flags, down and up on the same frame, through the same entry point as real input.
/// So the real gating applies: a mod's key listener, a dialog that has the keyboard, whether
/// character controls are allowed, the game mode, and which hotkey comes first when two share a
/// combination. <see cref="PressAsync"/> presses the keys one by one through
/// <see cref="VirtualInputController"/>, as a player would, and holds them for frames.
/// </para>
/// <para>
/// Hotkeys are process-wide in the game. <see cref="Rebind"/> changes a binding as the controls menu
/// does and returns a handle that puts the old one back.
/// </para>
/// </remarks>
public sealed class HotkeyDriver
{
    private static readonly FieldInfo? s_listeners = typeof(HotkeyManager).GetField("listeners", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly HeadlessClient _client;

    internal HotkeyDriver(HeadlessClient client)
    {
        _client = client;
    }

    private static HotkeyManager Manager => ScreenManager.hotkeyManager
        ?? throw new InvalidOperationException("The client has no hotkey manager yet.");

    /// <summary>The hotkey registered as <paramref name="code"/>, or null.</summary>
    public HotkeyInfo? Get(string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        return _client.RunOnClientThread(() => Manager.GetHotKeyByCode(code) is { } hotkey ? Info(hotkey) : null);
    }

    /// <summary>The hotkey registered as <paramref name="code"/>.</summary>
    /// <exception cref="KeyNotFoundException">No hotkey has that code.</exception>
    public HotkeyInfo Require(string code) => Get(code) ?? throw NotFound(code);

    /// <summary>Every registered hotkey, in the order the game tries them.</summary>
    public IReadOnlyList<HotkeyInfo> All() => _client.RunOnClientThread(() =>
        (IReadOnlyList<HotkeyInfo>)Manager.HotKeys.ValuesOrdered.Select(Info).ToList());

    /// <summary>
    /// Every group of hotkeys bound to exactly the same combination. Some are on purpose in the
    /// game itself: sneak and shift-click, sprint and ctrl-click, middle click and pick block.
    /// </summary>
    /// <remarks>
    /// Overlaps the game resolves at press time are not listed: a binding with no modifiers also
    /// fires on Ctrl, Shift or Alt with its key when no exact binding takes it, and a mouse binding
    /// with no modifiers matches any.
    /// </remarks>
    public IReadOnlyList<HotkeyConflict> Conflicts() =>
        All().GroupBy(h => h.Current)
            .Where(g => g.Count() > 1)
            .Select(g => new HotkeyConflict(g.Key, g.Select(h => h.Code).ToList()))
            .ToList();

    /// <summary>The hotkeys bound to the same combination as <paramref name="code"/>, itself included, or none.</summary>
    public HotkeyConflict? ConflictWith(string code)
    {
        HotkeyInfo hotkey = Require(code);
        return Conflicts().FirstOrDefault(c => c.Codes.Contains(hotkey.Code));
    }

    /// <summary>
    /// Sends the combination <paramref name="code"/> is bound to, down and up, with its modifiers,
    /// and steps one frame.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No hotkey has that code.</exception>
    /// <exception cref="InvalidOperationException">
    /// The client has not joined a world, or the game is not taking hotkeys, as while the controls
    /// menu captures a key.
    /// </exception>
    public async Task<HotkeyTriggerResult> TriggerAsync(string code, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        HotkeyTriggerResult result = _client.RunOnClientThread(() =>
        {
            RequirePlaying();
            HotKey hotkey = Manager.GetHotKeyByCode(code) ?? throw NotFound(code);
            HotkeyBinding binding = HotkeyBinding.From(hotkey.CurrentMapping);

            using Recorder recorder = new();
            bool consumed = binding.MouseButton is { } button ? SendMouse(button, binding) : SendKey(binding);
            if (!binding.IsMouseButton) (_client.Input.Sink as EngineInputSink)?.NoteKeyUp(binding.KeyCode);
            return new HotkeyTriggerResult(hotkey.Code, recorder.Fired, consumed || recorder.Fired != null);
        });

        await _client.StepAsync(ct).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Presses the keys <paramref name="code"/> is bound to through <see cref="VirtualInputController"/>,
    /// modifiers first, holds them for <paramref name="holdFrames"/> frames and releases them in
    /// reverse order. A double tap presses and releases its first key, then presses the second.
    /// </summary>
    /// <remarks>
    /// Modifier keys held for the press also hold whatever is bound to them, such as sprint on
    /// Ctrl or sneak on Shift, for <paramref name="holdFrames"/>. Everything is released when the
    /// press ends, even when it fails.
    /// </remarks>
    /// <returns>
    /// Which hotkey took the press, if any. <see cref="HotkeyTriggerResult.Consumed"/> is true only
    /// when a hotkey did: keys pressed one by one are not traced further.
    /// </returns>
    /// <exception cref="KeyNotFoundException">No hotkey has that code.</exception>
    public async Task<HotkeyTriggerResult> PressAsync(string code, int holdFrames = 1, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegative(holdFrames);
        (string hotkeyCode, HotkeyBinding binding) = _client.RunOnClientThread(() =>
        {
            RequirePlaying();
            HotKey hotkey = Manager.GetHotKeyByCode(code) ?? throw NotFound(code);
            return (hotkey.Code, HotkeyBinding.From(hotkey.CurrentMapping));
        });

        VirtualInputController input = _client.Input;
        List<GlKeys> modifiers = [];
        if (binding.Ctrl && !IsKey(binding, GlKeys.LControl, GlKeys.RControl)) modifiers.Add(GlKeys.LControl);
        if (binding.Shift && !IsKey(binding, GlKeys.LShift, GlKeys.RShift)) modifiers.Add(GlKeys.LShift);
        if (binding.Alt && !IsKey(binding, GlKeys.LAlt, GlKeys.RAlt)) modifiers.Add(GlKeys.LAlt);

        Recorder recorder = _client.RunOnClientThread(() => new Recorder());
        bool down = false;
        try
        {
            foreach (GlKeys modifier in modifiers) input.InjectKey(modifier, pressed: true);
            if (binding.MouseButton is { } button)
            {
                input.InjectMouseButton(button, pressed: true);
            }
            else
            {
                // Within the platform's double-tap window: nothing steps between the two. A key
                // released by an earlier press must not make the first tap a double tap already.
                if (binding.SecondKeyCode is { } first)
                {
                    (input.Sink as EngineInputSink)?.ForgetKeyUp();
                    input.InjectKey((GlKeys)first, pressed: true);
                    input.InjectKey((GlKeys)first, pressed: false);
                }

                input.InjectKey(binding.Key, pressed: true);
            }

            down = true;
            for (int i = 0; i < holdFrames; i++)
            {
                await _client.StepAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // Released whatever happens, so no key stays held into the next test.
            if (down)
            {
                if (binding.MouseButton is { } button) input.InjectMouseButton(button, pressed: false);
                else input.InjectKey(binding.Key, pressed: false);
            }

            for (int i = modifiers.Count - 1; i >= 0; i--) input.InjectKey(modifiers[i], pressed: false);
            try
            {
                _client.RunOnClientThread(recorder.Dispose);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
            {
                // The client is gone: so is its hotkey listener list.
            }
        }

        await _client.StepAsync(ct).ConfigureAwait(false);
        return new HotkeyTriggerResult(hotkeyCode, recorder.Fired, recorder.Fired != null);
    }

    /// <summary>
    /// Binds <paramref name="code"/> to <paramref name="binding"/> as the controls menu does: the
    /// binding is saved to the client settings, so the systems that watch key bindings, such as the
    /// player's movement, follow, and while Ctrl and Shift are not set apart in the controls menu,
    /// shift-click follows sneak and ctrl-click follows sprint. Dispose the result to put the old
    /// bindings back.
    /// </summary>
    /// <remarks>
    /// The menu also tells listeners of <c>HotkeysChanged</c> when it rebinds shift-click,
    /// ctrl-click, the mouse buttons or the tool mode key, which this does not.
    /// </remarks>
    /// <exception cref="KeyNotFoundException">No hotkey has that code.</exception>
    public IDisposable Rebind(string code, HotkeyBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(binding);

        List<(string Code, HotkeyBinding Previous, bool HadMapping)> previous = _client.RunOnClientThread(() =>
        {
            HotKey hotkey = Manager.GetHotKeyByCode(code) ?? throw NotFound(code);
            List<string> codes = [hotkey.Code];
            if (!ClientSettings.SeparateCtrl && Paired(hotkey.Code) is { } paired && Manager.GetHotKeyByCode(paired) != null) codes.Add(paired);

            List<(string, HotkeyBinding, bool)> before = codes
                .Select(c => (c, HotkeyBinding.From(Manager.GetHotKeyByCode(c)!.CurrentMapping), ClientSettings.KeyMapping.ContainsKey(c)))
                .ToList();
            foreach (string c in codes) Apply(c, binding);
            return before;
        });

        return new Restore(this, previous);
    }

    // The menu keeps shift-click on sneak's key and ctrl-click on sprint's while they are not set apart.
    private static string? Paired(string code) => code switch
    {
        "sneak" => "shift",
        "sprint" => "ctrl",
        _ => null,
    };

    private static bool IsKey(HotkeyBinding binding, params GlKeys[] keys) => !binding.IsMouseButton && keys.Contains(binding.Key);

    // Never changes the mapping in place: the game shares that object with the saved settings.
    private static void Apply(string code, HotkeyBinding binding)
    {
        if (Manager.GetHotKeyByCode(code) is not { } hotkey) return;
        KeyCombination combination = binding.ToKeyCombination();
        hotkey.CurrentMapping = combination;
        ClientSettings.Inst.SetKeyMapping(code, combination.Clone());
    }

    private void RequirePlaying()
    {
        if (_client.Client.player == null || _client.Client.disposed)
        {
            throw new InvalidOperationException("Hotkeys need an engine-mode client that has joined a world.");
        }

        if (!Manager.ShouldTriggerHotkeys)
        {
            throw new InvalidOperationException("The game is not taking hotkeys right now: the controls menu is capturing a key.");
        }
    }

    // One key event, passed to every handler, so whether it was taken can be read back. The
    // platform builds one per handler, which makes no difference with the single handler the game
    // registers. The typed character goes in between, as from a real keyboard: the chat toggle
    // swallows the character of its own key.
    private bool SendKey(HotkeyBinding binding)
    {
        ClientPlatformWindows platform = _client.Platform;
        KeyEvent down = new() { KeyCode = binding.KeyCode, CtrlPressed = binding.Ctrl, AltPressed = binding.Alt, ShiftPressed = binding.Shift };
        if (binding.SecondKeyCode is { } second) down.KeyCode2 = second;
        foreach (KeyEventHandler handler in platform.keyEventHandlers.ToArray()) handler.OnKeyDown(down);

        if (!binding.Ctrl && !binding.Alt && VirtualInputController.ToCharacter(binding.Key, binding.Shift) is char character)
        {
            KeyEvent press = new() { KeyCode = character, KeyChar = character };
            foreach (KeyEventHandler handler in platform.keyEventHandlers.ToArray()) handler.OnKeyPress(press);
        }

        KeyEvent up = new() { KeyCode = binding.KeyCode, CtrlPressed = binding.Ctrl, AltPressed = binding.Alt, ShiftPressed = binding.Shift };
        foreach (KeyEventHandler handler in platform.keyEventHandlers.ToArray()) handler.OnKeyUp(up);
        return down.Handled;
    }

    // A click lands where the cursor is, as a real one does.
    private bool SendMouse(EnumMouseButton button, HotkeyBinding binding)
    {
        ClientPlatformWindows platform = _client.Platform;
        (int x, int y) = _client.Input.GetMousePosition();
        int modifiers = (binding.Shift ? 1 : 0) | (binding.Ctrl ? 2 : 0) | (binding.Alt ? 4 : 0);
        MouseEvent down = new(x, y, button, modifiers);
        foreach (MouseEventHandler handler in platform.mouseEventHandlers.ToArray()) handler.OnMouseDown(down);
        MouseEvent up = new(x, y, button, modifiers);
        foreach (MouseEventHandler handler in platform.mouseEventHandlers.ToArray()) handler.OnMouseUp(up);
        return down.Handled;
    }

    private static HotkeyInfo Info(HotKey hotkey) => new(
        hotkey.Code, hotkey.Name, hotkey.KeyCombinationType,
        HotkeyBinding.From(hotkey.DefaultMapping ?? hotkey.CurrentMapping), HotkeyBinding.From(hotkey.CurrentMapping),
        hotkey.IsGlobalHotkey, hotkey.Handler != null, hotkey.TriggerOnUpAlso);

    private static KeyNotFoundException NotFound(string code)
    {
        string[] close = Manager.HotKeys.Keys.Where(k => k.Contains(code, StringComparison.OrdinalIgnoreCase) || code.Contains(k, StringComparison.OrdinalIgnoreCase)).Take(5).ToArray();
        return new KeyNotFoundException($"No hotkey is registered as '{code}'.{(close.Length > 0 ? $" Close: {string.Join(", ", close)}." : "")}");
    }

    /// <summary>
    /// Notes the first hotkey whose handler takes a key while it lives. Installed and removed on the
    /// client thread; the game clears its listeners when a world ends, so one is added per call.
    /// </summary>
    private sealed class Recorder : IDisposable
    {
        private readonly HotkeyManager _manager = Manager;
        private readonly OnHotKeyDelegate _listener;

        public Recorder()
        {
            if (s_listeners == null) throw new InvalidOperationException("Pharos cannot find the game's hotkey listeners: the game changed.");

            // The hotkey that took the press: not one that fired again on the key's release, and
            // not one a handler fired from inside itself, which reports before its caller does.
            _listener = (code, combination) =>
            {
                if (!combination.OnKeyUp) Fired = code;
            };
            _manager.AddHotkeyListener(_listener);
        }

        public string? Fired { get; private set; }

        public void Dispose()
        {
            if (s_listeners?.GetValue(_manager) is Delegate current)
            {
                s_listeners.SetValue(_manager, Delegate.Remove(current, _listener));
            }
        }
    }

    private sealed class Restore(HotkeyDriver driver, List<(string Code, HotkeyBinding Previous, bool HadMapping)> previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Bindings are process-wide, so they go back even when the client is gone: the next
            // client in the process would read them.
            if (driver._client.IsDisposed)
            {
                Put();
                return;
            }

            try
            {
                driver._client.RunOnClientThread(Put);
            }
            catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
            {
                Put();
            }
        }

        private void Put()
        {
            foreach ((string code, HotkeyBinding binding, bool hadMapping) in previous)
            {
                Apply(code, binding);
                if (!hadMapping) ClientSettings.KeyMapping.Remove(code);
            }
        }
    }
}
