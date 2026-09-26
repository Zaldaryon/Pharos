using System;
using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.World;

/// <summary>
/// Makes fixture block ids renderable as plain cubes, without a mod loader or a block registry.
/// </summary>
/// <remarks>
/// <para>
/// The headless bootstrap never loads the block registry, so <see cref="GameMain.Blocks"/> is a
/// 10,000 slot <c>BlockList</c> whose indexer lazily fabricates a
/// <see cref="BlockList.getNoBlock"/> for every id. Those placeholders have
/// <c>Code = null</c> and, critically, <c>DrawType = EnumDrawType.Empty</c>.
/// </para>
/// <para>
/// That is fatal to tessellation. <c>ChunkTesselator.TesselateBlock</c> returns immediately when
/// <c>block.DrawType == EnumDrawType.Empty</c>, and <c>ChunkTesselator</c> substitutes air for
/// any null block before that, so every fixture block silently tesselated to nothing and the
/// chunk render pools stayed empty.
/// </para>
/// <para>
/// Vanilla already solves this for code-less blocks: <c>ClientSystemStartup</c> assigns
/// <c>game.FastBlockTextureSubidsByBlockAndFace[k] = new int[7]</c> and
/// <c>block.DrawType = EnumDrawType.Cube</c> for any block whose <c>Code</c> is null. This type
/// applies exactly that, to the block ids a fixture actually references.
/// </para>
/// </remarks>
public static class FixtureBlockRegistry
{
    /// <summary>
    /// Ensures every id in <paramref name="blockIds"/> is a drawable cube block.
    /// Id 0 is air and is left alone.
    /// </summary>
    /// <param name="client">The client whose block list should be populated.</param>
    /// <param name="blockIds">Block ids referenced by the fixture.</param>
    public static void EnsureDrawableBlocks(ClientMain client, IEnumerable<int> blockIds)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(blockIds);

        int maxId = 0;
        foreach (int id in blockIds)
        {
            if (id > maxId) maxId = id;
        }

        EnsureSubidArray(client, Math.Max(maxId, 1));

        // Always ensure block 0 (air) is initialized in BlockList and FastBlockTextureSubidsByBlockAndFace.
        // In headless mode without a block registry, BlockList.blocks is a raw unpopulated array where
        // slot 0 is null. ChunkTesselator and ClientChunkData expect blocksFast[0] to be a valid air block.
        // If slot 0 is null, BuildExtendedChunkData propagates null to all air positions and
        // CalculateVisibleFaces crashes with NullReferenceException when dereferencing block3.SideOpaque.
        Block air = BlockList.getNoBlock(0, client.api);
        air.Code = new AssetLocation("air");
        air.DrawType = EnumDrawType.Empty;
        air.AllSidesOpaque = false;
        client.Blocks[0] = air;
        client.FastBlockTextureSubidsByBlockAndFace[0] = new int[7];

        Cuboidf[] boxes = [Block.DefaultCollisionBox];
        for (int id = 1; id <= maxId; id++)
        {
            Block block = BlockList.getNoBlock(id, client.api);
            block.DrawType = EnumDrawType.Cube;
            block.SelectionBoxes = boxes;
            block.CollisionBoxes = boxes;

            // Assign through the indexer, not the getter. The getter's getOrCreateNoBlock path
            // never grows BlockList.count, and BuildExtendedChunkData does
            //   int count = game.Blocks.Count;
            //   chunkdatasNearby[n].blocksLayer?.ClearPaletteOutsideMaxValue(count);
            // ClearPaletteOutsideMaxValue rewrites every palette id at or above count to 0, which
            // is air. With count stuck at 0 the injected blocks are silently erased before
            // CalculateVisibleFaces ever sees them, which is why the whole padded block array
            // came back as air. The setter grows count to id + 1.
            client.Blocks[id] = block;
            client.FastBlockTextureSubidsByBlockAndFace[id] = new int[7];
        }

        // Fill any remaining null slots in BlocksFast with air so chunk neighbour boundary reads
        // never see null.
        if (client.Blocks is BlockList blockList)
        {
            Block[] fast = blockList.BlocksFast;
            for (int i = 0; i < fast.Length; i++)
            {
                if (fast[i] == null) fast[i] = air;
            }
        }

        if (client.TerrainChunkTesselator is ChunkTesselator tct)
        {
            int count = client.Blocks.Count;
            FieldInfo? ptField = typeof(ChunkTesselator).GetField("isPartiallyTransparent", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (ptField?.GetValue(tct) == null || (ptField.GetValue(tct) as bool[])?.Length < count)
            {
                bool[] pt = new bool[count];
                for (int k = 0; k < count; k++)
                {
                    pt[k] = !client.Blocks[k].AllSidesOpaque;
                }
                ptField?.SetValue(tct, pt);
            }

            FieldInfo? lqField = typeof(ChunkTesselator).GetField("isLiquidBlock", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (lqField?.GetValue(tct) == null || (lqField.GetValue(tct) as bool[])?.Length < count)
            {
                bool[] lq = new bool[count];
                for (int k = 0; k < count; k++)
                {
                    lq[k] = client.Blocks[k].MatterState == EnumMatterState.Liquid;
                }
                lqField?.SetValue(tct, lq);
            }
        }
    }

    private static void EnsureSubidArray(ClientMain client, int maxId)
    {
        // ClientMain declares this as a bare public int[][] and only ClientSystemStartup ever
        // allocates it, so it is null in a headless client.
        if (client.FastBlockTextureSubidsByBlockAndFace is not null
            && client.FastBlockTextureSubidsByBlockAndFace.Length > maxId)
        {
            return;
        }

        int length = Math.Max(maxId + 1, Math.Max(client.Blocks?.Count ?? 0, 1));
        int[][] existing = client.FastBlockTextureSubidsByBlockAndFace ?? [];
        int[][] grown = new int[length][];

        for (int i = 0; i < Math.Min(length, existing.Length); i++)
        {
            grown[i] = existing[i];
        }

        client.FastBlockTextureSubidsByBlockAndFace = grown;
    }
}
