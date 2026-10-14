using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Zaldaryon.Pharos.Server;

public sealed partial class EmbeddedServerHost
{
    /// <summary>
    /// Advances the server until every online non-spectator player is sleeping and the game calendar
    /// has advanced at least six in-game minutes under Vintage Story's sleeping system.
    /// </summary>
    /// <param name="timeout">The tick budget as a duration at 20 server ticks per second. Defaults to 30 seconds.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="TimeoutException">The players did not skip time before the timeout.</exception>
    public Task WaitForNightSkipAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(30);
        if (limit <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), limit, "Timeout must be positive.");

        double requestedTicks = Math.Ceiling(limit.TotalSeconds * 20);
        int maxTicks = requestedTicks >= int.MaxValue ? int.MaxValue : Math.Max(1, (int)requestedTicks);
        return WaitForNightSkipTicksAsync(maxTicks, ct);
    }

    /// <summary>
    /// Advances the server until every online non-spectator player is sleeping and the game calendar
    /// has advanced at least six in-game minutes under Vintage Story's sleeping system.
    /// </summary>
    /// <param name="maxTicks">The maximum server ticks to wait. Defaults to 600.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="TimeoutException">The players did not skip time before the tick limit.</exception>
    public async Task WaitForNightSkipTicksAsync(int maxTicks = 600, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);

        double? sleepingStartHours = null;
        bool IsNightSkipReady()
        {
            (bool everyoneSleeping, double totalHours) = ReadNightSkipState();
            if (!everyoneSleeping)
            {
                sleepingStartHours = null;
                return false;
            }

            sleepingStartHours ??= totalHours;
            return totalHours >= sleepingStartHours.Value + 0.1;
        }

        bool skipped = await TickUntilAsync(IsNightSkipReady, maxTicks, ct).ConfigureAwait(false);

        // TickUntilAsync checks before each tick and returns after the last tick. The final tick may
        // itself trigger the night skip, so inspect the resulting state once more before timing out.
        if (!skipped)
        {
            ct.ThrowIfCancellationRequested();
            skipped = IsNightSkipReady();
        }

        if (skipped) return;

        throw new TimeoutException(
            $"The night did not skip within {maxTicks} server ticks. All online non-spectator players must use a bed and the server calendar must advance.");
    }

    private (bool EveryoneSleeping, double TotalHours) ReadNightSkipState() => RunOnGameThread(() =>
    {
        IServerPlayer[] activePlayers = Server.AllOnlinePlayers
            .OfType<IServerPlayer>()
            .Where(player => player.ConnectionState == EnumClientState.Playing
                && player.WorldData?.CurrentGameMode != EnumGameMode.Spectator)
            .ToArray();

        return (
            activePlayers.Length > 0 && activePlayers.All(IsSleeping),
            Server.Calendar.TotalHours);
    });

    private static bool IsSleeping(IPlayer player)
    {
        object? behavior = player.Entity?.GetBehavior("tiredness");
        PropertyInfo? isSleeping = behavior?.GetType().GetProperty("IsSleeping", BindingFlags.Instance | BindingFlags.Public);
        return isSleeping?.GetValue(behavior) is true;
    }
}
