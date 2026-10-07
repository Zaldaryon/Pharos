using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.World;

/// <summary>
/// Breaks, places and uses blocks the way a player does: the engine-mode client aims at the block
/// and presses the attack or use control, and the result is whatever the game makes of it, on the
/// client and on the server.
/// </summary>
/// <remarks>
/// <para>
/// Every interaction goes through the client's own block selection, its mouse handling and the
/// interaction packets it sends, so block behaviors, collectible behaviors, mods' interaction
/// handlers, reach checks and permission checks all run as in the game. Each method steps the
/// session in lockstep until the outcome is visible on both sides or the frame budget runs out.
/// </para>
/// <para>
/// Breaking in survival takes as long as the tool and block say. Placing needs the block in the
/// active hotbar slot; <see cref="PlaceAsync"/> puts it there through the server, as
/// <c>/giveblock</c> would.
/// </para>
/// </remarks>
public sealed class LiveBlockInteraction
{
    private readonly ClientServerLoopbackSession _session;

    internal LiveBlockInteraction(ClientServerLoopbackSession session)
    {
        _session = session;
    }

    private Core.HeadlessClient Client => _session.Client;

    private EmbeddedServerHost Server => _session.NativeServer
        ?? throw new InvalidOperationException("Live block interaction needs a session with an embedded server.");

    /// <summary>
    /// Aims at <paramref name="pos"/>, at the centre of <paramref name="face"/> when given, and
    /// steps until the client's block selection is on it.
    /// </summary>
    /// <returns>Whether the block was selected within <paramref name="maxFrames"/>.</returns>
    public async Task<bool> AimAtAsync(BlockPos pos, BlockFacing? face = null, int maxFrames = 120, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pos);
        Vec3d target = new Vec3d(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5);
        if (face != null)
        {
            target.Add(face.Normalf.X * 0.49, face.Normalf.Y * 0.49, face.Normalf.Z * 0.49);
        }

        return await _session.StepUntilAsync(() =>
        {
            Client.RunOnClientThread(() => Client.TestPlayer.Camera.LookAt(target));
            BlockSelection? selection = Client.Client.BlockSelection;
            return selection?.Position?.Equals(pos) == true && (face == null || selection.Face == face);
        }, maxFrames, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Aims at <paramref name="pos"/> and holds the attack control until the block is gone on the
    /// client and on the server.
    /// </summary>
    /// <returns>Whether the block was broken within <paramref name="maxFrames"/>.</returns>
    /// <exception cref="InvalidOperationException">The block could not be aimed at.</exception>
    public async Task<bool> BreakAsync(BlockPos pos, int maxFrames = 600, CancellationToken ct = default)
    {
        await RequireAimAsync(pos, null, ct).ConfigureAwait(false);

        Client.Controls.Press(PlayerAction.Attack);
        try
        {
            return await _session.StepUntilAsync(
                () => ClientBlockId(pos) == 0 && ServerBlockId(pos) == 0,
                maxFrames, ct: ct).ConfigureAwait(false);
        }
        finally
        {
            Client.Controls.Release(PlayerAction.Attack);
        }
    }

    /// <summary>
    /// Places <paramref name="blockCode"/> against <paramref name="face"/> of
    /// <paramref name="against"/>, the way a player right-clicks that face with the block in hand.
    /// </summary>
    /// <param name="against">The existing block to place against.</param>
    /// <param name="face">The face of <paramref name="against"/> to click. The new block goes on that side.</param>
    /// <param name="blockCode">The block to place, such as <c>game:rock-granite</c>.</param>
    /// <param name="maxFrames">How many frames the placement may take to show up on both sides.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Whether the block appeared on the client and on the server.</returns>
    /// <exception cref="InvalidOperationException">The block code is unknown or the face could not be aimed at.</exception>
    public async Task<bool> PlaceAsync(BlockPos against, BlockFacing face, string blockCode, int maxFrames = 300, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(face);
        BlockPos target = against.AddCopy(face);

        int blockId = HoldInActiveSlot(blockCode);
        await _session.StepUntilAsync(() => Client.Client.player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Id == blockId, 120, ct: ct).ConfigureAwait(false);

        await RequireAimAsync(against, face, ct).ConfigureAwait(false);

        Client.Controls.Press(PlayerAction.Use);
        await _session.StepFramesAsync(2, ct: ct).ConfigureAwait(false);
        Client.Controls.Release(PlayerAction.Use);

        return await _session.StepUntilAsync(
            () => ClientBlockId(target) == blockId && ServerBlockId(target) == blockId,
            maxFrames, ct: ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Aims at <paramref name="pos"/> and clicks the use control on it, as a player right-clicks a
    /// door, a chest or any other interactable block. The outcome is for the caller to check.
    /// </summary>
    /// <exception cref="InvalidOperationException">The block could not be aimed at.</exception>
    public async Task UseAsync(BlockPos pos, int holdFrames = 2, CancellationToken ct = default)
    {
        await RequireAimAsync(pos, null, ct).ConfigureAwait(false);

        Client.Controls.Press(PlayerAction.Use);
        await _session.StepFramesAsync(holdFrames, ct: ct).ConfigureAwait(false);
        Client.Controls.Release(PlayerAction.Use);
        await _session.StepFramesAsync(2, ct: ct).ConfigureAwait(false);
    }

    private async Task RequireAimAsync(BlockPos pos, BlockFacing? face, CancellationToken ct)
    {
        if (!await AimAtAsync(pos, face, ct: ct).ConfigureAwait(false))
        {
            BlockSelection? selection = Client.Client.BlockSelection;
            throw new InvalidOperationException(
                $"Could not aim at {pos}{(face != null ? " " + face.Code : "")}: the client selects " +
                $"{(selection == null ? "nothing" : selection.Position + " " + selection.Face?.Code)}. " +
                "Is it within reach and in line of sight?");
        }
    }

    private int HoldInActiveSlot(string blockCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockCode);
        string playerName = Client.Client.player?.PlayerName
            ?? throw new InvalidOperationException("The client has not joined.");

        return Server.RunOnGameThread(() =>
        {
            ICoreServerAPI api = (ICoreServerAPI)Server.Server.Api;
            Block block = api.World.GetBlock(new AssetLocation(blockCode))
                ?? throw new InvalidOperationException($"Unknown block '{blockCode}'.");
            IServerPlayer player = Server.Server.GetClientByPlayername(playerName)?.Player
                ?? throw new InvalidOperationException($"The server has no player '{playerName}'.");

            ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
            slot.Itemstack = new ItemStack(block, 64);
            slot.MarkDirty();
            return block.BlockId;
        });
    }

    private int ClientBlockId(BlockPos pos) =>
        Client.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetBlock(pos).BlockId);

    private int ServerBlockId(BlockPos pos) =>
        Server.RunOnGameThread(() => Server.Server.World.BlockAccessor.GetBlock(pos).BlockId);
}
