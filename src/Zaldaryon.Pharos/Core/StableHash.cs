using System.Text;

namespace Zaldaryon.Pharos.Core;

/// <summary>
/// A hash that gives the same value in every process and on every machine, for comparing the state of
/// two runs. It never uses <c>GetHashCode</c>, which the runtime randomizes per process.
/// </summary>
/// <remarks>
/// FNV-1a over the bytes of each value, then a final avalanche so that inputs that differ by one bit
/// spread across the output. Floating-point values hash by their bit pattern, so a value that differs
/// in the last bit gives a different hash.
/// </remarks>
internal static class StableHash
{
    private const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    /// <summary>The value a hash starts from.</summary>
    public const ulong Seed = OffsetBasis;

    /// <summary>Folds a 64-bit word into <paramref name="hash"/>, byte by byte, least significant first.</summary>
    public static ulong Add(ulong hash, ulong word)
    {
        for (int i = 0; i < 8; i++)
        {
            hash ^= (byte)(word >> (i * 8));
            hash *= Prime;
        }

        return hash;
    }

    /// <summary>Folds a signed 64-bit value into <paramref name="hash"/>.</summary>
    public static ulong AddLong(ulong hash, long value) => Add(hash, unchecked((ulong)value));

    /// <summary>Folds the bit pattern of <paramref name="value"/> into <paramref name="hash"/>.</summary>
    public static ulong AddDouble(ulong hash, double value) => Add(hash, unchecked((ulong)BitConverter.DoubleToInt64Bits(value)));

    /// <summary>Folds a string, by its length and then its UTF-8 bytes, into <paramref name="hash"/>.</summary>
    public static ulong AddString(ulong hash, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        hash = Add(hash, (ulong)bytes.Length);
        foreach (byte b in bytes)
        {
            hash ^= b;
            hash *= Prime;
        }

        return hash;
    }

    /// <summary>The final avalanche, applied once a value has been folded in.</summary>
    public static ulong Finish(ulong hash)
    {
        hash ^= hash >> 33;
        hash *= 0xff51afd7ed558ccdUL;
        hash ^= hash >> 33;
        hash *= 0xc4ceb9fe1a85ec53UL;
        hash ^= hash >> 33;
        return hash;
    }

    /// <summary>The hash of one string.</summary>
    public static ulong Of(string text) => Finish(AddString(Seed, text));
}
