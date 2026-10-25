namespace Zaldaryon.Pharos.Input;

/// <summary>A Pharos-owned clipboard that works headless on both platforms.</summary>
public sealed class ClipboardDriver
{
    private string _text = string.Empty;

    /// <summary>The clipboard's current text.</summary>
    public string Text
    {
        get => _text;
        set => _text = value ?? string.Empty;
    }

    /// <summary>Sets the clipboard text.</summary>
    public void Set(string text) => _text = text ?? string.Empty;

    /// <summary>Gets the clipboard text.</summary>
    public string Get() => _text;
}