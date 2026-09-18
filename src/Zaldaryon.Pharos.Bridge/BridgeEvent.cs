namespace Zaldaryon.Pharos.Bridge;

/// <summary>
/// An event published by the bridge channel for consumption by scenario code.
/// </summary>
public sealed record BridgeEvent
{
    /// <summary>The kind of event.</summary>
    public BridgeEventKind Kind { get; init; }

    /// <summary>Delta time in seconds (for frame events).</summary>
    public double Dt { get; init; }

    /// <summary>Chunk X coordinate (for ChunkTessellated events).</summary>
    public int ChunkX { get; init; }

    /// <summary>Chunk Y coordinate (for ChunkTessellated events).</summary>
    public int ChunkY { get; init; }

    /// <summary>Chunk Z coordinate (for ChunkTessellated events).</summary>
    public int ChunkZ { get; init; }

    /// <summary>GUI screen name (for GuiStateChanged events).</summary>
    public string? ScreenName { get; init; }

    /// <summary>Creates a FrameStart event.</summary>
    public static BridgeEvent FrameStart(double dt) => new()
    {
        Kind = BridgeEventKind.FrameStart,
        Dt = dt
    };

    /// <summary>Creates a FrameEnd event.</summary>
    public static BridgeEvent FrameEnd(double dt) => new()
    {
        Kind = BridgeEventKind.FrameEnd,
        Dt = dt
    };

    /// <summary>Creates a ChunkTessellated event.</summary>
    public static BridgeEvent ChunkTessellated(int chunkX, int chunkY, int chunkZ) => new()
    {
        Kind = BridgeEventKind.ChunkTessellated,
        ChunkX = chunkX,
        ChunkY = chunkY,
        ChunkZ = chunkZ
    };

    /// <summary>Creates a GuiStateChanged event.</summary>
    public static BridgeEvent GuiStateChanged(string screenName) => new()
    {
        Kind = BridgeEventKind.GuiStateChanged,
        ScreenName = screenName
    };
}
