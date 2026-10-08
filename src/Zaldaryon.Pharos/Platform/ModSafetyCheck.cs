using Vintagestory.ModDb;

namespace Zaldaryon.Pharos.Platform;

/// <summary>
/// Turns off the game's mod safety check for hosts Pharos boots.
/// </summary>
/// <remarks>
/// On its first boot in a process, the client and the server each fetch the list of blocked mods
/// from the game's website, on a background thread, and the server waits for it before it loads
/// mods. Offline, or with a slow network, that costs up to three ten-second timeouts and logs a
/// warning per attempt, at a moment that depends on the network and on which test booted first. A
/// test run has no use for the list, so Pharos fills it in empty before the game asks for it.
/// </remarks>
internal static class ModSafetyCheck
{
    public static void Disable()
    {
        ModDbUtil.ModBlockList ??= [];
        ModDbUtil.ModBlockListMRE.Set();
    }
}
