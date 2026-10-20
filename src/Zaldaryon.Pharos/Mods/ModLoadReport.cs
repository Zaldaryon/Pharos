using System.Globalization;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.Common;
using Zaldaryon.Pharos.Metrics;

namespace Zaldaryon.Pharos.Mods;

/// <summary>How the game loaded its mods: their states, systems, patches and compatibility assets.</summary>
/// <remarks>
/// Captured once, on the game's thread, after the game has loaded its mods. A test can then ask
/// the profile questions on its own thread without racing the game. See
/// <c>docs/mod-loading.md</c>.
/// </remarks>
public sealed class ModLoadReport : IMetricSource
{
    private readonly IReadOnlyDictionary<string, LoadedMod> _byId;

    private ModLoadReport(
        EnumAppSide side,
        IReadOnlyList<LoadedMod> loaded,
        IReadOnlyList<string> missingDependencies,
        IReadOnlyList<LoadedSystem> systems,
        IReadOnlyList<SystemRun> runLog,
        IReadOnlyList<RunPhase> phases,
        IReadOnlyList<PatchResult> patches,
        IReadOnlyList<CompatibilityAsset> compatibilityAssets,
        IReadOnlyList<string> notes)
    {
        Side = side;
        MissingDependencies = missingDependencies;
        Systems = systems;
        RunLog = runLog;
        Phases = phases;
        Patches = patches;
        CompatibilityAssets = compatibilityAssets;
        Notes = notes;
        _byId = loaded.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The side whose loader this is.</summary>
    public EnumAppSide Side { get; }

    /// <summary>Every mod the loader saw, by ID, including disabled and errored ones.</summary>
    public IReadOnlyDictionary<string, LoadedMod> Loaded => _byId;

    /// <summary>The dependency IDs the loader could not find.</summary>
    public IReadOnlyList<string> MissingDependencies { get; }

    /// <summary>Every mod system the loader instantiated, in execution order.</summary>
    public IReadOnlyList<LoadedSystem> Systems { get; }

    /// <summary>Every <c>TryRunModPhase</c> call, in the order the loader made them.</summary>
    public IReadOnlyList<SystemRun> RunLog { get; }

    /// <summary>The total time each run phase took, in the order the phases ran.</summary>
    public IReadOnlyList<RunPhase> Phases { get; }

    /// <summary>One result per patch target, plus the patches the loader skipped before applying them.</summary>
    public IReadOnlyList<PatchResult> Patches { get; }

    /// <summary>The compatibility assets the loader copied into another mod's domain.</summary>
    public IReadOnlyList<CompatibilityAsset> CompatibilityAssets { get; }

    /// <summary>What could not be observed, such as a game build without a patch loader.</summary>
    public IReadOnlyList<string> Notes { get; }

    /// <summary>One loaded mod, or null when no mod with <paramref name="modId"/> was loaded.</summary>
    public LoadedMod? Mod(string modId) =>
        _byId.TryGetValue(modId, out LoadedMod? mod) ? mod : null;

    /// <summary>The patches whose source asset belongs to <paramref name="modId"/>.</summary>
    public IReadOnlyList<PatchResult> PatchesOf(string modId) =>
        Patches.Where(patch => string.Equals(patch.SourceMod, modId, StringComparison.OrdinalIgnoreCase)).ToList();

    /// <summary>An empty report for a host or client that has not loaded mods yet.</summary>
    public static ModLoadReport Empty(EnumAppSide side) => new(
        side, [], [], [], [], [], [], [], []);

    /// <summary>Reads the live loader and the observer's trace. Run on the game's thread.</summary>
    internal static ModLoadReport Capture(ICoreAPI? api)
    {
        if (api?.ModLoader == null) return Empty(EnumAppSide.Universal);

        ModLoadTrace? trace = ModLoadObserver.TraceOf(api.ModLoader);
        List<ModContainer> containers = ReadContainers(api, trace);
        List<LoadedMod> loaded = BuildMods(api, containers);
        List<string> missing = trace?.MissingDependencies.ToList() ?? ReadMissingDependencies(api);
        List<LoadedSystem> systems = BuildSystems(api, trace);
        List<SystemRun> runLog = trace?.RunLog
            .OrderBy(run => run.Sequence)
            .Select(run => new SystemRun(
                run.Sequence,
                run.System.Mod?.Info?.ModID ?? "(none)",
                run.System.GetType(),
                run.Phase,
                run.ElapsedMs,
                run.Succeeded))
            .ToList() ?? [];
        List<RunPhase> phases = trace?.Phases.Select(phase => new RunPhase(phase.Phase, phase.ElapsedMs)).ToList() ?? [];
        List<PatchResult> patches = BuildPatches(api, trace, loaded);
        List<CompatibilityAsset> compatibility = trace?.Compatibility
            .Select(copy => new CompatibilityAsset(copy.Path, copy.TargetMod, copy.ProviderMod, copy.Replaced))
            .DistinctBy(copy => (copy.Path, copy.TargetMod, copy.ProviderMod))
            .OrderBy(copy => copy.TargetMod, StringComparer.OrdinalIgnoreCase)
            .ThenBy(copy => copy.Path, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? [];
        List<string> notes = trace?.Notes.ToList() ?? [];

        return new ModLoadReport(api.Side, loaded, missing, systems, runLog, phases, patches, compatibility, notes);
    }

    private static List<ModContainer> ReadContainers(ICoreAPI api, ModLoadTrace? trace)
    {
        List<ModContainer> containers = trace?.Containers.ToList() ?? [];
        foreach (Mod mod in api.ModLoader.Mods)
        {
            if (mod is ModContainer container && !containers.Any(existing => ReferenceEquals(existing, container)))
            {
                containers.Add(container);
            }
        }

        return containers;
    }

    private static List<LoadedMod> BuildMods(ICoreAPI api, List<ModContainer> containers)
    {
        List<ModContainer> enabledOrder = api.ModLoader.Mods.OfType<ModContainer>().ToList();
        List<ModContainer> ordered = [];
        ordered.AddRange(enabledOrder);
        ordered.AddRange(containers.Where(container => !enabledOrder.Any(enabled => ReferenceEquals(enabled, container))));

        List<LoadedMod> loaded = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (ModContainer container in ordered)
        {
            string id = container.Info.ModID;
            if (!seen.Add(id)) continue;
            loaded.Add(new LoadedMod(
                id,
                container.Info.Name ?? id,
                container.Info.Version,
                container.Info.NetworkVersion,
                container.Info.Side,
                container.Info.Type,
                container.Status,
                container.Error,
                (container.MissingDependencies ?? []).ToList(),
                (container.Info.Dependencies ?? [])
                    .Select(dependency => new ModDependencyInfo(dependency.ModID, dependency.Version))
                    .ToList(),
                enabledOrder.FindIndex(mod => ReferenceEquals(mod, container))));
        }

        return loaded;
    }

    private static List<string> ReadMissingDependencies(ICoreAPI api) =>
        api.ModLoader is ModLoader loader ? loader.MissingDependencies.ToList() : [];

    private static List<LoadedSystem> BuildSystems(ICoreAPI api, ModLoadTrace? trace)
    {
        Dictionary<ModSystem, List<SystemRunTrace>> runs = [];
        if (trace != null)
        {
            foreach (SystemRunTrace run in trace.RunLog)
            {
                if (!runs.TryGetValue(run.System, out List<SystemRunTrace>? list))
                {
                    list = [];
                    runs[run.System] = list;
                }

                list.Add(run);
            }
        }

        List<LoadedSystem> systems = [];
        IEnumerable<ModSystem> instantiated = trace?.InstantiatedSystems.Count > 0
            ? trace.InstantiatedSystems
            : api.ModLoader.Systems;
        foreach (ModSystem system in instantiated)
        {
            string modId = system.Mod?.Info?.ModID ?? "(none)";
            double executeOrder;
            try
            {
                executeOrder = system.ExecuteOrder();
            }
            catch (Exception)
            {
                executeOrder = double.NaN;
            }

            EnumAppSide loadedOn = 0;
            if (ShouldLoad(system, EnumAppSide.Server)) loadedOn |= EnumAppSide.Server;
            if (ShouldLoad(system, EnumAppSide.Client)) loadedOn |= EnumAppSide.Client;
            if (loadedOn == 0) loadedOn = EnumAppSide.Universal;

            runs.TryGetValue(system, out List<SystemRunTrace>? systemRuns);
            Dictionary<ModRunPhase, double> phaseMs = [];
            foreach (IGrouping<ModRunPhase, SystemRunTrace> phase in (systemRuns ?? []).GroupBy(run => run.Phase))
            {
                phaseMs[phase.Key] = phase.Sum(run => run.ElapsedMs);
            }

            SystemRunTrace? firstNormal = systemRuns?.FirstOrDefault(run => run.Phase == ModRunPhase.Normal);
            systems.Add(new LoadedSystem(
                modId,
                system.GetType(),
                executeOrder,
                loadedOn,
                systemRuns?.Count > 0,
                systemRuns?.Count ?? 0,
                firstNormal?.Sequence ?? -1,
                phaseMs));
        }

        return systems
            .OrderBy(system => system.Order < 0 ? int.MaxValue : system.Order)
            .ThenBy(system => system.ExecuteOrder)
            .ThenBy(system => system.ModId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(system => system.Type.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool ShouldLoad(ModSystem system, EnumAppSide side)
    {
        try
        {
            return system.ShouldLoad(side);
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static List<PatchResult> BuildPatches(ICoreAPI api, ModLoadTrace? trace, List<LoadedMod> loaded)
    {
        List<PatchResult> results = [];
        HashSet<(string Source, int Index)> observed = [];
        Dictionary<(string Source, int Index), List<PatchAttempt>> attempts = [];
        if (trace != null)
        {
            foreach (PatchAttempt attempt in trace.Patches)
            {
                attempts.TryGetValue((attempt.Source, attempt.Index), out List<PatchAttempt>? list);
                if (list == null)
                {
                    list = [];
                    attempts[(attempt.Source, attempt.Index)] = list;
                }

                list.Add(attempt);
            }
        }

        HashSet<string> enabledMods = loaded
            .Where(mod => mod.Status == ModStatus.Enabled)
            .Select(mod => mod.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<IAsset> assets;
        try
        {
            assets = api.Assets.GetMany("patches/", null, true);
        }
        catch (Exception exception)
        {
            results.Add(new PatchResult(
                "(patches)", "(unknown)", -1, "?", null, null, null, true,
                PatchOutcome.Error, "The patches could not be read: " + exception.Message));
            assets = [];
        }

        foreach (IAsset asset in assets)
        {
            string source = asset.Location.ToString();
            string sourceMod = asset.Location.Domain;
            JArray patches;
            try
            {
                patches = JArray.Parse(asset.ToText());
            }
            catch (Exception exception)
            {
                results.Add(new PatchResult(
                    source, sourceMod, -1, "parse", null, null, null, true,
                    PatchOutcome.Error, "The patch file is not a JSON array: " + exception.Message));
                continue;
            }

            for (int index = 0; index < patches.Count; index++)
            {
                JToken patch = patches[index];
                string op = patch["op"]?.ToString() ?? "?";
                string? target = patch["file"]?.Value<string>();
                string? path = patch["path"]?.Value<string>();
                string side = patch["side"]?.ToString() ?? "Universal";
                bool enabled = patch["enabled"]?.Value<bool>() ?? true;

                PatchOutcome? skipped = SkippedOutcome(api, patch, side, enabled, api.Side, enabledMods, out string? detail);
                if (skipped != null)
                {
                    results.Add(new PatchResult(
                        source, sourceMod, index, op, target, path, side, enabled, skipped.Value, detail));
                    observed.Add((source, index));
                    continue;
                }

                if (attempts.TryGetValue((source, index), out List<PatchAttempt>? patchAttempts))
                {
                    foreach (PatchAttempt attempt in patchAttempts)
                    {
                        results.Add(new PatchResult(
                            source,
                            sourceMod,
                            index,
                            attempt.Op,
                            attempt.Target ?? target,
                            attempt.Path ?? path,
                            attempt.Side ?? side,
                            enabled,
                            attempt.Outcome,
                            attempt.Detail));
                    }

                    observed.Add((source, index));
                }
                else
                {
                    results.Add(new PatchResult(
                        source, sourceMod, index, op, target, path, side, enabled,
                        PatchOutcome.Error, "The loader recorded no result for this patch."));
                    observed.Add((source, index));
                }
            }
        }

        foreach (((string source, int index), List<PatchAttempt> patchAttempts) in attempts)
        {
            if (observed.Contains((source, index))) continue;
            foreach (PatchAttempt attempt in patchAttempts)
            {
                results.Add(new PatchResult(
                    source,
                    DomainOf(source),
                    index,
                    attempt.Op,
                    attempt.Target,
                    attempt.Path,
                    attempt.Side,
                    true,
                    attempt.Outcome,
                    attempt.Detail));
            }
        }

        return results
            .OrderBy(result => result.SourceMod, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.Index)
            .ThenBy(result => result.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static PatchOutcome? SkippedOutcome(
        ICoreAPI api,
        JToken patch,
        string side,
        bool enabled,
        EnumAppSide actualSide,
        HashSet<string> enabledMods,
        out string? detail)
    {
        detail = null;
        if (!enabled)
        {
            detail = "The patch is disabled.";
            return PatchOutcome.Disabled;
        }

        if (!string.Equals(side, "Universal", StringComparison.OrdinalIgnoreCase)
            && side != "3"
            && !string.Equals(side, actualSide.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            detail = $"The patch is for the {side} side.";
            return PatchOutcome.Disabled;
        }

        if (patch["condition"] is JObject condition)
        {
            string? when = condition["when"]?.Value<string>();
            if (string.IsNullOrEmpty(when) || !ConditionMet(api, condition, when))
            {
                detail = $"The condition on '{when}' is not met.";
                return PatchOutcome.ConditionUnmet;
            }
        }

        if (patch["dependsOn"] is JArray dependencies)
        {
            foreach (JToken dependency in dependencies)
            {
                string? modId = dependency["modid"]?.Value<string>();
                if (string.IsNullOrEmpty(modId)) continue;
                bool invert = dependency["invert"]?.Value<bool>() ?? false;
                bool present = enabledMods.Contains(modId);
                if (present == invert)
                {
                    detail = invert
                        ? $"The patch requires that mod '{modId}' is absent."
                        : $"The patch requires mod '{modId}'.";
                    return PatchOutcome.DependencyMissing;
                }
            }
        }

        return null;
    }

    private static bool ConditionMet(ICoreAPI api, JObject condition, string when)
    {
        IAttribute? value = api.World?.Config?[when];
        if (value == null) return false;
        if (condition["useValue"]?.Value<bool>() == true) return true;
        string expected = condition["isValue"]?.ToString() ?? "";
        string actual = value.GetValue()?.ToString() ?? "";
        return string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
    }

    private static string DomainOf(string source)
    {
        int separator = source.IndexOf(':');
        return separator > 0 ? source[..separator] : source;
    }

    /// <inheritdoc />
    public IReadOnlyList<MetricReading> Metrics()
    {
        List<MetricReading> readings = [];
        void Count(string name, double value) =>
            readings.Add(new MetricReading(name, value, MetricUnit.Count, MetricDirection.Neutral, MetricClock.Exact));

        Count("mods.loaded", Loaded.Count);
        Count("mods.enabled", Loaded.Values.Count(mod => mod.Status == ModStatus.Enabled));
        Count("mods.disabled", Loaded.Values.Count(mod => mod.Status == ModStatus.Disabled));
        Count("mods.errored", Loaded.Values.Count(mod => mod.Error != null || mod.Status == ModStatus.Errored));
        Count("mods.missingdependencies", MissingDependencies.Count);
        Count("mods.systems", Systems.Count);
        Count("mods.runlog", RunLog.Count);
        Count("mods.compatibility.added", CompatibilityAssets.Count(copy => !copy.Replaced));
        Count("mods.compatibility.replaced", CompatibilityAssets.Count(copy => copy.Replaced));

        foreach (IGrouping<PatchOutcome, PatchResult> group in Patches.GroupBy(patch => patch.Outcome))
        {
            Count("mods.patches." + group.Key.ToString().ToLowerInvariant(), group.Count());
        }

        foreach (IGrouping<string, PatchResult> group in Patches.GroupBy(patch => patch.SourceMod, StringComparer.OrdinalIgnoreCase))
        {
            string prefix = "mods.patches." + MetricName(group.Key) + ".";
            Count(prefix + "applied", group.Count(patch => patch.Outcome == PatchOutcome.Applied));
            Count(prefix + "notfound", group.Count(patch => patch.Outcome == PatchOutcome.NotFound));
            Count(prefix + "errors", group.Count(patch => patch.Outcome == PatchOutcome.Error));
        }

        foreach (RunPhase phase in Phases)
        {
            readings.Add(new MetricReading(
                "mods.phase." + phase.Phase.ToString().ToLowerInvariant() + ".ms",
                phase.ElapsedMs,
                MetricUnit.Milliseconds,
                MetricDirection.LowerIsBetter,
                MetricClock.Stopwatch));
        }

        return readings;
    }

    private static string MetricName(string value)
    {
        System.Text.StringBuilder builder = new(value.Length);
        foreach (char character in value.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : '.');
        }

        return builder.ToString();
    }
}

/// <summary>One mod the loader saw, enabled or not.</summary>
public sealed record LoadedMod(
    string Id,
    string Name,
    string Version,
    string NetworkVersion,
    EnumAppSide Side,
    EnumModType Type,
    ModStatus Status,
    ModError? Error,
    IReadOnlyList<string> MissingDependencies,
    IReadOnlyList<ModDependencyInfo> Dependencies,
    int LoadOrder)
{
    /// <summary>Whether the loader enabled this mod.</summary>
    public bool Enabled => Status == ModStatus.Enabled;

    /// <inheritdoc />
    public override string ToString() => $"{Id} {Version} ({Status}{(Error == null ? "" : ", " + Error)})";
}

/// <summary>One dependency a mod declares.</summary>
public sealed record ModDependencyInfo(string ModId, string Version);

/// <summary>One mod system, its order, its sides and its phase times.</summary>
/// <param name="Order">The position of its first <see cref="ModRunPhase.Normal"/> run, or -1 when it never ran.</param>
public sealed record LoadedSystem(
    string ModId,
    Type Type,
    double ExecuteOrder,
    EnumAppSide LoadedOn,
    bool Ran,
    int RunCount,
    int Order,
    IReadOnlyDictionary<ModRunPhase, double> PhaseMs)
{
    /// <summary>The selector <c>mod:TypeName</c> used by <c>PharosAssert.LoadsBefore</c>.</summary>
    public string Key => $"{ModId}:{Type.Name}";

    /// <summary>Whether the system can load on <paramref name="side"/> according to its side flags.</summary>
    public bool LoadsOn(EnumAppSide side) => (LoadedOn & side) == side;

    /// <inheritdoc />
    public override string ToString() =>
        $"{Key} ({ExecuteOrder.ToString(CultureInfo.InvariantCulture)}, order {Order})";
}

/// <summary>One system run in one phase, in the order the loader made the calls.</summary>
public sealed record SystemRun(
    int Order,
    string ModId,
    Type Type,
    ModRunPhase Phase,
    double ElapsedMs,
    bool Succeeded)
{
    /// <summary>The selector <c>mod:TypeName</c> used by <c>PharosAssert.LoadsBefore</c>.</summary>
    public string Key => $"{ModId}:{Type.Name}";

    /// <inheritdoc />
    public override string ToString() => $"{Order} {Key} {Phase} {ElapsedMs:F3} ms";
}

/// <summary>The time one run phase took.</summary>
public sealed record RunPhase(ModRunPhase Phase, double ElapsedMs);

/// <summary>What happened to one patch at one target.</summary>
public sealed record PatchResult(
    string Source,
    string SourceMod,
    int Index,
    string Op,
    string? Target,
    string? Path,
    string? Side,
    bool Enabled,
    PatchOutcome Outcome,
    string? Detail = null)
{
    /// <inheritdoc />
    public override string ToString() =>
        $"{Source}[{Index}] {Op} {Target}{Path} -> {Outcome}{(Detail == null ? "" : ": " + Detail)}";
}

/// <summary>The outcome of one patch at one target.</summary>
public enum PatchOutcome
{
    /// <summary>The loader changed the target asset.</summary>
    Applied,
    /// <summary>The target asset does not exist.</summary>
    NotFound,
    /// <summary>The patch's condition on the world config is not met.</summary>
    ConditionUnmet,
    /// <summary>The patch's <c>dependsOn</c> requirement is not met.</summary>
    DependencyMissing,
    /// <summary>The loader reported an error while applying the patch.</summary>
    Error,
    /// <summary>The patch is disabled, or is for another side.</summary>
    Disabled,
}

/// <summary>One compatibility asset copied into a mod's domain.</summary>
public sealed record CompatibilityAsset(string Path, string TargetMod, string ProviderMod, bool Replaced);
