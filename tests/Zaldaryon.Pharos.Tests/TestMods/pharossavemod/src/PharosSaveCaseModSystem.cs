using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace PharosSaveCaseMod;

/// <summary>Registers the block entity of <c>pharossavemod:savecase</c>, in three variants: clean, lossy and throwing.</summary>
public sealed class PharosSaveCaseModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        base.Start(api);
        api.RegisterBlockEntityClass("PharosSaveCase", typeof(PharosSaveCase));
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
