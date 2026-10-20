using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Metrics;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// How much of the world is loaded right now: chunks, map chunks, map regions, entities and block
/// entities, counted by kind.
/// </summary>
/// <remarks>
/// Capture one from <see cref="EmbeddedServerHost.WorldStats"/>. The game clones its loaded chunk,
/// map chunk and map region dictionaries to answer, so read it between ticks rather than in a loop;
/// the counts are a snapshot, not a running total.
/// </remarks>
public sealed class WorldStats : IMetricSource
{
    internal WorldStats(int loadedChunks, int loadedMapChunks, int loadedMapRegions,
        int generatingChunkColumns, int loadedEntities, int blockEntities,
        IReadOnlyDictionary<string, int> entitiesByCode, IReadOnlyDictionary<string, int> blockEntitiesByType)
    {
        LoadedChunks = loadedChunks;
        LoadedMapChunks = loadedMapChunks;
        LoadedMapRegions = loadedMapRegions;
        GeneratingChunkColumns = generatingChunkColumns;
        LoadedEntities = loadedEntities;
        BlockEntities = blockEntities;
        EntitiesByCode = entitiesByCode;
        BlockEntitiesByType = blockEntitiesByType;
    }

    /// <summary>How many 3D chunks are loaded.</summary>
    public int LoadedChunks { get; }

    /// <summary>How many map chunks are loaded.</summary>
    public int LoadedMapChunks { get; }

    /// <summary>How many map regions are loaded.</summary>
    public int LoadedMapRegions { get; }

    /// <summary>How many chunk columns are in the generating queue.</summary>
    public int GeneratingChunkColumns { get; }

    /// <summary>How many entities are loaded, dead ones included.</summary>
    public int LoadedEntities { get; }

    /// <summary>How many block entities are in the loaded chunks.</summary>
    public int BlockEntities { get; }

    /// <summary>Entities by their code, such as <c>game:chicken-hen</c>.</summary>
    public IReadOnlyDictionary<string, int> EntitiesByCode { get; }

    /// <summary>Block entities by their class name, such as <c>BlockEntityQuern</c>.</summary>
    public IReadOnlyDictionary<string, int> BlockEntitiesByType { get; }

    /// <summary>
    /// Every count as a named metric, for a scenario's <c>result.json</c> and for
    /// <c>pharos compare</c>. Counts are exact.
    /// </summary>
    public IReadOnlyList<MetricReading> Metrics()
    {
        List<MetricReading> readings =
        [
            Exact("worldstats.chunks", LoadedChunks),
            Exact("worldstats.mapchunks", LoadedMapChunks),
            Exact("worldstats.mapregions", LoadedMapRegions),
            Exact("worldstats.generatingchunkcolumns", GeneratingChunkColumns),
            Exact("worldstats.entities", LoadedEntities),
            Exact("worldstats.blockentities", BlockEntities),
        ];

        foreach ((string code, int count) in EntitiesByCode)
        {
            readings.Add(Exact($"worldstats.entity.{code}", count));
        }

        foreach ((string type, int count) in BlockEntitiesByType)
        {
            readings.Add(Exact($"worldstats.blockentity.{type}", count));
        }

        return readings;
    }

    /// <summary>Publishes every count into the scenario running on this flow; a no-op outside one.</summary>
    public void Publish() => Zaldaryon.Pharos.Metrics.Metrics.Publish(Metrics());

    /// <inheritdoc/>
    public override string ToString() =>
        $"{LoadedChunks} chunks, {LoadedMapChunks} map chunks, {LoadedMapRegions} map regions, " +
        $"{LoadedEntities} entities, {BlockEntities} block entities";

    private static MetricReading Exact(string name, int value) =>
        new(name, value, MetricUnit.Count, MetricDirection.Neutral, MetricClock.Exact);

    internal static WorldStats Capture(EmbeddedServerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.RunOnGameThread(() =>
        {
            ICoreServerAPI api = host.Api;
            Dictionary<long, IServerChunk> chunks = api.WorldManager.AllLoadedChunks;
            Dictionary<string, int> blockEntitiesByType = new(StringComparer.Ordinal);
            int blockEntities = 0;
            foreach (IServerChunk chunk in chunks.Values)
            {
                if (chunk.BlockEntities is not { } atPosition) continue;

                foreach (BlockEntity blockEntity in atPosition.Values)
                {
                    blockEntities++;
                    string type = blockEntity.GetType().Name;
                    blockEntitiesByType[type] = blockEntitiesByType.GetValueOrDefault(type) + 1;
                }
            }

            Dictionary<string, int> entitiesByCode = new(StringComparer.Ordinal);
            int entities = 0;
            foreach (Entity entity in api.World.LoadedEntities.Values)
            {
                entities++;
                string code = entity.Code?.ToString() ?? entity.GetType().Name;
                entitiesByCode[code] = entitiesByCode.GetValueOrDefault(code) + 1;
            }

            return new WorldStats(
                chunks.Count,
                api.WorldManager.AllLoadedMapchunks.Count,
                api.WorldManager.AllLoadedMapRegions.Count,
                api.WorldManager.CurrentGeneratingChunkCount,
                entities,
                blockEntities,
                Sorted(entitiesByCode),
                Sorted(blockEntitiesByType));
        });
    }

    private static IReadOnlyDictionary<string, int> Sorted(Dictionary<string, int> counts) =>
        counts.OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
}
