using System.Collections.Generic;
using System.Reflection;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.UI;

/// <summary>
/// Inspects headless client GUI dialogs, HUD elements, and widget tree without rendering.
/// Uses reflection to access ScreenManager internal state.
/// </summary>
public sealed class GuiInspector
{
    private readonly ScreenManager? _screenManager;
    private readonly object _lock = new();

    // Mock state for testing without live ScreenManager
    private readonly List<string> _mockDialogs = new();
    private readonly List<string> _mockHudElements = new();
    private bool _mockHasModal;
    private readonly Dictionary<string, Dictionary<string, Action>> _mockButtonHandlers = new();

    /// <summary>
    /// Creates an inspector with a live ScreenManager.
    /// </summary>
    /// <param name="screenManager">The ScreenManager to inspect.</param>
    public GuiInspector(ScreenManager? screenManager)
    {
        _screenManager = screenManager;
    }

    /// <summary>
    /// Creates an inspector in mock mode for testing.
    /// </summary>
    public GuiInspector() : this(null)
    {
    }

    /// <summary>
    /// Gets a list of currently open dialog names or class names.
    /// </summary>
    /// <returns>Read-only list of dialog identifiers.</returns>
    public IReadOnlyList<string> GetOpenDialogs()
    {
        lock (_lock)
        {
            if (_screenManager == null)
            {
                return _mockDialogs.Count == 0
                    ? Array.Empty<string>()
                    : _mockDialogs.ToArray();
            }

            return GetDialogsFromScreenManager();
        }
    }

