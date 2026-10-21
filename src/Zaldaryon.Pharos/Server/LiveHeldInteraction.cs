using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Items;
using Zaldaryon.Pharos.Player;

namespace Zaldaryon.Pharos.Server;

/// <summary>The holds a client makes with the item in its hand, in a client-server scenario.</summary>
public sealed class LiveHeldInteraction
{
    private readonly ClientServerLoopbackSession _session;

    internal LiveHeldInteraction(ClientServerLoopbackSession session) => _session = session;

    /// <summary>Records the held-item hooks, until the scope is disposed.</summary>
    public HeldTraceScope Trace() => HeldTraceScope.Start();

    /// <summary>
    /// Holds the use control for a while, the way a player holds the right mouse button, and steps
    /// the client and the server in lockstep while it is down.
    /// </summary>
    /// <param name="duration">How long the button is held.</param>
    /// <param name="maxFrames">The most frames the hold may take.</param>
    /// <param name="ct">Cancels the hold.</param>
    /// <returns>Whether the hold ran for the whole duration.</returns>
    public Task<bool> UseAsync(TimeSpan duration, int maxFrames = 600, CancellationToken ct = default) =>
        HoldAsync(PlayerAction.Use, duration, maxFrames, ct);

    /// <summary>
    /// Holds the attack control for a while, the way a player holds the left mouse button.
    /// </summary>
    /// <param name="duration">How long the button is held.</param>
    /// <param name="maxFrames">The most frames the hold may take.</param>
    /// <param name="ct">Cancels the hold.</param>
    /// <returns>Whether the hold ran for the whole duration.</returns>
    public Task<bool> AttackAsync(TimeSpan duration, int maxFrames = 600, CancellationToken ct = default) =>
        HoldAsync(PlayerAction.Attack, duration, maxFrames, ct);

    private async Task<bool> HoldAsync(PlayerAction action, TimeSpan duration, int maxFrames, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(duration.TotalSeconds);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrames);

        int frames = (int)Math.Ceiling(duration.TotalSeconds / ClientHeldDriver.FrameTime);
        if (frames > maxFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(duration),
                $"A hold of {duration.TotalSeconds:0.###} s needs {frames} frames, more than the {maxFrames} allowed.");
        }

        await _session.HoldAsync(action, frames, ct).ConfigureAwait(false);
        return true;
    }
}
