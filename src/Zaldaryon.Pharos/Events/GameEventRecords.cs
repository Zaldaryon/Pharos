using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Zaldaryon.Pharos.Events;

/// <summary>
/// One game event a <see cref="GameEventLog"/> recorded: what the event delivered, where it sits
/// in the log, and when it fired.
/// </summary>
/// <remarks>
/// Item stacks, block selections and coordinates are copied when the event fires, so a record says
/// what the event delivered even after the game changes them. Players, entities, seats, damage
/// sources and map regions are the game's live objects; entity records also keep the entity's id
/// and code as they were.
/// </remarks>
public abstract record GameEventRecord
{
    /// <summary>
    /// The record's place in its log, from 0, in firing order. It keeps counting across
    /// <see cref="GameEventLog.Clear"/>.
    /// </summary>
    public long Index { get; init; }

    /// <summary>The side the event fired on.</summary>
    public EnumAppSide Side { get; init; }

    /// <summary>
    /// When it fired. On the server: <see cref="Server.EmbeddedServerHost.TickCount"/>, the number of
    /// ticks the host had completed, so an event fired while a tick runs carries the count from
    /// before that tick. On the client: <see cref="Timing.DeterministicFrameController.TotalFrames"/>,
    /// the frames completed.
    /// </summary>
    public long Step { get; init; }

    /// <summary>The start of <see cref="ToString"/>: index, side's step unit and step.</summary>
    protected string Prefix => $"#{Index} {(Side == EnumAppSide.Server ? "tick" : "frame")} {Step}";

    /// <summary>One line naming the event and its main arguments.</summary>
    public abstract override string ToString();

    internal static string Describe(IPlayer? player) => player?.PlayerName ?? "nobody";

    internal static string Describe(BlockPos? pos) => pos == null ? "?" : $"{pos.X},{pos.Y},{pos.Z}";

    internal static string Describe(DamageSource? source) =>
        source == null ? "no damage source" : $"{source.Source}/{source.Type}{(source.SourceEntity != null ? $" by {source.SourceEntity.Code}" : "")}";

    internal static ItemStack? Copy(ItemStack? stack)
    {
        if (stack == null) return null;
        try
        {
            return stack.Clone();
        }
        catch (Exception)
        {
            return stack;
        }
    }

    internal static BlockSelection Copy(BlockSelection selection)
    {
        try
        {
            return selection.Clone();
        }
        catch (Exception)
        {
            return selection;
        }
    }
}

/// <summary>
/// A record about an entity, with the entity's id and code as they were when the event fired.
/// </summary>
/// <param name="Entity">The entity, a live object.</param>
public abstract record EntityEventRecord(Entity Entity) : GameEventRecord
{
    /// <summary>The entity's id when the event fired.</summary>
    public long EntityId { get; init; } = Entity.EntityId;

    /// <summary>The entity's code when the event fired, such as <c>game:chicken-hen</c>.</summary>
    public AssetLocation? Code { get; init; } = Entity.Code?.Clone();
}

/// <summary>
/// A player placed a block (<c>IServerEventAPI.DidPlaceBlock</c>). It fires for placements a
/// client asks for, not for blocks set through a block accessor.
/// </summary>
/// <param name="Player">Who placed it.</param>
/// <param name="OldBlockId">The block that was there before.</param>
/// <param name="Selection">Where, and on which face, a copy.</param>
/// <param name="Stack">
/// A copy of the player's active slot stack as the event delivered it: the stack placed from, or
/// null when the slot held nothing. The game fires the event after the placement, so in survival
/// the stack size is already one less, and <see cref="ItemStack.StackSize"/> may be 0.
/// </param>
public sealed record BlockPlaced(IServerPlayer Player, int OldBlockId, BlockSelection Selection, ItemStack? Stack) : GameEventRecord
{
    /// <summary>The position of the placed block.</summary>
    public BlockPos Pos => Selection.Position;

    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} BlockPlaced {Stack?.Collectible?.Code?.ToString() ?? "(no stack)"} at {Describe(Pos)} by {Describe(Player)}";
}

/// <summary>A player broke a block (<c>IServerEventAPI.DidBreakBlock</c>).</summary>
/// <param name="Player">Who broke it.</param>
/// <param name="OldBlockId">The block that was broken.</param>
/// <param name="Selection">Where, a copy.</param>
public sealed record BlockBroken(IServerPlayer Player, int OldBlockId, BlockSelection Selection) : GameEventRecord
{
    /// <summary>The position of the broken block.</summary>
    public BlockPos Pos => Selection.Position;

    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} BlockBroken block {OldBlockId} at {Describe(Pos)} by {Describe(Player)}";
}

