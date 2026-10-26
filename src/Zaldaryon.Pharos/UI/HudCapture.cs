using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.UI;

/// <summary>Observes actual composition/rendering without reevaluating a mod's HUD callbacks.</summary>
internal static class HudCapture
{
    private static readonly object s_lock = new();
    private static bool s_installed;
    private static readonly ConditionalWeakTable<ClientMain, System.Func<long>> s_frames = new();
    private static readonly ConditionalWeakTable<DrawWorldInteractionUtil, HelpState> s_help = new();
    private static readonly ConditionalWeakTable<GuiComposer, HelpState> s_helpComposers = new();
    private static readonly ConditionalWeakTable<GuiDialog, MessageState> s_messages = new();
    private static readonly ConditionalWeakTable<GuiComposer, MessageState> s_messageComposers = new();

    internal sealed class HelpState
    {
        public readonly SortedDictionary<double, HudInteractionEntry> Entries = [];
        public List<HudHotkeySnapshot>? Keys;
        public long RenderedFrame = -1;
    }

    private sealed class MessageState(HudElement owner, bool discovery)
    {
        public readonly HudElement Owner = owner;
        public readonly bool Discovery = discovery;
        public long Occurrence;
        public long SeenOccurrence = -1;
        public long ActiveMarker;
        public long RenderedFrame = -1;
        public HudMessageSnapshot? Message;
    }

    public static void Register(ClientMain game, System.Func<long> frame) => s_frames.AddOrUpdate(game, frame);

    public static void Install()
    {
        lock (s_lock)
        {
            if (s_installed) return;
            (Type Type, string Method, string? Prefix, string? Postfix)[] hooks =
            [
                (typeof(DrawWorldInteractionUtil), nameof(DrawWorldInteractionUtil.ComposeBlockWorldInteractionHelp), nameof(BeforeHelpCompose), nameof(AfterHelpCompose)),
                (typeof(DrawWorldInteractionUtil), nameof(DrawWorldInteractionUtil.drawHelp), nameof(BeforeHelpDraw), nameof(AfterHelpDraw)),
                (typeof(DrawWorldInteractionUtil), "DrawHotkey", nameof(BeforeHotkeyDraw), null),
                (typeof(HudIngameError), nameof(HudIngameError.ComposeGuis), null, nameof(AfterErrorCompose)),
                (typeof(HudIngameDiscovery), nameof(HudIngameDiscovery.ComposeGuis), null, nameof(AfterDiscoveryCompose)),
                (typeof(HudIngameError), "Event_InGameError", null, nameof(AfterErrorEvent)),
                (typeof(GuiComposer), nameof(GuiComposer.Render), null, nameof(AfterComposerRender)),
            ];
            var methods = hooks.Select(h => AccessTools.Method(h.Type, h.Method)
                ?? throw new InvalidOperationException($"Cannot observe HUD method '{h.Type.FullName}.{h.Method}'. Check Pharos/game version compatibility.")).ToArray();
            var harmony = new Harmony("zaldaryon.pharos.hud");
            for (int i = 0; i < hooks.Length; i++)
                harmony.Patch(methods[i], Patch(hooks[i].Prefix), Patch(hooks[i].Postfix));
            s_installed = true;
        }
    }

    private static HarmonyMethod? Patch(string? name) => name == null ? null : new HarmonyMethod(typeof(HudCapture), name);

    private static void BeforeHelpCompose(DrawWorldInteractionUtil __instance)
    {
        HelpState state = s_help.GetOrCreateValue(__instance);
        state.Entries.Clear();
        state.RenderedFrame = -1;
    }

    private static void AfterHelpCompose(DrawWorldInteractionUtil __instance)
    {
        if (__instance.Composer is { } composer)
            s_helpComposers.AddOrUpdate(composer, s_help.GetOrCreateValue(__instance));
    }

    private static void BeforeHelpDraw(DrawWorldInteractionUtil __instance) => s_help.GetOrCreateValue(__instance).Keys = [];

