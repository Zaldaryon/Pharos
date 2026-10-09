using System.Collections;
using System.Globalization;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.UI;

/// <summary>A layer of the world map.</summary>
/// <param name="Code">The code the layer is registered under, such as <c>chunks</c>, <c>players</c> or <c>waypoints</c>.</param>
/// <param name="Title">The layer's own title, such as <c>Player Set Markers</c>.</param>
/// <param name="GroupCode">
/// The group the layer belongs to, one tab of the map each, such as <c>terrain</c> for the
/// chunks and the players. <see cref="WorldMapDriver.SetLayerGroupActiveAsync"/> takes it.
/// </param>
/// <param name="Active">Whether the layer is drawn.</param>
/// <param name="DataSide">Where the layer's data comes from: <c>Client</c> or <c>Server</c>.</param>
public sealed record MapLayerInfo(string Code, string Title, string GroupCode, bool Active, string DataSide);

/// <summary>A marker a layer of the world map draws.</summary>
/// <param name="Layer">The code of the layer.</param>
/// <param name="Label">What the marker stands for: a player's name, an entity's name or a waypoint's title.</param>
/// <param name="Position">Where it is in the world, in absolute block coordinates.</param>
public sealed record MapMarkerInfo(string Layer, string Label, Vec3d Position);

/// <summary>A waypoint, as the client or the server holds it.</summary>
/// <param name="Index">
/// Its number among the waypoints its owner owns, which <c>/waypoint remove</c> takes. In the
/// client's list, which also holds the waypoints of the player's groups, a waypoint another
/// player owns has -1.
/// </param>
/// <param name="Title">Its title.</param>
/// <param name="Icon">Its icon, such as <c>circle</c> or <c>home</c>.</param>
/// <param name="Color">Its colour as <c>#RRGGBB</c>.</param>
/// <param name="Position">Where it is, in absolute block coordinates.</param>
/// <param name="Pinned">Whether it stays on the edge of the map when off view.</param>
/// <param name="OwnerUid">The uid of the player who owns it.</param>
/// <param name="Guid">Its unique id.</param>
public sealed record WaypointInfo(int Index, string Title, string Icon, string Color, Vec3d Position, bool Pinned, string? OwnerUid, string? Guid);

