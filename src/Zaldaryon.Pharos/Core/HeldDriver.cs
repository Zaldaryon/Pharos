using Vintagestory.API.Common;
using Zaldaryon.Pharos.Items;
using Zaldaryon.Pharos.Player;

namespace Zaldaryon.Pharos.Core;

/// <summary>The item in the client's hand: the holds, and what the hooks did.</summary>
public sealed class ClientHeldDriver(HeadlessClient client)
{
    /// <summary>The frame time the holds are measured in, a sixtieth of a second.</summary>
    public const float FrameTime = 1f / 60f;

    /// <summary>Records the held-item hooks, until the scope is disposed.</summary>
    public HeldTraceScope Trace() => HeldTraceScope.Start();

    /// <summary>
    /// Holds the use control for a while, the way a player holds the right mouse button. The
    /// client's own mouse handling runs the item's held interaction.
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

        int frames = (int)Math.Ceiling(duration.TotalSeconds / FrameTime);
        if (frames > maxFrames)
        {
            throw new ArgumentOutOfRangeException(nameof(duration),
                $"A hold of {duration.TotalSeconds:0.###} s needs {frames} frames, more than the {maxFrames} allowed.");
        }

        client.Controls.Press(action);
        try
        {
            for (int frame = 0; frame < frames; frame++)
            {
                ct.ThrowIfCancellationRequested();
                client.FrameController.Step(FrameTime);
                await Task.Yield();
            }
        }
        finally
        {
            client.Controls.Release(action);
            client.FrameController.Step(FrameTime);
        }

        return true;
    }
}
