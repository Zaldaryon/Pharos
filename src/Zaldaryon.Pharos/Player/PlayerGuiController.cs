using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// Concrete GUI controller providing programmatic inspection and manipulation of client GUI dialogs.
/// </summary>
public sealed class PlayerGuiController : IPlayerGuiController
{
    // Read on each use, so the class follows the client into a new game session after a reconnect.
    private readonly Func<ClientMain> _game;

    private ClientMain _client => _game();

    public PlayerGuiController(ClientMain client)
        : this(Fixed(client ?? throw new ArgumentNullException(nameof(client))))
    {
    }

    internal PlayerGuiController(Func<ClientMain> game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
    }

    private static Func<ClientMain> Fixed(ClientMain client) => () => client;

    private IEnumerable<GuiDialog> GetLoadedGuis()
    {
        if (_client.api?.Gui?.LoadedGuis is { } loadedGuis)
        {
            return loadedGuis;
        }

        FieldInfo? field = typeof(ClientMain).GetField("LoadedGuis", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (field?.GetValue(_client) is IEnumerable<GuiDialog> guis)
        {
            return guis;
        }

        return Enumerable.Empty<GuiDialog>();
    }

    public IReadOnlyList<GuiDialog> LoadedDialogs =>
        GetLoadedGuis().ToList().AsReadOnly();

    public IReadOnlyList<GuiDialog> OpenedDialogs =>
        GetLoadedGuis().Where(dlg => dlg.IsOpened()).ToList().AsReadOnly();

    public bool IsDialogOpen<T>() where T : GuiDialog
    {
        return GetLoadedGuis().OfType<T>().Any(dlg => dlg.IsOpened());
    }

    public T? GetDialog<T>() where T : GuiDialog
    {
        return GetLoadedGuis().OfType<T>().FirstOrDefault();
    }

    public bool OpenDialog<T>() where T : GuiDialog
    {
        T? dialog = GetDialog<T>();
        if (dialog == null)
        {
            return false;
        }

        return dialog.TryOpen();
    }

    public bool CloseDialog<T>() where T : GuiDialog
    {
        T? dialog = GetDialog<T>();
        if (dialog == null)
        {
            return false;
        }

        if (!dialog.IsOpened())
        {
            return true;
        }

        return dialog.TryClose();
    }

    public bool OpenInventory()
    {
        GuiDialog? invDialog = GetLoadedGuis().FirstOrDefault(dlg =>
            dlg is GuiDialogInventory || dlg.GetType().Name.Contains("Inventory", StringComparison.OrdinalIgnoreCase));

        return invDialog?.TryOpen() ?? false;
    }

    public bool CloseInventory()
    {
        GuiDialog? invDialog = GetLoadedGuis().FirstOrDefault(dlg =>
            (dlg is GuiDialogInventory || dlg.GetType().Name.Contains("Inventory", StringComparison.OrdinalIgnoreCase)) && dlg.IsOpened());

        if (invDialog == null)
        {
            return true;
        }

        return invDialog.TryClose();
    }

    public bool IsInventoryOpen =>
        GetLoadedGuis().Any(dlg =>
            (dlg is GuiDialogInventory || dlg.GetType().Name.Contains("Inventory", StringComparison.OrdinalIgnoreCase)) && dlg.IsOpened());

    public void CloseAllDialogs()
    {
        foreach (GuiDialog dialog in GetLoadedGuis().Where(dlg => dlg.IsOpened()).ToList())
        {
            try
            {
                dialog.TryClose();
            }
            catch
            {
                // Suppress errors during headless batch dialog closure
            }
        }
    }
}