/// <summary>
/// Drives the world map and the minimap of an engine-mode client: opens them with their hotkeys,
/// reads and toggles the map's layers, reads the markers, and adds and removes waypoints through
/// the game's own waypoint dialogs.
/// </summary>
/// <remarks>
/// <para>
/// The map belongs to the essentials mod, which Pharos reaches by name. A world whose
/// configuration sets <c>allowMap</c> to false, for a player without the <c>allowMap</c>
/// privilege, has no map: <see cref="IsAvailable"/> is false and opening throws.
/// </para>
/// <para>
/// A layer group is toggled by a real click on its tab of the open map. A waypoint is added
/// through the "Add waypoint" dialog the map opens on a right-click. Pharos opens that dialog
/// itself, on the exact position (a right-click picks a position one map pixel wide, at the
/// terrain's height), then picks the icon and the colour, types the title and clicks Save with
/// real clicks and keys, so the dialog sends <c>/waypoint addati</c> as for a player. Removing
/// one opens, the same way, the dialog a right-click on its marker opens, and clicks its Delete
/// button, which sends <c>/waypoint remove</c>.
/// </para>
/// <para>
/// The map's dialog is 1200 by 800 GUI pixels with its layer tabs on its left: on a small window,
/// or at a large GUI scale, make it smaller with <see cref="SetSizeAsync"/> before opening it.
/// Showing or hiding the minimap changes the client's <c>showMinimapHud</c> setting, which is
/// process-wide: put it back after a test. While the full map is open, the minimap is not shown
/// and cannot be toggled.
/// </para>
/// </remarks>
public sealed class WorldMapDriver
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string ManagerTypeName = "Vintagestory.GameContent.WorldMapManager";
    private const string AddDialogTypeName = "Vintagestory.GameContent.GuiDialogAddWayPoint";
    private const string EditDialogTypeName = "Vintagestory.GameContent.GuiDialogEditWayPoint";

    private readonly HeadlessClient _client;
    private readonly GuiDriver _gui;

    internal WorldMapDriver(HeadlessClient client, GuiDriver gui)
    {
        _client = client;
        _gui = gui;
    }

    /// <summary>Whether the client may use the map: the essentials mod is loaded and the world allows it.</summary>
    public bool IsAvailable => _client.RunOnClientThread(() =>
        ManagerOrNull() is { } manager && manager.GetType().GetMethod("mapAllowedClient", AnyInstance)?.Invoke(manager, null) is true);

    /// <summary>Whether the full world map is open.</summary>
    public bool IsOpen => _client.RunOnClientThread(() => MapDialog() is { } dialog && dialog.IsOpened() && dialog.DialogType == EnumDialogType.Dialog);

    /// <summary>Whether the minimap is shown. False while the full map is open, which takes its place.</summary>
    public bool IsMinimapShown => _client.RunOnClientThread(() => MapDialog() is { } dialog && dialog.IsOpened() && dialog.DialogType == EnumDialogType.HUD);

    /// <summary>
    /// Opens the full world map with its hotkey and waits until it is open. Does nothing when it is open.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client has no map, or the hotkey did not open it.</exception>
    public async Task OpenAsync(CancellationToken ct = default)
    {
        RequireAvailable();
        if (IsOpen) return;

        await _client.Hotkeys.TriggerAsync("worldmapdialog", ct).ConfigureAwait(false);
        if (!await _gui.StepUntilAsync(() => MapDialog() is { } d && d.IsOpened() && d.DialogType == EnumDialogType.Dialog, 30, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The world map hotkey did not open the world map.");
        }

        await _client.StepAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Closes the full world map as its close button does, and steps a frame. The game turns it
    /// back into the minimap when the minimap is on. Does nothing when it is not open.
    /// </summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        bool closing = _client.RunOnClientThread(() =>
        {
            if (MapDialog() is not { } dialog || !dialog.IsOpened() || dialog.DialogType != EnumDialogType.Dialog) return false;
            dialog.TryClose();
            return true;
        });
        if (closing) await _client.StepAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Shows or hides the minimap with its hotkey, which also saves the client's
    /// <c>showMinimapHud</c> setting, as for a player. Does nothing when it already is.
    /// </summary>
    /// <remarks>
    /// With the full map open, the game's hotkey would turn the full map into the minimap without
    /// saving the setting, and closing the full map shows the minimap again when the setting is
    /// on: close the full map first.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The client has no map, the full map is open, or the hotkey did not change the minimap.
    /// </exception>
    public async Task SetMinimapAsync(bool shown, CancellationToken ct = default)
    {
        RequireAvailable();
        if (IsOpen) throw new InvalidOperationException("Close the full map first: while it is open, the minimap hotkey turns it into the minimap without saving the setting.");
        if (IsMinimapShown == shown) return;

        await _client.Hotkeys.TriggerAsync("worldmaphud", ct).ConfigureAwait(false);
        if (!await _gui.StepUntilAsync(() => (MapDialog() is { } d && d.IsOpened() && d.DialogType == EnumDialogType.HUD) == shown, 30, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"The minimap hotkey did not {(shown ? "show" : "hide")} the minimap.");
        }
    }

    /// <summary>
    /// Sets the size of the full map's dialog in GUI pixels, as <c>.map worldmapsize</c> does, and
    /// runs that command. The game registers the command when it first builds the map's dialog, so
    /// the full map is opened and closed first when it has not been built yet. The game keeps the
    /// size until the client leaves the world: put 1200 by 800 back after a test.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client has no map, the full map is open, or the command failed.</exception>
    public async Task SetSizeAsync(int width, int height, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        RequireAvailable();
        if (IsOpen) throw new InvalidOperationException("Close the full map first: the new size applies when it opens.");

        if (_client.RunOnClientThread(() => MapDialog() == null))
        {
            await OpenAsync(ct).ConfigureAwait(false);
            await CloseAsync(ct).ConfigureAwait(false);
        }

        await _client.Commands.ExecuteSuccessAsync(string.Create(CultureInfo.InvariantCulture, $".map worldmapsize {width} {height}"), ct: ct).ConfigureAwait(false);
    }

    /// <summary>The map's layers, in the order the game draws them.</summary>
    /// <exception cref="InvalidOperationException">The client has no map.</exception>
    public IReadOnlyList<MapLayerInfo> Layers() => _client.RunOnClientThread(() =>
    {
        object manager = RequireManager();
        Dictionary<string, Type> registry = Field<Dictionary<string, Type>>(manager, "MapLayerRegistry") ?? throw GameChanged();
        return (IReadOnlyList<MapLayerInfo>)LayersOf(manager).Select(layer => new MapLayerInfo(
            registry.FirstOrDefault(r => r.Value == layer.GetType()).Key ?? layer.GetType().Name,
            Property<string>(layer, "Title") ?? "",
            Property<string>(layer, "LayerGroupCode") ?? "",
            layer.GetType().GetProperty("Active")?.GetValue(layer) is true,
            layer.GetType().GetProperty("DataSide")?.GetValue(layer)?.ToString() ?? "")).ToList();
    });

    /// <summary>
    /// Shows or hides every layer of the group <paramref name="groupCode"/> by clicking the group's
    /// tab of the open world map, and waits until the layers follow. Does nothing when the tab is
    /// already in that state.
    /// </summary>
    /// <exception cref="InvalidOperationException">The world map is not open, or the click did not toggle the group.</exception>
    /// <exception cref="ArgumentException">The map has no such group.</exception>
    public async Task SetLayerGroupActiveAsync(string groupCode, bool active, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupCode);
        (string dialogName, int index, bool already) = _client.RunOnClientThread(() =>
        {
            GuiDialog dialog = MapDialog() is { } d && d.IsOpened() && d.DialogType == EnumDialogType.Dialog
                ? d
                : throw new InvalidOperationException("Open the world map first: its layer tabs are on the full map.");
            List<string> groups = Field<List<string>>(dialog, "tabnames") ?? throw GameChanged();
            int i = groups.IndexOf(groupCode);
            if (i < 0) throw new ArgumentException($"The world map has no layer group '{groupCode}'. Groups: {string.Join(", ", groups)}.", nameof(groupCode));
            GuiElement tabs = dialog.SingleComposer?.GetElement("verticalTabs") ?? throw GameChanged();
            return (dialog.GetType().Name, i, GuiTabs.Read(tabs)[i].Active == active);
        });
        if (already) return;

        _gui.ClickTab(dialogName, "verticalTabs", index);
        if (!await _gui.StepUntilAsync(() => GroupActive(groupCode) == active, 10, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Clicking the '{groupCode}' tab did not {(active ? "show" : "hide")} its layers.");
        }
    }

    /// <summary>
    /// The markers the layer <paramref name="layerCode"/> draws: the players for <c>players</c>,
    /// the creatures for <c>entities</c> and the player's waypoints for <c>waypoints</c>.
    /// <c>chunks</c> draws none.
    /// </summary>
    /// <remarks>
    /// The server sends players' positions for the map only when the world's configuration sets
    /// <c>allowMap</c>, as the game's play styles do, one player every tenth of a second; the
    /// <c>entities</c> layer exists only when it sets <c>entityMapLayer</c>, and tracks creatures only
    /// while the map, full or minimap, is open.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The client has no map.</exception>
    /// <exception cref="NotSupportedException">Pharos does not read the markers of that layer, such as a mod's.</exception>
    public IReadOnlyList<MapMarkerInfo> Markers(string layerCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(layerCode);
        return _client.RunOnClientThread(() =>
        {
            object layer = RequireLayer(layerCode);
            List<MapMarkerInfo> markers = [];
            switch (layerCode)
            {
                case "chunks":
                    break;
                case "waypoints":
                    markers.AddRange(WaypointsOf(layer).Select(w => new MapMarkerInfo(layerCode, w.Title, w.Position)));
                    break;
                case "players":
                    // The layer draws every player the remote tracking system knows of.
                    object? tracking = Field<object>(layer, "playerTracking");
                    if (tracking?.GetType().GetMethod("GetAllTrackedPlayerPositions")?.Invoke(tracking, null) is IEnumerable positions)
                    {
                        foreach (object position in positions)
                        {
                            if (position.GetType().GetField("AssociatedPlayer")?.GetValue(position) is not IPlayer player) continue;
                            Vec3d pos = player.Entity?.Pos.XYZ ?? new Vec3d(Number(position, "PosX"), 0, Number(position, "PosZ"));
                            markers.Add(new MapMarkerInfo(layerCode, player.PlayerName, pos));
                        }
                    }

                    break;
                case "entities":
                    if (Field<IDictionary>(layer, "mapComps") is { } components)
                    {
                        foreach (object component in components.Values)
                        {
                            if (Field<Entity>(component, "entity") is { } entity) markers.Add(new MapMarkerInfo(layerCode, entity.GetName(), entity.Pos.XYZ));
                        }
                    }

                    break;
                default:
                    throw new NotSupportedException($"Pharos reads the markers of the vanilla layers only (players, entities, waypoints, chunks), not of '{layerCode}'.");
            }

            return (IReadOnlyList<MapMarkerInfo>)markers;
        });
    }

    /// <summary>
    /// The player's waypoints as the client holds them, the ones of its groups included. See
    /// <see cref="WaypointInfo.Index"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client has no map.</exception>
    public IReadOnlyList<WaypointInfo> Waypoints => _client.RunOnClientThread(() => WaypointsOf(RequireLayer("waypoints")));

    /// <summary>The icons a waypoint can have, in the order the dialog shows them.</summary>
    /// <exception cref="InvalidOperationException">The client has no map.</exception>
    public IReadOnlyList<string> WaypointIcons => _client.RunOnClientThread(() => Icons(RequireLayer("waypoints")));

    /// <summary>The colours a waypoint can have, as <c>#RRGGBB</c>, in the order the dialog shows them.</summary>
    /// <exception cref="InvalidOperationException">The client has no map.</exception>
    public IReadOnlyList<string> WaypointColors => _client.RunOnClientThread(() => Colors(RequireLayer("waypoints")));

    /// <summary>
    /// Adds a waypoint through the game's "Add waypoint" dialog, opened on <paramref name="position"/>:
    /// clicks the icon and the colour, empties the name the dialog suggests, types
    /// <paramref name="title"/>, switches pinned on when asked and clicks Save. The dialog sends
    /// <c>/waypoint addati</c>; this waits until the server's answer reaches the client.
    /// </summary>
    /// <param name="position">Where the waypoint goes, in absolute block coordinates.</param>
    /// <param name="title">Its title. The dialog saves nothing without one.</param>
    /// <param name="icon">One of <see cref="WaypointIcons"/>.</param>
    /// <param name="color">One of <see cref="WaypointColors"/>, or null for the first.</param>
    /// <param name="pinned">Whether to pin it.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The waypoint as the client received it.</returns>
    /// <exception cref="ArgumentException">The icon or the colour is not one the dialog offers; the message lists them.</exception>
    /// <exception cref="InvalidOperationException">The client has no map, or the waypoint never arrived.</exception>
    public async Task<WaypointInfo> AddWaypointAsync(Vec3d position, string title, string icon = "circle", string? color = null, bool pinned = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(icon);
        RequireAvailable();

        Vec3d target = position.Clone();
        (GuiDialog dialog, int iconIndex, int colorIndex, HashSet<string?> before) = _client.RunOnClientThread(() =>
        {
            object layer = RequireLayer("waypoints");
            List<string> icons = Icons(layer);
            int i = icons.IndexOf(icon);
            if (i < 0) throw new ArgumentException($"Waypoints have no icon '{icon}'. Icons: {string.Join(", ", icons)}.", nameof(icon));
            List<string> colors = Colors(layer);
            int c = color == null ? 0 : colors.FindIndex(x => string.Equals(x, color, StringComparison.OrdinalIgnoreCase));
            if (c < 0) throw new ArgumentException($"Waypoints have no colour '{color}'. Colours: {string.Join(", ", colors)}.", nameof(color));

            // The dialog the map opens on a right-click, on the exact position.
            Type type = layer.GetType().Assembly.GetType(AddDialogTypeName) ?? throw GameChanged();
            GuiDialog added = (GuiDialog)Activator.CreateInstance(type, _client.Client.api, layer)!;
            type.GetProperty("WorldPos")!.SetValue(added, target);
            added.TryOpen();
            return (added, i, c, WaypointsOf(layer).Select(w => w.Guid).ToHashSet());
        });

        try
        {
            await _client.StepAsync(ct).ConfigureAwait(false);
            string name = dialog.GetType().Name;
            _gui.Click(name, $"iconPicker-{iconIndex}");
            _gui.Click(name, $"colorPicker-{colorIndex}");
            if (pinned) _gui.Click(name, "pinnedSwitch");

            // Picking an icon or a colour fills in a suggested name; typing over a suggestion
            // would clear it on the first key instead of adding to it.
            _gui.ClearInput(name, "nameInput");
            _client.Input.TypeText(title);
            await _client.StepAsync(ct).ConfigureAwait(false);

            string? typed = _gui.Find(name, "nameInput")?.Text;
            if (typed != title) throw new InvalidOperationException($"The waypoint dialog's name reads '{typed}' after typing '{title}'.");

            _gui.Click(name, "saveButton");
            bool arrived = await _gui.StepUntilAsync(
                () => WaypointsOf(RequireLayer("waypoints")).Any(w => !before.Contains(w.Guid) && w.Title == title && w.Position.Equals(target, 0.01)),
                300, ct).ConfigureAwait(false);
            if (!arrived)
            {
                bool stillOpen = _client.RunOnClientThread(dialog.IsOpened);
                throw new InvalidOperationException(stillOpen
                    ? "The waypoint dialog did not save: its Save button did nothing."
                    : $"The server never sent back the waypoint '{title}'.");
            }

            return _client.RunOnClientThread(() => WaypointsOf(RequireLayer("waypoints")).Last(w => !before.Contains(w.Guid) && w.Title == title && w.Position.Equals(target, 0.01)));
        }
        finally
        {
            _client.RunOnClientThread(() => Discard(dialog));
        }
    }

    /// <summary>
    /// Removes the waypoint titled <paramref name="title"/> that the player owns through the
    /// dialog a right-click on its marker opens, which Pharos opens for the marker, by clicking
    /// its Delete button. The dialog sends <c>/waypoint remove</c> with the waypoint's number
    /// among the player's own, as the server counts them; this waits until the server's answer
    /// reaches the client. Waypoints of the player's groups that others own are left alone.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The player owns no waypoint with that title.</exception>
    /// <exception cref="InvalidOperationException">The client has no map, or the waypoint was not removed.</exception>
    public async Task RemoveWaypointAsync(string title, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        RequireAvailable();

        (GuiDialog dialog, string? guid, string delete) = _client.RunOnClientThread(() =>
        {
            object layer = RequireLayer("waypoints");
            IList all = Field<IList>(layer, "ownWaypoints") ?? throw GameChanged();
            List<WaypointInfo> waypoints = WaypointsOf(layer);
            int at = waypoints.FindIndex(w => w.Title == title && w.Index >= 0);
            if (at < 0)
            {
                throw new KeyNotFoundException($"The player owns no waypoint '{title}'. Own waypoints: {string.Join(", ", waypoints.Where(w => w.Index >= 0).Select(w => w.Title))}.");
            }

            // The dialog a waypoint's marker opens on a right-click, with its number among the
            // player's own waypoints: the number /waypoint remove takes.
            WaypointInfo info = waypoints[at];
            Type type = layer.GetType().Assembly.GetType(EditDialogTypeName) ?? throw GameChanged();
            GuiDialog edit = (GuiDialog)Activator.CreateInstance(type, _client.Client.api, layer, all[at], info.Index)!;
            edit.TryOpen();
            return (edit, info.Guid, Lang.Get("Delete"));
        });

        try
        {
            await _client.StepAsync(ct).ConfigureAwait(false);
            string name = dialog.GetType().Name;
            GuiElementInfo button = _gui.Elements(name).FirstOrDefault(e => e.Type == "GuiElementTextButton" && e.Text == delete)
                ?? throw new InvalidOperationException("The waypoint dialog has no Delete button.");
            _gui.Click(name, button.Key);

            bool removed = await _gui.StepUntilAsync(
                () => !WaypointsOf(RequireLayer("waypoints")).Any(w => w.Guid == guid && w.Title == title),
                300, ct).ConfigureAwait(false);
            if (!removed) throw new InvalidOperationException($"The waypoint '{title}' was not removed.");
        }
        finally
        {
            _client.RunOnClientThread(() => Discard(dialog));
        }
    }

    // A dialog Pharos opened: closed, and not left among the client's dialogs.
    private void Discard(GuiDialog dialog)
    {
        if (dialog.IsOpened()) dialog.TryClose();
        _client.Client.api.Gui.LoadedGuis.Remove(dialog);
        dialog.Dispose();
    }

    // Client thread.
    private object? ManagerOrNull() => _client.Client.api?.ModLoader.GetModSystem(ManagerTypeName);

    // Client thread.
    private object RequireManager() => ManagerOrNull()
        ?? throw new InvalidOperationException("The client has no world map: the essentials mod is not loaded.");

    // Client thread.
    private GuiDialog? MapDialog() => ManagerOrNull() is { } manager ? Field<GuiDialog>(manager, "worldMapDlg") : null;

    private void RequireAvailable()
    {
        string? missing = _client.RunOnClientThread(() =>
        {
            if (ManagerOrNull() is not { } manager) return "The world map is not available: the client has no world map manager, so the essentials mod is not loaded.";
            return manager.GetType().GetMethod("mapAllowedClient", AnyInstance)?.Invoke(manager, null) is true
                ? null
                : "The world map is not available: the world's configuration sets allowMap to false, and the player lacks the allowMap privilege.";
        });
        if (missing != null) throw new InvalidOperationException(missing);
    }

    private static List<object> LayersOf(object manager) =>
        (Field<IList>(manager, "MapLayers") ?? throw GameChanged()).Cast<object>().ToList();

    // Client thread.
    private object RequireLayer(string code)
    {
        object manager = RequireManager();
        Dictionary<string, Type> registry = Field<Dictionary<string, Type>>(manager, "MapLayerRegistry") ?? throw GameChanged();
        if (!registry.TryGetValue(code, out Type? type))
        {
            throw new InvalidOperationException($"The world map has no layer '{code}'. Layers: {string.Join(", ", registry.Keys)}.");
        }

        return LayersOf(manager).FirstOrDefault(l => l.GetType() == type)
            ?? throw new InvalidOperationException($"The world map's layer '{code}' is not loaded: the world has not finished loading, or its configuration leaves the layer out.");
    }

    // Client thread.
    private bool GroupActive(string groupCode) =>
        LayersOf(RequireManager()).Where(l => Property<string>(l, "LayerGroupCode") == groupCode).All(l => l.GetType().GetProperty("Active")?.GetValue(l) is true);

    // The game's own ordered dictionary: its keys, in order.
    private static List<string> Icons(object layer) =>
        layer.GetType().GetProperty("WaypointIcons")?.GetValue(layer) is { } icons && icons.GetType().GetProperty("Keys")?.GetValue(icons) is IEnumerable keys
            ? keys.Cast<string>().ToList()
            : throw GameChanged();

    private static List<string> Colors(object layer) =>
        layer.GetType().GetProperty("WaypointColors")?.GetValue(layer) is List<int> colors ? colors.Select(ColorUtil.Int2Hex).ToList() : throw GameChanged();

    // Client thread. Numbered among the local player's own waypoints, as the server numbers them.
    private List<WaypointInfo> WaypointsOf(object layer)
    {
        string? uid = _client.Client.player?.PlayerUID;
        int own = 0;
        return (Field<IList>(layer, "ownWaypoints") ?? throw GameChanged()).Cast<object>()
            .Select(w => Waypoint(w, w.GetType().GetField("OwningPlayerUid")?.GetValue(w) as string == uid ? own++ : -1))
            .ToList();
    }

    /// <summary>A waypoint of the game as a <see cref="WaypointInfo"/>.</summary>
    internal static WaypointInfo Waypoint(object waypoint, int index)
    {
        Type type = waypoint.GetType();
        return new WaypointInfo(
            index,
            type.GetField("Title")?.GetValue(waypoint) as string ?? "",
            type.GetField("Icon")?.GetValue(waypoint) as string ?? "",
            ColorUtil.Int2Hex(type.GetField("Color")?.GetValue(waypoint) as int? ?? 0),
            (type.GetField("Position")?.GetValue(waypoint) as Vec3d)?.Clone() ?? new Vec3d(),
            type.GetField("Pinned")?.GetValue(waypoint) is true,
            type.GetField("OwningPlayerUid")?.GetValue(waypoint) as string,
            type.GetProperty("Guid")?.GetValue(waypoint) as string);
    }

    private static double Number(object target, string name) =>
        Convert.ToDouble(target.GetType().GetField(name)?.GetValue(target) ?? target.GetType().GetProperty(name)?.GetValue(target) ?? 0.0);

    private static T? Property<T>(object target, string name) where T : class =>
        target.GetType().GetProperty(name, AnyInstance)?.GetValue(target) as T;

    internal static T? Field<T>(object target, string name) where T : class
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            if (type.GetField(name, AnyInstance | BindingFlags.DeclaredOnly) is { } field) return field.GetValue(target) as T;
        }

        return null;
    }

    internal static InvalidOperationException GameChanged() =>
        new("Pharos cannot read the world map: the game changed.");
}

