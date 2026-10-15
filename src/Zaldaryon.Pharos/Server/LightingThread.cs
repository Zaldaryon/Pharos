using System.Collections;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Vintagestory.Server;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The server's lighting thread, which runs the relight queue. Its passes can be held between one another, so the queue
/// can be read or edited while no pass is taking an entry from it.
/// </summary>
internal static class LightingThread
{
    private const string ThreadName = "Relight";
    private static readonly FieldInfo? ThreadNameField = AccessTools.Field(typeof(ServerThread), "threadName");
    private static readonly FieldInfo? PausedField = AccessTools.Field(typeof(ServerThread), "paused");

    /// <summary>
    /// Asks the lighting thread to stop between passes and waits until it has stopped. Returns a scope that lets it run
    /// again when disposed, unless it had already been asked to stop.
    /// </summary>
    /// <param name="server">The server whose lighting thread to hold.</param>
    /// <param name="timeout">How long to wait for the thread to stop; 10 seconds by default, scaled by <c>PHAROS_TIMEOUT_SCALE</c>.</param>
    /// <exception cref="InvalidOperationException">The server has no running lighting thread.</exception>
    /// <exception cref="TimeoutException">The thread did not stop within <paramref name="timeout"/>.</exception>
    internal static IDisposable Pause(ServerMain server, TimeSpan? timeout = null)
    {
        ServerThread thread = Find(server) ?? throw new InvalidOperationException($"The server has no running {ThreadName} thread.");
        if (!thread.Alive) throw new InvalidOperationException($"The {ThreadName} thread has stopped.");

        bool askedBefore = thread.ShouldPause;
        thread.ShouldPause = true;
        TimeSpan wait = timeout ?? TimeSpan.FromSeconds(10 * ScenarioTimeouts.Scale);
        DateTime deadline = DateTime.UtcNow + wait;
        while (PausedField?.GetValue(thread) is not true)
        {
            if (DateTime.UtcNow > deadline)
            {
                if (!askedBefore) thread.ShouldPause = false;
                throw new TimeoutException($"The {ThreadName} thread did not pause within {wait.TotalSeconds:0.#} seconds.");
            }

            Thread.Sleep(1);
        }

        return new Hold(thread, resume: !askedBefore);
    }

    /// <summary>
    /// Removes the queued lighting tasks for positions in the chunks with the given indices, and keeps the rest in order.
    /// The thread must be held by <see cref="Pause"/>, or the queue would change under a pass.
    /// </summary>
    /// <returns>How many tasks were removed.</returns>
    internal static int DropQueued(ServerMain server, IReadOnlySet<long> chunks)
    {
        ServerWorldMap map = server.WorldMap;
        lock (map.LightingTasksLock)
        {
            UpdateLightingTask[] queued = [.. map.LightingTasks];
            UpdateLightingTask[] kept = [.. queued.Where(task => !chunks.Contains(ChunkIndex(map, task.pos)))];
            map.LightingTasks.Clear();
            foreach (UpdateLightingTask task in kept) map.LightingTasks.Enqueue(task);
            return queued.Length - kept.Length;
        }
    }

    private static long ChunkIndex(ServerWorldMap map, BlockPos pos) =>
        map.ChunkIndex3D(pos.X / GlobalConstants.ChunkSize, pos.InternalY / GlobalConstants.ChunkSize, pos.Z / GlobalConstants.ChunkSize);

    private static ServerThread? Find(ServerMain server)
    {
        if (AccessTools.Field(typeof(ServerMain), "ServerThreadLoops")?.GetValue(server) is not IEnumerable loops) return null;
        foreach (object? loop in loops)
        {
            if (loop is ServerThread thread && Equals(ThreadNameField?.GetValue(thread), ThreadName)) return thread;
        }

        return null;
    }

    private sealed class Hold(ServerThread thread, bool resume) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose()
        {
            if (resume) thread.ShouldPause = false;
        }
    }
}
