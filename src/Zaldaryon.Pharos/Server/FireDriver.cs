using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>The state of one block position that may hold a fire.</summary>
/// <param name="Block">The block code at the position.</param>
/// <param name="Burning">Whether the fire is burning: it spreads to nearby fuel and uses up its own.</param>
public sealed record FireState(string Block, bool Burning);

/// <summary>Starts fires and runs their spread on the server: the game's fire block and its burning behaviour.</summary>
public sealed class FireDriver
{
    // The fire block and its burning behaviour live in the game's content assemblies, which Pharos does not reference.
    private const string FireBlock = "fire";
    private const string BurningBehavior = "BEBehaviorBurning";

    // The game's BEBehaviorBurning.TrySpreadTo treats a block below this replaceable value as a wall to the fire.
    private const int ReplaceableThreshold = 6000;
    private readonly EmbeddedServerHost _host;

    internal FireDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// Places a fire at a position and sets it burning on the fuel at the side <paramref name="fuelSide"/>, as the game does
    /// when a player lights a fuel block. If that side is not fuel, the game picks another fuel next to the fire, or burns out.
    /// </summary>
    /// <param name="pos">Where the fire goes. The block there is replaced.</param>
    /// <param name="fuelSide">The side of <paramref name="pos"/> that holds the fuel; down by default.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pos"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Called on the game thread, the server is not running, the game's fire behaviour is missing, or the block at
    /// <paramref name="pos"/> is not replaceable. The game's spread applies the same check, with the same threshold.
    /// </exception>
    public void Ignite(BlockPos pos, BlockFacing? fuelSide = null)
    {
        ArgumentNullException.ThrowIfNull(pos);
        if (_host.IsOnGameThread) throw new InvalidOperationException("Ignite waits for the game thread and cannot run on it.");
        if (!_host.IsRunning) throw new InvalidOperationException("The server is not running.");

        // The game's own call: OnFirePlaced takes the side the fire was lit from, so the fuel sits on the opposite side.
        BlockFacing fuel = fuelSide ?? BlockFacing.DOWN;
        _host.RunOnGameThread(() =>
        {
            ICoreServerAPI api = ServerApi;
            if (api.World.BlockAccessor.GetBlock(pos).Replaceable < ReplaceableThreshold)
            {
                throw new InvalidOperationException($"The block at {pos} is not replaceable, so a fire cannot go there.");
            }

            Block fireBlock = api.World.GetBlock(new AssetLocation(FireBlock))
                ?? throw new InvalidOperationException($"The game has no {FireBlock} block.");
            api.World.BlockAccessor.SetBlock(fireBlock.BlockId, pos);
            object behavior = BurningBehaviorOf(api.World.BlockAccessor.GetBlockEntity(pos))
                ?? throw new InvalidOperationException($"The fire at {pos} has no {BurningBehavior} behaviour.");
            var onFirePlaced = behavior.GetType().GetMethod("OnFirePlaced", [typeof(BlockFacing), typeof(string)])
                ?? throw new MissingMethodException(behavior.GetType().FullName, "OnFirePlaced");
            onFirePlaced.Invoke(behavior, [fuel.Opposite, null]);
        });
    }

    /// <summary>Reads the block at a position and whether a fire there is burning.</summary>
    /// <param name="pos">The block position.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pos"/> is null.</exception>
    public FireState State(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return _host.RunOnGameThread(() =>
        {
            ICoreServerAPI api = ServerApi;
            string code = api.World.BlockAccessor.GetBlock(pos).Code.ToString();
            object? behavior = BurningBehaviorOf(api.World.BlockAccessor.GetBlockEntity(pos));
            bool burning = behavior is not null && ProcessingDriver.Member(behavior, "IsBurning") is true;
            return new FireState(code, burning);
        });
    }

    /// <summary>
    /// Advances the server by <paramref name="ticks"/> ticks, so fires burn and spread. The spread
    /// draws come from the seeded random generators, so a seeded test replays the same spread.
    /// </summary>
    /// <remarks>
    /// The ticks run on the calling thread, one at a time, and the returned task is already complete. Errors, including
    /// cancellation, are thrown synchronously, before any tick the call has not yet run.
    /// </remarks>
    /// <param name="ticks">The number of ticks to run; zero runs none.</param>
    /// <param name="ct">Cancellation token, checked before each tick.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticks"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">Called on the server's game thread.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled.</exception>
    public Task SpreadAsync(int ticks, CancellationToken ct = default)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "Tick count must be non-negative.");
        if (_host.IsOnGameThread) throw new InvalidOperationException("SpreadAsync advances the server and cannot run on its game thread.");
        for (int tick = 0; tick < ticks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            _host.Ticks(1);
        }

        return Task.CompletedTask;
    }

    private ICoreServerAPI ServerApi => (ICoreServerAPI)_host.Server.Api;

    private static object? BurningBehaviorOf(BlockEntity? be) =>
        be?.Behaviors.FirstOrDefault(b => b.GetType().Name == BurningBehavior);
}
