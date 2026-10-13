using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Server;
using ActiveSlotChangedRecord = Zaldaryon.Pharos.Events.ActiveSlotChanged;
using BlockBrokenRecord = Zaldaryon.Pharos.Events.BlockBroken;
using BlockPlacedRecord = Zaldaryon.Pharos.Events.BlockPlaced;
using BlockUsedRecord = Zaldaryon.Pharos.Events.BlockUsed;
using ChunkColumnLoadedRecord = Zaldaryon.Pharos.Events.ChunkColumnLoaded;
using ChunkColumnUnloadedRecord = Zaldaryon.Pharos.Events.ChunkColumnUnloaded;
using EntityDespawnedRecord = Zaldaryon.Pharos.Events.EntityDespawned;
using EntityDiedRecord = Zaldaryon.Pharos.Events.EntityDied;
using EntityInteractedRecord = Zaldaryon.Pharos.Events.EntityInteracted;
using EntityMountedRecord = Zaldaryon.Pharos.Events.EntityMounted;
using EntitySpawnAttemptedRecord = Zaldaryon.Pharos.Events.EntitySpawnAttempted;
using EntitySpawnedRecord = Zaldaryon.Pharos.Events.EntitySpawned;
using EntityUnmountedRecord = Zaldaryon.Pharos.Events.EntityUnmounted;
using GameModeChangedRecord = Zaldaryon.Pharos.Events.GameModeChanged;
using MapRegionLoadedRecord = Zaldaryon.Pharos.Events.MapRegionLoaded;
using PlayerDiedRecord = Zaldaryon.Pharos.Events.PlayerDied;
using PlayerRespawnedRecord = Zaldaryon.Pharos.Events.PlayerRespawned;
using WorldSavedRecord = Zaldaryon.Pharos.Events.WorldSaved;

namespace Zaldaryon.Pharos.Events;

/// <summary>
/// The server's game events, recorded from <see cref="ServerEventRecorder.Record"/> until disposed.
/// See <see cref="GameEventLog"/>.
/// </summary>
/// <remarks>
/// The log listens through the server's own <c>api.Event</c>, after the handlers registered before
/// it, as a mod would. It never changes an event's outcome: it lets every spawn attempt through and
/// leaves an interaction's handling alone.
/// </remarks>
public sealed class ServerEventLog : GameEventLog
{
    private readonly EmbeddedServerHost _host;
    private readonly ServerEventRecorder _recorder;
    private readonly IServerEventAPI _api;
    private readonly CommonHandlers _common;
    private readonly CommonHandlers.Selection _commonOn;

    private readonly BlockPlacedDelegate _placed;
    private readonly BlockBrokenDelegate _broken;
    private readonly BlockUsedDelegate _used;
    private readonly PlayerDeathDelegate _playerDied;
    private readonly PlayerDelegate _respawned;
    private readonly PlayerDelegate _gameMode;
    private readonly Action<IServerPlayer, ActiveSlotChangeEventArgs> _slot;
    private readonly OnInteractDelegate _interacted;
    private readonly TrySpawnEntityDelegate _spawnAttempted;
    private readonly ChunkColumnLoadedDelegate _columnLoaded;
    private readonly ChunkColumnUnloadDelegate _columnUnloaded;
    private readonly Action _saved;

