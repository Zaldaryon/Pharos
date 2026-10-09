using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Input;

namespace Zaldaryon.Pharos.UI;

/// <summary>A dialog the engine-mode client has open.</summary>
/// <param name="Type">The dialog's class name, such as <c>GuiDialogCharacter</c>.</param>
/// <param name="DebugName">The dialog's own debug name.</param>
/// <param name="ToggleKey">The hotkey code that toggles it, if any.</param>
/// <param name="IsFocused">Whether it holds the keyboard focus.</param>
/// <param name="Composers">The names of its composers.</param>
public sealed record GuiDialogInfo(string Type, string? DebugName, string? ToggleKey, bool IsFocused, IReadOnlyList<string> Composers);

/// <summary>An element of an open dialog, with its on-screen bounds.</summary>
/// <param name="Dialog">The dialog's class name.</param>
/// <param name="Composer">The composer the element belongs to.</param>
/// <param name="Key">The element's key within the composer.</param>
/// <param name="Type">The element's class name, such as <c>GuiElementTextButton</c>.</param>
/// <param name="X">Left edge in screen pixels.</param>
/// <param name="Y">Top edge in screen pixels.</param>
/// <param name="Width">Width in screen pixels.</param>
/// <param name="Height">Height in screen pixels.</param>
/// <param name="Text">
/// The element's text, for text, button, input and rich text elements; a rich text element gives
/// its components' display texts joined, without markup.
/// </param>
/// <param name="IsInteractive">Whether the element receives input.</param>
/// <param name="IsClipped">Whether the element lies in a clipped region, such as a scrolled list, which shows only part of it.</param>
public sealed record GuiElementInfo(
    string Dialog, string Composer, string Key, string Type,
    double X, double Y, double Width, double Height,
    string? Text, bool IsInteractive, bool IsClipped = false)
{
    /// <summary>The element's centre, where a click lands.</summary>
    public (int X, int Y) Center => ((int)(X + Width / 2), (int)(Y + Height / 2));
}

