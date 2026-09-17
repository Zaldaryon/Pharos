using System.Collections.Generic;
using Vintagestory.API.Client;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// Provides programmatic control over client GUI screens, dialogs, and inventory interfaces.
/// </summary>
public interface IPlayerGuiController
{
    /// <summary>
    /// Gets all registered client GUI dialogs.
    /// </summary>
    IReadOnlyList<GuiDialog> LoadedDialogs { get; }

    /// <summary>
    /// Gets all currently open client GUI dialogs.
    /// </summary>
    IReadOnlyList<GuiDialog> OpenedDialogs { get; }

    /// <summary>
    /// Checks whether a dialog of the specified type is currently open.
    /// </summary>
    bool IsDialogOpen<T>() where T : GuiDialog;

    /// <summary>
    /// Gets the registered dialog instance of the specified type, or null if not registered.
    /// </summary>
    T? GetDialog<T>() where T : GuiDialog;

    /// <summary>
    /// Programmatically opens a dialog of the specified type. Returns true if successfully opened.
    /// </summary>
    bool OpenDialog<T>() where T : GuiDialog;

    /// <summary>
    /// Programmatically closes a dialog of the specified type. Returns true if closed or was not open.
    /// </summary>
    bool CloseDialog<T>() where T : GuiDialog;

    /// <summary>
    /// Programmatically opens the player's survival or creative inventory dialog.
    /// </summary>
    bool OpenInventory();

    /// <summary>
    /// Programmatically closes the player's inventory dialog.
    /// </summary>
    bool CloseInventory();

    /// <summary>
    /// Gets whether the player's inventory dialog is currently open.
    /// </summary>
    bool IsInventoryOpen { get; }

    /// <summary>
    /// Closes all currently opened client GUI dialogs.
    /// </summary>
    void CloseAllDialogs();
}
