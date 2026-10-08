using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Timing;

using Zaldaryon.Pharos.World;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Coordinates lockstep execution and networking between a headless client and an in-process server.
/// </summary>
/// <remarks>
/// <para>
/// Supports both the legacy <see cref="AtlasServerHost"/> and the native <see cref="EmbeddedServerHost"/>.
/// When using <see cref="EmbeddedServerHost"/>, the session uses the native loopback binding with
/// correct handshake ordering (Packet 33 before Packet 1).
/// </para>
/// <para>
/// The coordinated step pipeline ensures deterministic packet ordering:
/// <list type="number">
/// <item>Flush client outbound network packets</item>
/// <item>Process server network receive queue</item>
/// <item>Advance server simulation ticks (ServerMain.Process())</item>
/// <item>Flush server outbound network packets</item>
/// <item>Process client network receive queue</item>
/// <item>Advance client frame rendering tick</item>
/// </list>
/// </para>
/// </remarks>
public sealed class ClientServerLoopbackSession : IDisposable
{
    private bool _disposed;
    private bool _isDisconnected;
    private int _frameCount;
    private int _serverTickCount;

    /// <summary>Gets the headless client in this loopback session.</summary>
    public HeadlessClient Client { get; }

    /// <summary>Gets the legacy Atlas server host, or null if using native server.</summary>
    [Obsolete("Use NativeServer instead. AtlasServerHost support will be removed in a future version.")]
    public AtlasServerHost? Server { get; }

    /// <summary>Gets the native embedded server host, or null if using legacy Atlas server.</summary>
    public EmbeddedServerHost? NativeServer { get; }

    /// <summary>Gets the client's test player interface.</summary>
    public IClientTestPlayer Player => Client.TestPlayer;

    /// <summary>
    /// Breaks, places and uses blocks through the engine-mode client, the way a player does.
    /// </summary>
    public LiveBlockInteraction Blocks => _blocks ??= new LiveBlockInteraction(this);

    private LiveBlockInteraction? _blocks;

    /// <summary>
    /// Holds <paramref name="action"/> for <paramref name="frames"/> lockstep frames, then releases
    /// it: walk, jump, sneak or sprint as a player holding the key would.
    /// </summary>
    public async Task HoldAsync(PlayerAction action, int frames, CancellationToken ct = default)
    {
        Client.Controls.Press(action);
        try
        {
            await StepFramesAsync(frames, ct: ct).ConfigureAwait(false);
        }
        finally
        {
            Client.Controls.Release(action);
        }
    }

    /// <summary>Gets whether the client has connected to the server.</summary>
    public bool IsConnected { get; private set; }

    /// <summary>
    /// Whether the client has joined. For an engine-mode client this is
    /// <see cref="HeadlessClient.IsJoined"/>. A fixture-mode client never processes network
    /// packets, so this stays false for it unless a player is synthesized.
    /// </summary>
    private void AdvanceLink(float dt)
    {
        if (_link == null || _linkTcp == null || _linkUdp == null) return;
        _link.Advance(dt * 1000.0, _linkTcp.Deliver, _linkUdp.Deliver);
    }

    private void DisconnectForReal(DisconnectReason reason, string message)
    {
        string? playerName = Client.Client.player?.PlayerName;

        switch (reason)
        {
            case DisconnectReason.Kicked:
            case DisconnectReason.ServerShutdown:
                // The server disconnects the player itself: the client receives the server's
                // disconnect packet with the message, as for a kick or a shutdown.
                KickOnServer(playerName, message);
                break;

            default:
                // The connection breaks: nothing passes any more, the client's own socket error
                // handler runs, and the server drops the player as it would after a timeout.
                _link?.Sever(message);
                KickOnServer(playerName, null);
                break;
        }
    }

    private void KickOnServer(string? playerName, string? message)
    {
        if (NativeServer == null || playerName == null || !NativeServer.IsRunning) return;

        NativeServer.RunOnGameThread(() =>
        {
            if (NativeServer.Server.GetClientByPlayername(playerName) is { } client)
            {
                NativeServer.Server.DisconnectPlayer(client, message, message);
            }
        });
    }

    private void MarkJoined()
    {
        if (IsConnected) return;
        IsConnected = true;

        if (Client.IsEngineMode && NativeServer != null)
        {
            RouteClientUdp(NativeServer, Client.Client.player?.PlayerName);
        }
    }

