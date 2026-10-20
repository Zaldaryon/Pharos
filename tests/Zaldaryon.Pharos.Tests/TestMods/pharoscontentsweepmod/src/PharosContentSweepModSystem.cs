using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace PharosContentSweepMod;

/// <summary>Registers the block entity and test entity used by content sweep integration tests.</summary>
public sealed class PharosContentSweepModSystem : ModSystem
{
    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockEntityClass("PharosSweepBlockEntity", typeof(PharosSweepBlockEntity));
        api.RegisterBlockClass("PharosSweepThrowingBlock", typeof(PharosSweepThrowingBlock));
        api.RegisterEntity("PharosSweepEntity", typeof(PharosSweepEntity));
    }
}

/// <summary>Fails one neighbour-update call so the sweep report can be tested.</summary>
public sealed class PharosSweepThrowingBlock : Block
{
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        world.Logger.Error("content-sweep-error");
        throw new InvalidOperationException("content-sweep-step-error");
    }
}

/// <summary>A block entity with a tick listener and serializable count.</summary>
public sealed class PharosSweepBlockEntity : BlockEntity
{
    private long _listenerId;

    public int TickCount { get; private set; }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server) _listenerId = RegisterGameTickListener(_ => TickCount++, 50);
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("tickCount", TickCount);
    }

    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolve)
    {
        base.FromTreeAttributes(tree, worldForResolve);
        TickCount = tree.GetInt("tickCount");
    }

    public override void OnBlockRemoved()
    {
        if (_listenerId != 0)
        {
            UnregisterGameTickListener(_listenerId);
            _listenerId = 0;
        }
        base.OnBlockRemoved();
    }
}

/// <summary>A minimal server entity for serialization, damage and despawn checks.</summary>
public sealed class PharosSweepEntity : Entity
{
}
