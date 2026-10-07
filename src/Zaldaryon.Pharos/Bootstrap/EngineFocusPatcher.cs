using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Bootstrap;

/// <summary>
/// Makes an engine-mode client's window count as focused.
/// </summary>
/// <remarks>
/// The game ignores the mouse while its window is not focused: the running game screen drops
/// mouse buttons, the mouse is never grabbed, so mouse look does nothing. A headless window is
/// hidden and never gets the focus, so for the platforms of engine-mode clients, and only those,
/// <c>ClientPlatformWindows.IsFocused</c> answers true.
/// </remarks>
internal static class EngineFocusPatcher
{
    private const string HarmonyId = "zaldaryon.pharos.enginefocus";
    private static readonly Harmony s_harmony = new(HarmonyId);
    private static readonly ConditionalWeakTable<ClientPlatformWindows, object> s_focused = new();
    private static readonly object s_lock = new();
    private static bool s_patched;

    public static void Register(ClientPlatformWindows platform)
    {
        lock (s_lock)
        {
            if (!s_patched)
            {
                MethodInfo getter = typeof(ClientPlatformWindows).GetProperty(nameof(ClientPlatformWindows.IsFocused))?.GetGetMethod()
                    ?? throw new MissingMethodException(nameof(ClientPlatformWindows), "get_IsFocused");
                s_harmony.Patch(getter, prefix: new HarmonyMethod(typeof(EngineFocusPatcher), nameof(Prefix_IsFocused)));
                s_patched = true;
            }

            s_focused.AddOrUpdate(platform, s_lock);
        }
    }

    private static bool Prefix_IsFocused(ClientPlatformWindows __instance, ref bool __result)
    {
        if (!s_focused.TryGetValue(__instance, out _)) return true;

        __result = true;
        return false;
    }
}
