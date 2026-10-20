using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace PharosWorldMod;

/// <summary>
/// Registers worldgen work and mod data the world metric tests attribute: a per-pass handler, a
/// map region and map chunk handler, savegame data, and a block entity.
/// </summary>
public sealed class PharosWorldModSystem : ModSystem
{
    /// <summary>The savegame key the mod stores, so a save profile has mod data to attribute.</summary>
    public const string BlobKey = "pharosworldmod:blob";

    /// <summary>The chunk key the worldgen handler stores.</summary>
    public const string ChunkKey = "pharosworldmod:column";

    /// <summary>The map region key the worldgen handler stores.</summary>
    public const string RegionKey = "pharosworldmod:region";

    /// <summary>The chunk key the save test stores on a loaded chunk.</summary>
    public const string SaveKey = "pharosworldmod:save";

    private static double s_sink;

    public override void Start(ICoreAPI api) =>
        api.RegisterBlockEntityClass("PharosWorldBlock", typeof(PharosWorldBlockEntity));

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.ChunkColumnGeneration(OnTerrain, EnumWorldGenPass.Terrain, "superflat");
        api.Event.ChunkColumnGeneration(OnVegetation, EnumWorldGenPass.Vegetation, "superflat");
        api.Event.MapRegionGeneration(OnMapRegion, "superflat");
        api.Event.MapChunkGeneration(OnMapChunk, "superflat");
        api.WorldManager.SaveGame.StoreData(BlobKey, new byte[4096]);
    }

    private static void OnTerrain(IChunkColumnGenerateRequest request) => Spin(2_000);

    private static void OnVegetation(IChunkColumnGenerateRequest request)
    {
        Spin(2_000);
        request.Chunks[0].SetModdata(ChunkKey, new byte[512]);
    }

    private static void OnMapRegion(IMapRegion region, int regionX, int regionZ, ITreeAttribute? parameters)
    {
        Spin(1_000);
#pragma warning disable CS0618 // The save profile reads the same obsolete dictionary.
        region.SetModdata(RegionKey, new byte[256]);
#pragma warning restore CS0618
    }

    private static void OnMapChunk(IMapChunk mapChunk, int chunkX, int chunkZ) => Spin(1_000);

    /// <summary>Work whose cost the profile should see, and which the JIT cannot remove.</summary>
    private static void Spin(int iterations)
    {
        double sum = 0;
        for (int i = 0; i < iterations; i++) sum += System.Math.Sqrt(i);
        s_sink = sum;
    }
}

/// <summary>A block entity the world stats test places and counts.</summary>
public sealed class PharosWorldBlockEntity : BlockEntity
{
}
