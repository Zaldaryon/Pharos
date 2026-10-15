namespace Zaldaryon.Pharos.Server;

/// <summary>How a restart ends the running server.</summary>
public enum RestartMode
{
    /// <summary>
    /// The world is saved first, so every change made before the restart is kept. The server stops as the game stops:
    /// players are disconnected, and the save is written once more on the way out.
    /// </summary>
    Graceful = 0,
}

public sealed partial class EmbeddedServerHost
{
    /// <summary>
    /// Stops this server and boots a new one on the same save, in the same sandbox and on the same port.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This host is disposed, and the returned host is the one to use from then on. Players, the virtual clock and the
    /// captured logs do not carry over to the new server: connect the players again, and set the clock again.
    /// </para>
    /// <para>
    /// The new server opens the save at its own location, so it starts from what the save holds. A save given through
    /// <see cref="ServerWorldOptions.SaveFile"/> is not copied again: that copy is the one already in the sandbox.
    /// </para>
    /// </remarks>
    /// <param name="mode">How this server stops. See <see cref="RestartMode"/>.</param>
    /// <param name="ct">Cancellation token, checked before the server stops.</param>
    /// <exception cref="InvalidOperationException">The server is not running, or it was booted with a scratch folder of its own rather than a sandbox.</exception>
    public async Task<EmbeddedServerHost> RestartAsync(RestartMode mode = RestartMode.Graceful, CancellationToken ct = default)
    {
        if (!IsRunning) throw new InvalidOperationException("The server is not running, so there is nothing to restart.");
        if (Sandbox is not { } sandbox)
        {
            throw new InvalidOperationException("Only a server booted in a sandbox can restart on its save: this one has a scratch data folder of its own.");
        }

        ct.ThrowIfCancellationRequested();
        ServerWorldOptions options = Options with { SaveFile = null, ListenPort = Port };

        if (mode == RestartMode.Graceful) await SaveWorldAsync(ct).ConfigureAwait(false);
        await DisposeAsync().ConfigureAwait(false);

        return Boot(sandbox, options);
    }
}