    /// <summary>
    /// Checks if a dialog with the given name or class is currently open.
    /// </summary>
    /// <param name="nameOrClass">Dialog name or class name to check.</param>
    /// <returns>True if the dialog is open.</returns>
    public bool IsDialogOpen(string nameOrClass)
    {
        if (string.IsNullOrEmpty(nameOrClass)) return false;

        var dialogs = GetOpenDialogs();
        for (int i = 0; i < dialogs.Count; i++)
        {
            if (string.Equals(dialogs[i], nameOrClass, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Gets a list of active HUD element names or class names.
    /// </summary>
    /// <returns>Read-only list of HUD element identifiers.</returns>
    public IReadOnlyList<string> GetHudElements()
    {
        lock (_lock)
        {
            if (_screenManager == null)
            {
                return _mockHudElements.Count == 0
                    ? Array.Empty<string>()
                    : _mockHudElements.ToArray();
            }

            return GetHudElementsFromScreenManager();
        }
    }

    /// <summary>
    /// Simulates a button click within a dialog.
    /// In mock mode, invokes registered handlers. In live mode, uses reflection
    /// to find and invoke the button's click handler.
    /// </summary>
    /// <param name="dialogName">Name or class of the dialog containing the button.</param>
    /// <param name="buttonKey">Key or identifier of the button to click.</param>
    /// <returns>True if the button was found and clicked successfully.</returns>
    public bool SimulateButtonClick(string dialogName, string buttonKey)
    {
        if (string.IsNullOrEmpty(dialogName) || string.IsNullOrEmpty(buttonKey))
            return false;

        lock (_lock)
        {
            if (_screenManager == null)
            {
                // Mock mode: invoke registered handler
                if (_mockButtonHandlers.TryGetValue(dialogName, out var buttons) &&
                    buttons.TryGetValue(buttonKey, out var handler))
                {
                    handler?.Invoke();
                    return true;
                }
                return false;
            }

            return SimulateButtonClickOnScreenManager(dialogName, buttonKey);
        }
    }

    /// <summary>
    /// Captures an immutable snapshot of the current GUI state.
    /// </summary>
    /// <returns>Immutable GUI snapshot.</returns>
    public GuiSnapshot Snapshot()
    {
        lock (_lock)
        {
            var dialogs = GetOpenDialogs();
            var huds = GetHudElements();
            bool hasModal = HasModalDialog();

            return new GuiSnapshot(
                dialogs.Count == 0 ? Array.Empty<string>() : dialogs.ToArray(),
                huds.Count == 0 ? Array.Empty<string>() : huds.ToArray(),
                hasModal);
        }
    }

    /// <summary>
    /// Checks if a modal dialog is currently blocking input.
    /// </summary>
    /// <returns>True if a modal dialog is open.</returns>
    public bool HasModalDialog()
    {
        lock (_lock)
        {
            if (_screenManager == null)
            {
                return _mockHasModal;
            }

            return CheckModalFromScreenManager();
        }
    }

    // -------------------------------------------------------------------------
    // Mock mode methods for testing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a mock dialog for testing (mock mode only).
    /// </summary>
    /// <param name="dialogName">Name of the dialog to add.</param>
    public void AddMockDialog(string dialogName)
    {
        if (string.IsNullOrEmpty(dialogName)) return;
        lock (_lock)
        {
            if (!_mockDialogs.Contains(dialogName))
                _mockDialogs.Add(dialogName);
        }
    }

    /// <summary>
    /// Removes a mock dialog (mock mode only).
    /// </summary>
    /// <param name="dialogName">Name of the dialog to remove.</param>
    public void RemoveMockDialog(string dialogName)
    {
        if (string.IsNullOrEmpty(dialogName)) return;
        lock (_lock)
        {
            _mockDialogs.Remove(dialogName);
            _mockButtonHandlers.Remove(dialogName);
        }
    }

    /// <summary>
    /// Adds a mock HUD element for testing (mock mode only).
    /// </summary>
    /// <param name="hudElementName">Name of the HUD element to add.</param>
    public void AddMockHudElement(string hudElementName)
    {
        if (string.IsNullOrEmpty(hudElementName)) return;
        lock (_lock)
        {
            if (!_mockHudElements.Contains(hudElementName))
                _mockHudElements.Add(hudElementName);
        }
    }

    /// <summary>
    /// Removes a mock HUD element (mock mode only).
    /// </summary>
    /// <param name="hudElementName">Name of the HUD element to remove.</param>
    public void RemoveMockHudElement(string hudElementName)
    {
        if (string.IsNullOrEmpty(hudElementName)) return;
        lock (_lock)
        {
            _mockHudElements.Remove(hudElementName);
        }
    }

    /// <summary>
    /// Sets the mock modal state (mock mode only).
    /// </summary>
    /// <param name="hasModal">Whether a modal dialog should be simulated.</param>
    public void SetMockModal(bool hasModal)
    {
        lock (_lock)
        {
            _mockHasModal = hasModal;
        }
    }

    /// <summary>
    /// Registers a mock button handler for testing (mock mode only).
    /// </summary>
    /// <param name="dialogName">Dialog containing the button.</param>
    /// <param name="buttonKey">Button identifier.</param>
    /// <param name="handler">Handler to invoke when button is clicked.</param>
    public void RegisterMockButtonHandler(string dialogName, string buttonKey, Action handler)
    {
        if (string.IsNullOrEmpty(dialogName) || string.IsNullOrEmpty(buttonKey))
            return;

        lock (_lock)
        {
            if (!_mockButtonHandlers.TryGetValue(dialogName, out var buttons))
            {
                buttons = new Dictionary<string, Action>();
                _mockButtonHandlers[dialogName] = buttons;
            }
            buttons[buttonKey] = handler;
        }
    }

    /// <summary>
    /// Clears all mock state (mock mode only).
    /// </summary>
    public void ClearMockState()
    {
        lock (_lock)
        {
            _mockDialogs.Clear();
            _mockHudElements.Clear();
            _mockButtonHandlers.Clear();
            _mockHasModal = false;
        }
    }

    // -------------------------------------------------------------------------
    // Live ScreenManager inspection via reflection
    // -------------------------------------------------------------------------

    private IReadOnlyList<string> GetDialogsFromScreenManager()
    {
        if (_screenManager == null) return Array.Empty<string>();

        try
        {
            // ScreenManager.OpenedGuis or similar field
            FieldInfo? dialogsField = typeof(ScreenManager).GetField("OpenedGuis",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (dialogsField?.GetValue(_screenManager) is IEnumerable<object> dialogs)
            {
                var result = new List<string>();
                foreach (var dialog in dialogs)
                {
                    string name = dialog.GetType().Name;
                    // Try to get DialogKey or similar identifier
                    PropertyInfo? keyProp = dialog.GetType().GetProperty("DialogKey") ??
                                           dialog.GetType().GetProperty("DialogName");
                    if (keyProp?.GetValue(dialog) is string key && !string.IsNullOrEmpty(key))
                    {
                        result.Add(key);
                    }
                    else
                    {
                        result.Add(name);
                    }
                }
                return result;
            }
        }
        catch
        {
            // Ignore reflection errors, return empty
        }

        return Array.Empty<string>();
    }

    private IReadOnlyList<string> GetHudElementsFromScreenManager()
    {
        if (_screenManager == null) return Array.Empty<string>();

        try
        {
            // ScreenManager.HudDialogs or similar
            FieldInfo? hudField = typeof(ScreenManager).GetField("HudDialogs",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (hudField?.GetValue(_screenManager) is IEnumerable<object> huds)
            {
                var result = new List<string>();
                foreach (var hud in huds)
                {
                    result.Add(hud.GetType().Name);
                }
                return result;
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return Array.Empty<string>();
    }

    private bool CheckModalFromScreenManager()
    {
        if (_screenManager == null) return false;

        try
        {
            PropertyInfo? modalProp = typeof(ScreenManager).GetProperty("IsModalOpen",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (modalProp?.GetValue(_screenManager) is bool isModal)
            {
                return isModal;
            }

            // Fallback: check if any dialog is modal
            FieldInfo? dialogsField = typeof(ScreenManager).GetField("OpenedGuis",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (dialogsField?.GetValue(_screenManager) is IEnumerable<object> dialogs)
            {
                foreach (var dialog in dialogs)
                {
                    PropertyInfo? isModalProp = dialog.GetType().GetProperty("IsModal") ??
                                               dialog.GetType().GetProperty("Modal");
                    if (isModalProp?.GetValue(dialog) is true)
                    {
                        return true;
                    }
                }
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return false;
    }

    private bool SimulateButtonClickOnScreenManager(string dialogName, string buttonKey)
    {
        if (_screenManager == null) return false;

        try
        {
            // Find the dialog
            FieldInfo? dialogsField = typeof(ScreenManager).GetField("OpenedGuis",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

            if (dialogsField?.GetValue(_screenManager) is IEnumerable<object> dialogs)
            {
                foreach (var dialog in dialogs)
                {
                    string name = dialog.GetType().Name;
                    PropertyInfo? keyProp = dialog.GetType().GetProperty("DialogKey") ??
                                           dialog.GetType().GetProperty("DialogName");
                    string? key = keyProp?.GetValue(dialog) as string;

                    if (string.Equals(name, dialogName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(key, dialogName, StringComparison.OrdinalIgnoreCase))
                    {
                        // Try to find and invoke button handler
                        MethodInfo? clickMethod = dialog.GetType().GetMethod("OnButtonClick",
                            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                        if (clickMethod != null)
                        {
                            clickMethod.Invoke(dialog, new object[] { buttonKey });
                            return true;
                        }

                        // Try GuiComposer approach
                        PropertyInfo? composerProp = dialog.GetType().GetProperty("SingleComposer") ??
                                                    dialog.GetType().GetProperty("Composer");
                        if (composerProp?.GetValue(dialog) is object composer)
                        {
                            MethodInfo? getButtonMethod = composer.GetType().GetMethod("GetButton");
                            if (getButtonMethod != null)
                            {
                                var button = getButtonMethod.Invoke(composer, new object[] { buttonKey });
                                if (button != null)
                                {
                                    MethodInfo? onClickMethod = button.GetType().GetMethod("OnMouseUpOnElement");
                                    onClickMethod?.Invoke(button, Array.Empty<object>());
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            // Ignore reflection errors
        }

        return false;
    }
}
