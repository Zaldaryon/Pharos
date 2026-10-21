namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The server that is running, for the hooks that run without a driver in hand. A server is a
/// process of its own, so there is only one.
/// </summary>
internal static class RunningServer
{
    /// <summary>The server that is running.</summary>
    internal static EmbeddedServerHost? Host { get; set; }

    /// <summary>The tick the server is at.</summary>
    internal static int TickCount => Host?.TickCount ?? 0;
}