    internal ServerEventLog(EmbeddedServerHost host, ServerEventRecorder recorder, ServerEvents events)
        : base(EnumAppSide.Server)
    {
        _host = host;
        _recorder = recorder;
        Events = events;
        _api = ((ICoreServerAPI)host.Server.Api).Event;
        _common = new CommonHandlers(this);
        _commonOn = new CommonHandlers.Selection(
            Has(ServerEvents.EntitySpawned), Has(ServerEvents.EntityDied), Has(ServerEvents.EntityDespawned),
            Has(ServerEvents.EntityMounted), Has(ServerEvents.EntityUnmounted), Has(ServerEvents.MapRegionLoaded));

        _placed = (player, oldId, selection, stack) => Add(() => new BlockPlacedRecord(player, oldId, GameEventRecord.Copy(selection), GameEventRecord.Copy(stack)));
        _broken = (player, oldId, selection) => Add(() => new BlockBrokenRecord(player, oldId, GameEventRecord.Copy(selection)));
        _used = (player, selection) => Add(() => new BlockUsedRecord(player, GameEventRecord.Copy(selection)));
        _playerDied = (player, source) => Add(() => new PlayerDiedRecord(player, source));
        _respawned = player => Add(() => new PlayerRespawnedRecord(player));
        _gameMode = player => Add(() => new GameModeChangedRecord(player, player.WorldData.CurrentGameMode));
        _slot = (player, args) => Add(() => new ActiveSlotChangedRecord(player, args.FromSlot, args.ToSlot));
        _interacted = (Entity entity, IPlayer byPlayer, ItemSlot slot, Vec3d hit, int mode, ref EnumHandling handling) =>
            Add(() => new EntityInteractedRecord(entity, byPlayer, GameEventRecord.Copy(slot?.Itemstack), hit?.Clone(), mode != 0 ? EnumInteractMode.Interact : EnumInteractMode.Attack));
        _spawnAttempted = (IBlockAccessor accessor, ref EntityProperties properties, Vec3d position, long herdId) =>
        {
            EntityProperties props = properties;
            bool onGameThread = _host.IsOnGameThread;
            Add(() => new EntitySpawnAttemptedRecord(props, position.Clone(), herdId, onGameThread));
            return true;
        };
        _columnLoaded = (coord, chunks) => Add(() => new ChunkColumnLoadedRecord(coord.X, coord.Y, chunks?.Length ?? 0));
        _columnUnloaded = coord => Add(() => new ChunkColumnUnloadedRecord(coord.X, coord.Y, coord.Z));
        _saved = () => Add(() => new WorldSavedRecord());
    }

    /// <summary>The events the log listens to.</summary>
    public ServerEvents Events { get; }

    /// <summary>The <see cref="BlockPlacedRecord"/> records.</summary>
    public IReadOnlyList<BlockPlacedRecord> BlockPlaced => Of<BlockPlacedRecord>();

    /// <summary>The <see cref="BlockBrokenRecord"/> records.</summary>
    public IReadOnlyList<BlockBrokenRecord> BlockBroken => Of<BlockBrokenRecord>();

    /// <summary>The <see cref="BlockUsedRecord"/> records.</summary>
    public IReadOnlyList<BlockUsedRecord> BlockUsed => Of<BlockUsedRecord>();

    /// <summary>The <see cref="PlayerDiedRecord"/> records.</summary>
    public IReadOnlyList<PlayerDiedRecord> PlayerDied => Of<PlayerDiedRecord>();

    /// <summary>The <see cref="PlayerRespawnedRecord"/> records.</summary>
    public IReadOnlyList<PlayerRespawnedRecord> PlayerRespawned => Of<PlayerRespawnedRecord>();

    /// <summary>The <see cref="GameModeChangedRecord"/> records.</summary>
    public IReadOnlyList<GameModeChangedRecord> GameModeChanged => Of<GameModeChangedRecord>();

    /// <summary>The <see cref="ActiveSlotChangedRecord"/> records.</summary>
    public IReadOnlyList<ActiveSlotChangedRecord> ActiveSlotChanged => Of<ActiveSlotChangedRecord>();

    /// <summary>The <see cref="EntityInteractedRecord"/> records.</summary>
    public IReadOnlyList<EntityInteractedRecord> EntityInteracted => Of<EntityInteractedRecord>();

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

    /// <summary>The <see cref="EntitySpawnAttemptedRecord"/> records.</summary>
    public IReadOnlyList<EntitySpawnAttemptedRecord> EntitySpawnAttempted => Of<EntitySpawnAttemptedRecord>();

    /// <summary>The <see cref="ChunkColumnLoadedRecord"/> records.</summary>
    public IReadOnlyList<ChunkColumnLoadedRecord> ChunkColumnLoaded => Of<ChunkColumnLoadedRecord>();

    /// <summary>The <see cref="ChunkColumnUnloadedRecord"/> records.</summary>
    public IReadOnlyList<ChunkColumnUnloadedRecord> ChunkColumnUnloaded => Of<ChunkColumnUnloadedRecord>();

    /// <summary>The <see cref="MapRegionLoadedRecord"/> records.</summary>
    public IReadOnlyList<MapRegionLoadedRecord> MapRegionLoaded => Of<MapRegionLoadedRecord>();

    /// <summary>The <see cref="WorldSavedRecord"/> records.</summary>
    public IReadOnlyList<WorldSavedRecord> WorldSaved => Of<WorldSavedRecord>();

