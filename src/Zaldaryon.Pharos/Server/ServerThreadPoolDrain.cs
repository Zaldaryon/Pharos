using System.Diagnostics;
using System.Reflection;
using System.Threading;
using Vintagestory.API.Common;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>Waits briefly for Vintage Story thread-pool callbacks during server teardown.</summary>
internal sealed class ServerThreadPoolDrain(ServerMain server, ILogger? logger) : ServerSystem(server)
{
    private const int TimeoutMs = 12000;
    private const int PollMs = 10;
    private const int QuietMs = 250;

    /// <summary>Appends a final system so callbacks queued by other systems are drained before the pool is cleared.</summary>
    internal static bool TryInstallBeforeThreadPoolDisposal(ServerMain server, ILogger? logger)
    {
        try
        {
            FieldInfo systemsField = typeof(ServerMain).GetField("Systems", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException(typeof(ServerMain).FullName, "Systems");
            ServerSystem[] systems = (ServerSystem[]?)systemsField.GetValue(server)
                ?? throw new InvalidOperationException("ServerMain.Systems was null.");
            Array.Resize(ref systems, systems.Length + 1);
            systems[^1] = new ServerThreadPoolDrain(server, logger);
            systemsField.SetValue(server, systems);
            return true;
        }
        catch (Exception error)
        {
            logger?.Warning("Pharos thread pool drain could not attach to server system disposal: {0}", error.Message);
            return false;
        }
    }

    public override void Dispose()
    {
        if (!WaitForQuietPeriod()) LogTimeout(logger, "during server system disposal");
    }

    /// <summary>
    /// Waits for a quiet thread-pool interval with an available worker, within a twelve-second
    /// drain window for this phase.
    /// </summary>
    /// <remarks>
    /// The game exposes running callback names and a separate queued-work count, not an atomic
    /// shutdown signal. This is a best-effort quiet-period check; callbacks can still be submitted
    /// by external producers after the check observes an empty pool.
    /// </remarks>
    internal bool WaitForQuietPeriod()
    {
        Stopwatch elapsed = Stopwatch.StartNew();
        int quiet = 0;
        while (elapsed.ElapsedMilliseconds < TimeoutMs)
        {
            ThreadPool.GetAvailableThreads(out int availableWorkers, out _);
            ThreadPool.GetMaxThreads(out int maxWorkers, out _);
            if (availableWorkers == maxWorkers
                && TyronThreadPool.Inst.RunningTasks.IsEmpty
                && ThreadPool.PendingWorkItemCount == 0)
            {
                quiet += PollMs;
                if (quiet >= QuietMs) return true;
            }
            else
            {
                quiet = 0;
            }

            Thread.Sleep(PollMs);
        }

        return false;
    }

    /// <summary>Reports that this phase's drain window expired before the pool became quiet.</summary>
    internal void LogTimeout(ILogger? logger, string phase)
    {
        ThreadPool.GetAvailableThreads(out int workersAvailable, out _);
        logger?.Warning(
            "Pharos thread pool did not become idle within {0}ms {1}: {2} queued, " +
            "{3} workers available; {4}",
            TimeoutMs,
            phase,
            ThreadPool.PendingWorkItemCount,
            workersAvailable,
            TyronThreadPool.Inst.ListAllRunningTasks());
    }
}