/// <summary>A player used a block (<c>IServerEventAPI.DidUseBlock</c>).</summary>
/// <param name="Player">Who used it.</param>
/// <param name="Selection">Where, a copy.</param>
public sealed record BlockUsed(IServerPlayer Player, BlockSelection Selection) : GameEventRecord
{
    /// <summary>The position of the used block.</summary>
    public BlockPos Pos => Selection.Position;

    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} BlockUsed at {Describe(Pos)} by {Describe(Player)}";
}

/// <summary>
/// A player died (<c>IServerEventAPI.PlayerDeath</c>). The server fires it on the tick after the
/// player's entity died, so the player's <see cref="EntityDied"/> comes first.
/// </summary>
/// <param name="Player">Who died.</param>
/// <param name="Source">What killed them, when known.</param>
public sealed record PlayerDied(IServerPlayer Player, DamageSource? Source) : GameEventRecord
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} PlayerDied {Describe(Player)} ({Describe(Source)})";
}

/// <summary>
/// A player asked to respawn (<c>IServerEventAPI.PlayerRespawn</c>). The game fires it for every
/// request, also for one it ignores because the player is alive, but not once the world's
/// <c>playerlives</c> are used up. The game's own respawn handler
/// runs before the log's, but it may move and revive the player only later, once the chunks at
/// the spawn point are loaded: wait for <c>Player.Entity.Alive</c> to see the respawn done.
/// </summary>
/// <param name="Player">Who asked.</param>
public sealed record PlayerRespawned(IServerPlayer Player) : GameEventRecord
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} PlayerRespawned {Describe(Player)}";
}

/// <summary>A player's game mode changed (<c>IServerEventAPI.PlayerSwitchGameMode</c>).</summary>
/// <param name="Player">Whose.</param>
/// <param name="Mode">The game mode when the event fired: the new one.</param>
public sealed record GameModeChanged(IServerPlayer Player, EnumGameMode Mode) : GameEventRecord
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} GameModeChanged {Describe(Player)} to {Mode}";
}

/// <summary>A player selected another hotbar slot (<c>IServerEventAPI.AfterActiveSlotChanged</c>).</summary>
/// <param name="Player">Who.</param>
/// <param name="FromSlot">The slot left.</param>
/// <param name="ToSlot">The slot selected.</param>
public sealed record ActiveSlotChanged(IServerPlayer Player, int FromSlot, int ToSlot) : GameEventRecord
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} ActiveSlotChanged {Describe(Player)} {FromSlot} -> {ToSlot}";
}

/// <summary>
/// A player attacked or interacted with an entity (<c>IServerEventAPI.OnPlayerInteractEntity</c>).
/// Recorded only when no handler before the log's stopped the event with
/// <see cref="EnumHandling.PreventSubsequent"/>.
/// </summary>
/// <param name="Entity">The entity, a live object.</param>
/// <param name="Player">Who.</param>
/// <param name="Stack">A copy of what the player held, or null.</param>
/// <param name="HitPosition">Where the entity was hit, relative to it, a copy.</param>
/// <param name="Mode">Attack for a left click, Interact otherwise, as the game maps the mouse button.</param>
public sealed record EntityInteracted(Entity Entity, IPlayer Player, ItemStack? Stack, Vec3d? HitPosition, EnumInteractMode Mode) : EntityEventRecord(Entity)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} EntityInteracted {Mode} {Code} ({EntityId}) by {Describe(Player)}";
}

/// <summary>
/// The server is about to spawn an entity (<c>IServerEventAPI.OnTrySpawnEntity</c>), from the
/// creature spawner or from world generation.
/// </summary>
/// <remarks>
/// The game asks the handlers in turn and stops at the first that refuses, so a log sees only the
/// attempts no handler before it refused, and cannot tell whether one after it does. World
/// generation asks from its own thread: such a record's place among the others is not exact.
/// </remarks>
/// <param name="Properties">The entity type, as earlier handlers left it.</param>
/// <param name="Position">Where, a copy.</param>
/// <param name="HerdId">The herd id the entity would get.</param>
/// <param name="OnGameThread">Whether it fired on the server's game thread.</param>
public sealed record EntitySpawnAttempted(EntityProperties Properties, Vec3d Position, long HerdId, bool OnGameThread) : GameEventRecord
{
    /// <summary>The entity type's code.</summary>
    public AssetLocation? Code => Properties?.Code;

    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} EntitySpawnAttempted {Code} at {Position}{(OnGameThread ? "" : " (off the game thread)")}";
}

