using System.Collections;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Sets an embedded server's weather: the cloud pattern and wind of its regions, the
/// precipitation, and whether the weather changes on its own.
/// </summary>
/// <remarks>
/// <para>
/// The game simulates weather per map region, in the essentials mod every world loads. Weather at
/// a position blends the four regions around it, so the setters that take a position set all four
/// of those. Without a position they set every loaded region; regions that load later start with
/// weather of their own.
/// </para>
/// <para>
/// Precipitation is world-wide: it follows noise over the calendar unless
/// <see cref="SetPrecipitation"/> overrides it. <see cref="StopChanges"/> stops patterns, winds and
/// weather events from changing, but not precipitation; freeze the calendar or override
/// precipitation to hold it too. Changes are sent to connected clients, which show cloud patterns
/// and wind as they render; a headless client applies the precipitation override at once.
/// Everything runs on the server's game thread.
/// </para>
/// </remarks>
public sealed class WeatherDriver
{
    private const string SystemType = "Vintagestory.GameContent.WeatherSystemServer";

    private readonly EmbeddedServerHost _host;

    internal WeatherDriver(EmbeddedServerHost host)
    {
        _host = host;
    }

    /// <summary>Whether the world has a weather system Pharos can drive.</summary>
    public bool IsAvailable => _host.RunOnGameThread(() => FindSystem() is { } system && Member.Get(system, "dummySim") != null);

    /// <summary>The codes of the cloud patterns, such as <c>clearsky</c> or <c>overcast</c>.</summary>
    public IReadOnlyList<string> Patterns => _host.RunOnGameThread(() => Codes(System(), "WeatherConfigs"));

    /// <summary>The codes of the wind patterns, such as <c>still</c> or <c>storm</c>.</summary>
    public IReadOnlyList<string> Winds => _host.RunOnGameThread(() => Codes(System(), "WindConfigs"));

    /// <summary>The precipitation override, from -1 to 1, or null when precipitation follows the calendar.</summary>
    public float? PrecipitationOverride => _host.RunOnGameThread(() => (float?)Member.Get(System(), "OverridePrecipitation"));

    /// <summary>Whether patterns, winds and weather events change on their own.</summary>
    public bool ChangesStopped => _host.RunOnGameThread(() => !(bool)Member.Get(System(), "autoChangePatterns")!);

    /// <summary>The cloud pattern of the region <paramref name="pos"/> is in.</summary>
    public string PatternAt(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return _host.RunOnGameThread(() =>
        {
            object region = RegionContaining(System(), pos) ?? throw new InvalidOperationException($"No map region is loaded at {pos}.");
            return PatternCode(Member.Get(region, "NewWePattern"));
        });
    }

    /// <summary>The wind speed the game reads at <paramref name="pos"/>, from 0 (still) up.</summary>
    public double WindSpeedAt(Vec3d pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return _host.RunOnGameThread(() => _host.Server.BlockAccessor.GetWindSpeedAt(pos).X);
    }

    /// <summary>
    /// Sets the cloud pattern of the four regions weather at <paramref name="at"/> blends, or of
    /// every loaded region, at once.
    /// </summary>
    /// <exception cref="ArgumentException">No pattern has that code.</exception>
    public void SetPattern(string code, BlockPos? at = null) => _host.RunOnGameThread(() =>
    {
        object system = System();
        RequireCode(system, "WeatherConfigs", code, nameof(code));
        foreach (object region in Regions(system, at))
        {
            Member.Call(region, "SetWeatherPattern", code, true);
        }
    });

    /// <summary>
    /// Sets the wind pattern of the four regions weather at <paramref name="at"/> blends, or of
    /// every loaded region, at once.
    /// </summary>
    /// <exception cref="ArgumentException">No wind pattern has that code.</exception>
    public void SetWind(string code, BlockPos? at = null) => _host.RunOnGameThread(() =>
    {
        object system = System();
        RequireCode(system, "WindConfigs", code, nameof(code));
        foreach (object region in Regions(system, at))
        {
            Member.Call(region, "SetWindPattern", code, true);
            SettleWind(region);
        }
    });

