namespace Zaldaryon.Pharos.Bridge;

/// <summary>
/// Interface for the bridge communication channel that publishes game events.
/// </summary>
public interface IBridgeChannel
{
    /// <summary>Publishes a frame start event with the given delta time.</summary>
    /// <param name="dt">Delta time in seconds since last frame.</param>
    void PublishFrameStart(double dt);

    /// <summary>Publishes a frame end event with the given delta time.</summary>
    /// <param name="dt">Delta time in seconds since last frame.</param>
    void PublishFrameEnd(double dt);

    /// <summary>Publishes a chunk tessellated event.</summary>
    /// <param name="chunkX">Chunk X coordinate.</param>
    /// <param name="chunkY">Chunk Y coordinate.</param>
    /// <param name="chunkZ">Chunk Z coordinate.</param>
    void PublishChunkTessellated(int chunkX, int chunkY, int chunkZ);

    /// <summary>Publishes a GUI state changed event.</summary>
    /// <param name="screenName">Name of the dialog or screen, such as <c>GuiDialogInventory</c>.</param>
    /// <param name="isOpen">Whether it opened (true) or closed (false).</param>
    void PublishGuiStateChanged(string screenName, bool isOpen = true);
}