    /// <summary>
    /// Ticks until the log holds a <typeparamref name="T"/> after <paramref name="since"/> that
    /// matches <paramref name="predicate"/>, and returns the earliest one. Inside a client-server
    /// session the whole session steps, so the client keeps up.
    /// </summary>
    /// <param name="predicate">What the record must match, or null for any.</param>
    /// <param name="since">
    /// Only records with a higher <see cref="GameEventRecord.Index"/> count; take it from
    /// <see cref="GameEventLog.Mark"/>. By default the whole log counts, from when it began
    /// recording, so an event that fired before the call is found. (The mod network driver's
    /// waits count from the call by default.)
    /// </param>
    /// <param name="maxTicks">How many ticks to wait at most. 0 only looks.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TimeoutException">None came within <paramref name="maxTicks"/> ticks; the message lists what was recorded.</exception>
    /// <exception cref="ObjectDisposedException">None matched, and the log no longer records.</exception>
    public Task<T> WaitForAsync<T>(System.Func<T, bool>? predicate = null, long? since = null, int maxTicks = 600, CancellationToken ct = default) where T : GameEventRecord =>
        WaitCoreAsync(predicate, since, maxTicks, ct);

    /// <summary>Subscribes the chosen events. Runs on the game thread.</summary>
    internal void Subscribe()
    {
        _common.Subscribe(_api, _commonOn);
        if (Has(ServerEvents.BlockPlaced)) _api.DidPlaceBlock += _placed;
        if (Has(ServerEvents.BlockBroken)) _api.DidBreakBlock += _broken;
        if (Has(ServerEvents.BlockUsed)) _api.DidUseBlock += _used;
        if (Has(ServerEvents.PlayerDied)) _api.PlayerDeath += _playerDied;
        if (Has(ServerEvents.PlayerRespawned)) _api.PlayerRespawn += _respawned;
        if (Has(ServerEvents.GameModeChanged)) _api.PlayerSwitchGameMode += _gameMode;
        if (Has(ServerEvents.ActiveSlotChanged)) _api.AfterActiveSlotChanged += _slot;
        if (Has(ServerEvents.EntityInteracted)) _api.OnPlayerInteractEntity += _interacted;
        if (Has(ServerEvents.EntitySpawnAttempted)) _api.OnTrySpawnEntity += _spawnAttempted;
        if (Has(ServerEvents.ChunkColumnLoaded)) _api.ChunkColumnLoaded += _columnLoaded;
        if (Has(ServerEvents.ChunkColumnUnloaded)) _api.ChunkColumnUnloaded += _columnUnloaded;
        if (Has(ServerEvents.WorldSaved)) _api.GameWorldSave += _saved;
    }

    internal override void Unsubscribe()
    {
        if (!_host.IsRunning) return;
        _host.RunOnGameThread(() =>
        {
            _common.Unsubscribe(_api, _commonOn);
            if (Has(ServerEvents.BlockPlaced)) _api.DidPlaceBlock -= _placed;
            if (Has(ServerEvents.BlockBroken)) _api.DidBreakBlock -= _broken;
            if (Has(ServerEvents.BlockUsed)) _api.DidUseBlock -= _used;
            if (Has(ServerEvents.PlayerDied)) _api.PlayerDeath -= _playerDied;
            if (Has(ServerEvents.PlayerRespawned)) _api.PlayerRespawn -= _respawned;
            if (Has(ServerEvents.GameModeChanged)) _api.PlayerSwitchGameMode -= _gameMode;
            if (Has(ServerEvents.ActiveSlotChanged)) _api.AfterActiveSlotChanged -= _slot;
            if (Has(ServerEvents.EntityInteracted)) _api.OnPlayerInteractEntity -= _interacted;
            if (Has(ServerEvents.EntitySpawnAttempted)) _api.OnTrySpawnEntity -= _spawnAttempted;
            if (Has(ServerEvents.ChunkColumnLoaded)) _api.ChunkColumnLoaded -= _columnLoaded;
            if (Has(ServerEvents.ChunkColumnUnloaded)) _api.ChunkColumnUnloaded -= _columnUnloaded;
            if (Has(ServerEvents.WorldSaved)) _api.GameWorldSave -= _saved;
        });
    }

    internal override void Closed() => _recorder.Closed(this);

    internal override bool SourceAlive => _host.IsRunning;

    internal override long CurrentStep => _host.TickCount;

    internal override string StepUnit => "ticks";

    internal override Task StepAsync(CancellationToken ct)
    {
        if (_host.SessionStepper is { } session) return session(ct);
        _host.Tick();
        return Task.CompletedTask;
    }

    private bool Has(ServerEvents e) => (Events & e) != 0;
}