    private static void RouteClientUdp(EmbeddedServerHost server, string? playerName)
    {
        // The in-memory UDP server attributes every UDP packet it reads to one connection object
        // and drops all but connection requests until that object has a player. Singleplayer sets
        // it while sending the server identification; the engine-mode client takes the
        // multiplayer path, so it is set here, which lets the server accept the client's UDP
        // traffic (its own movement, among others).
        if (playerName == null) return;

        server.RunOnGameThread(() =>
        {
            if (server.Server.UdpSockets[0] is Vintagestory.Server.Network.DummyUdpNetServer udp
                && server.Server.GetClientByPlayername(playerName) is { Player: not null } client)
            {
                udp.Client.Player = client.Player;
            }
        });
    }

    private bool HasJoined => Client.IsEngineMode
        ? Client.IsJoined
        : Client.Client.player != null && Client.Client.World != null;

    /// <summary>Gets whether this session uses the native EmbeddedServerHost.</summary>
    public bool IsNativeSession => NativeServer != null;

    /// <summary>Gets the total number of client frames stepped.</summary>
    public int FrameCount => _frameCount;

    /// <summary>Gets the total number of server ticks processed.</summary>
    public int ServerTickCount => _serverTickCount;

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
        client.SessionStepper = _stepper = ct => StepAsync(ct: ct);
    }

    /// <summary>
    /// Creates a loopback session with a native embedded server host.
    /// </summary>
    internal ClientServerLoopbackSession(HeadlessClient client, EmbeddedServerHost server, LoopbackLink link, LinkedTcpNetClient tcp, LinkedUdpNetClient udp)
        : this(client, server)
    {
        _link = link;
        _linkTcp = tcp;
        _linkUdp = udp;
        client.DisconnectSimulator.Sink = DisconnectForReal;
    }

    private readonly LoopbackLink? _link;
    private readonly LinkedTcpNetClient? _linkTcp;
    private readonly LinkedUdpNetClient? _linkUdp;

    /// <summary>
    /// Whether the wire between client and server has been cut by a simulated connection loss.
    /// </summary>
    public bool IsLinkSevered => _link?.IsSevered ?? false;

    internal ClientServerLoopbackSession(HeadlessClient client, EmbeddedServerHost server)
    {
        Client = client ?? throw new ArgumentNullException(nameof(client));
        NativeServer = server ?? throw new ArgumentNullException(nameof(server));
#pragma warning disable CS0618 // Suppress obsolete warning for internal use
        Server = null;
#pragma warning restore CS0618
        client.SessionStepper = _stepper = ct => StepAsync(ct: ct);
    }

    // What the client's drivers step while they wait, so the server ticks too.
    private readonly Func<CancellationToken, Task> _stepper;

    /// <summary>
    /// Advances the server and client in coordinated lockstep.
    /// </summary>
    /// <param name="dt">Delta time for the client frame. Default is 1/60 second.</param>
    /// <param name="serverTicksPerFrame">Number of server ticks per client frame. Default is 1.</param>
    /// <remarks>
    /// <para>
    /// The coordinated step pipeline:
    /// <list type="number">
    /// <item>Flush client outbound network packets (if degradation active, route through simulator)</item>
    /// <item>Process server network receive queue</item>
    /// <item>Advance server simulation ticks (ServerMain.Process())</item>
    /// <item>Flush server outbound network packets</item>
    /// <item>Process client network receive queue</item>
    /// <item>Advance client frame rendering tick</item>
    /// </list>
    /// </para>
    /// <para>
    /// When <paramref name="serverTicksPerFrame"/> is greater than 1, the server processes
    /// multiple simulation ticks before the client advances one frame. This models scenarios
    /// where the server runs at a higher tick rate than the client frame rate.
    /// </para>
    /// </remarks>
    public void Step(float dt = 1f / 60f, int serverTicksPerFrame = 1)
    {
        if (_disposed) return;

        if (serverTicksPerFrame < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(serverTicksPerFrame), serverTicksPerFrame, "Server ticks per frame must be at least 1.");
        }

        // Phase 1 & 2: deliver what the client sent and the link held back (latency, jitter)
        AdvanceLink(dt);

        // Phase 3: Advance server simulation ticks
        for (int tick = 0; tick < serverTicksPerFrame; tick++)
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
            _serverTickCount++;
        }

        // Phase 4 & 5: Server outbound -> Client receive (handled internally by loopback)

        // Phase 6: Advance client frame rendering tick
        Client.FrameController.Step(dt);
        _frameCount++;

        // Update connection state
        if (HasJoined)
        {
            MarkJoined();
        }
    }

    /// <summary>
    /// Advances the server and client by N frames in lockstep.
    /// </summary>
    /// <param name="frameCount">Number of frames to step.</param>
    /// <param name="dt">Delta time per frame. Default is 1/60 second.</param>
    /// <param name="serverTicksPerFrame">Number of server ticks per client frame. Default is 1.</param>
    public void StepFrames(int frameCount, float dt = 1f / 60f, int serverTicksPerFrame = 1)
    {
        if (_disposed) return;

        if (frameCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameCount), frameCount, "Frame count must be non-negative.");
        }

        for (int i = 0; i < frameCount; i++)
        {
            Step(dt, serverTicksPerFrame);
        }
    }

    /// <summary>
    /// Steps until the client's calendar matches the server's: the same time, to the second when
    /// the calendar is frozen and within <paramref name="tolerance"/> while it runs, the same speed
    /// of time and day length (to the precision the calendar packet carries), and the same month
    /// length.
    /// </summary>
    /// <remarks>
    /// Clients take the time from the server's calendar packets and count on by themselves in
    /// between, in whole seconds. Each side's time is read as it would be if its calendar ticked
    /// now, so a running calendar compares fairly between frames.
    /// </remarks>
    /// <param name="tolerance">How far apart a running calendar may be. Defaults to 60 game seconds, two real seconds.</param>
    /// <param name="maxFrames">How many frames to wait.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="NotSupportedException">The session has no embedded server.</exception>
    /// <exception cref="InvalidOperationException">The client has no calendar, or ignores the server's.</exception>
    /// <exception cref="TimeoutException">The calendars did not match within <paramref name="maxFrames"/>.</exception>
    public async Task WaitForCalendarSyncAsync(TimeSpan? tolerance = null, int maxFrames = 600, CancellationToken ct = default)
    {
        EmbeddedServerHost server = NativeServer ?? throw new NotSupportedException("Calendar sync needs a session with an embedded server.");
        Client.RunOnClientThread(() =>
        {
            if (Client.Client.Calendar == null) throw new InvalidOperationException("The client has no calendar yet: it has not joined.");
            if (ClientCalendarInternals.IgnoresServer(Client.Client)) throw new InvalidOperationException("The client ignores the server's calendar.");
        });

        CalendarReading serverNow = default, clientNow = default;
        bool InSync()
        {
            serverNow = server.RunOnGameThread(() => CalendarReading.Of((Vintagestory.Common.GameCalendar)server.Server.Calendar));
            clientNow = Client.RunOnClientThread(() => CalendarReading.Of((Vintagestory.Common.GameCalendar)Client.Client.Calendar));
            double allowed = serverNow.Rate == 0 ? 0 : (tolerance ?? TimeSpan.FromSeconds(60)).TotalSeconds;
            return Math.Abs(serverNow.Seconds - clientNow.Seconds) <= allowed
                // The calendar packet carries these as scaled integers, so the client's copies can
                // fall short of the server's by a little.
                && Math.Abs(serverNow.Rate - clientNow.Rate) <= 1e-4 + 1e-3 * Math.Abs(serverNow.Rate)
                && Math.Abs(serverNow.HoursPerDay - clientNow.HoursPerDay) <= 1e-3
                && serverNow.DaysPerMonth == clientNow.DaysPerMonth;
        }

        if (!await StepUntilAsync(InSync, maxFrames, ct: ct).ConfigureAwait(false))
        {
            throw new TimeoutException(
                $"The client's calendar did not match the server's within {maxFrames} frames: server {serverNow}, client {clientNow}.");
        }
    }

    /// <summary>A calendar's time and speed, read on its own thread.</summary>
    private readonly record struct CalendarReading(double Seconds, double Rate, float HoursPerDay, int DaysPerMonth)
    {
        // A frozen calendar sits on a whole second on both sides; a running one is projected to now.
        public static CalendarReading Of(Vintagestory.Common.GameCalendar calendar)
        {
            double rate = calendar.IsRunning ? calendar.SpeedOfTime * calendar.CalendarSpeedMul : 0;
            double seconds = rate == 0 ? Math.Floor(CalendarDriver.Internals.Timespan(calendar).TotalSeconds) : CalendarDriver.Internals.ProjectedSeconds(calendar);
            return new CalendarReading(seconds, rate, calendar.HoursPerDay, calendar.DaysPerMonth);
        }

        public override string ToString() =>
            $"{TimeSpan.FromSeconds(Seconds).TotalHours:0.###} h at {Rate:0.##} game s per s ({HoursPerDay} h days, {DaysPerMonth}-day months)";
    }

    /// <summary>The client's own calendar flags, read where they are.</summary>
    internal static class ClientCalendarInternals
    {
        [System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = "ignoreServerCalendarUpdates")]
        private static extern ref bool IgnoreField(Vintagestory.Client.NoObf.ClientMain client);

        public static bool IgnoresServer(Vintagestory.Client.NoObf.ClientMain client) => IgnoreField(client);

        public static void FollowServer(Vintagestory.Client.NoObf.ClientMain client) => IgnoreField(client) = false;
    }

    /// <summary>
    /// Asynchronously steps until a condition is satisfied or the maximum frame count is reached.
    /// </summary>
    /// <param name="condition">A function that returns true when the wait condition is satisfied.</param>
    /// <param name="maxFrames">Maximum number of frames to step. Default is 600 (10 seconds at 60 FPS).</param>
    /// <param name="dt">Delta time per frame. Default is 1/60 second.</param>
    /// <param name="serverTicksPerFrame">Number of server ticks per client frame. Default is 1.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the condition was satisfied; false if maxFrames was reached without the condition being met.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="condition"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxFrames"/> is less than or equal to zero.</exception>
    /// <exception cref="OperationCanceledException">The operation was canceled.</exception>
    public Task<bool> StepUntilAsync(Func<bool> condition, int maxFrames = 600, float dt = 1f / 60f, int serverTicksPerFrame = 1, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(condition);

        if (maxFrames <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxFrames), maxFrames, "Maximum frame count must be positive.");
        }

        if (serverTicksPerFrame < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(serverTicksPerFrame), serverTicksPerFrame, "Server ticks per frame must be at least 1.");
        }

        return StepUntilAsyncCore(condition, maxFrames, dt, serverTicksPerFrame, ct);
    }

    private async Task<bool> StepUntilAsyncCore(Func<bool> condition, int maxFrames, float dt, int serverTicksPerFrame, CancellationToken ct)
    {
        for (int i = 0; i < maxFrames; i++)
        {
            ct.ThrowIfCancellationRequested();

            // Check condition before stepping (early exit)
            if (condition())
            {
                return true;
            }

            await StepAsync(dt, serverTicksPerFrame, ct).ConfigureAwait(false);
        }

        // Final check after all frames
        return condition();
    }

    /// <summary>
    /// Asynchronously advances the server and client in coordinated lockstep.
    /// </summary>
    /// <param name="dt">Delta time for the client frame. Default is 1/60 second.</param>
    /// <param name="serverTicksPerFrame">Number of server ticks per client frame. Default is 1.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task StepAsync(float dt = 1f / 60f, int serverTicksPerFrame = 1, CancellationToken ct = default)
    {
        if (_disposed) return;

        ct.ThrowIfCancellationRequested();

        if (serverTicksPerFrame < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(serverTicksPerFrame), serverTicksPerFrame, "Server ticks per frame must be at least 1.");
        }

        AdvanceLink(dt);

        // Server ticks
        for (int tick = 0; tick < serverTicksPerFrame; tick++)
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
            _serverTickCount++;
        }

        // Client frame
        await Client.Frame(dt, ct).ConfigureAwait(false);
        _frameCount++;

        // Update connection state
        if (HasJoined)
        {
            MarkJoined();
        }
    }

    /// <summary>
    /// Asynchronously advances the server and client by N frames in lockstep.
    /// </summary>
    public async Task StepFramesAsync(int count, float dt = 1f / 60f, int serverTicksPerFrame = 1, CancellationToken ct = default)
    {
        if (_disposed) return;

        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Frame count must be non-negative.");
        }

        for (int i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            await StepAsync(dt, serverTicksPerFrame, ct).ConfigureAwait(false);
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

            if (HasJoined)
            {
                MarkJoined();
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
            await StepAsync(dt, ct: ct).ConfigureAwait(false);

            if (HasJoined)
            {
                MarkJoined();
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

            await StepAsync(dt, ct: ct).ConfigureAwait(false);
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

            await StepAsync(dt, ct: ct).ConfigureAwait(false);
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

            await StepAsync(dt, ct: ct).ConfigureAwait(false);
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

            await StepAsync(dt, ct: ct).ConfigureAwait(false);
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
            Client.RunOnClientThread(() => Client.Client.SendLeave(0));
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
            // Session teardown deletes GL objects and joins the game's threads, so it runs on the
            // client's own thread like the rest of the engine.
            Client.RunOnClientThread(() => Client.Client.DestroyGameSession(gotDisconnected: false, EnumExitMode.SoftExit));
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
        if (Client.SessionStepper == _stepper) Client.SessionStepper = null;
    }
}