    /// <summary>
    /// Overrides precipitation everywhere, from -1 (dry) to 1 (pouring), or hands it back to the
    /// calendar with null, as <c>/weather setprecip</c> does.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The level is outside -1 to 1.</exception>
    public void SetPrecipitation(float? level)
    {
        if (level is { } value && !(value >= -1 && value <= 1)) throw new ArgumentOutOfRangeException(nameof(level), level, "Precipitation goes from -1 to 1.");

        _host.RunOnGameThread(() =>
        {
            object system = System();
            Member.Set(system, "OverridePrecipitation", level);
            Member.Call(system, "broadCastConfigUpdate");
        });
    }

    /// <summary>
    /// Stops patterns, winds and weather events from changing on their own, as
    /// <c>/weather acp</c> does, and finishes any change under way.
    /// </summary>
    public void StopChanges() => _host.RunOnGameThread(() =>
    {
        object system = System();
        Member.Set(system, "autoChangePatterns", false);
        foreach (object region in Regions(system, null))
        {
            if (!(bool)Member.Get(region, "Transitioning")!) continue;
            Member.Set(region, "Weight", 1f);
            Member.Set(region, "Transitioning", false);
            Member.Call(region, "sendWeatherUpdatePacket");
        }
    });

    /// <summary>Lets the weather change on its own again.</summary>
    public void ResumeChanges() => _host.RunOnGameThread(() => Member.Set(System(), "autoChangePatterns", true));

    /// <summary>The weather as it is now, for a world snapshot, or null without a weather system. Runs on the game thread.</summary>
    internal Saved? Capture()
    {
        if (FindSystem() is not { } system || Member.Get(system, "dummySim") == null) return null;

        Dictionary<long, byte[]> regions = [];
        foreach ((long key, object region) in LoadedRegions(system))
        {
            regions[key] = (byte[])Member.Call(region, "ToBytes")!;
        }

        return new Saved(regions, (float?)Member.Get(system, "OverridePrecipitation"), (double)Member.Get(system, "RainCloudDaysOffset")!, (bool)Member.Get(system, "autoChangePatterns")!);
    }

    /// <summary>
    /// Puts the weather back as <paramref name="saved"/> holds it, after the calendar. Regions
    /// loaded after the capture keep their weather. Runs on the game thread.
    /// </summary>
    internal void Restore(Saved saved)
    {
        object system = System();
        foreach ((long key, object region) in LoadedRegions(system))
        {
            if (!saved.Regions.TryGetValue(key, out byte[]? bytes)) continue;

            // The region's snow accumulation comes back with it.
            lock (SnowLock(region)) Member.Call(region, "FromBytes", bytes);
            SettleWind(region);
            Member.Call(region, "UpdateWeatherData");
            Member.Call(region, "sendWeatherUpdatePacket");
        }

        Member.Set(system, "OverridePrecipitation", saved.PrecipitationOverride);
        Member.Set(system, "RainCloudDaysOffset", saved.RainCloudDaysOffset);
        Member.Set(system, "autoChangePatterns", saved.AutoChange);
        Member.Call(system, "broadCastConfigUpdate");
    }

    /// <summary>The weather as a snapshot holds it.</summary>
    internal sealed record Saved(IReadOnlyDictionary<long, byte[]> Regions, float? PrecipitationOverride, double RainCloudDaysOffset, bool AutoChange);

    private ICoreServerAPI Api => (ICoreServerAPI)_host.Server.Api;

    private object? FindSystem() => Api.ModLoader.GetModSystem(SystemType);

    private object System() => FindSystem() is { } system && Member.Get(system, "dummySim") != null
        ? system
        : throw new InvalidOperationException("This world has no weather system: the essentials mod did not load.");

    // A wind pattern without strength noise, such as still, only takes its strength when it begins.
    private static void SettleWind(object region)
    {
        object wind = Member.Get(region, "CurWindPattern")!;
        object state = Member.Get(wind, "State")!;
        Member.Set(wind, "Strength", Member.Get(state, "BaseStrength"));
        Member.Call(region, "UpdateWeatherData");
    }