/// <summary>
/// Reads and drives the GUI of an engine-mode client: the open dialogs, their elements with text
/// and bounds, and clicks and typing aimed at a specific element.
/// </summary>
/// <remarks>
/// Clicks are real mouse clicks at the element's centre, delivered through
/// <see cref="VirtualInputController"/>, so they go through the same hit testing, focus handling
/// and event handlers as a player's click. Nothing calls an element's handler directly. Dialogs
/// are matched by class name or debug name.
/// </remarks>
public sealed class GuiDriver
{
    private static readonly FieldInfo? s_interactive = typeof(GuiComposer).GetField("interactiveElements", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_static = typeof(GuiComposer).GetField("staticElements", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly HeadlessClient _client;

    internal GuiDriver(HeadlessClient client)
    {
        _client = client;
    }

    /// <summary>
    /// The survival handbook: open it, search it, open a page by its code and read the page. See
    /// <see cref="HandbookDriver"/>.
    /// </summary>
    public HandbookDriver Handbook => _handbook ??= new HandbookDriver(_client, this);

    private HandbookDriver? _handbook;

    /// <summary>
    /// The world map and the minimap: their layers, markers and waypoints. See <see cref="WorldMapDriver"/>.
    /// </summary>
    public WorldMapDriver WorldMap => _worldMap ??= new WorldMapDriver(_client, this);

    private WorldMapDriver? _worldMap;

    /// <summary>
    /// The character dialog: its tabs, the stats beside it and the gear slots. See <see cref="CharacterDriver"/>.
    /// </summary>
    public CharacterDriver Character => _character ??= new CharacterDriver(_client, this);

    private CharacterDriver? _character;

    /// <summary>The dialogs currently open, HUD elements included.</summary>
    public IReadOnlyList<GuiDialogInfo> OpenDialogs() => _client.RunOnClientThread(() =>
        _client.Client.api.Gui.OpenedGuis
            .Select(d => new GuiDialogInfo(d.GetType().Name, d.DebugName, d.ToggleKeyCombinationCode, d.Focused, d.Composers.Select(c => c.Key).ToList()))
            .ToList());

    /// <summary>Whether a dialog matching <paramref name="nameOrType"/> is open.</summary>
    public bool IsOpen(string nameOrType) => _client.RunOnClientThread(() => Dialog(nameOrType) != null);

    /// <summary>
    /// Every element of the open dialog matching <paramref name="nameOrType"/>, across its
    /// composers, with bounds and text.
    /// </summary>
    /// <exception cref="InvalidOperationException">No such dialog is open.</exception>
    public IReadOnlyList<GuiElementInfo> Elements(string nameOrType) => _client.RunOnClientThread(() => ElementsOf(RequireDialog(nameOrType)));

    // Client thread.
    private static List<GuiElementInfo> ElementsOf(GuiDialog dialog)
    {
        List<GuiElementInfo> elements = [];
        foreach (KeyValuePair<string, GuiComposer> entry in ((IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers).ToList())
        {
            (string composerName, GuiComposer composer) = (entry.Key, entry.Value);
            Dictionary<string, GuiElement>? interactive = s_interactive?.GetValue(composer) as Dictionary<string, GuiElement>;
            Dictionary<string, GuiElement>? statics = s_static?.GetValue(composer) as Dictionary<string, GuiElement>;
            HashSet<ElementBounds> clips = [];
            foreach (GuiElement element in (statics?.Values ?? Enumerable.Empty<GuiElement>()).Concat(interactive?.Values ?? Enumerable.Empty<GuiElement>()))
            {
                if (IsClip(element)) clips.Add(element.Bounds);
            }

            Add(elements, dialog, composerName, interactive, interactive: true, clips);
            Add(elements, dialog, composerName, statics, interactive: false, clips);
        }

        return elements;
    }

    /// <summary>
    /// How the open dialog matching <paramref name="nameOrType"/> is laid out now, for
    /// <see cref="Assertions.PharosAssert.DialogOnScreen(DialogLayout, double)"/> and
    /// <see cref="Assertions.PharosAssert.NoOverlappingElements"/>. Read it after a frame has run
    /// since the window or the GUI scale changed: dialogs lay themselves out again in a frame.
    /// </summary>
    /// <exception cref="InvalidOperationException">No such dialog is open.</exception>
    public DialogLayout Layout(string nameOrType) =>
        _client.RunOnClientThread(() =>
        {
            GuiDialog dialog = RequireDialog(nameOrType);

            // A composer lists an element that takes input among both its interactive and its
            // static elements: each once here.
            List<GuiElementInfo> elements = [];
            HashSet<(string, string)> seen = [];
            foreach (GuiElementInfo element in ElementsOf(dialog).OrderByDescending(e => e.IsInteractive))
            {
                if (seen.Add((element.Composer, element.Key))) elements.Add(element);
            }

            List<ComposerBounds> composers = [];
            foreach (KeyValuePair<string, GuiComposer> entry in ((IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers).ToList())
            {
                ElementBounds bounds = entry.Value.Bounds;
                composers.Add(new ComposerBounds(entry.Key, bounds.absX, bounds.absY, bounds.OuterWidth, bounds.OuterHeight));
            }

            return new DialogLayout(
                dialog.GetType().Name, _client.Platform.WindowSize.Width, _client.Platform.WindowSize.Height,
                RuntimeEnv.GUIScale, composers, elements);
        });

    /// <summary>How the open dialog of type <typeparamref name="TDialog"/> is laid out now. See <see cref="Layout(string)"/>.</summary>
    public DialogLayout Layout<TDialog>() where TDialog : GuiDialog => Layout(typeof(TDialog).Name);

    /// <summary>The element with key <paramref name="elementKey"/> in the dialog, or null.</summary>
    public GuiElementInfo? Find(string nameOrType, string elementKey) =>
        Elements(nameOrType).FirstOrDefault(e => e.Key == elementKey);

    /// <summary>
    /// Clicks the centre of an element with a real mouse click.
    /// </summary>
    /// <exception cref="InvalidOperationException">The dialog is not open or has no such element.</exception>
    public void Click(string nameOrType, string elementKey, VirtualMouseButton button = VirtualMouseButton.Left)
    {
        GuiElementInfo element = Find(nameOrType, elementKey)
            ?? throw new InvalidOperationException($"Dialog '{nameOrType}' has no element '{elementKey}'. Elements: {string.Join(", ", Elements(nameOrType).Select(e => e.Key))}.");
        (int x, int y) = element.Center;
        _client.Input.Click(x, y, button);
    }

    /// <summary>
    /// Clicks a text field to focus it and types <paramref name="text"/> into it.
    /// </summary>
    public void TypeInto(string nameOrType, string elementKey, string text)
    {
        Click(nameOrType, elementKey);
        _client.Input.TypeText(text);
    }

    /// <summary>
    /// Clicks a text field to focus it and empties it as a player does: Ctrl+A selects its text
    /// and Backspace deletes it, so the field's change handler sees the empty text.
    /// </summary>
    /// <exception cref="InvalidOperationException">The dialog is not open or has no such element.</exception>
    public void ClearInput(string nameOrType, string elementKey)
    {
        Click(nameOrType, elementKey);
        _client.Input.InjectKey(GlKeys.LControl, pressed: true);
        try
        {
            _client.Input.PressKey(GlKeys.A);
        }
        finally
        {
            _client.Input.InjectKey(GlKeys.LControl, pressed: false);
        }

        _client.Input.PressKey(GlKeys.BackSpace);
    }

    /// <summary>
    /// The tabs of the tab strip <paramref name="tabsKey"/> in the open dialog, vertical or
    /// horizontal, with their labels and which are active.
    /// </summary>
    /// <exception cref="InvalidOperationException">The dialog is not open or has no such tab strip.</exception>
    public IReadOnlyList<GuiTabInfo> Tabs(string nameOrType, string tabsKey) =>
        _client.RunOnClientThread(() => GuiTabs.Read(RequireTabStrip(nameOrType, tabsKey)));

    /// <summary>
    /// Clicks tab <paramref name="index"/> of the tab strip <paramref name="tabsKey"/> with a real
    /// mouse click, at a point inside the area the strip's own handler tests for that tab.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The dialog is not open, has no such tab strip, or the tab is scrolled out of view.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">The strip has no tab <paramref name="index"/>.</exception>
    public void ClickTab(string nameOrType, string tabsKey, int index)
    {
        (int x, int y) = _client.RunOnClientThread(() => GuiTabs.ClickPoint(RequireTabStrip(nameOrType, tabsKey), index));
        _client.Input.Click(x, y);
    }

    /// <summary>
    /// Clicks the tab labelled <paramref name="tabName"/> (ignoring case) of the tab strip
    /// <paramref name="tabsKey"/>. See <see cref="ClickTab(string, string, int)"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The dialog is not open, or no tab has that label.</exception>
    public void ClickTab(string nameOrType, string tabsKey, string tabName)
    {
        IReadOnlyList<GuiTabInfo> tabs = Tabs(nameOrType, tabsKey);
        GuiTabInfo tab = tabs.FirstOrDefault(t => string.Equals(t.Name, tabName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Tab strip '{tabsKey}' has no tab '{tabName}'. Tabs: {string.Join(", ", tabs.Select(t => t.Name))}.");
        ClickTab(nameOrType, tabsKey, tab.Index);
    }

    // Client thread.
    private GuiElement RequireTabStrip(string nameOrType, string tabsKey)
    {
        GuiDialog dialog = RequireDialog(nameOrType);
        return ((IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers).ToList()
            .Select(c => c.Value.GetElement(tabsKey))
            .FirstOrDefault(e => e != null && GuiTabs.IsTabStrip(e))
            ?? throw new InvalidOperationException($"Dialog '{nameOrType}' has no tab strip '{tabsKey}'.");
    }

    /// <summary>
    /// Clicks slot <paramref name="slotIndex"/> of an item slot grid, as a player clicks a slot to
    /// pick up or put down the stack.
    /// </summary>
    /// <param name="nameOrType">The dialog holding the grid.</param>
    /// <param name="gridKey">The slot grid's element key, such as <c>hotbar</c>.</param>
    /// <param name="slotIndex">The index of the slot within the grid, in display order.</param>
    /// <param name="button">The mouse button; right-click takes or drops half a stack.</param>
    /// <exception cref="InvalidOperationException">The grid or the slot does not exist.</exception>
    public void ClickSlot(string nameOrType, string gridKey, int slotIndex, VirtualMouseButton button = VirtualMouseButton.Left)
    {
        (int x, int y) = _client.RunOnClientThread(() =>
        {
            GuiDialog dialog = RequireDialog(nameOrType);
            GuiElementItemSlotGridBase grid = dialog.Composers.Values
                .Select(c => c.GetElement(gridKey))
                .OfType<GuiElementItemSlotGridBase>()
                .FirstOrDefault()
                ?? throw new InvalidOperationException($"Dialog '{nameOrType}' has no slot grid '{gridKey}'.");

            if (grid.SlotBounds == null || slotIndex < 0 || slotIndex >= grid.SlotBounds.Length)
            {
                throw new InvalidOperationException($"Slot grid '{gridKey}' has no slot {slotIndex} ({grid.SlotBounds?.Length ?? 0} slots).");
            }

            ElementBounds slot = grid.SlotBounds[slotIndex];
            return ((int)(slot.absX + slot.OuterWidth / 2), (int)(slot.absY + slot.OuterHeight / 2));
        });

        _client.Input.Click(x, y, button);
    }

    /// <summary>
    /// Steps frames, the whole session's when the client is in one, until <paramref name="condition"/>
    /// holds on the client thread, for at most <paramref name="maxFrames"/> frames.
    /// </summary>
    internal async Task<bool> StepUntilAsync(Func<bool> condition, int maxFrames, CancellationToken ct)
    {
        for (int i = 0; ; i++)
        {
            if (_client.RunOnClientThread(condition)) return true;
            if (i >= maxFrames) return false;
            await _client.StepAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>Closes the dialog the way its own close action does.</summary>
    /// <returns>Whether the dialog was open and closed.</returns>
    public bool Close(string nameOrType) => _client.RunOnClientThread(() => Dialog(nameOrType)?.TryClose() == true);

    // Client thread.
    internal GuiDialog? Dialog(string nameOrType) =>
        _client.Client.api.Gui.OpenedGuis.FirstOrDefault(d =>
            string.Equals(d.GetType().Name, nameOrType, StringComparison.OrdinalIgnoreCase)
            || string.Equals(d.DebugName, nameOrType, StringComparison.OrdinalIgnoreCase));

    // Client thread.
    internal GuiDialog RequireDialog(string nameOrType) =>
        Dialog(nameOrType) ?? throw new InvalidOperationException(
            $"No open dialog '{nameOrType}'. Open: {string.Join(", ", _client.Client.api.Gui.OpenedGuis.Select(d => d.GetType().Name))}.");

    private static void Add(List<GuiElementInfo> into, GuiDialog dialog, string composer, Dictionary<string, GuiElement>? elements, bool interactive, HashSet<ElementBounds> clips)
    {
        if (elements == null) return;

        foreach ((string key, GuiElement element) in elements)
        {
            ElementBounds bounds = element.Bounds;
            into.Add(new GuiElementInfo(
                dialog.GetType().Name, composer, key, element.GetType().Name,
                bounds.absX, bounds.absY, bounds.OuterWidth, bounds.OuterHeight,
                TextOf(element), interactive, !IsClip(element) && InClip(bounds, clips)));
        }
    }

    // The game's clip region element is internal: matched by name.
    private static bool IsClip(GuiElement element) => element.GetType().Name == "GuiElementClip";

    // Laid out inside a clip region: the region's bounds are among its parents.
    private static bool InClip(ElementBounds bounds, HashSet<ElementBounds> clips)
    {
        if (clips.Count == 0) return false;
        for (ElementBounds? parent = bounds.ParentBounds; parent != null && parent != parent.ParentBounds; parent = parent.ParentBounds)
        {
            if (clips.Contains(parent)) return true;
        }

        return false;
    }

    internal static string? TextOf(GuiElement element) => element switch
    {
        GuiElementTextButton button => button.Text,
        GuiElementTextBase text => text.GetText(),
        GuiElementRichtext rich => RichText(rich.Components),
        _ => null,
    };

    /// <summary>
    /// The text of rich text components, their display texts joined: links give their label and
    /// item stacks and images give nothing.
    /// </summary>
    internal static string RichText(IEnumerable<RichTextComponentBase>? components) =>
        components == null ? "" : string.Concat(components.OfType<RichTextComponent>().Select(c => c.DisplayText));
}
