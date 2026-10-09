using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.UI;

/// <summary>
/// Drives the character dialog of an engine-mode client: opens it with its hotkey, switches its
/// tabs with real clicks, reads the stats the survival game shows beside it, and equips gear by
/// clicking a hotbar stack into a gear slot.
/// </summary>
/// <remarks>
/// <para>
/// The dialog's first tab holds the gear slots. The survival mod adds a tab for the class's
/// traits, and the essentials mod shows the environment and the stats in two panels beside the
/// dialog. Its tabs and slots are the game's own, so a mod that adds a tab or replaces the
/// dialog shows here too.
/// </para>
/// <para>
/// Equipping clicks the stack in the hotbar, then the gear slot, so the client sends the same slot
/// packets as for a player and the server applies its own rules. A creative player has no
/// character inventory open in the dialog: equipping needs survival or guest mode.
/// </para>
/// </remarks>
public sealed class CharacterDriver
{
    private const string HotbarDialog = "HudHotbar";
    private const string HotbarGrid = "hotbargrid";
    private const string MainComposer = "playercharacter";
    private const string StatsComposer = "playerstats";

    // Where each gear slot is in the dialog's grids, as GuiDialogCharacter lays them out.
    private static readonly (string Grid, int[] SlotIds)[] s_grids =
    [
        ("armorSlotsHead", [12]),
        ("armorSlotsBody", [13]),
        ("armorSlotsLegs", [14]),
        ("leftSlots", [0, 1, 2, 11, 3, 4]),
        ("rightSlots", [6, 7, 8, 10, 5, 9]),
    ];

