using System.Runtime.ExceptionServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>The result of asking the server to handle a headless player's action.</summary>
/// <param name="Accepted">Whether the server confirmed the requested state change or dispatched the interaction event.</param>
/// <param name="Error">The in-game error sent by the server when the action was refused.</param>
/// <param name="Reason">Why the action was refused when the server sent no in-game error.</param>
public sealed record BotResult(bool Accepted, ReceivedIngameMessage? Error = null, string? Reason = null);

/// <summary>
/// Drives a joined headless player by sending the same client packets as Vintage Story's own
/// client. The server validates and handles these actions through its normal packet handlers.
/// </summary>
public sealed class HeadlessPlayerBot
{
    private const double PositionTolerance = 0.12;
    private const int TicksPerSecond = 20;
    private const double MaxWalkSpeed = 5;

    private readonly ServerTestPlayer _owner;
    private readonly SemaphoreSlim _interactionGate = new(1, 1);
    private int _movementTick;

    internal HeadlessPlayerBot(ServerTestPlayer owner) => _owner = owner;

    /// <summary>Changes one of the player's movement controls through packet 21.</summary>
    public Task<BotResult> SetControlAsync(EnumEntityAction action, bool pressed, int maxTicks = 4, CancellationToken ct = default)
    {
        if (action == EnumEntityAction.None || !Enum.IsDefined(action))
        {
            throw new ArgumentOutOfRangeException(nameof(action), action, "The action must be a client movement control.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        int errorsBefore = _owner.Received.IngameErrors.Count;
        Packet_Client packet = new()
        {
            Id = 21,
            MoveKeyChange = new Packet_MoveKeyChange { Key = (int)action, Down = pressed ? 1 : 0 },
        };

        ct.ThrowIfCancellationRequested();
        Send(packet);
        return WaitForAsync(
            () => CurrentControls()[action] == pressed,
            maxTicks,
            errorsBefore,
            $"The server did not {(pressed ? "press" : "release")} {action}.",
            ct);
    }

    /// <summary>Selects one of the player's hotbar slots through packet 13.</summary>
    public Task<BotResult> SelectSlotAsync(int slotNumber, int maxTicks = 10, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        int slotCount = _owner.RunBotOnGameThread(() => RequirePlayer().InventoryManager.GetHotbarInventory().Count);
        ArgumentOutOfRangeException.ThrowIfNegative(slotNumber);
        if (slotNumber >= slotCount) throw new ArgumentOutOfRangeException(nameof(slotNumber), slotNumber, $"The hotbar has {slotCount} slots.");

        int errorsBefore = _owner.Received.IngameErrors.Count;
        Send(new Packet_Client
        {
            Id = 13,
            SelectedHotbarSlot = new Packet_SelectedHotbarSlot { SlotNumber = slotNumber },
        });

        return WaitForAsync(
            () => RequirePlayer().InventoryManager.ActiveHotbarSlotNumber == slotNumber,
            maxTicks,
            errorsBefore,
            $"The server did not select hotbar slot {slotNumber}.",
            ct);
    }

    /// <summary>Asks the server to break the block at a position through packet 3.</summary>
    public Task<BotResult> BreakBlockAsync(BlockPos position, BlockFacing? face = null, int maxTicks = 10, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        BlockFacing hitFace = face ?? BlockFacing.UP;
        int oldBlockId = GetBlockId(position);
        if (oldBlockId == 0) return Task.FromResult(new BotResult(false, null, $"There is no block at {position} to break."));

        int errorsBefore = _owner.Received.IngameErrors.Count;
        Send(new Packet_Client
        {
            Id = 3,
            BlockPlaceOrBreak = new Packet_ClientBlockPlaceOrBreak
            {
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                Mode = 0,
                BlockType = oldBlockId,
                OnBlockFace = hitFace.Index,
                HitX = CollectibleNet.SerializeDouble(0.5),
                HitY = CollectibleNet.SerializeDouble(0.5),
                HitZ = CollectibleNet.SerializeDouble(0.5),
            },
        });

        return WaitForAsync(
            () => GetBlockId(position) != oldBlockId,
            maxTicks,
            errorsBefore,
            $"The server did not break the block at {position}.",
            ct);
    }

    /// <summary>Asks the server to place the active hotbar block at a position through packet 3.</summary>
    public async Task<BotResult> PlaceBlockAsync(BlockPos position, int? hotbarSlot = null, BlockFacing? face = null, int maxTicks = 10, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        if (hotbarSlot is { } slot)
        {
            BotResult selection = await SelectSlotAsync(slot, ct: ct).ConfigureAwait(false);
            if (!selection.Accepted) return selection;
        }

        BlockFacing hitFace = face ?? BlockFacing.UP;
        (int oldBlockId, int itemId) = _owner.RunBotOnGameThread(() =>
        {
            IServerPlayer player = RequirePlayer();
            ItemStack? held = player.InventoryManager.ActiveHotbarSlot?.Itemstack;
            return (player.Entity.World.BlockAccessor.GetBlock(position).BlockId, held?.Class == EnumItemClass.Block ? held.Id : 0);
        });
        if (itemId == 0) return new(false, null, "The active hotbar slot does not contain a block.");

        int errorsBefore = _owner.Received.IngameErrors.Count;
        Send(new Packet_Client
        {
            Id = 3,
            BlockPlaceOrBreak = new Packet_ClientBlockPlaceOrBreak
            {
                X = position.X,
                Y = position.Y,
                Z = position.Z,
                Mode = 1,
                BlockType = itemId,
                OnBlockFace = hitFace.Index,
                HitX = CollectibleNet.SerializeDouble(0.5),
                HitY = CollectibleNet.SerializeDouble(0.5),
                HitZ = CollectibleNet.SerializeDouble(0.5),
            },
        });

        return await WaitForAsync(
            () => GetBlockId(position) != oldBlockId,
            maxTicks,
            errorsBefore,
            $"The server did not place a block at {position}.",
            ct).ConfigureAwait(false);
    }

    /// <summary>Uses a block through the server's block-use interaction packet.</summary>
    public async Task<BotResult> UseBlockAsync(BlockPos position, BlockFacing? face = null, int maxTicks = 10, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(position);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        BlockFacing hitFace = face ?? BlockFacing.UP;
        ct.ThrowIfCancellationRequested();
        await _interactionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_owner.RunBotOnGameThread(() => RequireEntity().Controls.HandUse != EnumHandInteract.None))
            {
                return new(false, null, "The player is already using a block or item.");
            }

            int errorsBefore = _owner.Received.IngameErrors.Count;
            SendBlockUsePacket(position, hitFace, EnumHandInteractNw.StartBlockUse);

            BotResult? operation = null;
            Exception? operationFailure = null;
            try
            {
                operation = await WaitForAsync(
                    () => RequireEntity().Controls.HandUse == EnumHandInteract.BlockInteract,
                    maxTicks,
                    errorsBefore,
                    $"The server did not accept block use at {position}.",
                    ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                operationFailure = ex;
            }

            BotResult? cleanup = null;
            Exception? cleanupFailure = null;
            try
            {
                SendBlockUsePacket(position, hitFace, EnumHandInteractNw.StopBlockUse);
                int cleanupErrorsBefore = _owner.Received.IngameErrors.Count;
                cleanup = await WaitForAsync(
                    () => RequireEntity().Controls.HandUse == EnumHandInteract.None,
                    maxTicks: 4,
                    cleanupErrorsBefore,
                    $"The server did not stop block use at {position}.",
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                cleanupFailure = ex;
            }

            if (cleanupFailure is not null)
            {
                if (operationFailure is not null)
                    throw new AggregateException("Block use failed and the server hand-use state could not be cleared.", operationFailure, cleanupFailure);
                throw new InvalidOperationException("The server hand-use state could not be cleared.", cleanupFailure);
            }

            if (cleanup is not { Accepted: true })
            {
                if (operationFailure is not null)
                {
                    throw new AggregateException(
                        "Block use failed and the server hand-use state remained active.",
                        operationFailure,
                        new InvalidOperationException(cleanup?.Reason ?? cleanup?.Error?.Message ?? "The server did not clear HandUse."));
                }
                return cleanup ?? new BotResult(false, null, "The server did not confirm block-use cleanup.");
            }

            if (operationFailure is not null) ExceptionDispatchInfo.Capture(operationFailure).Throw();

            return operation!;
        }
        finally
        {
            _interactionGate.Release();
        }
    }

    private void SendBlockUsePacket(BlockPos position, BlockFacing face, EnumHandInteractNw interaction) => Send(new Packet_Client
    {
        Id = 25,
        HandInteraction = new Packet_ClientHandInteraction
        {
            UseType = (int)EnumHandInteract.BlockInteract,
            MouseButton = (int)EnumMouseButton.Right,
            X = position.X,
            Y = position.Y,
            Z = position.Z,
            OnBlockFace = face.Index,
            HitX = CollectibleNet.SerializeDoublePrecise(0.5),
            HitY = CollectibleNet.SerializeDoublePrecise(0.5),
            HitZ = CollectibleNet.SerializeDoublePrecise(0.5),
            EnumHandInteract = (int)interaction,
            FirstEvent = interaction == EnumHandInteractNw.StartBlockUse ? 1 : 0,
        },
    });

    /// <summary>
    /// Moves items between two inventories the server has opened for this player through packet 8.
    /// Inventory IDs are the engine IDs, such as <c>hotbar-playeruid</c> or a chest's ID.
    /// </summary>
    public Task<BotResult> MoveSlotAsync(
        string sourceInventoryId,
        int sourceSlot,
        string targetInventoryId,
        int targetSlot,
        int quantity = 0,
        int maxTicks = 10,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceInventoryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetInventoryId);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceSlot);
        ArgumentOutOfRangeException.ThrowIfNegative(targetSlot);
        ArgumentOutOfRangeException.ThrowIfNegative(quantity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);

        (InventoryBase source, InventoryBase target, StackSnapshot beforeSource, StackSnapshot beforeTarget, long sourceChanged, long targetChanged) =
            _owner.RunBotOnGameThread(() =>
            {
                IServerPlayer player = RequirePlayer();
                if (!player.InventoryManager.GetInventory(sourceInventoryId, out InventoryBase? source) || source is null)
                {
                    throw new ArgumentException($"Player inventory '{sourceInventoryId}' does not exist.", nameof(sourceInventoryId));
                }

                if (!player.InventoryManager.GetInventory(targetInventoryId, out InventoryBase? target) || target is null)
                {
                    throw new ArgumentException($"Player inventory '{targetInventoryId}' does not exist.", nameof(targetInventoryId));
                }

                if (sourceSlot >= source.Count) throw new ArgumentOutOfRangeException(nameof(sourceSlot), sourceSlot, "The source slot is outside the inventory.");
                if (targetSlot >= target.Count) throw new ArgumentOutOfRangeException(nameof(targetSlot), targetSlot, "The target slot is outside the inventory.");

                return (source, target, Snapshot(source[sourceSlot]), Snapshot(target[targetSlot]),
                    source.LastChanged, target.LastChanged);
            });

        if (beforeSource.IsEmpty) return Task.FromResult(new BotResult(false, null, "The source inventory slot is empty."));

        int errorsBefore = _owner.Received.IngameErrors.Count;
        Send(new Packet_Client
        {
            Id = 8,
            MoveItemstack = new Packet_MoveItemstack
            {
                SourceInventoryId = sourceInventoryId,
                TargetInventoryId = targetInventoryId,
                SourceSlot = sourceSlot,
                TargetSlot = targetSlot,
                Quantity = quantity,
                SourceLastChanged = sourceChanged,
                TargetLastChanged = targetChanged,
                MouseButton = (int)EnumMouseButton.Left,
                Priority = (int)EnumMergePriority.AutoMerge,
                TabIndex = 0,
            },
        });

        return WaitForAsync(
            () => _owner.RunBotOnGameThread(() =>
                Snapshot(source[sourceSlot]) != beforeSource || Snapshot(target[targetSlot]) != beforeTarget),
            maxTicks,
            errorsBefore,
            $"The server did not move items from {sourceInventoryId}[{sourceSlot}] to {targetInventoryId}[{targetSlot}].",
            ct);
    }

