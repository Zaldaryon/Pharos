using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>The climate values the game uses at one position.</summary>
/// <param name="Temperature">The temperature, in degrees Celsius.</param>
/// <param name="Rainfall">The rainfall, from 0 to 1.</param>
/// <param name="Fertility">The soil fertility, from 0 to 1.</param>
/// <param name="ForestDensity">The forest density, from 0 to 1.</param>
/// <param name="ShrubDensity">The shrub density, from 0 to 1.</param>
/// <param name="GeologicActivity">The geologic activity, from 0 to 1.</param>
public readonly record struct ClimateReading(
    float Temperature,
    float Rainfall,
    float Fertility,
    float ForestDensity,
    float ShrubDensity,
    float GeologicActivity);

/// <summary>Reads the climate at a position and overrides it inside a region for a test.</summary>
public sealed class ClimateDriver
{
    private readonly EmbeddedServerHost _host;
    private readonly List<ClimatePatches.ClimateOverrideRule> _active = [];

    internal ClimateDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>Reads the climate the server uses at a position now.</summary>
    /// <param name="pos">The block position.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pos"/> is null.</exception>
    public ClimateReading At(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return _host.RunOnGameThread(() =>
        {
            ClimateCondition climate = ServerBlockAccessor.GetClimateAt(pos, EnumGetClimateMode.NowValues);
            return new ClimateReading(
                climate.Temperature,
                climate.Rainfall,
                climate.Fertility,
                climate.ForestDensity,
                climate.ShrubDensity,
                climate.GeologicActivity);
        });
    }

    /// <summary>
    /// Replaces the temperature and rainfall the server reads inside a region until the returned override is
    /// disposed. Other values, and positions outside the region, keep the game's own climate.
    /// </summary>
    /// <param name="region">The box to override, in block coordinates.</param>
    /// <param name="temperature">The temperature to set, or null to keep the game's value.</param>
    /// <param name="rainfall">The rainfall to set, from 0 to 1, or null to keep the game's value.</param>
    /// <exception cref="ArgumentNullException"><paramref name="region"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="rainfall"/> is outside 0 to 1.</exception>
    public ClimateOverride Override(Cuboidi region, float? temperature = null, float? rainfall = null)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (rainfall is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(rainfall), "Rainfall is a value from 0 to 1.");
        ClimatePatches.ClimateOverrideRule rule = new(region, temperature, rainfall);
        _host.RunOnGameThread(() => ClimatePatches.Add(rule));
        lock (_active) _active.Add(rule);
        return new ClimateOverride(this, rule);
    }

    internal void End(ClimatePatches.ClimateOverrideRule rule)
    {
        _host.RunOnGameThread(() => ClimatePatches.Remove(rule));
        lock (_active) _active.Remove(rule);
    }

    /// <summary>Removes every override a test started, for a rollback.</summary>
    internal Baseline Capture() => new(this);

    private IBlockAccessor ServerBlockAccessor => ((Vintagestory.API.Server.ICoreServerAPI)_host.Server.Api).World.BlockAccessor;

    /// <summary>The overrides the world started with: none. A restore ends the ones a test started.</summary>
    internal sealed class Baseline
    {
        private readonly ClimateDriver _driver;

        internal Baseline(ClimateDriver driver) => _driver = driver;

        /// <summary>Ends every override the driver started since the world started.</summary>
        public void Restore()
        {
            ClimatePatches.ClimateOverrideRule[] rules;
            lock (_driver._active) rules = _driver._active.ToArray();
            foreach (ClimatePatches.ClimateOverrideRule rule in rules) _driver.End(rule);
        }
    }
}

/// <summary>An active climate override. Disposing it restores the game's climate in its region.</summary>
public sealed class ClimateOverride : IDisposable
{
    private readonly ClimateDriver _driver;
    private readonly ClimatePatches.ClimateOverrideRule _rule;
    private bool _ended;

    internal ClimateOverride(ClimateDriver driver, ClimatePatches.ClimateOverrideRule rule)
    {
        _driver = driver;
        _rule = rule;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ended) return;
        _ended = true;
        _driver.End(_rule);
    }
}
