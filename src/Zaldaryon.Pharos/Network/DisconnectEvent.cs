namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Records a disconnect event with metadata for testing and diagnostics.
/// </summary>
/// <param name="Reason">The reason for disconnection.</param>
/// <param name="TimestampMs">Milliseconds since simulation started when this disconnect occurred.</param>
/// <param name="ReconnectAttempts">Number of reconnect attempts made after this disconnect.</param>
/// <param name="Message">Optional message describing the disconnect (e.g., kick reason).</param>
public sealed record DisconnectEvent(
    DisconnectReason Reason,
    long TimestampMs,
    int ReconnectAttempts = 0,
    string? Message = null)
{
    /// <summary>
    /// Whether this disconnect was due to a simulated condition.
    /// </summary>
    public bool IsSimulated => Reason == DisconnectReason.SimulatedCrash;

    /// <summary>
    /// Whether reconnection should be attempted for this type of disconnect.
    /// </summary>
    public bool ShouldAttemptReconnect => Reason != DisconnectReason.Kicked && Reason != DisconnectReason.ServerShutdown;

    /// <summary>
    /// Creates a copy of this event with an incremented reconnect attempt count.
    /// </summary>
    public DisconnectEvent WithReconnectAttempt() =>
        this with { ReconnectAttempts = ReconnectAttempts + 1 };
}
