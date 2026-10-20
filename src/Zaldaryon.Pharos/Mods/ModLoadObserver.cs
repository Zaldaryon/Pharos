using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.Mods;

/// <summary>
/// What one <see cref="ModLoader"/> did while it loaded and ran its mods. The observer fills it
/// through Harmony hooks, and <see cref="ModLoadReport"/> turns it into the public report.
/// </summary>
internal sealed class ModLoadTrace
{
    public EnumAppSide Side { get; set; } = EnumAppSide.Universal;
    public List<ModContainer> Containers { get; } = [];
    public List<ModSystem> InstantiatedSystems { get; } = [];
    public List<string> MissingDependencies { get; } = [];
    public List<SystemRunTrace> RunLog { get; } = [];
    public List<PhaseRunTrace> Phases { get; } = [];
    public List<PatchAttempt> Patches { get; } = [];
    public List<CompatibilityCopy> Compatibility { get; } = [];
    public List<string> Notes { get; } = [];
    public int NextSequence { get; set; }
}

internal sealed record SystemRunTrace(
    int Sequence,
    ModSystem System,
    ModRunPhase Phase,
    double ElapsedMs,
    bool Succeeded);

internal sealed record PhaseRunTrace(ModRunPhase Phase, double ElapsedMs);

internal sealed record PatchAttempt(
    string Source,
    int Index,
    string Op,
    string? Target,
    string? Path,
    string? Side,
    PatchOutcome Outcome,
    string? Detail);

internal sealed record CompatibilityCopy(
    string Path,
    string TargetMod,
    string ProviderMod,
    bool Replaced);

internal readonly record struct PatchCounters(int Applied, int NotFound, int Errors);

internal readonly record struct PatchMeta(
    string Op,
    string? Target,
    string? Path,
    string? Side);

/// <summary>
/// Installs the mod loading hooks once per process. They are no-ops until a loader has a trace, so
/// a client, a server, and a restarted host can share them. Call <see cref="Install"/> before the
/// first <c>ModLoader.LoadMods</c>.
/// </summary>
internal static class ModLoadObserver
{
    private static readonly object s_lock = new();
    private static readonly Harmony s_harmony = new("zaldaryon.pharos.mod-load");
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ModLoader, ModLoadTrace> s_traces = new();
    private static bool s_installed;
    private static bool s_extended;
    private static FieldInfo? s_jsonPatchFile;
    private static FieldInfo? s_jsonPatchPath;
    private static FieldInfo? s_jsonPatchOp;
    private static FieldInfo? s_jsonPatchSide;

    [ThreadStatic]
    private static ModLoadTrace? t_trace;

    /// <summary>Patches the mod loader, once. Safe to call from every boot.</summary>
    public static void Install()
    {
        lock (s_lock)
        {
            if (s_installed) return;
            s_installed = true;

            MethodInfo? loadMods = AccessTools.Method(
                typeof(ModLoader),
                nameof(ModLoader.LoadMods),
                [typeof(List<ModContainer>), typeof(IEnumerable<string>)]);
            MethodInfo? loadModInfos = AccessTools.Method(typeof(ModLoader), nameof(ModLoader.LoadModInfos));
            MethodInfo? tryRun = AccessTools.Method(typeof(ModLoader), "TryRunModPhase");
            MethodInfo? runPhase = AccessTools.Method(
                typeof(ModLoader),
                nameof(ModLoader.RunModPhase),
                [typeof(List<ModSystem>).MakeByRefType(), typeof(ModRunPhase)]);

            if (loadMods == null || loadModInfos == null || tryRun == null || runPhase == null)
            {
                throw new MissingMethodException(
                    "This Vintage Story build does not expose ModLoader.LoadMods, TryRunModPhase and RunModPhase.");
            }

            s_harmony.Patch(
                loadModInfos,
                postfix: new HarmonyMethod(typeof(ModLoadObserver), nameof(AfterLoadModInfos)));
            s_harmony.Patch(loadMods, postfix: new HarmonyMethod(typeof(ModLoadObserver), nameof(AfterLoadMods)));
            s_harmony.Patch(
                tryRun,
                prefix: new HarmonyMethod(typeof(ModLoadObserver), nameof(BeforeTryRun)),
                postfix: new HarmonyMethod(typeof(ModLoadObserver), nameof(AfterTryRun)));
            s_harmony.Patch(
                runPhase,
                prefix: new HarmonyMethod(typeof(ModLoadObserver), nameof(BeforeRunPhase)),
                postfix: new HarmonyMethod(typeof(ModLoadObserver), nameof(AfterRunPhase)));
        }
    }

    /// <summary>The trace for a loaded mod loader, or null when the loader never loaded mods.</summary>
    public static ModLoadTrace? TraceOf(IModLoader? loader) =>
        loader is ModLoader concrete && s_traces.TryGetValue(concrete, out ModLoadTrace? trace) ? trace : null;

