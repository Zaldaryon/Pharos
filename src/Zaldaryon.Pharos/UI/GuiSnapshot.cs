using System.Collections.Generic;

namespace Zaldaryon.Pharos.UI;

/// <summary>
/// Immutable snapshot of GUI state at a point in time.
/// Thread-safe capture of open dialogs, HUD elements, and modal state.
/// </summary>
/// <param name="OpenDialogs">Names or class names of currently open dialogs.</param>
/// <param name="HudElements">Names or class names of active HUD elements.</param>
/// <param name="HasModal">Whether a modal dialog is currently blocking input.</param>
public sealed record GuiSnapshot(
    IReadOnlyList<string> OpenDialogs,
    IReadOnlyList<string> HudElements,
    bool HasModal)
{
    /// <summary>
    /// Empty snapshot with no GUI state.
    /// </summary>
    public static GuiSnapshot Empty { get; } = new(
        Array.Empty<string>(),
        Array.Empty<string>(),
        false);

    /// <summary>
    /// Returns true if no dialogs or HUD elements are active.
    /// </summary>
    public bool IsEmpty => OpenDialogs.Count == 0 && HudElements.Count == 0;

    /// <summary>
    /// Total count of active GUI elements (dialogs + HUD).
    /// </summary>
    public int TotalElementCount => OpenDialogs.Count + HudElements.Count;

    /// <summary>
    /// Checks if a dialog with the given name or class is open.
    /// </summary>
    /// <param name="nameOrClass">Dialog name or class name to check.</param>
    /// <returns>True if the dialog is open.</returns>
    public bool IsDialogOpen(string nameOrClass)
    {
        if (string.IsNullOrEmpty(nameOrClass)) return false;
        
        for (int i = 0; i < OpenDialogs.Count; i++)
        {
            if (string.Equals(OpenDialogs[i], nameOrClass, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a HUD element with the given name or class is active.
    /// </summary>
    /// <param name="nameOrClass">HUD element name or class name to check.</param>
    /// <returns>True if the HUD element is active.</returns>
    public bool IsHudElementActive(string nameOrClass)
    {
        if (string.IsNullOrEmpty(nameOrClass)) return false;
        
        for (int i = 0; i < HudElements.Count; i++)
        {
            if (string.Equals(HudElements[i], nameOrClass, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
