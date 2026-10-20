using System.Globalization;
using Zaldaryon.Pharos.Metrics;

namespace Zaldaryon.Pharos.Server;

/// <summary>What one part of a save cost: its time, the bytes it wrote and how many items it wrote.</summary>
public sealed class SaveCategoryProfile
{
    internal SaveCategoryProfile(string name, double ms, long bytes, int count)
    {
        Name = name;
        Ms = ms;
        Bytes = bytes;
        Count = count;
    }

    /// <summary><c>chunks</c>, <c>mapchunks</c>, <c>mapregions</c>, <c>players</c> or <c>savegame</c>.</summary>
    public string Name { get; }

    /// <summary>The time spent in the database writes for this category, in milliseconds.</summary>
    public double Ms { get; }

    /// <summary>The serialized bytes this category wrote.</summary>
    public long Bytes { get; }

    /// <summary>How many chunks, map chunks, regions, players or savegame blobs it wrote.</summary>
    public int Count { get; }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Name}: {Ms:F2} ms, {Bytes:N0} bytes, {Count} items");
}

/// <summary>A mod data entry the save would write, and the mod its key belongs to.</summary>
/// <param name="Key">The key as the mod stored it, such as <c>mymod:notes</c>.</param>
/// <param name="Mod">The mod whose id is the key's domain, or null when the key has no loaded mod's domain.</param>
/// <param name="Bytes">The serialized payload size.</param>
public sealed record SaveModDataEntry(string Key, string? Mod, long Bytes)
{
    /// <summary>The mod id, or <c>unattributed</c> when the key's domain is no mod.</summary>
    public string Owner => Mod ?? "unattributed";
}

/// <summary>
/// What one save wrote: the wall time, the bytes and time of each category, and the mod data payloads
/// the save carries, attributed by the domain of their keys.
/// </summary>
/// <remarks>
/// Times are wall-clock time around the game's database writes, measured on the game thread. They
/// are noisy; the bytes and counts are exact. Mod data bytes are the in-memory serialized payloads
/// the save would write, not the database framing around them. Entries aggregate the same key across
/// the savegame, map regions and loaded chunks, so a key a mod stores in every chunk appears once
/// with the sum of those payloads.
/// </remarks>
public sealed class SaveProfile : IMetricSource
{
    internal SaveProfile(double totalMs, IReadOnlyList<SaveCategoryProfile> categories,
        IReadOnlyList<SaveModDataEntry> modData)
    {
        TotalMs = totalMs;
        Categories = categories;
        ModData = modData;
    }

    /// <summary>The wall time of the whole save, in milliseconds.</summary>
    public double TotalMs { get; }

    /// <summary>The categories, in the order the save writes them.</summary>
    public IReadOnlyList<SaveCategoryProfile> Categories { get; }

    /// <summary>The chunks the save wrote.</summary>
    public SaveCategoryProfile Chunks => Category("chunks");

    /// <summary>The map chunks the save wrote.</summary>
    public SaveCategoryProfile MapChunks => Category("mapchunks");

    /// <summary>The map regions the save wrote.</summary>
    public SaveCategoryProfile MapRegions => Category("mapregions");

    /// <summary>The player data the save wrote.</summary>
    public SaveCategoryProfile Players => Category("players");

    /// <summary>The serialized savegame blob, which includes the mod data payloads.</summary>
    public SaveCategoryProfile SaveGame => Category("savegame");

    /// <summary>The sum of the bytes of every category.</summary>
    public long TotalBytes => Categories.Sum(category => category.Bytes);

    /// <summary>Every mod data entry, largest first.</summary>
    public IReadOnlyList<SaveModDataEntry> ModData { get; }

    /// <summary>The sum of every mod data payload.</summary>
    public long TotalModDataBytes => ModData.Sum(entry => entry.Bytes);

    /// <summary>The mod data payloads whose key belongs to <paramref name="modId"/>.</summary>
    /// <param name="modId">The mod id, such as <c>mymod</c>.</param>
    public long ModDataBytes(string modId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        return ModData.Where(entry => string.Equals(entry.Mod, modId, StringComparison.Ordinal))
            .Sum(entry => entry.Bytes);
    }