    /// <summary>Asks the server to use an entity through packet 17.</summary>
    public Task<BotResult> UseEntityAsync(Entity entity, int maxTicks = 10, CancellationToken ct = default) =>
        InteractWithEntityAsync(entity, mode: (int)EnumInteractMode.Interact, maxTicks, ct);

    /// <summary>Asks the server to attack an entity through packet 17.</summary>
    public Task<BotResult> AttackEntityAsync(Entity entity, int maxTicks = 10, CancellationToken ct = default) =>
        InteractWithEntityAsync(entity, mode: (int)EnumInteractMode.Attack, maxTicks, ct);

    private async Task<BotResult> InteractWithEntityAsync(Entity entity, int mode, int maxTicks, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        ct.ThrowIfCancellationRequested();
        await _interactionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            ct.ThrowIfCancellationRequested();
            // EntitySelection.HitPosition and packet 17 coordinates are entity-local offsets.
            Vec3d hit = _owner.RunBotOnGameThread(() => new Vec3d(
                entity.SelectionBox.MidX,
                entity.SelectionBox.MidY,
                entity.SelectionBox.MidZ));
            int errorsBefore = _owner.Received.IngameErrors.Count;
            int observed = 0;
            IServerEventAPI events = _owner.RunBotOnGameThread(
                () => (IServerEventAPI)_owner.RequireBotHost().Server.Api.Event);
            OnInteractDelegate handler = (Entity interactedEntity, IPlayer byPlayer, ItemSlot slot, Vec3d hitPosition, int eventMode, ref EnumHandling handling) =>
            {
                if (interactedEntity.EntityId == entity.EntityId
                    && byPlayer.PlayerUID == _owner.PlayerUID
                    && eventMode == mode)
                {
                    Interlocked.Exchange(ref observed, 1);
                }
            };

            _owner.RunBotOnGameThread(() =>
            {
                // Packet 17 updates the server's current entity selection before dispatching
                // OnPlayerInteractEntity. Clear stale selection so event handlers see the selection
                // derived from this interaction packet.
                ConnectedClient client = _owner.RequireBotHost().Server.GetClientByPlayername(_owner.PlayerName)
                    ?? throw new InvalidOperationException("The bot player is no longer connected.");
                client.Entityplayer.EntitySelection = null;
                events.OnPlayerInteractEntity += handler;
            });
            try
            {
                Send(new Packet_Client
                {
                    Id = 17,
                    EntityInteraction = new Packet_EntityInteraction
                    {
                        // Packet 17 uses protocol modes: vanilla right-use is 1 and attack is 0.
                        MouseButton = mode,
                        EntityId = entity.EntityId,
                        OnBlockFace = BlockFacing.UP.Index,
                        // The server's entity interaction handler decodes these fields with DeserializeDouble.
                        HitX = CollectibleNet.SerializeDouble(hit.X),
                        HitY = CollectibleNet.SerializeDouble(hit.Y),
                        HitZ = CollectibleNet.SerializeDouble(hit.Z),
                        SelectionBoxIndex = 0,
                    },
                });

                return await WaitForAsync(
                    () => Volatile.Read(ref observed) != 0,
                    maxTicks,
                    errorsBefore,
                    $"The bot did not observe the requested {(mode == (int)EnumInteractMode.Attack ? "attack" : "use")} interaction with entity {entity.EntityId} within {maxTicks} ticks.",
                    ct).ConfigureAwait(false);
            }
            finally
            {
                _owner.RunBotOnGameThread(() => events.OnPlayerInteractEntity -= handler);
            }
        }
        finally
        {
            _interactionGate.Release();
        }
    }

    /// <summary>
    /// Walks in a straight line to a world position, sending one position packet per simulated
    /// client tick at 20 updates per second. It can step up one block when the direct position is
    /// blocked. Speeds above five blocks per second are refused to keep position steps collision-safe.
    /// This is a direct walk, not a pathfinder.
    /// </summary>
    public async Task<BotResult> WalkToAsync(Vec3d destination, int maxTicks = 600, double speed = 3, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        if (!double.IsFinite(destination.X) || !double.IsFinite(destination.Y) || !double.IsFinite(destination.Z))
        {
            throw new ArgumentOutOfRangeException(nameof(destination), "Destination coordinates must be finite.");
        }
        if (!double.IsFinite(speed) || speed <= 0 || speed > MaxWalkSpeed)
        {
            throw new ArgumentOutOfRangeException(nameof(speed), speed, $"Speed must be positive and no greater than {MaxWalkSpeed} blocks per second.");
        }
        ct.ThrowIfCancellationRequested();

        EmbeddedServerHost host = _owner.RequireBotHost();
        (Vec3d start, float yaw) = _owner.RunBotOnGameThread(() =>
        {
            EntityPlayer entity = RequireEntity();
            Vec3d position = new(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            double dx = destination.X - position.X;
            double dz = destination.Z - position.Z;
            float heading = dx * dx + dz * dz > 0.0001 ? (float)Math.Atan2(dx, dz) : entity.Pos.Yaw;
            return (position, heading);
        });

        if (Distance(start, destination) <= PositionTolerance) return new(true);

        try
        {
            BotResult control = await SetControlAsync(EnumEntityAction.Forward, true, maxTicks: 4, ct).ConfigureAwait(false);
            if (!control.Accepted) return control;

            double stepLength = speed / TicksPerSecond;
            for (int tick = 0; tick < maxTicks; tick++)
            {
                ct.ThrowIfCancellationRequested();
                EntityPlayer entity = _owner.RunBotOnGameThread(RequireEntity);
                Vec3d current = new(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
                if (Distance(current, destination) <= PositionTolerance) return new(true);

                Vec3d? next = _owner.RunBotOnGameThread(() => FindNextPosition(entity, current, destination, stepLength));
                if (next is null)
                {
                    return new(false, null, $"The straight path to {destination} is blocked and cannot step up one block.");
                }

                Packet_EntityPosition positionPacket = _owner.RunBotOnGameThread(() => CreatePositionPacket(entity, current, next, yaw));
                Packet_UdpPacket udpPacket = new() { Id = 2, EntityPosition = positionPacket };
                int errorsBefore = _owner.Received.IngameErrors.Count;
                Send(new Packet_Client { Id = 35, UdpPacket = udpPacket });

                BotResult moved = await WaitForAsync(
                    () => Distance(Position(), next) <= PositionTolerance,
                    maxTicks: 4,
                    errorsBefore,
                    $"The server refused the position packet at {next}.",
                    ct).ConfigureAwait(false);
                if (!moved.Accepted) return moved;
            }

            Vec3d finalPosition = Position();
            return new(false, null, $"The player reached {finalPosition}, not {destination}, in {maxTicks} ticks.");
        }
        finally
        {
            BotResult released = await SetControlAsync(EnumEntityAction.Forward, false, maxTicks: 4, CancellationToken.None).ConfigureAwait(false);
            if (!released.Accepted)
            {
                throw new InvalidOperationException(released.Reason ?? released.Error?.Message ?? "The server did not release the Forward control.");
            }
        }
    }

    private BotResult? FindNewError(int errorsBefore)
    {
        ReceivedIngameMessage? error = _owner.Received.IngameErrors.Skip(errorsBefore).FirstOrDefault();
        return error is null ? null : new BotResult(false, error, error.Message ?? error.Code);
    }

    private async Task<BotResult> WaitForAsync(Func<bool> accepted, int maxTicks, int errorsBefore, string failure, CancellationToken ct)
    {
        EmbeddedServerHost host = _owner.RequireBotHost();
        for (int tick = 0; tick <= maxTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            host.Tick();
            await Task.Delay(1, ct).ConfigureAwait(false);
            if (FindNewError(errorsBefore) is { } error) return error;
            if (_owner.RunBotOnGameThread(accepted)) return new(true);
            if (tick == maxTicks) break;
        }

        return FindNewError(errorsBefore) ?? new BotResult(false, null, failure);
    }

    private void Send(Packet_Client packet)
    {
        HeadlessPlayerConnection connection = _owner.RequireBotConnection();
        _owner.RunBotOnGameThread(() => connection.SendClientPacket(packet));
    }

    private EntityControls CurrentControls()
    {
        EntityPlayer entity = RequireEntity();
        return entity.MountedOn?.Controls ?? entity.Controls;
    }

    private IServerPlayer RequirePlayer() => _owner.Player
        ?? throw new InvalidOperationException("The headless player has not joined the server.");

    private EntityPlayer RequireEntity() => _owner.Entity
        ?? throw new InvalidOperationException("The headless player has no spawned entity.");

    private Vec3d Position() => _owner.RunBotOnGameThread(() =>
    {
        EntityPlayer entity = RequireEntity();
        return new Vec3d(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
    });

    private int GetBlockId(BlockPos position) => _owner.RunBotOnGameThread(() =>
        _owner.RequireBotHost().Server.Api.World.BlockAccessor.GetBlock(position).BlockId);

    private static StackSnapshot Snapshot(ItemSlot? slot)
    {
        ItemStack? stack = slot?.Itemstack;
        return stack is null
            ? new StackSnapshot(null, EnumItemClass.Item, 0)
            : new StackSnapshot(stack.Collectible.Code.ToString(), stack.Class, stack.StackSize);
    }

    private Vec3d? FindNextPosition(EntityPlayer entity, Vec3d current, Vec3d destination, double stepLength)
    {
        double dx = destination.X - current.X;
        double dy = destination.Y - current.Y;
        double dz = destination.Z - current.Z;
        double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance <= PositionTolerance) return destination;

        double fraction = Math.Min(1, stepLength / distance);
        Vec3d preferred = new(current.X + dx * fraction, current.Y + dy * fraction, current.Z + dz * fraction);
        if (CanOccupy(entity, preferred)) return preferred;

        Vec3d keepHeight = new(preferred.X, current.Y, preferred.Z);
        if (CanOccupy(entity, keepHeight)) return keepHeight;

        Vec3d stepUp = new(preferred.X, current.Y + 1, preferred.Z);
        return CanOccupy(entity, stepUp) ? stepUp : null;
    }

    private bool CanOccupy(EntityPlayer entity, Vec3d position)
    {
        ICoreServerAPI api = _owner.Server?.Api as ICoreServerAPI
            ?? throw new InvalidOperationException("The headless player is not attached to a server API.");
        Cuboidf playerCollisionBox = entity.CollisionBox;
        int minX = (int)Math.Floor(position.X + playerCollisionBox.MinX);
        int maxX = (int)Math.Floor(position.X + playerCollisionBox.MaxX);
        int minY = (int)Math.Floor(position.Y + playerCollisionBox.MinY);
        int maxY = (int)Math.Floor(position.Y + playerCollisionBox.MaxY);
        int minZ = (int)Math.Floor(position.Z + playerCollisionBox.MinZ);
        int maxZ = (int)Math.Floor(position.Z + playerCollisionBox.MaxZ);

        for (int x = minX; x <= maxX; x++)
        for (int y = minY; y <= maxY; y++)
        for (int z = minZ; z <= maxZ; z++)
        {
            BlockPos blockPos = new(x, y, z, entity.Pos.Dimension);
            Block block = api.World.BlockAccessor.GetBlock(blockPos);
            Cuboidf[]? collisionBoxes = block.GetCollisionBoxes(api.World.BlockAccessor, blockPos);
            if (collisionBoxes is null) continue;

            foreach (Cuboidf blockCollisionBox in collisionBoxes)
            {
                if (CollisionTester.AabbIntersect(blockCollisionBox, x, y, z, playerCollisionBox, position)) return false;
            }
        }

        return true;
    }

    private Packet_EntityPosition CreatePositionPacket(EntityPlayer entity, Vec3d current, Vec3d next, float yaw)
    {
        Packet_EntityPosition packet = ServerPackets.getEntityPositionPacket(entity.Pos, entity, ++_movementTick);
        packet.X = CollectibleNet.SerializeDoublePrecise(next.X);
        packet.Y = CollectibleNet.SerializeDoublePrecise(next.Y);
        packet.Z = CollectibleNet.SerializeDoublePrecise(next.Z);
        packet.Yaw = CollectibleNet.SerializeFloatPrecise(yaw);
        packet.HeadYaw = CollectibleNet.SerializeFloatPrecise(yaw);
        packet.BodyYaw = CollectibleNet.SerializeFloatPrecise(yaw);
        packet.MotionX = CollectibleNet.SerializeDoublePrecise((next.X - current.X) * TicksPerSecond);
        packet.MotionY = CollectibleNet.SerializeDoublePrecise((next.Y - current.Y) * TicksPerSecond);
        packet.MotionZ = CollectibleNet.SerializeDoublePrecise((next.Z - current.Z) * TicksPerSecond);
        packet.Controls = entity.Controls.ToInt();
        return packet;
    }

    private static double Distance(Vec3d left, Vec3d right)
    {
        double dx = left.X - right.X;
        double dy = left.Y - right.Y;
        double dz = left.Z - right.Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    private readonly record struct StackSnapshot(string? Code, EnumItemClass Class, int Quantity)
    {
        public bool IsEmpty => Quantity == 0;
    }
}
