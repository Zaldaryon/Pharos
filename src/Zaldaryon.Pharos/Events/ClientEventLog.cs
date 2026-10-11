using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;
using EntityDespawnedRecord = Zaldaryon.Pharos.Events.EntityDespawned;
using EntityDiedRecord = Zaldaryon.Pharos.Events.EntityDied;
using EntityMountedRecord = Zaldaryon.Pharos.Events.EntityMounted;
using EntitySpawnedRecord = Zaldaryon.Pharos.Events.EntitySpawned;
using EntityUnmountedRecord = Zaldaryon.Pharos.Events.EntityUnmounted;
using MapRegionLoadedRecord = Zaldaryon.Pharos.Events.MapRegionLoaded;

namespace Zaldaryon.Pharos.Events;

/// <summary>
/// The client's game events, recorded from <see cref="ClientEventRecorder.Record"/> until disposed.
/// See <see cref="GameEventLog"/>.
/// </summary>
/// <remarks>
/// The log follows the client across a reconnect: it listens to the new game session from its
/// start, as <see cref="HeadlessClient.SessionNumber"/> goes up.
/// </remarks>
public sealed class ClientEventLog : GameEventLog
{
    private readonly HeadlessClient _client;
    private readonly ClientEventRecorder _recorder;
    private readonly CommonHandlers _common;
    private readonly CommonHandlers.Selection _commonOn;
    private ClientMain? _game;

    internal ClientEventLog(HeadlessClient client, ClientEventRecorder recorder, ClientEvents events)
        : base(EnumAppSide.Client)
    {
        _client = client;
        _recorder = recorder;
        Events = events;
        _common = new CommonHandlers(this);
        _commonOn = new CommonHandlers.Selection(
            Has(ClientEvents.EntitySpawned), Has(ClientEvents.EntityDied), Has(ClientEvents.EntityDespawned),
            Has(ClientEvents.EntityMounted), Has(ClientEvents.EntityUnmounted), Has(ClientEvents.MapRegionLoaded));
    }

    /// <summary>The events the log listens to.</summary>
    public ClientEvents Events { get; }

    /// <summary>The <see cref="EntitySpawnedRecord"/> records.</summary>
    public IReadOnlyList<EntitySpawnedRecord> EntitySpawned => Of<EntitySpawnedRecord>();

    /// <summary>The <see cref="EntityDiedRecord"/> records.</summary>
    public IReadOnlyList<EntityDiedRecord> EntityDied => Of<EntityDiedRecord>();

    /// <summary>The <see cref="EntityDespawnedRecord"/> records.</summary>
    public IReadOnlyList<EntityDespawnedRecord> EntityDespawned => Of<EntityDespawnedRecord>();

    /// <summary>The <see cref="EntityMountedRecord"/> records.</summary>
    public IReadOnlyList<EntityMountedRecord> EntityMounted => Of<EntityMountedRecord>();

    /// <summary>The <see cref="EntityUnmountedRecord"/> records.</summary>
    public IReadOnlyList<EntityUnmountedRecord> EntityUnmounted => Of<EntityUnmountedRecord>();

    /// <summary>The <see cref="MapRegionLoadedRecord"/> records.</summary>
    public IReadOnlyList<MapRegionLoadedRecord> MapRegionLoaded => Of<MapRegionLoadedRecord>();

    /// <summary>
    /// Steps frames until the log holds a <typeparamref name="T"/> after <paramref name="since"/>
    /// that matches <paramref name="predicate"/>, and returns the earliest one. Inside a
    /// client-server session the whole session steps, so the server keeps ticking.
    /// </summary>
    /// <param name="predicate">What the record must match, or null for any.</param>
    /// <param name="since">
    /// Only records with a higher <see cref="GameEventRecord.Index"/> count; take it from
    /// <see cref="GameEventLog.Mark"/>. By default the whole log counts, from when it began
    /// recording. (The mod network driver's waits count from the call by default.)
    /// </param>
    /// <param name="maxFrames">How many frames to wait at most. 0 only looks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TimeoutException">None came within <paramref name="maxFrames"/> frames; the message lists what was recorded.</exception>
    /// <exception cref="ObjectDisposedException">None matched, and the log no longer records.</exception>
    public Task<T> WaitForAsync<T>(System.Func<T, bool>? predicate = null, long? since = null, int maxFrames = 600, CancellationToken ct = default) where T : GameEventRecord =>
        WaitCoreAsync(predicate, since, maxFrames, ct);

    /// <summary>Subscribes the chosen events to <paramref name="game"/>. Runs on the client thread.</summary>
    internal void Subscribe(ClientMain game)
    {
        _common.Subscribe(game.api.Event, _commonOn);
        _game = game;
    }

    /// <summary>
    /// Moves the log from the game session it listens to onto <paramref name="next"/>. Runs on the
    /// client thread, between frames.
    /// </summary>
    internal void MoveTo(ClientMain next)
    {
        if (ReferenceEquals(_game, next)) return;
        UnsubscribeFromGame();
        Subscribe(next);
    }

    internal override void Unsubscribe()
    {
        if (_client.IsDisposed || _game == null) return;
        _client.RunOnClientThread(UnsubscribeFromGame);
    }

    private void UnsubscribeFromGame()
    {
        ClientMain? game = _game;
        _game = null;
        if (game?.api?.Event == null) return;
        try
        {
            _common.Unsubscribe(game.api.Event, _commonOn);
        }
        catch (Exception) when (game.eventManager == null)
        {
            // A game session that has ended has no event manager left to take the handlers out of.
        }
    }

    internal override void Closed() => _recorder.Closed(this);

    internal override bool SourceAlive => !_client.IsDisposed;

    internal override long CurrentStep => _client.FrameController.TotalFrames;

    internal override string StepUnit => "frames";

    internal override Task StepAsync(CancellationToken ct) => _client.StepAsync(ct);

    private bool Has(ClientEvents e) => (Events & e) != 0;
}
