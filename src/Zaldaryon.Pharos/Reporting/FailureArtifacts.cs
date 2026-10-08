namespace Zaldaryon.Pharos.Reporting;

/// <summary>
/// What a scenario saves when it fails or times out. See <c>docs/failure-artifacts.md</c>.
/// </summary>
[Flags]
public enum FailureArtifacts
{
    /// <summary>Nothing is saved.</summary>
    None = 0,

    /// <summary><c>screenshot.png</c>: the last frame the client rendered.</summary>
    Screenshot = 1,

    /// <summary>
    /// <c>client.log</c> and <c>server.log</c>, what the client and the server logged during the
    /// test, and a copy of the game's own log files.
    /// </summary>
    Logs = 2,

    /// <summary>
    /// <c>packets.json</c>: the client-server traffic of the test. The packet recorder is started
    /// when the test body starts, which costs memory for every packet; off by default.
    /// </summary>
    Packets = 4,

    /// <summary><c>run.json</c>: the test, the outcome, the game and Pharos versions, the world and the run.</summary>
    RunInfo = 8,

    /// <summary>What a scenario saves unless it says otherwise: everything but the packets.</summary>
    Default = Screenshot | Logs | RunInfo,

    /// <summary>Everything.</summary>
    All = Screenshot | Logs | Packets | RunInfo,
}
