using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Input;

/// <summary>
/// Receives what a <see cref="VirtualInputController"/> injects and acts on it.
/// </summary>
internal interface IVirtualInputSink
{
    void KeyDown(GlKeys key, bool shift, bool ctrl, bool alt);
    void KeyUp(GlKeys key, bool shift, bool ctrl, bool alt);
    void KeyPress(char character);
    void MouseMove(int x, int y, int deltaX, int deltaY);
    void MouseButton(EnumMouseButton button, bool pressed, int x, int y, int modifiers);
    void MouseWheel(int delta);
}

/// <summary>
/// Delivers injected input to an engine-mode client the way the platform delivers real input.
/// </summary>
/// <remarks>
/// <para>
/// <c>ClientPlatformWindows</c> turns window events into engine events in a handful of private
/// handlers and passes each one to every registered keyboard or mouse handler: the
/// <c>ScreenManager</c>, which forwards it to the current screen, the running game, which updates
/// its keyboard state, runs hotkeys and hands it to open dialogs, and from there the player
/// controls. This builds the same engine events and passes them to the same handler lists, so an
/// injected key press travels exactly the path of a real one: it moves the player, triggers
/// hotkeys, types into text fields and clicks GUI elements.
/// </para>
/// <para>
/// The platform also keeps the cursor position itself, for whoever asks for it outside of an
/// event, so that is updated too. Everything runs on the client's main thread.
/// </para>
/// </remarks>
internal sealed class EngineInputSink : IVirtualInputSink
{
    private static readonly FieldInfo? s_mouseX = typeof(ClientPlatformWindows).GetField("mouseX", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_mouseY = typeof(ClientPlatformWindows).GetField("mouseY", BindingFlags.Instance | BindingFlags.NonPublic);

    // A key pressed within this long of the last key released carries that key as its second
    // key, as the platform does: double-tap bindings such as the fly toggle rely on it. Measured
    // in the frames' own time, not the wall clock, so the outcome does not depend on how fast the
    // machine steps.
    private const double DoubleTapMs = 200;

    private readonly HeadlessClient _client;
    private float _wheelValue;
    private double _lastKeyUpMs = double.NegativeInfinity;
    private int _lastKeyUpKey;

    public EngineInputSink(HeadlessClient client)
    {
        _client = client;
    }

    private ClientPlatformWindows Platform => _client.Platform;

    private double NowMs => _client.FrameController.TotalElapsedSeconds * 1000;

    /// <summary>Remembers a key released outside this sink, such as by a triggered hotkey.</summary>
    public void NoteKeyUp(int key)
    {
        _lastKeyUpMs = NowMs;
        _lastKeyUpKey = key;
    }

    /// <summary>Forgets the last key released, so the next key pressed is not part of a double tap.</summary>
    public void ForgetKeyUp() => _lastKeyUpMs = double.NegativeInfinity;

    public void KeyDown(GlKeys key, bool shift, bool ctrl, bool alt) => _client.RunOnClientThread(() =>
    {
        int? second = NowMs - _lastKeyUpMs <= DoubleTapMs ? _lastKeyUpKey : null;
        foreach (KeyEventHandler handler in Platform.keyEventHandlers.ToArray())
        {
            KeyEvent e = new() { KeyCode = (int)key, ShiftPressed = shift, CtrlPressed = ctrl, AltPressed = alt };
            if (second.HasValue) e.KeyCode2 = second.Value;
            handler.OnKeyDown(e);
        }
    });

    public void KeyUp(GlKeys key, bool shift, bool ctrl, bool alt) => _client.RunOnClientThread(() =>
    {
        NoteKeyUp((int)key);
        foreach (KeyEventHandler handler in Platform.keyEventHandlers.ToArray())
        {
            handler.OnKeyUp(new KeyEvent { KeyCode = (int)key, ShiftPressed = shift, CtrlPressed = ctrl, AltPressed = alt });
        }
    });

    public void KeyPress(char character) => _client.RunOnClientThread(() =>
    {
        foreach (KeyEventHandler handler in Platform.keyEventHandlers.ToArray())
        {
            handler.OnKeyPress(new KeyEvent { KeyCode = character, KeyChar = character });
        }
    });

    public void MouseMove(int x, int y, int deltaX, int deltaY) => _client.RunOnClientThread(() =>
    {
        s_mouseX?.SetValue(Platform, (float)x);
        s_mouseY?.SetValue(Platform, (float)y);

        foreach (MouseEventHandler handler in Platform.mouseEventHandlers.ToArray())
        {
            handler.OnMouseMove(new MouseEvent(x, y, deltaX, deltaY));
        }
    });

    public void MouseButton(EnumMouseButton button, bool pressed, int x, int y, int modifiers) => _client.RunOnClientThread(() =>
    {
        s_mouseX?.SetValue(Platform, (float)x);
        s_mouseY?.SetValue(Platform, (float)y);

        foreach (MouseEventHandler handler in Platform.mouseEventHandlers.ToArray())
        {
            MouseEvent e = new(x, y, button, modifiers);
            if (pressed) handler.OnMouseDown(e);
            else handler.OnMouseUp(e);
        }
    });

    public void MouseWheel(int delta) => _client.RunOnClientThread(() =>
    {
        _wheelValue += delta;
        foreach (MouseEventHandler handler in Platform.mouseEventHandlers.ToArray())
        {
            handler.OnMouseWheel(new MouseWheelEventArgs { delta = delta, deltaPrecise = delta, value = (int)_wheelValue, valuePrecise = _wheelValue });
        }
    });
}
