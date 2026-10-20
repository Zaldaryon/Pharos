using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Metrics;
using PharosMetrics = Zaldaryon.Pharos.Metrics.Metrics;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Worldgen, saves and loaded-world counts are measured and published per pass, category and mod.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, Isolation = Isolation.Restart)]
[ServerMods("TestMods/pharosworldmod")]
public class WorldMetricsTests : ServerScenarioBase
{
    private const string Mod = "pharosworldmod";
    private const string SaveKey = "pharosworldmod:save";
    private const string RegionKey = "pharosworldmod:region";
    private const string BlobKey = "pharosworldmod:blob";

    [ServerScenario]
    public async Task Worldgen_ReportsPassesHandlersAndColumns()
    {
        (int x, int z) = FreshArea(1);
        WorldgenProfile profile = await Server.Worldgen.MeasureAsync(x, z, x, z);

        Assert.Equal(1, profile.RequestedColumns);
        Assert.True(profile.Columns >= 1, "No column was generated.");
        Assert.True(profile.WallMs > 0);
        Assert.NotEmpty(profile.Passes);
        Assert.NotEmpty(profile.MapChunks);

        WorldgenHandlerProfile handler = Assert.Single(profile.Passes.Where(profileHandler =>
            profileHandler.Mod == Mod && profileHandler.Handler.EndsWith(".OnTerrain", StringComparison.Ordinal)));
        Assert.Equal("terrain", handler.Stage);
        Assert.True(handler.Items >= 1);
        Assert.Equal(handler.Items, handler.Calls);
        Assert.True(handler.TotalMs > 0, "The mod's terrain handler cost nothing.");
        Assert.True(handler.MeanMsPerItem > 0);
        Assert.True(profile.MeanMsPerColumn > 0);

        WorldgenModProfile mod = profile.Mod(Mod);
        Assert.Equal(Mod, mod.Mod);
        Assert.Equal(Mod, mod.Owner);
        Assert.True(mod.Calls > 0);
        Assert.True(mod.Columns >= 1);
        Assert.True(mod.MeanMsPerColumn > 0);
        Assert.Equal(profile.Passes.Where(pass => pass.Mod == Mod).Sum(pass => pass.TotalMs), mod.TotalMs, 0.01);
    }

    [ServerScenario]
    public async Task Worldgen_RejectsAnAreaTheWorldAlreadyLoaded()
    {
        int sizeX = Server.Api.WorldManager.MapSizeX / GlobalConstants.ChunkSize;
        int sizeZ = Server.Api.WorldManager.MapSizeZ / GlobalConstants.ChunkSize;
        int x = sizeX / 2;
        int z = sizeZ / 2;
        Assert.True(Server.Server.IsChunkColumnFullyLoaded(x, z), "The spawn column was not loaded.");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Server.Worldgen.MeasureAsync(x, z, x, z));
        Assert.Contains("already loaded", error.Message, StringComparison.Ordinal);

