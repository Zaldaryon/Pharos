using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Events;

/// <summary>Closes the event logs a test left open, when its scenario ends.</summary>
internal static class LeftOpenLogs
{
    /// <summary>
    /// Disposes every log still recording on <paramref name="server"/> and <paramref name="client"/>.
    /// The scenario bases call it first in their teardown, before players leave and before the world
    /// is rolled back, so neither is recorded. It never throws.
    /// </summary>
    /// <returns>How many logs were closed.</returns>
    public static int Close(EmbeddedServerHost? server, HeadlessClient? client)
    {
        int closed = 0;
        try
        {
            if (server != null) closed += server.Events.CloseAll();
        }
        catch (Exception)
        {
            // A server that cannot be reached is torn down anyway, and its handlers with it.
        }

        try
        {
            if (client != null) closed += client.Events.CloseAll();
        }
        catch (Exception)
        {
            // The same for a client.
        }

        return closed;
    }
}
