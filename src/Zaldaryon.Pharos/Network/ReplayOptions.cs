namespace Zaldaryon.Pharos.Network;

/// <summary>What a traffic replay does with a recording.</summary>
public sealed record ReplayOptions
{
    /// <summary>Skips the packets of the opening handshake, which the replayed connection has already done.</summary>
    public bool SkipHandshake { get; init; } = true;

    /// <summary>The packet ids to skip, instead of the default set for the direction.</summary>
    public IReadOnlySet<int>? SkipPacketIds { get; init; }

    /// <summary>Puts the replayed player at the first recorded position before the replay starts.</summary>
    public bool TeleportToFirstPosition { get; init; } = true;

    /// <summary>Gives each replayed position the player's current position version, so the server does not drop it as stale.</summary>
    public bool RewritePositionVersion { get; init; } = true;

    /// <summary>How many game ticks run after each recorded frame.</summary>
    public int TicksPerFrame { get; init; } = 1;
}

/// <summary>What a traffic replay delivered, skipped, and discarded.</summary>
/// <param name="Delivered">The packets that were sent or delivered.</param>
/// <param name="Skipped">The packets the options skipped, or that were not deliverable.</param>
/// <param name="Frames">The frames the replay stepped through.</param>
/// <param name="LiveDiscarded">The live packets the replay discarded while it held the link.</param>
/// <param name="SkippedPackets">The skipped packets, in recording order.</param>
public sealed record TrafficReplayResult(int Delivered, int Skipped, int Frames, int LiveDiscarded, IReadOnlyList<RecordedPacket> SkippedPackets);