/// <summary>A chunk column was loaded (<c>IServerEventAPI.ChunkColumnLoaded</c>).</summary>
/// <param name="ChunkX">The column's chunk X.</param>
/// <param name="ChunkZ">The column's chunk Z.</param>
/// <param name="ChunkCount">How many chunks the column has.</param>
public sealed record ChunkColumnLoaded(int ChunkX, int ChunkZ, int ChunkCount) : GameEventRecord
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} ChunkColumnLoaded {ChunkX},{ChunkZ}";
}

/// <summary>
/// A chunk column was unloaded (<c>IServerEventAPI.ChunkColumnUnloaded</c>). The game fires it once
/// per column, with <paramref name="ChunkY"/> 0, when it unloads columns nobody is near; once per
/// chunk of the column from <c>IWorldManagerAPI.UnloadChunkColumn</c>; and once per loaded chunk
/// at shutdown.
/// </summary>
/// <param name="ChunkX">The chunk X.</param>
/// <param name="ChunkY">The chunk Y, see above.</param>
/// <param name="ChunkZ">The chunk Z.</param>
public sealed record ChunkColumnUnloaded(int ChunkX, int ChunkY, int ChunkZ) : GameEventRecord
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} ChunkColumnUnloaded {ChunkX},{ChunkY},{ChunkZ}";
}

/// <summary>A map region was loaded (<c>IEventAPI.MapRegionLoaded</c>), on either side.</summary>
/// <param name="RegionX">The region's X.</param>
/// <param name="RegionZ">The region's Z.</param>
/// <param name="Region">The region, a live object.</param>
public sealed record MapRegionLoaded(int RegionX, int RegionZ, IMapRegion? Region) : GameEventRecord
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} MapRegionLoaded {RegionX},{RegionZ}";
}

/// <summary>The server is saving the world (<c>IServerEventAPI.GameWorldSave</c>).</summary>
public sealed record WorldSaved : GameEventRecord
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} WorldSaved";
}

/// <summary>
/// An entity spawned (<c>IEventAPI.OnEntitySpawn</c>), on either side. An entity the client gets
/// together with the chunk it is in, such as one spawned while the client is still loading the
/// world, comes as loaded (<c>OnEntityLoaded</c>, not recorded) instead.
/// </summary>
/// <param name="Entity">The entity, a live object.</param>
public sealed record EntitySpawned(Entity Entity) : EntityEventRecord(Entity)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} EntitySpawned {Code} ({EntityId})";
}

/// <summary>
/// An entity died (<c>IEventAPI.OnEntityDeath</c>): <c>Entity.Die</c> with the reason Death. The
/// dead entity stays in the world until it despawns. The vanilla client rarely runs <c>Die</c>,
/// so the client seldom records this; it records the despawn.
/// </summary>
/// <param name="Entity">The entity, a live object.</param>
/// <param name="Source">What killed it, when known.</param>
public sealed record EntityDied(Entity Entity, DamageSource? Source) : EntityEventRecord(Entity)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} EntityDied {Code} ({EntityId}, {Describe(Source)})";
}

/// <summary>An entity left the world (<c>IEventAPI.OnEntityDespawn</c>), on either side.</summary>
/// <param name="Entity">The entity, a live object.</param>
/// <param name="Reason">Why, when known.</param>
public sealed record EntityDespawned(Entity Entity, EntityDespawnData? Reason) : EntityEventRecord(Entity)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} EntityDespawned {Code} ({EntityId}, {Reason?.Reason.ToString() ?? "no reason"})";
}

/// <summary>An entity mounted a seat (<c>IEventAPI.EntityMounted</c>), on either side.</summary>
/// <param name="Rider">Who mounted, a live object; also <see cref="EntityEventRecord.Entity"/>.</param>
/// <param name="Seat">The seat, a live object.</param>
public sealed record EntityMounted(EntityAgent Rider, IMountableSeat? Seat) : EntityEventRecord(Rider)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} EntityMounted {Code} ({EntityId}) on {Seat?.SeatId ?? "?"}";
}

/// <summary>An entity left a seat (<c>IEventAPI.EntityUnmounted</c>), on either side.</summary>
/// <param name="Rider">Who left, a live object; also <see cref="EntityEventRecord.Entity"/>.</param>
/// <param name="Seat">The seat, a live object.</param>
public sealed record EntityUnmounted(EntityAgent Rider, IMountableSeat? Seat) : EntityEventRecord(Rider)
{
    /// <inheritdoc/>
    public override string ToString() => $"{Prefix} EntityUnmounted {Code} ({EntityId}) from {Seat?.SeatId ?? "?"}";
}
