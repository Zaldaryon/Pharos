using System.Reflection;
using HarmonyLib;
using Vintagestory.Client;

namespace Zaldaryon.Pharos.Bootstrap;

/// <summary>
/// Keeps an offline engine-mode client on its running game screen when that screen is loaded.
/// </summary>
/// <remarks>
/// <para>
/// <c>GuiScreen.OnScreenLoaded</c> checks the cached session key on every screen it loads and
/// replaces the screen with the login screen when the key does not verify. The key is signed by
/// the Vintage Story auth server, so a headless test client never has a valid one. Vanilla
/// reaches that check for the running game screen when the map is loaded: the client hands
/// rendering over to the game by loading its screen, and the login screen takes its place.
/// Nothing renders the game after that, so main thread tasks, among them every received chunk,
/// pile up unprocessed.
/// </para>
/// <para>
/// The prefix skips the check for <see cref="GuiScreenRunningGame"/> alone, which is the only
/// screen an engine-mode client loads. Every other screen keeps the vanilla behavior.
/// </para>
/// </remarks>
internal static class OfflineSessionPatcher
{
    private const string HarmonyId = "zaldaryon.pharos.offlinesession";
    private static readonly Harmony s_harmony = new(HarmonyId);
    private static readonly object s_lock = new();
    private static bool s_patched;

    public static void Patch()
    {
        lock (s_lock)
        {
            if (s_patched) return;

            MethodInfo onScreenLoaded = typeof(GuiScreen).GetMethod(nameof(GuiScreen.OnScreenLoaded), BindingFlags.Public | BindingFlags.Instance)
                ?? throw new MissingMethodException(nameof(GuiScreen), nameof(GuiScreen.OnScreenLoaded));
            s_harmony.Patch(onScreenLoaded, prefix: new HarmonyMethod(typeof(OfflineSessionPatcher), nameof(Prefix_OnScreenLoaded)));
            s_patched = true;
        }
    }

    private static bool Prefix_OnScreenLoaded(GuiScreen __instance) => __instance is not GuiScreenRunningGame;
}
