using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Coordinates lockstep execution and networking between a headless client and an in-process server.
/// </summary>
/// <remarks>
/// Supports both the legacy <see cref="AtlasServerHost"/> and the native <see cref="EmbeddedServerHost"/>.
/// When using <see cref="EmbeddedServerHost"/>, the session uses the native loopback binding with
/// correct handshake ordering (Packet 33 before Packet 1).
/// </remarks>
public sealed class ClientServerLoopbackSession : IDisposable
{
    private bool _disposed;
    private bool _isDisconnected;

    /// <summary>Gets the headless client in this loopback session.</summary>
    public HeadlessClient Client { get; }

    /// <summary>Gets the legacy Atlas server host, or null if using native server.</summary>
    [Obsolete("Use NativeServer instead. AtlasServerHost support will be removed in a future version.")]
    public AtlasServerHost? Server { get; }

    /// <summary>Gets the native embedded server host, or null if using legacy Atlas server.</summary>
    public EmbeddedServerHost? NativeServer { get; }

    /// <summary>Gets the client's test player interface.</summary>
    public IClientTestPlayer Player => Client.TestPlayer;

    /// <summary>Gets whether the client has connected to the server.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>Gets whether this session uses the native EmbeddedServerHost.</summary>
    public bool IsNativeSession => NativeServer != null;

    /// <summary>
    /// Creates a loopback session with a legacy Atlas server host.
    /// </summary>
    [Obsolete("Use the EmbeddedServerHost constructor instead.")]
    internal ClientServerLoopbackSession(HeadlessClient client, AtlasServerHost server)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
#pragma warning disable CS0618 // Suppress obsolete warning for internal use
        Server = server ?? throw new ArgumentNullException(nameof(server));
#pragma warning restore CS0618
        NativeServer = null;
    }

    /// <summary>
    /// Creates a loopback session with a native embedded server host.
    /// </summary>
    internal ClientServerLoopbackSession(HeadlessClient client, EmbeddedServerHost server)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        NativeServer = server ?? throw new ArgumentNullException(nameof(server));
#pragma warning disable CS0618 // Suppress obsolete warning for internal use
        Server = null;
