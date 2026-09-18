namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Reasons for client disconnection from server.
/// </summary>
public enum DisconnectReason
{
    /// <summary>
    /// Connection timed out due to no response from server.
    /// </summary>
    Timeout,

    /// <summary>
    /// Server shut down gracefully.
    /// </summary>
    ServerShutdown,

    /// <summary>
    /// Player was kicked by server administrator.
    /// </summary>
    Kicked,

    /// <summary>
    /// Generic network error (packet loss, connection reset, etc.).
    /// </summary>
    NetworkError,

    /// <summary>
    /// Simulated server crash for testing purposes.
    /// </summary>
    SimulatedCrash
}
