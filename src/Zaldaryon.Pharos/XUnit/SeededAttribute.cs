using System.Text;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Seeds the game's random generators before each scenario test it covers, so a failure can be
/// replayed. See <c>docs/writing-server-scenarios.md</c>, "Seeded randomness".
/// </summary>
/// <remarks>
/// <para>
/// Put it on a test, a class, or the assembly (<c>[assembly: Seeded]</c> seeds every scenario
/// test in it). The nearest one wins: the test's, then the class's, then the assembly's. Tests it
/// does not cover keep the game's own, unseeded generators.
/// </para>
/// <para>
/// Without a seed, the seed is a stable hash of the test's class, method and theory arguments.
/// <c>PHAROS_RANDOM_SEED</c> replaces the seed of every test this attribute covers: the test's
/// output and its <c>run.json</c> give the seed to replay it with.
/// </para>
/// <para>
/// Only the scenario pipeline (<c>[ServerScenario]</c>, <c>[ClientScenario]</c>,
/// <c>[ClientServerScenario]</c> and their theories) reads it: under a plain <c>[Fact]</c> it does
/// nothing, and the test can call <c>Host.Random.Seed</c> itself, which seeds the statics of the
/// calling assembly too.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class SeededAttribute : Attribute
{
    /// <summary>The environment variable that replaces the seed of every seeded test: <c>PHAROS_RANDOM_SEED=1234</c>.</summary>
    public const string SeedVariable = "PHAROS_RANDOM_SEED";

    /// <summary>Seeds with a stable hash of the test's name.</summary>
    public SeededAttribute()
    {
    }

    /// <summary>Seeds with <paramref name="seed"/>.</summary>
    public SeededAttribute(int seed)
    {
        Seed = seed;
    }

    /// <summary>The seed, or null for a hash of the test's name.</summary>
    public int? Seed { get; }

    /// <summary>
    /// The seed a test of this name gets by default: FNV-1a over its UTF-8 bytes, folded to a
    /// non-negative 32-bit number.
    /// </summary>
    internal static int SeedFor(string testName)
    {
        ulong hash = Fnv64(testName);
        return (int)((hash ^ (hash >> 32)) & 0x7FFFFFFF);
    }

    /// <summary>FNV-1a, 64 bits, over the UTF-8 bytes of <paramref name="text"/>.</summary>
    internal static ulong Fnv64(string text)
    {
        ulong hash = 14695981039346656037UL;
        foreach (byte b in Encoding.UTF8.GetBytes(text))
        {
            hash ^= b;
            hash *= 1099511628211UL;
        }

        return hash;
    }
}
