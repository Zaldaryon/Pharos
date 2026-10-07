using Vintagestory.API.Common;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Network;

/// <summary>
/// An engine-mode client connected over the network to a server this process does not run,
/// such as a dedicated server in a container.
/// </summary>
/// <remarks>
/// The server ticks on its own clock, so stepping a remote session advances only the client.
/// Waits are therefore bounded by frames or wall-clock time rather than by server ticks.
/// </remarks>
public sealed class RemoteServerSession : IDisposable
{
    private bool _disposed;

    internal RemoteServerSession(HeadlessClient client, string host, int port)
    {
        Client = client;
        Host = host;
        Port = port;
    }

    /// <summary>The connected client.</summary>
    public HeadlessClient Client { get; }

    /// <summary>The server's host name or address.</summary>
    public string Host { get; }

    /// <summary>The server's port.</summary>
    public int Port { get; }

    /// <summary>Whether the client has joined: it has its player entity, its blocks, and is playing.</summary>
    public bool IsJoined => Client.IsJoined;

    /// <summary>Why the client was disconnected, or null.</summary>
    public string? DisconnectReason => Client.RunOnClientThread(() => Client.Client.disconnectReason);

    /// <summary>Renders one client frame.</summary>
    public Task StepAsync(float dt = 1f / 60f, CancellationToken ct = default) => _disposed ? Task.CompletedTask : Client.Frame(dt, ct);

    /// <summary>Renders <paramref name="count"/> client frames.</summary>
    public async Task StepFramesAsync(int count, float dt = 1f / 60f, CancellationToken ct = default)
    {
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await StepAsync(dt, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Renders frames until the client has joined, it was disconnected, or <paramref name="timeout"/>
    /// passes. Returns whether it joined.
    /// </summary>
    public async Task<bool> WaitForPlayerJoinedAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        DateTime start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            await StepAsync(ct: ct).ConfigureAwait(false);
            if (IsJoined) return true;
            if (DisconnectReason != null) return false;

            // The server and the auth server answer in real time; give them a moment.
            await Task.Delay(5, ct).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>Leaves the server and ends the client's game session.</summary>
    public void Dispose()
    {
        if (_disposed || Client.IsDisposed) return;
        _disposed = true;

        try
        {
            Client.RunOnClientThread(() =>
            {
                Client.Client.SendLeave(0);
                Client.Client.DestroyGameSession(gotDisconnected: false, EnumExitMode.SoftExit);
            });
        }
        catch
        {
            // Ignore teardown errors; the server drops the connection either way
        }
    }
}
