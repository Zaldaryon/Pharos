using Vintagestory.API.Client;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.UI;

/// <summary>Reads and drives a text field in an open dialog.</summary>
public sealed class TextDriver
{
    private readonly HeadlessClient _client;
    private readonly string _dialog;
    private readonly string _key;

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

    /// <summary>The caret position in code units.</summary>
    public int Caret => _client.RunOnClientThread(() =>
    {
        var element = Element();
        return element is GuiElementEditableTextBase editable ? editable.CaretPosWithoutLineBreaks : 0;
    });

    private GuiElement? Element()
    {
        GuiDialog? dialog = _client.Ui?.Dialog(_dialog);
        if (dialog == null) return null;
        return ((IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers).ToList()
            .Select(c => c.Value.GetElement(_key))
            .FirstOrDefault(e => e != null);
    }

    /// <summary>Selects all text in the field.</summary>
    public void SelectAll() => _client.RunOnClientThread(() =>
    {
        var element = Element();
        if (element is GuiElementEditableTextBase editable)
        {
            editable.SetCaretPos(0, 0);
            // Select all by setting caret to end; game handles selection via shift
        }
    });

    /// <summary>Moves the caret by <paramref name="n"/> positions.</summary>
    public void MoveCaret(int n) => _client.RunOnClientThread(() =>
    {
        var element = Element();
        if (element is GuiElementEditableTextBase editable)
        {
            int target = Math.Clamp(editable.CaretPosWithoutLineBreaks + n, 0, editable.GetText().Length);
            editable.SetCaretPos(target);
        }
    });

    /// <summary>Deletes <paramref name="n"/> characters before the caret.</summary>
    public void Backspace(int n)
    {
        for (int i = 0; i < n; i++)
        {
            _client.Input.PressKey(GlKeys.BackSpace);
        }
    }
}