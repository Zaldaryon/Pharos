using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Player;

namespace Zaldaryon.Pharos.UI;

/// <summary>A detached HUD element, including mod HUDs whose class names do not start with Hud.</summary>
/// <param name="Type">The full runtime type name.</param>
/// <param name="Visible">Whether the HUD is open and accepts rendering, with overlays enabled.</param>
/// <param name="Layout">Its composed bounds and element tree, including text.</param>
public sealed record HudElementSnapshot(string Type, bool Visible, DialogLayout Layout);

/// <summary>The values held by a composed stat bar, not by the player's watched attributes.</summary>
/// <param name="Value">The bar's current value.</param>
/// <param name="Max">The bar's upper bound.</param>
/// <param name="Future">The predicted future value, when the bar has one.</param>
/// <param name="Visible">Whether the bar is rendered, including its hide-when-full rule.</param>
/// <param name="Flashing">The bar's current ShouldFlash state.</param>
public sealed record HudStatbarSnapshot(float Value, float Max, float? Future, bool Visible, bool Flashing);

/// <summary>The inventory and selection used by the hotbar HUD.</summary>
/// <param name="ActiveSlot">The highlighted slot, including the skill slot or backpack selection; -1 when none.</param>
/// <param name="Slots">Detached hotbar inventory slots, including the skill and offhand slots.</param>
/// <param name="ShownItemName">The actual fading hover label, or empty when hidden.</param>
/// <param name="OffhandSlot">The player's left-hand slot.</param>
/// <param name="Visible">Whether the hotbar is rendered.</param>
/// <param name="Layout">The hotbar's composers and elements.</param>
public sealed record HudHotbarSnapshot(int ActiveSlot, IReadOnlyList<SlotSnapshot> Slots, string ShownItemName,
    SlotSnapshot OffhandSlot, bool Visible, DialogLayout Layout);

/// <summary>The coordinates text actually composed by the game.</summary>
/// <param name="Text">The shown text, or empty when hidden.</param>
/// <param name="Visible">Whether the HUD is rendered.</param>
/// <param name="HiddenBySetting">Whether ShowCoordinateHud is disabled.</param>
public sealed record HudCoordinatesSnapshot(string Text, bool Visible, bool HiddenBySetting);

/// <summary>
/// Reads detached snapshots of the engine-mode client's HUD. Call after stepping the frame that
/// applies a sync or setting change. Reads dispatch to the client thread; they do not advance time,
/// reevaluate mod text callbacks, or substitute entity attributes for composed GUI state.
/// </summary>
public sealed class HudDriver
{
    private readonly HeadlessClient _client;
    private readonly GuiDriver _gui;

    internal HudDriver(HeadlessClient client, GuiDriver gui)
    {
        _client = client;
        _gui = gui;
    }

    /// <summary>The composed health bar.</summary>
    public HudStatbarSnapshot Health => Statbar("healthstatbar");

    /// <summary>The composed saturation bar.</summary>
    public HudStatbarSnapshot Satiety => Statbar("saturationstatbar");

    /// <summary>The oxygen bar; normally hidden when full.</summary>
    public HudStatbarSnapshot Oxygen => Statbar("oxygenstatbar");

    /// <summary>The hotbar's highlighted slot, contents, shown item name and offhand.</summary>
    public HudHotbarSnapshot Hotbar => _client.RunOnClientThread(() =>
    {
        HudHotbar hud = Require<HudHotbar>();
        var inventory = RequiredField<IInventory>(hud, "hotbarInv");
        var grid = RequiredField<GuiElementItemSlotGrid>(hud, "hotbarSlotGrid");
        var backpack = RequiredField<GuiElementItemSlotGrid>(hud, "backpackGrid");
        int active = grid.highlightSlotId;
        if (active < 0 && backpack.highlightSlotId >= 0)
            active = 10 + ((inventory[10] ?? throw Changed("HudHotbar.hotbarInv[10]")).Empty ? 0 : 1) + backpack.highlightSlotId;
        var composer = hud.Composers["hotbar"]
            ?? throw new InvalidOperationException("HudHotbar has no composed hotbar. Join a world and step a frame before reading it.");
        var hover = composer.GetHoverText("iteminfoHover") ?? throw Changed("HudHotbar.iteminfoHover");
        bool visible = Visible(hud);
        return new HudHotbarSnapshot(active, Enumerable.Range(0, inventory.Count).Select(i => Slot(inventory[i], i)).ToArray(),
            visible && hover.IsVisible ? hover.GetText() : "",
            Slot(_client.Client.player.Entity.LeftHandItemSlot, 11), visible, _gui.LayoutOf(hud));
    });

