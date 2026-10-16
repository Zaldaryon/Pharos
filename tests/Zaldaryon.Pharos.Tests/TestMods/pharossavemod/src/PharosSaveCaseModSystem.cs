using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace PharosSaveCaseMod;

/// <summary>
/// Registers the block entity of <c>pharossavemod:savecase</c>, in three variants: clean, lossy and throwing. It also counts
/// the saves of the world in <c>pharossavemod:saves</c>, once a test has stored that counter.
/// </summary>
public sealed class PharosSaveCaseModSystem : ModSystem
{
    public const string SaveCountKey = "pharossavemod:saves";

    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityClass("PharosSaveCase", typeof(PharosSaveCase));
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        api.Event.GameWorldSave += () =>
        {
            byte[]? counter = api.WorldManager.SaveGame.GetData(SaveCountKey);
            if (counter != null)
            {
                api.WorldManager.SaveGame.StoreData(SaveCountKey, BitConverter.GetBytes(BitConverter.ToInt32(counter) + 1));
            }
        };
    }
}

/// <summary>
/// The count is kept by the clean variant. The lossy variant writes it but never reads it back, so a reload resets it. The
/// throwing variant fails to serialize a negative count, which the game logs when it saves the chunk.
/// </summary>
public class PharosSaveCase : BlockEntity
{
    public int Count;

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        if (Block.Variant["kind"] == "throwing" && Count < 0)
        {
            throw new InvalidOperationException("The count is negative: the save case cannot be serialized.");
        }

        tree.SetInt("count", Count);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolve)
    {
        base.FromTreeAttributes(tree, worldForResolve);
        if (Block.Variant["kind"] != "lossy")
        {
            Count = tree.GetInt("count");
        }
    }
}