/// <summary>
/// The waypoints a server holds, for checking what a client's waypoint dialogs sent.
/// </summary>
public static class ServerWaypoints
{
    private const string ManagerTypeName = "Vintagestory.GameContent.WorldMapManager";

    /// <summary>
    /// The waypoints the player <paramref name="playerUid"/> owns on the server, numbered as
    /// <c>/waypoint remove</c> numbers them. Call it on the server's game thread.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server has no world map.</exception>
    public static IReadOnlyList<WaypointInfo> Of(ICoreServerAPI sapi, string playerUid)
    {
        ArgumentNullException.ThrowIfNull(sapi);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerUid);

        object manager = sapi.ModLoader.GetModSystem(ManagerTypeName)
            ?? throw new InvalidOperationException("The server has no world map: the essentials mod is not loaded.");
        object layer = (WorldMapDriver.Field<IList>(manager, "MapLayers") ?? throw WorldMapDriver.GameChanged())
            .Cast<object>().FirstOrDefault(l => l.GetType().Name == "WaypointMapLayer")
            ?? throw new InvalidOperationException("The server's world map has no waypoint layer.");

        return (WorldMapDriver.Field<IList>(layer, "Waypoints") ?? throw WorldMapDriver.GameChanged())
            .Cast<object>()
            .Where(w => w.GetType().GetField("OwningPlayerUid")?.GetValue(w) as string == playerUid)
            .Select(WorldMapDriver.Waypoint)
            .ToList();
    }
}
