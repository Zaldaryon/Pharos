using System.Reflection;
using Vintagestory.API.Client;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Input;

namespace Zaldaryon.Pharos.UI;

/// <summary>Snapshot of a text field's selection state.</summary>
public sealed record TextSelectionSnapshot(int Start, int End, string SelectedText);

/// <summary>Reads and drives a text field in an open dialog.</summary>
public sealed class TextDriver
{
    private readonly HeadlessClient _client;
    private readonly string _dialog;
    private readonly string _key;

    // Cached reflection for private selection field
    private static readonly FieldInfo? SelectedTextStartField =
        typeof(GuiElementEditableTextBase).GetField("selectedTextStart", BindingFlags.NonPublic | BindingFlags.Instance);

    internal TextDriver(HeadlessClient client, string dialog, string key)
    {
        _client = client;
        _dialog = dialog;
        _key = key;
    }

    /// <summary>The text the field currently holds.</summary>
    public string Value => _client.RunOnClientThread(() =>
    {
        var element = Element();
        return element is GuiElementEditableTextBase editable ? editable.GetText() : string.Empty;
    });

    /// <summary>The caret position in code units (without line breaks).</summary>
    public int Caret => _client.RunOnClientThread(() =>
    {
        var element = Element();
        return element is GuiElementEditableTextBase editable ? editable.CaretPosWithoutLineBreaks : 0;
    });

    /// <summary>The caret line number (0-based) for multiline fields.</summary>
    public int CaretLine => _client.RunOnClientThread(() =>
    {
        var element = Element();
        return element is GuiElementEditableTextBase editable ? editable.CaretPosLine : 0;
    });

    /// <summary>The caret position within its line (0-based).</summary>
    public int CaretInLine => _client.RunOnClientThread(() =>
    {
        var element = Element();
        return element is GuiElementEditableTextBase editable ? editable.CaretPosInLine : 0;
    });

    /// <summary>Total text length without line break characters.</summary>
    public int Length => _client.RunOnClientThread(() =>
    {
        var element = Element();
        return element is GuiElementEditableTextBase editable ? editable.TextLengthWithoutLineBreaks : 0;
    });

    /// <summary>Current selection state, or null if no selection.</summary>
    public TextSelectionSnapshot? Selection => _client.RunOnClientThread(() =>
    {
        var element = Element();
        if (element is not GuiElementEditableTextBase editable) return null;

        // Read the private selectedTextStart field
        int? selStart = SelectedTextStartField?.GetValue(editable) as int?;
        if (selStart == null) return null;

        int caretPos = editable.CaretPosWithoutLineBreaks;
        int start = Math.Min(selStart.Value, caretPos);
        int end = Math.Max(selStart.Value, caretPos);
        if (start == end) return null;

        string text = editable.GetText();
        string selected = text.Substring(start, end - start);
        return new TextSelectionSnapshot(start, end, selected);
    });

    /// <summary>Whether there is a text selection active.</summary>
    public bool HasSelection => Selection != null;

    /// <summary>Whether the text field is currently focused.</summary>
    public bool IsFocused => _client.RunOnClientThread(() =>
    {
        var element = Element();
        return element?.HasFocus ?? false;
    });

