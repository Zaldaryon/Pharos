using Vintagestory.API.Common;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>How a restart ends the running server.</summary>
public enum RestartMode
{
    /// <summary>
    /// The world is saved first, so every change made before the restart is kept. The server stops as the game stops:
    /// players are disconnected, and the save is written once more on the way out.
    /// </summary>
    Graceful = 0,

    /// <summary>
    /// The server stops without saving, as a killed process would: the changes since the last save are lost, and what
    /// the save already holds is kept. Chunks that are loaded and changed are not written, so they are lost.
    /// </summary>
    Crash = 1,
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

        if (mode == RestartMode.Graceful)
        {
            await SaveWorldAsync(ct).ConfigureAwait(false);
            await DisposeAsync().ConfigureAwait(false);
        }
        else
        {
            await CrashAsync(ct).ConfigureAwait(false);
        }

        return Boot(sandbox, options);
    }

    /// <summary>
    /// Stops this server without saving, as a killed process would stop. Changes made since the last save are lost. The
    /// server's threads are cancelled, the world database is released, and the server is disposed. Use
    /// <see cref="RestartAsync"/> with <see cref="RestartMode.Crash"/> to boot a new server on the save afterwards.
    /// </summary>
    /// <param name="ct">Cancellation token, checked before the server stops.</param>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    public async Task CrashAsync(CancellationToken ct = default)
    {
        if (!IsRunning) throw new InvalidOperationException("The server is not running, so it cannot crash.");
        ct.ThrowIfCancellationRequested();

        _random?.ReleaseOnDispose();
        _disposed = true;

        try { await Task.Run(KillOnGameThread).ConfigureAwait(false); }
        finally { ReleaseSessionActivity(); }
    }

    private void KillOnGameThread()
    {
        EndVirtualClock();
        if (_clockState != null && ReferenceEquals(ServerClockPatches.Current, _clockState)) ServerClockPatches.Current = null;
        try
        {
            _gameThread.Invoke(KillCore);
        }
        finally
        {
            _gameThread.Dispose();
        }
    }

    private void KillCore()
    {
        ServerMain server = Server;

        // The exit mode first, so the threads that check it stop sending and parsing packets while they are cancelled.
        server.exitState.Mode = EnumExitMode.HardExit;
        server.ServerThreadsCts.Cancel();
        WaitForServerThreadsToShutDown();

        // A cancelled thread does not run its shutdown hooks, so the world database would stay open. Release it as the
        // game's own shutdown hook does, without writing anything: the save keeps what it held before the crash.
        try
        {
            object? chunkThread = s_chunkThread?.GetValue(server);
            if (chunkThread is not null && s_loadSaveGame?.GetValue(chunkThread) is ServerSystem saver)
            {
                saver.OnSeperateThreadShutDown();
            }
        }
        catch (Exception ex)
        {
            ServerMain.Logger?.Warning("Releasing the world database during a crash failed: {0}", ex.Message);
        }

        try
        {
            if (!server.stopped) server.Stop("Pharos crash", EnumExitMode.HardExit);
        }
        catch
        {
            // A crash ignores what the stop reports.
        }

        try
        {
            server.Dispose();
        }
        catch
        {
            // Disposing a server that crashed may fail in its own systems; the save is already released.
        }
    }
}
