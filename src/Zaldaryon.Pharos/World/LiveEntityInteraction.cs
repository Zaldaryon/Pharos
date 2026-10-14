using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client;
using Vintagestory.Server;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.World;

/// <summary>
/// Uses entities through the engine-mode client's normal selection and interaction packet.
/// </summary>
public sealed class LiveEntityInteraction
{
    private readonly ClientServerLoopbackSession _session;
    private readonly SemaphoreSlim _interactionGate = new(1, 1);

    internal LiveEntityInteraction(ClientServerLoopbackSession session)
    {
        _session = session;
    }

    private Core.HeadlessClient Client => _session.Client;

    private EmbeddedServerHost Server => _session.NativeServer
        ?? throw new InvalidOperationException("Live entity interaction needs a session with an embedded server.");

    /// <summary>Aims the rendering client at an entity until it becomes the selected entity.</summary>
    /// <returns>Whether the entity was selected within <paramref name="maxFrames"/>.</returns>
    public Task<bool> AimAtAsync(Entity entity, int maxFrames = 120, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrames);

        Vec3d target = Server.RunOnGameThread(() => new Vec3d(
            entity.Pos.X + entity.SelectionBox.MidX,
            entity.Pos.Y + entity.SelectionBox.MidY,
            entity.Pos.Z + entity.SelectionBox.MidZ));

        return _session.StepUntilAsync(() =>
        {
            Client.RunOnClientThread(() => Client.TestPlayer.Camera.LookAt(target));
            return Client.RunOnClientThread(() =>
                Client.Client.EntitySelection?.Entity?.EntityId == entity.EntityId);
        }, maxFrames, ct: ct);
    }

    /// <summary>
    /// Aims at an entity and sends the engine's normal right-click interaction packet for it.
    /// </summary>
    /// <returns>Whether the server dispatched the matching event within <paramref name="maxFrames"/>.</returns>
    /// <exception cref="InvalidOperationException">The entity could not be aimed at.</exception>
    public async Task<bool> UseAsync(Entity entity, int maxFrames = 120, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrames);
        await _interactionGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            int initialFrame = _session.FrameCount;
            return await UseCoreAsync(entity, initialFrame, maxFrames, ct).ConfigureAwait(false);
        }
        finally
        {
            _interactionGate.Release();
        }
    }

    private async Task<bool> UseCoreAsync(Entity entity, int initialFrame, int maxFrames, CancellationToken ct)
    {
        if (!await AimAtAsync(entity, maxFrames, ct).ConfigureAwait(false))
        {
            long? selectedEntityId = Client.RunOnClientThread(() =>
                Client.Client.EntitySelection?.Entity?.EntityId);
            throw new InvalidOperationException(
                $"Could not aim at entity {entity.EntityId}; the client selected entity {selectedEntityId?.ToString() ?? "none"}.");
        }

        EntitySelection selection = Client.RunOnClientThread(() =>
        {
            EntitySelection? current = Client.Client.EntitySelection;
            return current?.Entity?.EntityId == entity.EntityId
                ? current
                : throw new InvalidOperationException($"Entity {entity.EntityId} is no longer selected by the client.");
        });
        ct.ThrowIfCancellationRequested();
        int remainingFrames = maxFrames - (_session.FrameCount - initialFrame);
        if (remainingFrames <= 0) return false;

        int observed = 0;
        string playerUid = Client.RunOnClientThread(() => Client.Client.player.PlayerUID);
        string playerName = Client.RunOnClientThread(() => Client.Client.player.PlayerName);
        IServerEventAPI events = Server.RunOnGameThread(() =>
        {
            ConnectedClient player = Server.Server.GetClientByPlayername(playerName)
                ?? throw new InvalidOperationException("The rendering client is no longer connected to the embedded server.");
            // Ensure the interaction handler replaces stale selection with the one from this packet.
            player.Entityplayer.EntitySelection = null;
            return (IServerEventAPI)Server.Server.Api.Event;
        });
        OnInteractDelegate handler = (Entity interactedEntity, IPlayer byPlayer, ItemSlot slot, Vec3d hitPosition, int mode, ref EnumHandling handling) =>
        {
            if (interactedEntity.EntityId == entity.EntityId
                && byPlayer.PlayerUID == playerUid
                && mode == (int)EnumInteractMode.Interact)
            {
                Interlocked.Exchange(ref observed, 1);
            }
        };

        Server.RunOnGameThread(() => events.OnPlayerInteractEntity += handler);
        try
        {
            Client.RunOnClientThread(() => Client.Client.SendPacketClient(ClientPackets.EntityInteraction(
                // Packet 17's right-use value is EnumInteractMode.Interact (1), not EnumMouseButton.Right (2).
                (int)EnumInteractMode.Interact,
                selection.Entity.EntityId,
                selection.Face ?? BlockFacing.NORTH,
                selection.HitPosition,
                selection.SelectionBoxIndex)));
            return await _session.StepUntilAsync(
                () => Volatile.Read(ref observed) != 0,
                remainingFrames,
                ct: ct).ConfigureAwait(false);
        }
        finally
        {
            Server.RunOnGameThread(() => events.OnPlayerInteractEntity -= handler);
        }
    }
}
