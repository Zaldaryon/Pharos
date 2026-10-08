using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>
/// The hooks the client inspectors read from: the render stage loop, the particle pools and block
/// highlights.
/// </summary>
/// <remarks>
/// Installed when an engine-mode client boots, before its first frame: a method the runtime has
/// already compiled into its callers would go on running unpatched there. Each hook does nothing
/// until an inspector asks it to.
/// </remarks>
internal static class ClientInspectionPatches
{
    private static readonly object s_lock = new();
    private static readonly Harmony s_harmony = new("zaldaryon.pharos.inspection");
    private static bool s_installed;

    public static void Install()
    {
        lock (s_lock)
        {
            if (s_installed) return;

            s_harmony.Patch(
                AccessTools.Method(typeof(ClientEventManager), nameof(ClientEventManager.TriggerRenderStage)),
                prefix: new HarmonyMethod(typeof(RendererInspector), nameof(RendererInspector.BeforeStage)));
            s_harmony.Patch(
                AccessTools.Method(typeof(ParticlePoolQuads), nameof(ParticlePoolQuads.SpawnParticles), [typeof(IParticlePropertiesProvider)]),
                postfix: new HarmonyMethod(typeof(ParticleInspector), nameof(ParticleInspector.AfterSpawn)));
            s_harmony.Patch(
                AccessTools.Method(typeof(BlockHighlight), nameof(BlockHighlight.TesselateModel)),
                postfix: new HarmonyMethod(typeof(HighlightInspector), nameof(HighlightInspector.AfterTesselate)));
            s_installed = true;
        }
    }
}
