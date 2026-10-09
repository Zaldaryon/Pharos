using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.World;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>
/// <see cref="EmbeddedServerHost.PlaceSchematicAsync"/> with <c>Fixtures/schematics/pharos-room.json</c>,
/// a WorldEdit export of a 4x3x4 corner of a granite room: floor, two walls, an east-facing chest
/// at (1, 1, 1) holding 3 sticks and 2 granite, and a torch at (3, 1, 3).
/// </summary>
[Collection("Sequential")]
[ServerWorld(seed: 4242)]
public class SchematicTests : ServerScenarioBase
{
    internal const string Room = "Fixtures/schematics/pharos-room.json";

    [ServerScenario]
    public async Task PlaceSchematic_PlacesTheBlocksAndTheChestWithItsContents_InChunksItLoads()
    {
        // Far from spawn: the chunks are not loaded until the placement asks for them.
        BlockPos origin = Spawn().AddCopy(320, 0, 320);
        Assert.Null(Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetChunkAtBlockPos(origin)));

        SchematicPlacement placement = await Host.PlaceSchematicAsync(Room, origin);

        Assert.Equal(origin, placement.Start);
        Assert.Equal(new Vec3i(4, 3, 4), placement.Size);
        Assert.True(placement.BlocksPlaced > 0);
        Assert.Contains(ChunkPos.FromBlockPos(origin), placement.Chunks);
        Assert.Empty(placement.MissingBlockCodes);
        Assert.Equal("game:rock-granite", CodeAt(origin));
        Assert.Equal("game:chest-east", CodeAt(origin.AddCopy(1, 1, 1)));
        Assert.Equal("game:torch-basic-lit-up", CodeAt(origin.AddCopy(3, 1, 3)));
        Assert.Equal(["3 game:stick", "2 game:rock-granite"], ChestContents(origin.AddCopy(1, 1, 1)));
    }

    [ServerScenario]
    public async Task PlaceSchematic_ReplaceAll_ClearsTheBox_AndReplaceOnlyAir_KeepsWhatIsThere()
    {
        BlockPos first = Spawn().AddCopy(6, 0, 6);
        int clay = this.GetBlockId("game:rawclay-blue-none");
        this.SetBlock(first.AddCopy(2, 1, 2), clay);
        await Host!.PlaceSchematicAsync(Room, first);
        Assert.Equal("game:air", CodeAt(first.AddCopy(2, 1, 2)));

        BlockPos second = Spawn().AddCopy(-12, 0, 6);
        this.SetBlock(second, clay);
        await Host.PlaceSchematicAsync(Room, second, new SchematicOptions { ReplaceMode = EnumReplaceMode.ReplaceOnlyAir });
        Assert.Equal("game:rawclay-blue-none", CodeAt(second));
        Assert.Equal("game:chest-east", CodeAt(second.AddCopy(1, 1, 1)));
    }

    [ServerScenario]
    public async Task PlaceSchematic_Rotated_TurnsTheBlocks_AndKeepsTheChestContents()
    {
        BlockPos origin = Spawn().AddCopy(6, 0, -12);
        SchematicPlacement placement = await Host!.PlaceSchematicAsync(Room, origin, new SchematicOptions { Angle = 90 });

        // A quarter turn takes (x, z) to (-z, x), moved back into the box: the chest at (1, 1, 1)
        // goes to (2, 1, 1), the torch at (3, 1, 3) to (0, 1, 3).
        Assert.Equal(origin, placement.Start);
        Assert.Equal(new Vec3i(4, 3, 4), placement.Size);
        Assert.Equal("game:torch-basic-lit-up", CodeAt(origin.AddCopy(0, 1, 3)));
        Assert.StartsWith("game:chest-", CodeAt(origin.AddCopy(2, 1, 1)));
        Assert.Equal("game:air", CodeAt(origin.AddCopy(1, 1, 1)));
        Assert.Equal(["3 game:stick", "2 game:rock-granite"], ChestContents(origin.AddCopy(2, 1, 1)));
    }

    [ServerScenario]
    public async Task PlaceSchematic_WithAnUnknownBlock_Fails_OrLeavesItOutWhenAllowed()
    {
        string unknown = Path.Combine(Path.GetTempPath(), "pharos-unknown-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        File.WriteAllText(unknown, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, Room)).Replace("game:torch-basic-lit-up", "game:pharos-no-such-block"));
        try
        {
            BlockPos origin = Spawn().AddCopy(-12, 0, -12);
            InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Host!.PlaceSchematicAsync(unknown, origin));
            Assert.Contains("game:pharos-no-such-block", error.Message);

            SchematicPlacement placement = await Host!.PlaceSchematicAsync(unknown, origin, new SchematicOptions { AllowMissingBlocks = true });
            Assert.Equal("game:pharos-no-such-block", Assert.Single(placement.MissingBlockCodes).ToString());
            Assert.Equal("game:chest-east", CodeAt(origin.AddCopy(1, 1, 1)));
            Assert.Equal("game:air", CodeAt(origin.AddCopy(3, 1, 3)));
        }
        finally
        {
            File.Delete(unknown);
        }
    }

    [ServerScenario]
    public async Task EnsureChunksLoaded_LoadsAFarBox_AndRefusesOneOutsideTheWorld()
    {
        BlockPos far = Spawn().AddCopy(-400, 0, 400);
        IReadOnlyList<ChunkPos> chunks = await Host!.EnsureChunksLoadedAsync(far, far.AddCopy(40, 0, 0));

        Assert.Equal(2, chunks.Count);
        Assert.All(chunks, c => Assert.NotNull(Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetChunk(c.X, c.Y, c.Z))));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Host.EnsureChunksLoadedAsync(far, far.AddCopy(0, 100000, 0)));
    }

    [ServerScenario]
    public async Task PlaceSchematic_RejectsMissingFilesAndBadAngles()
    {
        await Assert.ThrowsAsync<FileNotFoundException>(() => Host!.PlaceSchematicAsync("Fixtures/schematics/no-such.json", Spawn()));
        await Assert.ThrowsAsync<ArgumentException>(() => Host!.PlaceSchematicAsync(Room, Spawn(), new SchematicOptions { Angle = 45 }));
    }

    private BlockPos Spawn() => Host!.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos);

    private string CodeAt(BlockPos pos) => Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(pos).Code.ToString());

    private string[] ChestContents(BlockPos pos) => Host!.RunOnGameThread(() =>
    {
        IBlockEntityContainer chest = Assert.IsAssignableFrom<IBlockEntityContainer>(Api!.World.BlockAccessor.GetBlockEntity(pos));
        return chest.Inventory.Where(slot => !slot.Empty).Select(slot => $"{slot.StackSize} {slot.Itemstack.Collectible.Code}").ToArray();
    });
}