    private static ModLoadTrace TraceFor(ModLoader loader)
    {
        if (!s_traces.TryGetValue(loader, out ModLoadTrace? trace))
        {
            trace = new ModLoadTrace();
            s_traces.Add(loader, trace);
        }

        return trace;
    }

    private static void AfterLoadModInfos(ModLoader __instance, List<ModContainer> __result)
    {
        ModLoadTrace trace = TraceFor(__instance);
        trace.Containers.Clear();
        trace.Containers.AddRange(__result);
    }

    private static void AfterLoadMods(ModLoader __instance, List<ModContainer> mods)
    {
        ModLoadTrace trace = TraceFor(__instance);
        trace.Side = ReadSide(__instance);
        foreach (ModContainer mod in mods)
        {
            if (!trace.Containers.Any(existing => ReferenceEquals(existing, mod)))
            {
                trace.Containers.Add(mod);
            }
        }

        trace.InstantiatedSystems.Clear();
        trace.InstantiatedSystems.AddRange(__instance.Systems);
        trace.MissingDependencies.Clear();
        trace.MissingDependencies.AddRange(__instance.MissingDependencies);
        InstallExtendedHooks(trace);
    }

    private static EnumAppSide ReadSide(ModLoader loader)
    {
        FieldInfo? side = AccessTools.Field(typeof(ModLoader), "side");
        if (side?.GetValue(loader) is EnumAppSide sideValue) return sideValue;
        FieldInfo? api = AccessTools.Field(typeof(ModLoader), "api");
        if (api?.GetValue(loader) is ICoreAPI core) return core.Side;
        return EnumAppSide.Universal;
    }

    private static void InstallExtendedHooks(ModLoadTrace trace)
    {
        if (s_extended) return;
        s_extended = true;

        Type? patchLoader = AccessTools.TypeByName("Vintagestory.ServerMods.NoObf.ModJsonPatchLoader");
        MethodInfo? applyPatch = patchLoader == null ? null : AccessTools.Method(patchLoader, "ApplyPatch");
        MethodInfo? processPatch = patchLoader == null ? null : AccessTools.Method(patchLoader, "ProcessSinglePatch");
        Type? patchType = AccessTools.TypeByName("Vintagestory.ServerMods.NoObf.JsonPatch");
        if (applyPatch == null || patchType == null)
        {
            trace.Notes.Add("JSON patch inspection is unavailable: the game's ModJsonPatchLoader was not loaded.");
        }
        else
        {
            s_jsonPatchFile = AccessTools.Field(patchType, "File");
            s_jsonPatchPath = AccessTools.Field(patchType, "Path");
            s_jsonPatchOp = AccessTools.Field(patchType, "Op");
            s_jsonPatchSide = AccessTools.Field(patchType, "Side");
            s_harmony.Patch(
                applyPatch,
                prefix: new HarmonyMethod(typeof(ModLoadObserver), nameof(BeforeApplyPatch)),
                postfix: new HarmonyMethod(typeof(ModLoadObserver), nameof(AfterApplyPatch)));
            if (processPatch != null)
            {
                s_harmony.Patch(
                    processPatch,
                    prefix: new HarmonyMethod(typeof(ModLoadObserver), nameof(BeforeProcessPatch)),
                    postfix: new HarmonyMethod(typeof(ModLoadObserver), nameof(AfterProcessPatch)));
            }
        }

        Type? compatibility = AccessTools.TypeByName("Vintagestory.ServerMods.ModCompatiblityUtil");
        MethodInfo? compatibilityAssets = compatibility == null ? null : AccessTools.Method(compatibility, "AssetsLoaded");
        if (compatibilityAssets == null)
        {
            trace.Notes.Add("Compatibility asset inspection is unavailable: ModCompatiblityUtil was not loaded.");
        }
        else
        {
            s_harmony.Patch(
                compatibilityAssets,
                prefix: new HarmonyMethod(typeof(ModLoadObserver), nameof(BeforeCompatibilityAssets)));
        }
    }

    private static void BeforeRunPhase(ModLoader __instance, ModRunPhase __1, out RunPhaseState __state)
    {
        ModLoadTrace trace = TraceFor(__instance);
        __state = new RunPhaseState(t_trace, trace, Stopwatch.GetTimestamp(), __1);
        t_trace = trace;
    }

    private static void AfterRunPhase(RunPhaseState __state)
    {
        ModLoadTrace trace = __state.Trace;
        double elapsed = (Stopwatch.GetTimestamp() - __state.Started) * 1000.0 / Stopwatch.Frequency;
        trace.Phases.Add(new PhaseRunTrace(__state.Phase, elapsed));
        t_trace = __state.Previous;
    }

