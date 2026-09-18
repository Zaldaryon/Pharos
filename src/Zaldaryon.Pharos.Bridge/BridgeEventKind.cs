namespace Zaldaryon.Pharos.Bridge;

/// <summary>
/// Kinds of events published by the bridge channel.
/// </summary>
public enum BridgeEventKind
{
    /// <summary>Start of a frame render cycle.</summary>
    FrameStart,

    /// <summary>End of a frame render cycle.</summary>
    FrameEnd,

    /// <summary>A chunk was tessellated by the client.</summary>
    ChunkTessellated,

    /// <summary>The GUI screen state changed.</summary>
    GuiStateChanged
}