    /// <summary>The entries whose key belongs to <paramref name="modId"/>.</summary>
    /// <param name="modId">The mod id, such as <c>mymod</c>.</param>
    public IReadOnlyList<SaveModDataEntry> ModDataOf(string modId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        return [.. ModData.Where(entry => string.Equals(entry.Mod, modId, StringComparison.Ordinal))];
    }

    /// <summary>
    /// Every number as a named metric, for a scenario's <c>result.json</c> and for
    /// <c>pharos compare</c>. Times use the stopwatch clock; bytes and counts are exact.
    /// </summary>
    /// <remarks>
    /// The names are <c>save.total.ms</c>, <c>save.total.bytes</c>,
    /// <c>save.&lt;category&gt;.ms</c>, <c>.bytes</c> and <c>.count</c>,
    /// <c>save.moddata.bytes</c>, <c>save.moddata.&lt;owner&gt;.bytes</c>,
    /// <c>save.moddata.&lt;owner&gt;.keys</c> and
    /// <c>save.moddata.key.&lt;owner&gt;.&lt;key&gt;.bytes</c>.
    /// </remarks>
    public IReadOnlyList<MetricReading> Metrics()
    {
        List<MetricReading> readings =
        [
            new("save.total.ms", TotalMs, MetricUnit.Milliseconds, MetricDirection.LowerIsBetter, MetricClock.Stopwatch),
            new("save.total.bytes", TotalBytes, MetricUnit.Bytes, MetricDirection.LowerIsBetter, MetricClock.Exact),
            new("save.moddata.bytes", TotalModDataBytes, MetricUnit.Bytes, MetricDirection.LowerIsBetter, MetricClock.Exact),
        ];

        foreach (SaveCategoryProfile category in Categories)
        {
            readings.Add(new MetricReading($"save.{category.Name}.ms", category.Ms, MetricUnit.Milliseconds,
                MetricDirection.LowerIsBetter, MetricClock.Stopwatch));
            readings.Add(new MetricReading($"save.{category.Name}.bytes", category.Bytes, MetricUnit.Bytes,
                MetricDirection.LowerIsBetter, MetricClock.Exact));
            readings.Add(new MetricReading($"save.{category.Name}.count", category.Count, MetricUnit.Count,
                MetricDirection.Neutral, MetricClock.Exact));
        }

        foreach (IGrouping<string, SaveModDataEntry> owner in ModData.GroupBy(entry => entry.Owner))
        {
            string ownerName = Name(owner.Key);
            readings.Add(new MetricReading($"save.moddata.{ownerName}.bytes", owner.Sum(entry => entry.Bytes),
                MetricUnit.Bytes, MetricDirection.LowerIsBetter, MetricClock.Exact));
            readings.Add(new MetricReading($"save.moddata.{ownerName}.keys", owner.Count(),
                MetricUnit.Count, MetricDirection.Neutral, MetricClock.Exact));
        }

        foreach (SaveModDataEntry entry in ModData)
        {
            string key = entry.Mod == null ? entry.Key : entry.Key[(entry.Key.IndexOf(':') + 1)..];
            readings.Add(new MetricReading($"save.moddata.key.{Name(entry.Owner)}.{Name(key)}.bytes", entry.Bytes,
                MetricUnit.Bytes, MetricDirection.LowerIsBetter, MetricClock.Exact));
        }

        return readings;
    }

    /// <summary>Publishes every metric into the scenario running on this flow; a no-op outside one.</summary>
    public void Publish() => Zaldaryon.Pharos.Metrics.Metrics.Publish(Metrics());

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{TotalMs:F1} ms, {TotalBytes:N0} bytes, {ModData.Count} mod data keys");

    private SaveCategoryProfile Category(string name) =>
        Categories.FirstOrDefault(category => category.Name == name)
        ?? new SaveCategoryProfile(name, 0, 0, 0);

    private static string Name(string part)
    {
        if (part.Length == 0) return "_";
        return part.Replace(' ', '_').Replace('/', '.').Replace(':', '.');
    }
}