    private static readonly FieldInfo? s_interactive = typeof(GuiComposer).GetField("interactiveElements", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_static = typeof(GuiComposer).GetField("staticElements", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly HeadlessClient _client;
    private readonly GuiDriver _gui;

    internal CharacterDriver(HeadlessClient client, GuiDriver gui)
    {
        _client = client;
        _gui = gui;
    }

    /// <summary>Whether the character dialog is open.</summary>
    public bool IsOpen => _client.RunOnClientThread(() => Dialog().IsOpened());

    /// <summary>
    /// Opens the character dialog with its hotkey and waits until it is open. Does nothing when it is open.
    /// </summary>
    /// <exception cref="InvalidOperationException">The hotkey did not open the dialog.</exception>
    public async Task OpenAsync(CancellationToken ct = default)
    {
        if (IsOpen) return;

        await _client.Hotkeys.TriggerAsync("characterdialog", ct).ConfigureAwait(false);
        if (!await _gui.StepUntilAsync(() => Dialog().IsOpened(), 30, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The character dialog hotkey did not open the dialog.");
        }

        await _client.StepAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Closes the character dialog, as its close button does, and steps a frame. Does nothing when it is closed.</summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (!_client.RunOnClientThread(() => Dialog() is { } dialog && dialog.IsOpened() && dialog.TryClose())) return;
        await _client.StepAsync(ct).ConfigureAwait(false);
    }

    /// <summary>The dialog's tabs, such as Character and Traits, with the one shown marked active.</summary>
    /// <exception cref="InvalidOperationException">The dialog is not open.</exception>
    public IReadOnlyList<GuiTabInfo> Tabs => _gui.Tabs(DialogName(), "tabs");

    /// <summary>The label of the tab the dialog shows.</summary>
    /// <exception cref="InvalidOperationException">The dialog is not open.</exception>
    public string CurrentTab => Tabs.First(t => t.Active).Name;

    /// <summary>Clicks the tab labelled <paramref name="name"/> (ignoring case) and waits until the dialog shows it.</summary>
    /// <exception cref="InvalidOperationException">The dialog is not open, has no such tab, or did not switch.</exception>
    public Task SwitchTabAsync(string name, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        IReadOnlyList<GuiTabInfo> tabs = Tabs;
        GuiTabInfo tab = tabs.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"The character dialog has no tab '{name}'. Tabs: {string.Join(", ", tabs.Select(t => t.Name))}.");
        return SwitchTabAsync(tab.Index, ct);
    }

    /// <summary>Clicks tab <paramref name="index"/> and waits until the dialog shows it.</summary>
    /// <exception cref="InvalidOperationException">The dialog is not open, or did not switch.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The dialog has no such tab.</exception>
    public async Task SwitchTabAsync(int index, CancellationToken ct = default)
    {
        string dialog = DialogName();
        _gui.ClickTab(dialog, "tabs", index);
        if (!await _gui.StepUntilAsync(() => GuiTabs.Read(TabStrip()).Any(t => t.Index == index && t.Active), 10, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Clicking tab {index} did not switch the character dialog to it.");
        }
    }

    /// <summary>
    /// The values of the stats panel beside the open dialog, by their element key: <c>health</c>,
    /// <c>satiety</c>, <c>bodytemp</c>, <c>walkspeed</c>, <c>healeffectiveness</c>,
    /// <c>hungerrate</c>, <c>rangedweaponacc</c> and <c>rangedweaponchargespeed</c> in the
    /// survival game, as the panel shows them, such as <c>100%</c>. Empty when nothing shows the panel.
    /// </summary>
    /// <exception cref="InvalidOperationException">The dialog is not open.</exception>
    public IReadOnlyDictionary<string, string> Stats() => _client.RunOnClientThread(() =>
    {
        GuiDialog dialog = RequireOpen();
        Dictionary<string, string> stats = [];
        if (dialog.Composers[StatsComposer] is not { } composer) return stats;

        foreach (Dictionary<string, GuiElement>? elements in new[] { s_static?.GetValue(composer), s_interactive?.GetValue(composer) }.Cast<Dictionary<string, GuiElement>?>())
        {
            foreach ((string key, GuiElement element) in elements ?? [])
            {
                // Labels have no key of their own: the composer numbers them.
                if (key.StartsWith("element-", StringComparison.Ordinal)) continue;
                if (GuiDriver.TextOf(element) is { } text) stats[key] = text;
            }
        }

        return (IReadOnlyDictionary<string, string>)stats;
    });

    /// <summary>The value of the stat <paramref name="code"/>. See <see cref="Stats"/>.</summary>
    /// <exception cref="KeyNotFoundException">The panel shows no such stat; the message lists those it shows.</exception>
    public string Stat(string code)
    {
        IReadOnlyDictionary<string, string> stats = Stats();
        return stats.TryGetValue(code, out string? value)
            ? value
            : throw new KeyNotFoundException($"The stats panel shows no '{code}'. Stats: {string.Join(", ", stats.Keys)}.");
    }

    /// <summary>
    /// What the client's character inventory holds in the slot for <paramref name="dressType"/>,
    /// a copy, or null when it is empty.
    /// </summary>
    public ItemStack? Gear(EnumCharacterDressType dressType) => _client.RunOnClientThread(() =>
        CharacterInventory()?[(int)dressType]?.Itemstack?.Clone());

    /// <summary>
    /// Equips the stack in hotbar slot <paramref name="hotbarSlot"/> as <paramref name="dressType"/>:
    /// clicks it in the hotbar, then clicks the gear slot in the dialog's first tab, and waits
    /// until the client's character inventory holds it. A slot that refuses the stack leaves it on
    /// the cursor: it is put back into the hotbar and this throws.
    /// </summary>
    /// <remarks>
    /// When the gear slot already holds something, the click swaps the two, as for a player: the
    /// old gear lands on the cursor and is then clicked into the hotbar slot the new gear came from.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The dialog is not open on its first tab, the player is not in survival or guest mode, the
    /// hotbar slot is empty, or the gear slot refused the stack.
    /// </exception>
    public async Task EquipAsync(int hotbarSlot, EnumCharacterDressType dressType, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hotbarSlot);
        (string grid, int index) = s_grids
            .Select(g => (g.Grid, Index: Array.IndexOf(g.SlotIds, (int)dressType)))
            .FirstOrDefault(g => g.Index >= 0);
        if (grid == null) throw new ArgumentOutOfRangeException(nameof(dressType), dressType, "The character dialog has no slot for it.");

        (string dialog, string code) = _client.RunOnClientThread(() =>
        {
            GuiDialog open = RequireOpen();
            EnumGameMode mode = _client.Client.player.WorldData.CurrentGameMode;
            if (mode is not (EnumGameMode.Survival or EnumGameMode.Guest))
            {
                throw new InvalidOperationException($"The player is in {mode} mode: the character dialog takes gear in survival or guest mode only.");
            }

            ItemSlot? slot = _client.Client.player.InventoryManager.GetHotbarInventory()?[hotbarSlot];
            if (slot == null || slot.Empty) throw new InvalidOperationException($"Hotbar slot {hotbarSlot} is empty.");
            if (open.Composers[MainComposer]?.GetElement(grid) == null)
            {
                throw new InvalidOperationException("Show the character dialog's first tab to equip gear: its gear slots are there.");
            }

            return (open.GetType().Name, slot.Itemstack.Collectible.Code.ToString());
        });

        _gui.ClickSlot(HotbarDialog, HotbarGrid, hotbarSlot);
        await _client.StepAsync(ct).ConfigureAwait(false);
        _gui.ClickSlot(dialog, grid, index);

        bool equipped = await _gui.StepUntilAsync(
            () => CharacterInventory()?[(int)dressType]?.Itemstack?.Collectible?.Code?.ToString() == code,
            60, ct).ConfigureAwait(false);
        if (equipped)
        {
            // A swap left the old gear on the cursor: into the hotbar slot, empty now.
            if (_client.RunOnClientThread(() => !_client.Client.player.InventoryManager.MouseItemSlot.Empty))
            {
                _gui.ClickSlot(HotbarDialog, HotbarGrid, hotbarSlot);
                if (!await _gui.StepUntilAsync(() => _client.Client.player.InventoryManager.MouseItemSlot.Empty, 60, ct).ConfigureAwait(false))
                {
                    throw new InvalidOperationException($"The gear {dressType} held before stayed on the cursor: hotbar slot {hotbarSlot} did not take it.");
                }
            }

            return;
        }

        // Refused: the stack is still on the cursor.
        if (_client.RunOnClientThread(() => !_client.Client.player.InventoryManager.MouseItemSlot.Empty))
        {
            _gui.ClickSlot(HotbarDialog, HotbarGrid, hotbarSlot);
            await _client.StepAsync(ct).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"The {dressType} slot did not take {code}.");
    }

    // Client thread.
    private GuiDialog Dialog() =>
        _client.Client.api.Gui.LoadedGuis.FirstOrDefault(d => d is GuiDialogCharacterBase)
        ?? throw new InvalidOperationException("The client has no character dialog.");

    // Client thread.
    private GuiDialog RequireOpen()
    {
        GuiDialog dialog = Dialog();
        return dialog.IsOpened() ? dialog : throw new InvalidOperationException("Open the character dialog first.");
    }

    private string DialogName() => _client.RunOnClientThread(() => RequireOpen().GetType().Name);

    // Client thread.
    private GuiElement TabStrip() =>
        RequireOpen().Composers[MainComposer]?.GetElement("tabs") ?? throw new InvalidOperationException("The character dialog has no tabs.");

    // Client thread.
    private IInventory? CharacterInventory() => _client.Client.player?.InventoryManager.GetOwnInventory("character");
}