    private GuiElement? Element()
    {
        GuiDialog? dialog = _client.Ui?.Dialog(_dialog);
        if (dialog == null) return null;
        return ((IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers).ToList()
            .Select(c => c.Value.GetElement(_key))
            .FirstOrDefault(e => e != null);
    }

    /// <summary>Sets the caret to a specific position (code units without line breaks).</summary>
    public void SetCaret(int position) => _client.RunOnClientThread(() =>
    {
        var element = Element();
        if (element is GuiElementEditableTextBase editable)
        {
            editable.CaretPosWithoutLineBreaks = Math.Clamp(position, 0, editable.TextLengthWithoutLineBreaks);
        }
    });

    /// <summary>Sets the caret to a specific line and column position.</summary>
    public void SetCaret(int line, int column) => _client.RunOnClientThread(() =>
    {
        var element = Element();
        if (element is GuiElementEditableTextBase editable)
        {
            editable.SetCaretPos(column, line);
        }
    });

    /// <summary>Moves the caret by <paramref name="n"/> positions.</summary>
    public void MoveCaret(int n) => _client.RunOnClientThread(() =>
    {
        var element = Element();
        if (element is GuiElementEditableTextBase editable)
        {
            int target = Math.Clamp(editable.CaretPosWithoutLineBreaks + n, 0, editable.TextLengthWithoutLineBreaks);
            editable.CaretPosWithoutLineBreaks = target;
        }
    });

    /// <summary>Selects all text in the field using keyboard shortcut.</summary>
    public void SelectAll()
    {
        // Ctrl+A is the standard select-all shortcut
        _client.Input.InjectKey(GlKeys.ControlLeft, pressed: true);
        _client.Input.PressKey(GlKeys.A);
        _client.Input.InjectKey(GlKeys.ControlLeft, pressed: false);
    }

    /// <summary>
    /// Creates a selection from <paramref name="start"/> to <paramref name="end"/> positions.
    /// </summary>
    public void Select(int start, int end) => _client.RunOnClientThread(() =>
    {
        var element = Element();
        if (element is GuiElementEditableTextBase editable)
        {
            int textLength = editable.TextLengthWithoutLineBreaks;
            start = Math.Clamp(start, 0, textLength);
            end = Math.Clamp(end, 0, textLength);

            // Set selection start via reflection
            SelectedTextStartField?.SetValue(editable, start);
            // Move caret to end position
            editable.CaretPosWithoutLineBreaks = end;
        }
    });

    /// <summary>Clears any active selection.</summary>
    public void ClearSelection() => _client.RunOnClientThread(() =>
    {
        var element = Element();
        if (element is GuiElementEditableTextBase editable)
        {
            SelectedTextStartField?.SetValue(editable, null);
        }
    });

    /// <summary>Copies the current selection to clipboard.</summary>
    public void Copy()
    {
        _client.Input.InjectKey(GlKeys.ControlLeft, pressed: true);
        _client.Input.PressKey(GlKeys.C);
        _client.Input.InjectKey(GlKeys.ControlLeft, pressed: false);
    }

    /// <summary>Cuts the current selection to clipboard.</summary>
    public void Cut()
    {
        _client.Input.InjectKey(GlKeys.ControlLeft, pressed: true);
        _client.Input.PressKey(GlKeys.X);
        _client.Input.InjectKey(GlKeys.ControlLeft, pressed: false);
    }

    /// <summary>Pastes from clipboard at the current caret position.</summary>
    public void Paste()
    {
        _client.Input.InjectKey(GlKeys.ControlLeft, pressed: true);
        _client.Input.PressKey(GlKeys.V);
        _client.Input.InjectKey(GlKeys.ControlLeft, pressed: false);
    }

    /// <summary>Deletes <paramref name="n"/> characters before the caret (backspace).</summary>
    public void Backspace(int n = 1)
    {
        for (int i = 0; i < n; i++)
        {
            _client.Input.PressKey(GlKeys.BackSpace);
        }
    }

    /// <summary>Deletes <paramref name="n"/> characters after the caret (delete key).</summary>
    public void Delete(int n = 1)
    {
        for (int i = 0; i < n; i++)
        {
            _client.Input.PressKey(GlKeys.Delete);
        }
    }

    /// <summary>Moves caret to start of text.</summary>
    public void Home()
    {
        _client.Input.PressKey(GlKeys.Home);
    }

    /// <summary>Moves caret to end of text.</summary>
    public void End()
    {
        _client.Input.PressKey(GlKeys.End);
    }

    /// <summary>Types text into the field using the game's text input system.</summary>
    public void Type(string text)
    {
        _client.Input.TypeText(text);
    }

    /// <summary>
    /// Commits text using IME-style composition (distinct from TypeText for international input).
    /// Currently uses the same path as TypeText; distinct IME events may be added in the future.
    /// </summary>
    public void ImeCommit(string text)
    {
        _client.Input.TypeText(text, TextInputMode.ImeCommit);
    }
}
