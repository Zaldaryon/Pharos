namespace Zaldaryon.Pharos.World;

/// <summary>
/// Immutable result of a block interaction operation.
/// Contains success status, block details, drops, and error information.
/// </summary>
/// <param name="Success">True if the interaction completed successfully.</param>
/// <param name="BlockCode">The block code at the target position, or null if not applicable.</param>
/// <param name="Position">The block position that was interacted with.</param>
/// <param name="DroppedItemCode">Item code dropped as a result of the interaction, or null if none.</param>
/// <param name="ErrorMessage">Error description if the interaction failed, or null if successful.</param>
public sealed record BlockInteractionResult(
    bool Success,
    string? BlockCode,
    SimBlockPos Position,
    string? DroppedItemCode,
    string? ErrorMessage)
{
    /// <summary>
    /// Creates a successful interaction result.
    /// </summary>
    public static BlockInteractionResult Succeeded(SimBlockPos position, string? blockCode, string? droppedItemCode = null) =>
        new(true, blockCode, position, droppedItemCode, null);

    /// <summary>
    /// Creates a failed interaction result with an error message.
    /// </summary>
    public static BlockInteractionResult Failed(SimBlockPos position, string? blockCode, string errorMessage) =>
        new(false, blockCode, position, null, errorMessage);
}

/// <summary>
/// Represents a 3D block position in the simulated world.
/// Named SimBlockPos to avoid conflict with Vintagestory.API.MathTools.BlockPos.
/// </summary>
/// <param name="X">Block X coordinate.</param>
/// <param name="Y">Block Y coordinate.</param>
/// <param name="Z">Block Z coordinate.</param>
public readonly record struct SimBlockPos(int X, int Y, int Z)
{
    /// <summary>
    /// Creates a block position at the origin.
    /// </summary>
    public static readonly SimBlockPos Origin = new(0, 0, 0);

    /// <summary>
    /// Returns a position offset by the given amounts.
    /// </summary>
    public SimBlockPos Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    /// <summary>
    /// Returns the position above this one.
    /// </summary>
    public SimBlockPos Up() => Offset(0, 1, 0);

    /// <summary>
    /// Returns the position below this one.
    /// </summary>
    public SimBlockPos Down() => Offset(0, -1, 0);

    /// <summary>
    /// Returns the position to the north (negative Z).
    /// </summary>
    public SimBlockPos North() => Offset(0, 0, -1);

    /// <summary>
    /// Returns the position to the south (positive Z).
    /// </summary>
    public SimBlockPos South() => Offset(0, 0, 1);

    /// <summary>
    /// Returns the position to the east (positive X).
    /// </summary>
    public SimBlockPos East() => Offset(1, 0, 0);

    /// <summary>
    /// Returns the position to the west (negative X).
    /// </summary>
    public SimBlockPos West() => Offset(-1, 0, 0);

    public override string ToString() => $"({X}, {Y}, {Z})";
}
