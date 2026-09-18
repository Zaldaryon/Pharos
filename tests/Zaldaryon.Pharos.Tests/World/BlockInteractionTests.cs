using Xunit;
using Zaldaryon.Pharos.World;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>
/// Pure-logic unit tests for BlockInteractionSimulator.
/// Tests use mock mode - no live client or native libraries required.
/// </summary>
public sealed class BlockInteractionTests : IDisposable
{
    private readonly BlockInteractionSimulator _simulator = new();

    public void Dispose() => _simulator.ResetMockState();

    // -------------------------------------------------------------------------
    // Mock mode tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_StartsInMockMode()
    {
        Assert.True(_simulator.IsMockMode);
    }

    [Fact]
    public void EnableMockMode_SetsFlag()
    {
        _simulator.DisableMockMode();
        _simulator.EnableMockMode();

        Assert.True(_simulator.IsMockMode);
    }

    [Fact]
    public void DisableMockMode_ClearsFlag()
    {
        _simulator.DisableMockMode();

        Assert.False(_simulator.IsMockMode);
    }

    // -------------------------------------------------------------------------
    // Block placement tests
    // -------------------------------------------------------------------------

    [Fact]
    public void PlaceBlock_ValidPosition_ReturnsTrue()
    {
        SimBlockPos pos = new(10, 64, 10);

        bool result = _simulator.PlaceBlock(pos, "game:stone");

        Assert.True(result);
    }

    [Fact]
    public void PlaceBlock_SetsBlockAtPosition()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.PlaceBlock(pos, "game:cobblestone");

        string block = _simulator.GetBlock(pos);

