namespace Zaldaryon.Pharos.Input;

/// <summary>How <see cref="VirtualInputController.TypeText(string, TextInputMode)"/> delivers text.</summary>
public enum TextInputMode
{
    /// <summary>One key press per UTF-16 code unit, as a keyboard types.</summary>
    PerCodePoint,

    /// <summary>A single committed string, as an IME commits it.</summary>
    ImeCommit,
}