    /// <summary>The coordinates text and the setting that hides it.</summary>
    public HudCoordinatesSnapshot Coordinates => _client.RunOnClientThread(() =>
    {
        HudElementCoordinates hud = Require<HudElementCoordinates>();
        bool visible = Visible(hud) && ClientSettings.ShowCoordinateHud;
        return new HudCoordinatesSnapshot(visible ? hud.SingleComposer?.GetDynamicText("text")?.GetText() ?? "" : "",
            visible, !ClientSettings.ShowCoordinateHud);
    });

    /// <summary>Reads one registered HUD of the given type, including closed elements.</summary>
    /// <exception cref="InvalidOperationException">No such HUD is registered, or several match; use Elements for duplicates.</exception>
    public HudElementSnapshot Element<T>() where T : HudElement => _client.RunOnClientThread(() => Snapshot(Require<T>()));

    /// <summary>Reads all registered HUDs. Closed elements remain inspectable but are marked invisible.</summary>
    public IReadOnlyList<HudElementSnapshot> Elements() => _client.RunOnClientThread(() =>
        _client.Client.api.Gui.LoadedGuis.OfType<HudElement>().Select(Snapshot).ToArray());

    /// <summary>Steps session-aware frames until the predicate holds, or throws after maxFrames steps.</summary>
    /// <remarks>A zero budget tests the current state without stepping. Cancellation wins even when the predicate holds.</remarks>
    /// <exception cref="TimeoutException">The predicate did not hold within the frame budget.</exception>
    public async Task WaitForAsync(System.Func<HudDriver, bool> predicate, int maxFrames, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentOutOfRangeException.ThrowIfNegative(maxFrames);
        ct.ThrowIfCancellationRequested();
        bool matched = await _gui.StepUntilAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            return predicate(this);
        }, maxFrames, ct).ConfigureAwait(false);
        if (!matched) throw new TimeoutException($"HUD condition did not hold within {maxFrames} frames.");
    }

    private HudStatbarSnapshot Statbar(string key) => _client.RunOnClientThread(() =>
    {
        HudStatbar hud = Require<HudStatbar>();
        var bar = hud.Composers["statbar"]?.GetStatbar(key)
            ?? throw new InvalidOperationException($"HUD stat bar '{key}' is not composed. Load a world with the survival HUD and step a frame.");
        float value = bar.GetValue();
        float max = RequiredField<float>(bar, "maxValue");
        return new HudStatbarSnapshot(value, max, RequiredField<float?>(bar, "futureValue"),
            Visible(hud) && !(bar.HideWhenFull && value == max), bar.ShouldFlash);
    });

    private T Require<T>() where T : HudElement
    {
        T[] matches = _client.Client.api.Gui.LoadedGuis.OfType<T>().ToArray();
        return matches.Length == 1 ? matches[0] : throw new InvalidOperationException(
            $"Expected one registered HUD '{typeof(T).FullName}', found {matches.Length}. Use Hud.Elements() to inspect registered elements.");
    }

    private HudElementSnapshot Snapshot(HudElement hud) => new(hud.GetType().FullName!, Visible(hud), _gui.LayoutOf(hud));

    private bool Visible(HudElement hud) => !_client.Client.api.HideGuis && hud.IsOpened() && hud.ShouldReceiveRenderEvents()
        && (hud is not (HudStatbar or HudHotbar) || _client.Client.player.WorldData.CurrentGameMode != EnumGameMode.Spectator);

    private static SlotSnapshot Slot(ItemSlot? slot, int index) => slot?.Itemstack is { } stack
        ? SlotSnapshot.WithItem(index, stack.Collectible.Code.ToString(), stack.StackSize) : SlotSnapshot.Empty(index);

    internal static T RequiredField<T>(object instance, string name)
    {
        for (Type? type = instance.GetType(); type != null; type = type.BaseType)
        {
            if (type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) is not { } field) continue;
            if (!typeof(T).IsAssignableFrom(field.FieldType))
                throw Changed($"{type.FullName}.{name} ({field.FieldType.FullName})");
            object? value = field.GetValue(instance);
            if (value is T result) return result;
            if (value == null && Nullable.GetUnderlyingType(typeof(T)) != null) return default!;
            throw Changed($"{type.FullName}.{name} ({field.FieldType.FullName})");
        }
        throw Changed($"{instance.GetType().FullName}.{name}");
    }

    private static InvalidOperationException Changed(string member) => new(
        $"Cannot inspect HUD member '{member}'. The installed game changed its HUD contract; check the Pharos/game version compatibility.");
}
