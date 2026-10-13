using System.Diagnostics;
using System.Reflection;
using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.Server;

public sealed partial class EmbeddedServerHost
{
    /// <summary>
    /// Advances the server until every online survival player is sleeping and the game calendar
    /// has advanced at least six in-game minutes under Vintage Story's sleeping system.
    /// </summary>
    /// <param name="timeout">How long to wait. Defaults to 30 seconds.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="TimeoutException">The players did not skip time before the timeout.</exception>
    public async Task WaitForNightSkipAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(30);
        if (limit <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout), limit, "Timeout must be positive.");

        double startHours = Calendar.TotalHours;
        Stopwatch elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < limit)
        {
            ct.ThrowIfCancellationRequested();
            Tick();

            bool skipped = RunOnGameThread(() =>
            {
                IPlayer[] activePlayers = Server.AllOnlinePlayers
                    .Where(player => player.Entity is not null
                        && player.WorldData?.CurrentGameMode is not (EnumGameMode.Creative or EnumGameMode.Spectator))
                    .ToArray();

                return activePlayers.Length > 0
                    && activePlayers.All(IsSleeping)
                    && Server.Calendar.TotalHours >= startHours + 0.1;
            });

            if (skipped) return;
            await Task.Delay(TimeSpan.FromMilliseconds(20), ct).ConfigureAwait(false);
        }

        throw new TimeoutException(
            $"The night did not skip within {limit}. All online survival players must use a bed and the server calendar must advance.");
    }

    private static bool IsSleeping(IPlayer player)
    {
        object? behavior = player.Entity?.GetBehavior("tiredness");
        PropertyInfo? isSleeping = behavior?.GetType().GetProperty("IsSleeping", BindingFlags.Instance | BindingFlags.Public);
        return isSleeping?.GetValue(behavior) is true;
    }
}