    private static object SnowLock(object region) =>
        region.GetType().GetField("snowAccumSnapshotLock", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) ?? region;

    // The regions to change: the four weather at a position blends, or every loaded one. The game's
    // dummy region, used where none is loaded, is never one of them.
    private IEnumerable<object> Regions(object system, BlockPos? at)
    {
        if (at == null) return LoadedRegions(system).Select(r => r.Region).ToList();

        int size = Api.World.BlockAccessor.RegionSize;
        int baseX = (int)Math.Round(at.X / (double)size) - 1;
        int baseZ = (int)Math.Round(at.Z / (double)size) - 1;
        List<object> regions = [];
        for (int dx = 0; dx <= 1; dx++)
        {
            for (int dz = 0; dz <= 1; dz++)
            {
                if (Member.Call(system, "getOrCreateWeatherSimForRegion", baseX + dx, baseZ + dz) is { } region) regions.Add(region);
            }
        }

        return regions;
    }

    private object? RegionContaining(object system, BlockPos pos)
    {
        int size = Api.World.BlockAccessor.RegionSize;
        return Member.Call(system, "getOrCreateWeatherSimForRegion", pos.X / size, pos.Z / size);
    }

    // Every loaded map region's weather, keyed the way the weather system keys it. Goes through the
    // world's own list rather than the weather system's map, which the snow thread adds to.
    private IEnumerable<(long Key, object Region)> LoadedRegions(object system)
    {
        List<(long, object)> regions = [];
        foreach (long index in Api.WorldManager.AllLoadedMapRegions.Keys.ToList())
        {
            Vec3i pos = Api.WorldManager.MapRegionPosFromIndex2D(index);
            if (Member.Call(system, "getOrCreateWeatherSimForRegion", pos.X, pos.Z) is { } region)
            {
                regions.Add(((long)Member.Call(system, "MapRegionIndex2D", pos.X, pos.Z)!, region));
            }
        }

        return regions;
    }

    private static IReadOnlyList<string> Codes(object system, string configs) =>
        ((IEnumerable)Member.Get(system, configs)!).Cast<object>().Select(c => (string)Member.Get(c, "Code")!).ToList();

    private static void RequireCode(object system, string configs, string code, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code, parameter);
        IReadOnlyList<string> codes = Codes(system, configs);
        if (!codes.Contains(code)) throw new ArgumentException($"No such code '{code}'. Known: {string.Join(", ", codes)}.", parameter);
    }

    private static string PatternCode(object? pattern) => (string)Member.Get(Member.Get(pattern!, "config")!, "Code")!;

    /// <summary>
    /// The weather system's members, found by name: Pharos does not build against the essentials
    /// mod. A member the game renamed is reported by name.
    /// </summary>
    private static class Member
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static object? Get(object target, string name)
        {
            Type type = target.GetType();
            if (type.GetProperty(name, Flags) is { } property) return property.GetValue(target);
            if (type.GetField(name, Flags) is { } field) return field.GetValue(target);
            throw Missing(type, name);
        }

        public static void Set(object target, string name, object? value)
        {
            Type type = target.GetType();
            if (type.GetProperty(name, Flags) is { CanWrite: true } property) property.SetValue(target, value);
            else if (type.GetField(name, Flags) is { } field) field.SetValue(target, value);
            else throw Missing(type, name);
        }

        public static object? Call(object target, string name, params object?[] args)
        {
            Type type = target.GetType();
            MethodInfo method = type.GetMethods(Flags)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length && m.GetParameters().Zip(args).All(p => p.Second == null || p.First.ParameterType.IsInstanceOfType(p.Second)))
                ?? throw Missing(type, name);
            try
            {
                return method.Invoke(target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static MissingMemberException Missing(Type type, string name) =>
            new($"Pharos cannot find {type.Name}.{name} in the game's weather system: the game changed.");
    }
}
