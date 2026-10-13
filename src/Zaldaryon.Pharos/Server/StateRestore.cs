using System.Runtime.CompilerServices;
using Vintagestory.API.Datastructures;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Puts saved attribute trees and mod data back into live ones without replacing the live objects,
/// which behaviours and mods hold references to.
/// </summary>
internal static class StateRestore
{
    /// <summary>
    /// Makes <paramref name="live"/> hold what <paramref name="saved"/> holds. A sub-tree present in
    /// both is restored into the live one, so references to it stay valid. Keys are written to the
    /// tree's storage directly: nothing is marked dirty and no modified listener fires.
    /// </summary>
    /// <param name="live">The tree to restore.</param>
    /// <param name="saved">What it held. Its values are moved into <paramref name="live"/>, so it must be a fresh copy.</param>
    /// <param name="restoreKey">Whether a top-level key is put back; a refused key keeps its live value. Every nested key is put back.</param>
    /// <returns>Whether anything changed.</returns>
    public static bool TreeInPlace(TreeAttribute live, TreeAttribute saved, Func<string, bool> restoreKey)
    {
        IDictionary<string, IAttribute> current = Storage(live);
        IDictionary<string, IAttribute> wanted = Storage(saved);
        bool changed = false;

        foreach (string key in current.Keys.ToList())
        {
            if (wanted.ContainsKey(key) || !restoreKey(key)) continue;

            current.Remove(key);
            changed = true;
        }

        foreach ((string key, IAttribute value) in wanted.ToList())
        {
            if (!restoreKey(key)) continue;

            if (current.TryGetValue(key, out IAttribute? existing) && existing is TreeAttribute liveTree && value is TreeAttribute savedTree)
            {
                changed |= TreeInPlace(liveTree, savedTree, static _ => true);
            }
            else if (existing == null || !Bytes(existing).AsSpan().SequenceEqual(Bytes(value)))
            {
                current[key] = value;
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>
    /// Makes <paramref name="live"/> hold what <paramref name="saved"/> holds, with fresh copies of
    /// the saved bytes. A key stored with null counts as absent.
    /// </summary>
    /// <param name="live">The mod data to restore.</param>
    /// <param name="saved">What it held.</param>
    /// <param name="restoreKey">Whether a key is put back; a refused key keeps its live value.</param>
    /// <returns>How many keys were added back, changed back or removed.</returns>
    public static int ModDataInPlace(IDictionary<string, byte[]> live, IReadOnlyDictionary<string, byte[]> saved, Func<string, bool> restoreKey)
    {
        int changed = 0;
        foreach ((string key, byte[]? value) in live.ToArray())
        {
            if ((saved.TryGetValue(key, out byte[]? kept) && kept != null) || !restoreKey(key)) continue;

            // A key stored with null holds nothing: removed, but not counted.
            live.Remove(key);
            if (value != null) changed++;
        }

        foreach ((string key, byte[]? value) in saved)
        {
            if (value == null || !restoreKey(key)) continue;
            if (live.TryGetValue(key, out byte[]? existing) && existing != null && existing.AsSpan().SequenceEqual(value)) continue;

            live[key] = (byte[])value.Clone();
            changed++;
        }

        return changed;
    }

    /// <summary>A copy of <paramref name="data"/>, its byte arrays copied too, without keys stored with null.</summary>
    public static Dictionary<string, byte[]> Copy(IEnumerable<KeyValuePair<string, byte[]>>? data)
    {
        Dictionary<string, byte[]> copy = [];
        if (data == null) return copy;

        foreach ((string key, byte[] value) in data.ToArray())
        {
            if (value != null) copy[key] = (byte[])value.Clone();
        }

        return copy;
    }

    private static byte[] Bytes(IAttribute attribute)
    {
        using MemoryStream stream = new();
        using BinaryWriter writer = new(stream);
        writer.Write((byte)attribute.GetAttributeId());
        attribute.ToBytes(writer);
        writer.Flush();
        return stream.ToArray();
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "attributes")]
    private static extern ref IDictionary<string, IAttribute> Storage(TreeAttribute tree);
}