        // A failed measurement leaves no handlers wrapped and no measurement open.
        (int freshX, int freshZ) = FreshArea(1);
        WorldgenProfile profile = await Server.Worldgen.MeasureAsync(freshX, freshZ, freshX, freshZ);
        Assert.True(profile.Columns >= 1);
    }

    [ServerScenario]
    public async Task Save_ReportsCategoriesAndModData()
    {
        Server.RunOnGameThread(() =>
        {
            IServerChunk chunk = Server.Api.WorldManager.AllLoadedChunks.Values.First();
            chunk.SetModdata(SaveKey, new byte[2048]);

            IMapRegion region = Server.Api.WorldManager.AllLoadedMapRegions.Values.First();
#pragma warning disable CS0618 // The save profile reads the same obsolete dictionary.
            region.SetModdata(RegionKey, new byte[1024]);
#pragma warning restore CS0618
        });

        SaveProfile save = await Server.Saves.MeasureAsync();

        Assert.True(save.TotalMs > 0);
        Assert.True(save.TotalBytes > 0);
        Assert.Equal(1, save.SaveGame.Count);
        Assert.True(save.SaveGame.Bytes > 0, "The savegame blob wrote no bytes.");
        Assert.True(save.Chunks.Count >= 1, "The dirty chunk was not written.");
        Assert.True(save.Chunks.Bytes > 0);
        Assert.Equal(save.Categories.Sum(category => category.Bytes), save.TotalBytes);

        string entries = string.Join("; ", save.ModData.Select(entry => $"{entry.Key}={entry.Bytes}/{entry.Mod}"));
        Assert.True(save.ModData.Any(entry => entry.Key == SaveKey && entry.Mod == Mod && entry.Bytes == 2048),
            $"The chunk's mod data entry is missing or has the wrong size. Entries: {entries}");
        Assert.Contains(save.ModData, entry => entry.Key == RegionKey
            && entry.Mod == Mod && entry.Bytes >= 1024);
        Assert.True(save.ModDataBytes(Mod) >= 4096 + 2048 + 1024);
        Assert.Contains(save.ModDataOf(Mod), entry => entry.Key == BlobKey);
    }

    [ServerScenario]
    public async Task WorldStats_CountsLoadedChunksEntitiesAndBlockEntities()
    {
        Block block = Server.Api.World.GetBlock(new AssetLocation($"{Mod}:worldblock"))
            ?? throw new InvalidOperationException("The test mod's block is missing.");
        Vec3d spawn = Server.Api.World.DefaultSpawnPosition.XYZ;
        BlockPos pos = new((int)spawn.X, (int)spawn.Y, (int)spawn.Z);
        Server.RunOnGameThread(() => Server.Api.World.BlockAccessor.SetBlock(block.BlockId, pos));
        await Server.Entities.SpawnAsync("game:chicken-hen", pos.AddCopy(1, 0, 0));

        WorldStats stats = Server.WorldStats;

        Assert.True(stats.LoadedChunks > 0);
        Assert.True(stats.LoadedMapChunks > 0);
        Assert.True(stats.LoadedMapRegions > 0);
        Assert.True(stats.LoadedEntities >= 1);
        Assert.Contains("game:chicken-hen", stats.EntitiesByCode.Keys);
        Assert.True(stats.BlockEntities >= 1, "The placed block entity was not counted.");
        Assert.Contains("PharosWorldBlockEntity", stats.BlockEntitiesByType.Keys);
        Assert.Equal(stats.BlockEntitiesByType.Values.Sum(), stats.BlockEntities);
        Assert.Equal(stats.EntitiesByCode.Values.Sum(), stats.LoadedEntities);
    }

    [ServerScenario]
    public async Task Publish_FeedsTheNumbersIntoTheScenarioMetrics()
    {
        (int x, int z) = FreshArea(1);
        WorldgenProfile worldgen = await Server.Worldgen.MeasureAsync(x, z, x, z);
        SaveProfile save = await Server.Saves.MeasureAsync();
        WorldStats stats = Server.WorldStats;
        Zaldaryon.Pharos.Metrics.Metrics.Publish(worldgen, save, stats);

        IReadOnlyDictionary<string, MetricSeries> metrics = PharosMetrics.Current!.Snapshot();
        Assert.Contains("worldgen.wall.ms", metrics.Keys);
        Assert.Contains("worldgen.columns", metrics.Keys);
        Assert.Contains(metrics.Keys, name => name.StartsWith($"worldgen.terrain.{Mod}.", StringComparison.Ordinal));
        Assert.Contains(metrics.Keys, name => name.StartsWith($"worldgen.mod.{Mod}.", StringComparison.Ordinal));
        Assert.Contains("save.total.ms", metrics.Keys);
        Assert.Contains("save.total.bytes", metrics.Keys);
        Assert.Contains(metrics.Keys, name => name.StartsWith($"save.moddata.{Mod}.", StringComparison.Ordinal));
        Assert.Contains("worldstats.chunks", metrics.Keys);
        Assert.Contains("worldstats.entities", metrics.Keys);
        Assert.Equal(MetricClock.Stopwatch, metrics["worldgen.wall.ms"].Clock);
        Assert.Equal(MetricClock.Exact, metrics["worldstats.chunks"].Clock);
    }

    private (int X, int Z) FreshArea(int width) => Server.RunOnGameThread(() =>
    {
        int sizeX = Server.Api.WorldManager.MapSizeX / GlobalConstants.ChunkSize;
        int sizeZ = Server.Api.WorldManager.MapSizeZ / GlobalConstants.ChunkSize;
        for (int x = 1; x + width <= sizeX; x += 2)
        {
            for (int z = 1; z + width <= sizeZ; z += 2)
            {
                bool loaded = false;
                for (int dx = 0; dx < width && !loaded; dx++)
                {
                    for (int dz = 0; dz < width && !loaded; dz++)
                    {
                        loaded = Server.Server.IsChunkColumnFullyLoaded(x + dx, z + dz);
                    }
                }

                if (!loaded) return (x, z);
            }
        }

        throw new InvalidOperationException($"The world has no fresh {width} by {width} chunk area.");
    });
}