    private static void BeforeTryRun(ModSystem __1, ModRunPhase __3, out long __state) =>
        __state = Stopwatch.GetTimestamp();

    private static void AfterTryRun(ModSystem __1, ModRunPhase __3, bool __result, long __state)
    {
        ModLoadTrace? trace = t_trace;
        if (trace == null) return;
        double elapsed = (Stopwatch.GetTimestamp() - __state) * 1000.0 / Stopwatch.Frequency;
        trace.RunLog.Add(new SystemRunTrace(trace.NextSequence++, __1, __3, elapsed, __result));
    }

    private static void BeforeApplyPatch(
        int __0,
        AssetLocation __1,
        object? __2,
        ref int __3,
        ref int __4,
        ref int __5,
        out ApplyPatchState __state)
    {
        __state = new ApplyPatchState(
            new PatchCounters(__3, __4, __5),
            ReadPatchMeta(__2),
            __0,
            __1?.ToString() ?? "?",
            null,
            0);
    }

    private static void AfterApplyPatch(
        ref int __3,
        ref int __4,
        ref int __5,
        ApplyPatchState __state)
    {
        RecordPatchOutcome(__3, __4, __5, __state);
    }

    private static void BeforeProcessPatch(
        AssetLocation __0,
        object? __1,
        AssetLocation __2,
        int __3,
        ref int __4,
        ref int __5,
        ref int __6,
        out ApplyPatchState __state)
    {
        __state = new ApplyPatchState(
            new PatchCounters(__4, __5, __6),
            ReadPatchMeta(__1),
            __3,
            __2?.ToString() ?? "?",
            __0?.ToString(),
            t_trace?.Patches.Count ?? 0);
    }

    private static void AfterProcessPatch(
        ref int __4,
        ref int __5,
        ref int __6,
        ApplyPatchState __state)
    {
        ModLoadTrace? trace = t_trace;
        if (trace != null && trace.Patches.Count > __state.AttemptCount)
        {
            // A wildcard ProcessSinglePatch delegates to ApplyPatch once per target. Those
            // attempts are the per-target results; the outer call must not add a wildcard result.
            return;
        }

        RecordPatchOutcome(__4, __5, __6, __state);
    }

    private static void RecordPatchOutcome(int appliedNow, int notFoundNow, int errorsNow, ApplyPatchState state)
    {
        ModLoadTrace? trace = t_trace;
        if (trace == null) return;

        int applied = appliedNow - state.Before.Applied;
        int notFound = notFoundNow - state.Before.NotFound;
        int errors = errorsNow - state.Before.Errors;
        PatchOutcome? outcome = applied > 0 ? PatchOutcome.Applied
            : notFound > 0 ? PatchOutcome.NotFound
            : errors > 0 ? PatchOutcome.Error
            : null;
        if (outcome == null) return;

        PatchMeta meta = state.Meta;
        trace.Patches.Add(new PatchAttempt(
            state.Source,
            state.Index,
            meta.Op,
            state.Target ?? meta.Target,
            meta.Path,
            meta.Side,
            outcome.Value,
            null));
    }

    private static PatchMeta ReadPatchMeta(object? patch)
    {
        if (patch == null) return new PatchMeta("?", null, null, null);
        string op = s_jsonPatchOp?.GetValue(patch)?.ToString() ?? "?";
        string? target = (s_jsonPatchFile?.GetValue(patch) as AssetLocation)?.ToString();
        string? path = s_jsonPatchPath?.GetValue(patch) as string;
        object? sideValue = s_jsonPatchSide?.GetValue(patch);
        string? side = sideValue?.ToString();
        return new PatchMeta(op, target, path, side);
    }

    private static void BeforeCompatibilityAssets(ICoreAPI api)
    {
        ModLoadTrace? trace = t_trace;
        if (trace == null) return;

        foreach (Mod mod in api.ModLoader.Mods)
        {
            string prefix = "compatibility/" + mod.Info.ModID + "/";
            foreach (IAsset asset in api.Assets.GetManyInCategory("compatibility", mod.Info.ModID + "/", null, true))
            {
                string path = asset.Location.Path;
                if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                string targetPath = path[prefix.Length..];
                AssetLocation target = new(mod.Info.ModID, targetPath);
                trace.Compatibility.Add(new CompatibilityCopy(
                    target.ToString(),
                    mod.Info.ModID,
                    asset.Location.Domain,
                    api.Assets.AllAssets.ContainsKey(target)));
            }
        }
    }

    private readonly record struct RunPhaseState(
        ModLoadTrace? Previous,
        ModLoadTrace Trace,
        long Started,
        ModRunPhase Phase);

    private readonly record struct ApplyPatchState(
        PatchCounters Before,
        PatchMeta Meta,
        int Index,
        string Source,
        string? Target,
        int AttemptCount);
}
