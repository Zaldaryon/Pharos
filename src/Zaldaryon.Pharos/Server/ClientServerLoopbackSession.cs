using System;
using System.Threading;
using System.Threading.Tasks;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Coordinates lockstep execution and networking between a headless client and an in-process Atlas server.
/// </summary>
public sealed class ClientServerLoopbackSession : IDisposable
{
    private bool _disposed;
    private bool _isDisconnected;

    public HeadlessClient Client { get; }
    public AtlasServerHost Server { get; }
    public bool IsConnected { get; private set; }

    internal ClientServerLoopbackSession(HeadlessClient client, AtlasServerHost server)
    {
        Client = client;
        Server = server;
    }

    /// <summary>
    /// Advances the embedded server by one tick and the client by one frame in lockstep.
    /// </summary>
    public void Step(float dt = 1f / 60f)
    {
        if (_disposed) return;
        Server.Tick();
        Client.FrameController.Step(dt);

        if (Client.Client.player != null && Client.Client.World != null)
        {
            IsConnected = true;
        }
    }

    /// <summary>
    /// Advances the server and client by N frames in lockstep.
    /// </summary>
    public void StepFrames(int count, float dt = 1f / 60f)
    {
        if (_disposed) return;
        for (int i = 0; i < count; i++)
        {
            Step(dt);
        }
    }

    /// <summary>
    /// Asynchronously advances the server by one tick and the client by one frame in lockstep.
    /// </summary>
    public async Task StepAsync(float dt = 1f / 60f, CancellationToken ct = default)
    {
        if (_disposed) return;
        Server.Tick();
        await Client.Frame(dt, ct).ConfigureAwait(false);

        if (Client.Client.player != null && Client.Client.World != null)
        {
            IsConnected = true;
        }
    }

    /// <summary>
    /// Asynchronously advances the server and client by N frames in lockstep.
    /// </summary>
    public async Task StepFramesAsync(int count, float dt = 1f / 60f, CancellationToken ct = default)
    {
        if (_disposed) return;
        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await StepAsync(dt, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Drives server ticks and client frames in lockstep until the player has successfully connected and spawned into the world.
    /// </summary>
    public bool WaitForPlayerJoined(TimeSpan timeout, float dt = 1f / 60f)
    {
        DateTime start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            Step(dt);

            if (Client.Client.player != null && Client.Client.World != null)
            {
                IsConnected = true;
                return true;
            }

            Thread.Sleep(1);
        }

        return false;
    }

    /// <summary>
    /// Asynchronously drives server ticks and client frames in lockstep until the player has connected and spawned into the world.
    /// </summary>
    public async Task<bool> WaitForPlayerJoinedAsync(TimeSpan timeout, float dt = 1f / 60f, CancellationToken ct = default)
    {
        DateTime start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            ct.ThrowIfCancellationRequested();
            await StepAsync(dt, ct).ConfigureAwait(false);

            if (Client.Client.player != null && Client.Client.World != null)
            {
                IsConnected = true;
                return true;
            }

            await Task.Delay(1, ct).ConfigureAwait(false);
        }

        return false;
    }

    /// <summary>
    /// Disconnects the client from the server and flushes pending packets through loopback queues.
    /// </summary>
    public void Disconnect()
    {
        if (_isDisconnected) return;
        _isDisconnected = true;

        try
        {
            Client.Client.SendLeave(0);
        }
        catch
        {
            // Ignore send leave errors during teardown
        }

        // Drain pending loopback packets before tearing down the client session
        for (int i = 0; i < 5; i++)
        {
            try
            {
                Server.Tick();
                Client.FrameController.Step(1f / 60f);
            }
            catch
            {
                break;
            }
        }

        try
        {
            Client.Client.DestroyGameSession(gotDisconnected: false, EnumExitMode.SoftExit);
        }
        catch
        {
            // Ignore destroy game session errors during teardown
        }

        IsConnected = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}
