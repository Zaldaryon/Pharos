using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>
/// Harmony hooks to track weather-spawned particles separately from other particle sources.
/// </summary>
/// <remarks>
/// The game's <c>ParticleManager.ParticlesAlive</c> counts all particles by model type without
/// distinguishing their origin. This class patches <c>WeatherSimulationParticles</c> spawn methods
/// to count weather particles separately. The count is approximate because particles decay over time
/// and the internal pool recycles slots; the hooks count spawn events, not live particles.
/// </remarks>
internal static class ParticleAttributionHooks
{
    private const string HarmonyId = "zaldaryon.pharos.particle-attribution";
    private static Harmony? s_harmony;
    private static bool s_installed;

    // Spawn counters for current frame window. These are cumulative and reset by the driver.
    private static long s_rainSpawns;
    private static long s_snowSpawns;
    private static long s_hailSpawns;
    private static long s_dustSpawns;
    private static long s_splashSpawns;

    /// <summary>Total rain particle spawn calls since last reset.</summary>
    public static long RainSpawns => Interlocked.Read(ref s_rainSpawns);

    /// <summary>Total snow particle spawn calls since last reset.</summary>
    public static long SnowSpawns => Interlocked.Read(ref s_snowSpawns);

    /// <summary>Total hail particle spawn calls since last reset.</summary>
    public static long HailSpawns => Interlocked.Read(ref s_hailSpawns);

    /// <summary>Total dust/storm particle spawn calls since last reset.</summary>
    public static long DustSpawns => Interlocked.Read(ref s_dustSpawns);

    /// <summary>Total splash particle spawn calls since last reset.</summary>
    public static long SplashSpawns => Interlocked.Read(ref s_splashSpawns);

    /// <summary>Total weather-attributed particle spawn calls since last reset.</summary>
    public static long TotalWeatherSpawns => RainSpawns + SnowSpawns + HailSpawns + DustSpawns + SplashSpawns;

    /// <summary>Resets all spawn counters to zero.</summary>
    public static void ResetCounters()
    {
        Interlocked.Exchange(ref s_rainSpawns, 0);
        Interlocked.Exchange(ref s_snowSpawns, 0);
        Interlocked.Exchange(ref s_hailSpawns, 0);
        Interlocked.Exchange(ref s_dustSpawns, 0);
        Interlocked.Exchange(ref s_splashSpawns, 0);
    }

    /// <summary>
    /// Installs Harmony patches on WeatherSimulationParticles to track weather particle spawns.
    /// </summary>
    /// <returns>True if patches were installed (first call) or already installed.</returns>
    internal static bool Install()
    {
        if (s_installed) return true;

        // Find the WeatherSimulationParticles type in VSEssentials
        Type? weatherParticles = null;
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name != "vsessentialsmod") continue;
            weatherParticles = asm.GetType("Vintagestory.GameContent.WeatherSimulationParticles");
            break;
        }

        if (weatherParticles == null)
            return false; // VSEssentials not loaded yet or changed

        s_harmony = new Harmony(HarmonyId);

        // Patch the private spawn methods. These are called from asyncParticleSpawn.
        // We use postfix patches because prefix would require us to replicate the spawn logic.
        TryPatchMethod(weatherParticles, "SpawnRainParticles", nameof(AfterSpawnRain));
        TryPatchMethod(weatherParticles, "SpawnSnowParticles", nameof(AfterSpawnSnow));
        TryPatchMethod(weatherParticles, "SpawnHailParticles", nameof(AfterSpawnHail));
        TryPatchMethod(weatherParticles, "SpawnDustParticles", nameof(AfterSpawnDust));

        s_installed = true;
        return true;
    }

    private static void TryPatchMethod(Type targetType, string methodName, string postfixName)
    {
        var method = AccessTools.Method(targetType, methodName);
        if (method == null) return;

        var postfix = new HarmonyMethod(typeof(ParticleAttributionHooks), postfixName);
        try
        {
            s_harmony!.Patch(method, postfix: postfix);
        }
        catch
        {
            // Method signature changed; skip this patch
        }
    }

    /// <summary>Uninstalls all particle attribution patches.</summary>
    internal static void Uninstall()
    {
        if (!s_installed || s_harmony == null) return;
        s_harmony.UnpatchAll(HarmonyId);
        s_installed = false;
    }

    // --- Harmony postfix methods ---
    // These are called after the game's spawn methods. We count the spawn event, not actual particles.
    // The methods have varying signatures, but we only need to count calls.

    private static void AfterSpawnRain()
    {
        Interlocked.Increment(ref s_rainSpawns);
    }

    private static void AfterSpawnSnow()
    {
        Interlocked.Increment(ref s_snowSpawns);
    }

    private static void AfterSpawnHail()
    {
        Interlocked.Increment(ref s_hailSpawns);
    }

    private static void AfterSpawnDust()
    {
        Interlocked.Increment(ref s_dustSpawns);
    }

    // Splash is spawned inline in SpawnRainParticles and SpawnDustParticles, not separate method.
    // We'll handle this via the rain/dust counter; splash is part of weather.
}