#pragma warning restore CS0618
    }

    /// <summary>
    /// Advances the embedded server by one tick and the client by one frame in lockstep.
    /// </summary>
    public void Step(float dt = 1f / 60f)
    {
        if (_disposed) return;

        if (NativeServer != null)
        {
            NativeServer.Tick();
        }
        else
        {
#pragma warning disable CS0618
            Server?.Tick();
#pragma warning restore CS0618
        }

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

        if (NativeServer != null)
        {
            NativeServer.Tick();
        }
        else
        {
#pragma warning disable CS0618
            Server?.Tick();
#pragma warning restore CS0618
        }

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
    /// Advances server and client in lockstep until the specified chunk radius around the player is loaded and meshed.
    /// </summary>
    public async Task<bool> WaitForWorldReadyAsync(int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        List<ChunkPos> unmeshed = new();
        for (int frame = 0; frame < maxFrames; frame++)
        {
            if (Client.FrameController.IsWorldReady(radius, out unmeshed))
            {
                return true;
            }

            await StepAsync(dt, ct).ConfigureAwait(false);
        }

        if (Client.FrameController.IsWorldReady(radius, out unmeshed))
        {
            return true;
        }

        Vec3d pos = Player.Position;
        int cx = (int)Math.Floor(pos.X / 32.0);
        int cy = (int)Math.Floor(pos.Y / 32.0);
        int cz = (int)Math.Floor(pos.Z / 32.0);

        string unmeshedSummary = unmeshed.Count <= 10
            ? string.Join(", ", unmeshed)
            : $"{string.Join(", ", unmeshed.Take(10))}... ({unmeshed.Count} total)";

        throw new TimeoutException(
            $"World was not ready in loopback session within {maxFrames} frames (dt={dt:F4}s). " +
            $"Player chunk: ({cx}, {cy}, {cz}), radius: {radius}. " +
            $"Unmeshed chunks remaining ({unmeshed.Count}): [{unmeshedSummary}]. " +
            $"Queues: awaitingTesselation={Vintagestory.Client.RuntimeStats.chunksAwaitingTesselation}, awaitingPooling={Vintagestory.Client.RuntimeStats.chunksAwaitingPooling}, " +
            $"dirtyPriority={Client.FrameController.DirtyChunksPriorityCount}, dirtyNormal={Client.FrameController.DirtyChunksCount}.");
    }

    public Task<bool> WaitForWorldReady(int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
        => WaitForWorldReadyAsync(radius, maxFrames, dt, ct);

    public async Task<bool> WaitForWorldReadyAsync(ChunkPos center, int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        List<ChunkPos> unmeshed = new();
        for (int frame = 0; frame < maxFrames; frame++)
        {
            if (Client.FrameController.IsWorldReady(radius, out unmeshed, center))
            {
                return true;
            }

            await StepAsync(dt, ct).ConfigureAwait(false);
        }

        if (Client.FrameController.IsWorldReady(radius, out unmeshed, center))
        {
            return true;
        }

        string unmeshedSummary = unmeshed.Count <= 10
            ? string.Join(", ", unmeshed)
            : $"{string.Join(", ", unmeshed.Take(10))}... ({unmeshed.Count} total)";

        throw new TimeoutException(
            $"World was not ready in loopback session within {maxFrames} frames (dt={dt:F4}s). " +
            $"Center chunk: {center}, radius: {radius}. " +
            $"Unmeshed chunks remaining ({unmeshed.Count}): [{unmeshedSummary}]. " +
            $"Queues: awaitingTesselation={Vintagestory.Client.RuntimeStats.chunksAwaitingTesselation}, awaitingPooling={Vintagestory.Client.RuntimeStats.chunksAwaitingPooling}, " +
            $"dirtyPriority={Client.FrameController.DirtyChunksPriorityCount}, dirtyNormal={Client.FrameController.DirtyChunksCount}.");
    }

    public Task<bool> WaitForWorldReady(ChunkPos center, int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
        => WaitForWorldReadyAsync(center, radius, maxFrames, dt, ct);

    public Task<bool> WaitForWorldReadyAsync(BlockPos center, int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(center);
        return WaitForWorldReadyAsync(ChunkPos.FromBlockPos(center), radius, maxFrames, dt, ct);
    }

    public Task<bool> WaitForWorldReady(BlockPos center, int radius = 1, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
        => WaitForWorldReadyAsync(center, radius, maxFrames, dt, ct);

    public async Task<bool> WaitForChunkMeshedAsync(ChunkPos chunkPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            if (Client.FrameController.IsChunkMeshed(chunkPos))
            {
                return true;
            }

            await StepAsync(dt, ct).ConfigureAwait(false);
        }

        if (Client.FrameController.IsChunkMeshed(chunkPos))
        {
            return true;
        }

        var chunk = Client.Client.WorldMap?.GetChunk(chunkPos.X, chunkPos.Y, chunkPos.Z);
        string status = chunk == null ? "Not loaded / null" : chunk.Empty ? "Empty" : "Loaded, pending tessellation";

        throw new TimeoutException(
            $"Chunk at {chunkPos} was not meshed in loopback session within {maxFrames} frames (dt={dt:F4}s). " +
            $"Chunk status: {status}. " +
            $"Queues: awaitingTesselation={Vintagestory.Client.RuntimeStats.chunksAwaitingTesselation}, awaitingPooling={Vintagestory.Client.RuntimeStats.chunksAwaitingPooling}, " +
            $"dirtyPriority={Client.FrameController.DirtyChunksPriorityCount}, dirtyNormal={Client.FrameController.DirtyChunksCount}.");
    }

    public Task<bool> WaitForChunkMeshed(ChunkPos chunkPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
        => WaitForChunkMeshedAsync(chunkPos, maxFrames, dt, ct);

    public Task<bool> WaitForChunkMeshedAsync(int chunkX, int chunkY, int chunkZ, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
        => WaitForChunkMeshedAsync(new ChunkPos(chunkX, chunkY, chunkZ), maxFrames, dt, ct);

    public Task<bool> WaitForChunkMeshed(int chunkX, int chunkY, int chunkZ, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
        => WaitForChunkMeshedAsync(new ChunkPos(chunkX, chunkY, chunkZ), maxFrames, dt, ct);

    public Task<bool> WaitForChunkMeshedAsync(BlockPos blockPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
        => WaitForChunkMeshedAsync(ChunkPos.FromBlockPos(blockPos), maxFrames, dt, ct);

    public Task<bool> WaitForChunkMeshed(BlockPos blockPos, int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
        => WaitForChunkMeshedAsync(ChunkPos.FromBlockPos(blockPos), maxFrames, dt, ct);

    public async Task<bool> WaitForAllMeshesReadyAsync(int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
    {
        for (int frame = 0; frame < maxFrames; frame++)
        {
            if (Client.FrameController.AreAllMeshesReady())
            {
                return true;
            }

            await StepAsync(dt, ct).ConfigureAwait(false);
        }

        if (Client.FrameController.AreAllMeshesReady())
        {
            return true;
        }

        throw new TimeoutException(
            $"Background mesh tessellation queue was not drained in loopback session within {maxFrames} frames (dt={dt:F4}s). " +
            $"Queues remaining: awaitingTesselation={Vintagestory.Client.RuntimeStats.chunksAwaitingTesselation}, awaitingPooling={Vintagestory.Client.RuntimeStats.chunksAwaitingPooling}, " +
            $"dirtyPriority={Client.FrameController.DirtyChunksPriorityCount}, dirtyNormal={Client.FrameController.DirtyChunksCount}, dirtyLast={Client.FrameController.DirtyChunksLastCount}, " +
            $"tessPriority={Client.FrameController.TesselatedChunksPriorityCount}, tessNormal={Client.FrameController.TesselatedChunksCount}.");
    }

    public Task<bool> WaitForAllMeshesReady(int maxFrames = 600, float dt = 1f / 60f, CancellationToken ct = default)
        => WaitForAllMeshesReadyAsync(maxFrames, dt, ct);

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
                if (NativeServer != null)
                {
                    NativeServer.Tick();
                }
                else
                {
#pragma warning disable CS0618
                    Server?.Tick();
#pragma warning restore CS0618
                }
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