    private static void BeforeHotkeyDraw(DrawWorldInteractionUtil __instance, HotKey __0)
    {
        if (s_help.GetOrCreateValue(__instance).Keys is not { } keys) return;
        KeyCombination mapping = __0.CurrentMapping;
        string label;
        if (mapping.IsMouseButton(mapping.KeyCode))
            label = (mapping.KeyCode - 240) switch { 0 => "leftmousebutton", 2 => "rightmousebutton", 1 => "mb", int button => "b" + button };
        else
        {
            List<string> parts = [];
            if (mapping.Ctrl) parts.Add(GlKeyNames.ToString(GlKeys.LControl));
            if (mapping.Shift) parts.Add(GlKeyNames.ToString(GlKeys.LShift));
            parts.Add(mapping.PrimaryAsString());
            label = string.Join(" + ", parts);
        }
        keys.Add(new HudHotkeySnapshot(__0.Code, label));
    }

    private static void AfterHelpDraw(DrawWorldInteractionUtil __instance, ElementBounds __2, ItemStack[]? __3, WorldInteraction __5)
    {
        HelpState state = s_help.GetOrCreateValue(__instance);
        state.Entries[__2.fixedY] = new HudInteractionEntry(__5.HotKeyCode, __5.HotKeyCodes?.ToArray() ?? (__5.HotKeyCode is { } code ? [code] : []),
            state.Keys?.ToArray() ?? [], Lang.Get(__5.ActionLangCode), __5.MouseButton,
            __3?.Select(s => s.Collectible.Code.ToString()).ToArray() ?? [], __5.RequireFreeHand);
        state.Keys = null;
    }

    private static void AfterErrorCompose(HudIngameError __instance) => RememberMessage(__instance, discovery: false);
    private static void AfterDiscoveryCompose(HudIngameDiscovery __instance) => RememberMessage(__instance, discovery: true);

    private static void RememberMessage(HudElement hud, bool discovery)
    {
        var state = new MessageState(hud, discovery);
        s_messages.AddOrUpdate(hud, state);
        s_messageComposers.AddOrUpdate(hud.Composers["ingameerror"], state);
    }

    private static void AfterErrorEvent(HudIngameError __instance)
    {
        if (s_messages.TryGetValue(__instance, out MessageState? state)) state.Occurrence++;
    }

    private static void AfterComposerRender(GuiComposer __instance)
    {
        // Main-menu composers (login screen after a kick or before joining) carry a MainMenuAPI whose
        // World getter throws NotImplementedException; only in-game composers have a ClientMain world.
        if (!__instance.Enabled) return;
        ClientMain? game;
        try
        {
            game = __instance.Api.World as ClientMain;
        }
        catch (NotImplementedException)
        {
            return;
        }
        if (game == null || !s_frames.TryGetValue(game, out var frame)) return;
        long rendered = frame();
        if (s_helpComposers.TryGetValue(__instance, out HelpState? help)) help.RenderedFrame = rendered;
        if (!s_messageComposers.TryGetValue(__instance, out MessageState? state)) return;
        var element = HudDriver.RequiredField<GuiElementHoverText>(state.Owner, "elem");
        if (!element.IsVisible) return;
        long marker = HudDriver.RequiredField<long>(state.Owner, state.Discovery ? "textActiveMs" : "errorTextActiveMs");
        if (marker == 0) return;
        string text = element.GetText();
        if (state.Message == null || state.ActiveMarker != marker || state.SeenOccurrence != state.Occurrence || state.Message.Text != text)
        {
            state.Message = new HudMessageSnapshot(text, rendered);
            state.ActiveMarker = marker;
            state.SeenOccurrence = state.Occurrence;
        }
        state.RenderedFrame = rendered;
    }

    public static IReadOnlyList<HudInteractionEntry> Help(DrawWorldInteractionUtil util, long frame) =>
        util.Composer is { Enabled: true } && s_help.TryGetValue(util, out HelpState? state) && state.RenderedFrame == frame
            ? state.Entries.Values.Select(e => e with
            {
                HotkeyCodes = e.HotkeyCodes.ToArray(), Hotkeys = e.Hotkeys.ToArray(), ItemCodes = e.ItemCodes.ToArray(),
            }).ToArray() : [];

    public static IReadOnlyList<HudMessageSnapshot> Messages(HudElement hud, long frame) =>
        s_messages.TryGetValue(hud, out MessageState? state) && state.RenderedFrame == frame && state.Message != null
            ? [state.Message] : [];
}