        Assert.Equal("game:cobblestone", block);
    }

    [Fact]
    public void PlaceBlock_NullBlockCode_ReturnsFalse()
    {
        SimBlockPos pos = new(10, 64, 10);

        bool result = _simulator.PlaceBlock(pos, null!);

        Assert.False(result);
    }

    [Fact]
    public void PlaceBlock_EmptyBlockCode_ReturnsFalse()
    {
        SimBlockPos pos = new(10, 64, 10);

        bool result = _simulator.PlaceBlock(pos, "");

        Assert.False(result);
    }

    [Fact]
    public void PlaceBlock_OccupiedPosition_ReturnsFalse()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.PlaceBlock(pos, "game:stone");

        bool result = _simulator.PlaceBlock(pos, "game:dirt");

        Assert.False(result);
    }

    [Fact]
    public void PlaceBlock_ReplacesAir()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.SetMockBlock(pos, "air");

        bool result = _simulator.PlaceBlock(pos, "game:stone");

        Assert.True(result);
        Assert.Equal("game:stone", _simulator.GetBlock(pos));
    }

    [Fact]
    public void PlaceBlock_ReplacesLiquid()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.SetMockBlock(pos, "game:water");

        bool result = _simulator.PlaceBlock(pos, "game:stone");

        Assert.True(result);
        Assert.Equal("game:stone", _simulator.GetBlock(pos));
    }

    // -------------------------------------------------------------------------
    // Block breaking tests
    // -------------------------------------------------------------------------

    [Fact]
    public void BreakBlock_ExistingBlock_ReturnsSuccess()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.SetMockBlock(pos, "game:stone");

        BlockInteractionResult result = _simulator.BreakBlock(pos);

        Assert.True(result.Success);
        Assert.Equal("game:stone", result.BlockCode);
        Assert.Equal(pos, result.Position);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void BreakBlock_RemovesBlock()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.SetMockBlock(pos, "game:stone");

        _simulator.BreakBlock(pos);

        Assert.True(_simulator.IsEmpty(pos));
    }

    [Fact]
    public void BreakBlock_EmptyPosition_ReturnsFailed()
    {
        SimBlockPos pos = new(10, 64, 10);

        BlockInteractionResult result = _simulator.BreakBlock(pos);

        Assert.False(result.Success);
        Assert.Equal("No block at position", result.ErrorMessage);
    }

    [Fact]
    public void BreakBlock_AirBlock_ReturnsFailed()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.SetMockBlock(pos, "air");

        BlockInteractionResult result = _simulator.BreakBlock(pos);

        Assert.False(result.Success);
    }

    [Fact]
    public void BreakBlock_WithRegisteredDrop_ReturnsDroppedItem()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.SetMockBlock(pos, "game:ore-iron");
        _simulator.RegisterBlockDrop("game:ore-iron", "game:nugget-iron");

        BlockInteractionResult result = _simulator.BreakBlock(pos);

        Assert.True(result.Success);
        Assert.Equal("game:nugget-iron", result.DroppedItemCode);
    }

    [Fact]
    public void BreakBlock_LiquidBlock_NoDrop()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.SetMockBlock(pos, "game:water");

        BlockInteractionResult result = _simulator.BreakBlock(pos);

        Assert.True(result.Success);
        Assert.Null(result.DroppedItemCode);
    }

    // -------------------------------------------------------------------------
    // Block activation tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ActivateBlock_EmptyPosition_ReturnsFalse()
    {
        SimBlockPos pos = new(10, 64, 10);

        bool result = _simulator.ActivateBlock(pos);

        Assert.False(result);
    }

    [Fact]
    public void ActivateBlock_NonActivatableBlock_ReturnsFalse()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.SetMockBlock(pos, "game:stone");

        bool result = _simulator.ActivateBlock(pos);

        Assert.False(result);
    }

    [Fact]
    public void ActivateBlock_ActivatableBlock_ReturnsTrue()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.SetMockBlock(pos, "game:chest");
        _simulator.RegisterActivatableBlock("game:chest");

        bool result = _simulator.ActivateBlock(pos);

        Assert.True(result);
    }

    // -------------------------------------------------------------------------
    // GetBlock tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GetBlock_EmptyPosition_ReturnsAir()
    {
        SimBlockPos pos = new(999, 999, 999);

        string block = _simulator.GetBlock(pos);

        Assert.Equal("air", block);
    }

    [Fact]
    public void GetBlock_SetBlock_ReturnsCorrectCode()
    {
        SimBlockPos pos = new(5, 5, 5);
        _simulator.SetMockBlock(pos, "game:granite");

        string block = _simulator.GetBlock(pos);

        Assert.Equal("game:granite", block);
    }

    // -------------------------------------------------------------------------
    // Block type query tests
    // -------------------------------------------------------------------------

    [Fact]
    public void IsLiquid_WaterBlock_ReturnsTrue()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.SetMockBlock(pos, "game:water");

        Assert.True(_simulator.IsLiquid(pos));
    }

    [Fact]
    public void IsLiquid_StoneBlock_ReturnsFalse()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.SetMockBlock(pos, "game:stone");

        Assert.False(_simulator.IsLiquid(pos));
    }

    [Fact]
    public void IsSolid_RegisteredSolidBlock_ReturnsTrue()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.RegisterSolidBlock("game:stone");
        _simulator.SetMockBlock(pos, "game:stone");

        Assert.True(_simulator.IsSolid(pos));
    }

    [Fact]
    public void IsActivatable_RegisteredBlock_ReturnsTrue()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.RegisterActivatableBlock("game:door-oak");
        _simulator.SetMockBlock(pos, "game:door-oak");

        Assert.True(_simulator.IsActivatable(pos));
    }

    [Fact]
    public void IsEmpty_EmptyPosition_ReturnsTrue()
    {
        SimBlockPos pos = new(100, 100, 100);

        Assert.True(_simulator.IsEmpty(pos));
    }

    [Fact]
    public void IsEmpty_OccupiedPosition_ReturnsFalse()
    {
        SimBlockPos pos = new(100, 100, 100);
        _simulator.SetMockBlock(pos, "game:stone");

        Assert.False(_simulator.IsEmpty(pos));
    }

    // -------------------------------------------------------------------------
    // Mock state management tests
    // -------------------------------------------------------------------------

    [Fact]
    public void MockBlockCount_TracksPlacedBlocks()
    {
        _simulator.SetMockBlock(new(0, 0, 0), "game:stone");
        _simulator.SetMockBlock(new(1, 0, 0), "game:dirt");
        _simulator.SetMockBlock(new(2, 0, 0), "game:grass");

        Assert.Equal(3, _simulator.MockBlockCount);
    }

    [Fact]
    public void ClearMockBlocks_RemovesAllBlocks()
    {
        _simulator.SetMockBlock(new(0, 0, 0), "game:stone");
        _simulator.SetMockBlock(new(1, 0, 0), "game:dirt");

        _simulator.ClearMockBlocks();

        Assert.Equal(0, _simulator.MockBlockCount);
    }

    [Fact]
    public void ResetMockState_ClearsAllState()
    {
        _simulator.SetMockBlock(new(0, 0, 0), "game:stone");
        _simulator.RegisterBlockDrop("game:stone", "game:cobblestone");
        _simulator.RegisterSolidBlock("game:stone");
        _simulator.RegisterActivatableBlock("game:door");

        _simulator.ResetMockState();

        Assert.Equal(0, _simulator.MockBlockCount);
    }

    [Fact]
    public void SetMockBlock_Air_RemovesBlock()
    {
        SimBlockPos pos = new(0, 0, 0);
        _simulator.SetMockBlock(pos, "game:stone");

        _simulator.SetMockBlock(pos, "air");

        Assert.True(_simulator.IsEmpty(pos));
    }

    [Fact]
    public void GetAllMockBlockPositions_ReturnsAllPositions()
    {
        _simulator.SetMockBlock(new(0, 0, 0), "game:stone");
        _simulator.SetMockBlock(new(1, 1, 1), "game:dirt");
        _simulator.SetMockBlock(new(2, 2, 2), "game:grass");

        var positions = _simulator.GetAllMockBlockPositions();

        Assert.Equal(3, positions.Count);
    }

    // -------------------------------------------------------------------------
    // SimBlockPos tests
    // -------------------------------------------------------------------------

    [Fact]
    public void SimBlockPos_Offset_ReturnsNewPosition()
    {
        SimBlockPos pos = new(10, 20, 30);

        SimBlockPos offset = pos.Offset(5, -10, 15);

        Assert.Equal(new SimBlockPos(15, 10, 45), offset);
    }

    [Fact]
    public void SimBlockPos_Up_IncreasesY()
    {
        SimBlockPos pos = new(10, 64, 10);

        SimBlockPos up = pos.Up();

        Assert.Equal(new SimBlockPos(10, 65, 10), up);
    }

    [Fact]
    public void SimBlockPos_Down_DecreasesY()
    {
        SimBlockPos pos = new(10, 64, 10);

        SimBlockPos down = pos.Down();

        Assert.Equal(new SimBlockPos(10, 63, 10), down);
    }

    [Fact]
    public void SimBlockPos_CardinalDirections()
    {
        SimBlockPos pos = new(0, 0, 0);

        Assert.Equal(new SimBlockPos(0, 0, -1), pos.North());
        Assert.Equal(new SimBlockPos(0, 0, 1), pos.South());
        Assert.Equal(new SimBlockPos(1, 0, 0), pos.East());
        Assert.Equal(new SimBlockPos(-1, 0, 0), pos.West());
    }

    [Fact]
    public void SimBlockPos_Origin_IsZero()
    {
        Assert.Equal(new SimBlockPos(0, 0, 0), SimBlockPos.Origin);
    }

    // -------------------------------------------------------------------------
    // BlockInteractionResult tests
    // -------------------------------------------------------------------------

    [Fact]
    public void BlockInteractionResult_Succeeded_CreatesSuccessResult()
    {
        SimBlockPos pos = new(10, 64, 10);

        var result = BlockInteractionResult.Succeeded(pos, "game:stone", "game:cobblestone");

        Assert.True(result.Success);
        Assert.Equal("game:stone", result.BlockCode);
        Assert.Equal(pos, result.Position);
        Assert.Equal("game:cobblestone", result.DroppedItemCode);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void BlockInteractionResult_Failed_CreatesFailureResult()
    {
        SimBlockPos pos = new(10, 64, 10);

        var result = BlockInteractionResult.Failed(pos, "game:stone", "Block is unbreakable");

        Assert.False(result.Success);
        Assert.Equal("game:stone", result.BlockCode);
        Assert.Equal(pos, result.Position);
        Assert.Null(result.DroppedItemCode);
        Assert.Equal("Block is unbreakable", result.ErrorMessage);
    }

    // -------------------------------------------------------------------------
    // Block constraint tests
    // -------------------------------------------------------------------------

    [Fact]
    public void PlaceBlock_LiquidInSolid_ReturnsFalse()
    {
        SimBlockPos pos = new(10, 64, 10);
        _simulator.RegisterSolidBlock("game:stone");
        _simulator.SetMockBlock(pos, "game:stone");

        bool result = _simulator.PlaceBlock(pos, "game:water");

        Assert.False(result);
    }
}
