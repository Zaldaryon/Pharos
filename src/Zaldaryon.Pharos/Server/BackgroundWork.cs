using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Waits until the server's background threads have no work queued: lighting, chunk loading and
/// generation, random block ticks, the creature spawner and async pathfinding.
/// </summary>
/// <remarks>
/// <para>
/// These threads keep their own clocks, so they run on real time whatever drives the game thread.
/// Each is idle when its queue is empty and one whole pass of its thread has started and finished
/// since the queue was last seen holding work, or since the wait began. The passes are counted by
/// hooks on the methods that do the work, installed once per process before any server thread
/// runs, from <see cref="Platform.HeadlessPlatformResolver"/>.
/// </para>
/// <para>
/// What a pass hands to the game thread (random ticks to run, creatures to spawn, chunk columns
/// loaded) is done there on the next ticks.
/// </para>
/// </remarks>
internal static class BackgroundWork
{
    /// <summary>The kinds of background work, also indexes into the pass counters.</summary>
    internal enum Kind
    {
        Lighting,
        Chunks,
        RandomTicks,
        Spawner,
        Pathfinding,
    }

    private const string HarmonyId = "zaldaryon.pharos.backgroundwork";
    private static readonly object s_lock = new();
    private static readonly Harmony s_harmony = new(HarmonyId);
    private static readonly ConcurrentDictionary<MethodBase, Kind> s_kinds = new();
    private static readonly HashSet<Assembly> s_essentialsPatched = [];
    private static readonly long[] s_started = new long[5];
    private static readonly long[] s_finished = new long[5];
    private static bool s_installed;

    /// <summary>Whether the async pathfinder's passes are counted: the essentials mod has loaded.</summary>
    internal static bool PathfindingCounted { get; private set; }

    /// <summary>Installs the pass counters; does nothing the second time.</summary>
    public static void Install()
    {
        lock (s_lock)
        {
            if (s_installed) return;
            s_installed = true;

            Count(Kind.Lighting, AccessTools.Method(typeof(ServerSystemRelight), nameof(ServerSystemRelight.ProcessLightingQueue)));
            Count(Kind.Chunks, AccessTools.Method(AccessTools.TypeByName("Vintagestory.Server.ServerSystemSupplyChunks"), "OnSeparateThreadTick"));
            Count(Kind.RandomTicks, AccessTools.Method(typeof(ServerSystemBlockSimulation), nameof(ServerSystemBlockSimulation.OnSeparateThreadTick)));
            Count(Kind.Spawner, AccessTools.Method(typeof(ServerSystemEntitySpawner), "FindMobSpawnPositions_offthread"));

            // The pathfinder lives in the essentials mod, which the server's mod loader loads at
            // boot: its pass is hooked as soon as it loads, before the pathfinding thread starts.
            AppDomain.CurrentDomain.AssemblyLoad += (_, args) => CountPathfinding(args.LoadedAssembly);
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) CountPathfinding(assembly);
        }
    }

    private static void CountPathfinding(Assembly assembly)
    {
        if (assembly.GetName().Name != "VSEssentials") return;
        lock (s_lock)
        {
            if (!s_essentialsPatched.Add(assembly)) return;
            try
            {
                Type? type = assembly.GetType("Vintagestory.Essentials.PathfindingAsync");
                if (type?.GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public) is { } method)
                {
                    Count(Kind.Pathfinding, method);
                    PathfindingCounted = true;
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Pharos could not count the async pathfinder's passes: {ex.Message}");
            }
        }
    }

    private static void Count(Kind kind, MethodBase? method)
    {
        if (method == null) return;
        try
        {
            s_kinds[method] = kind;
            s_harmony.Patch(method,
                prefix: new HarmonyMethod(typeof(BackgroundWork), nameof(PassStarting)),
                postfix: new HarmonyMethod(typeof(BackgroundWork), nameof(PassDone)));
        }
        catch (Exception ex)
        {
            s_kinds.TryRemove(method, out _);
            Trace.TraceWarning($"Pharos could not count the passes of {method.DeclaringType?.Name}.{method.Name}: {ex.Message}");
        }
    }

    private static void PassStarting(MethodBase __originalMethod)
    {
        if (s_kinds.TryGetValue(__originalMethod, out Kind kind)) Interlocked.Increment(ref s_started[(int)kind]);
    }

    private static void PassDone(MethodBase __originalMethod)
    {
        if (s_kinds.TryGetValue(__originalMethod, out Kind kind)) Interlocked.Increment(ref s_finished[(int)kind]);
    }

    internal static long Started(Kind kind) => Interlocked.Read(ref s_started[(int)kind]);

    internal static long Finished(Kind kind) => Interlocked.Read(ref s_finished[(int)kind]);

    /// <summary>
    /// Waits until every background system of <paramref name="server"/> whose thread runs is
    /// idle. Polls from the calling thread, which must not be the server's game thread.
    /// </summary>
    /// <exception cref="TimeoutException">A system still had work after <paramref name="timeout"/>.</exception>
    public static async Task WaitAsync(ServerMain server, TimeSpan timeout, CancellationToken ct, Kind? only = null)
    {
        Install();
        List<Probe> probes = Probes(server).Where(p => only is not { } kind || p.Kind == kind).ToList();
        Stopwatch waited = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            bool idle = true;
            foreach (Probe probe in probes) idle &= probe.Poll();
            if (idle) return;

            if (waited.Elapsed >= timeout)
            {
                StringBuilder busy = new();
                foreach (Probe probe in probes.Where(p => !p.Idle))
                {
                    if (busy.Length > 0) busy.Append(", ");
                    busy.Append(probe.Describe());
                }

                string[] paused = PausedThreads(server);
                string suspended = server.Suspended ? " The server is suspended, so its threads do not run."
                    : paused.Length > 0 ? $" These server threads are paused: {string.Join(", ", paused)}."
                    : "";
                throw new TimeoutException($"The server's background work did not settle within {timeout.TotalSeconds:0.#} s: {busy}.{suspended}");
            }

            await Task.Delay(5, ct).ConfigureAwait(false);
        }
    }

    // One probe per system whose thread is alive.
    private static List<Probe> Probes(ServerMain server)
    {
        List<Probe> probes = [];
        ServerWorldMap map = server.WorldMap;
        AddIfThreadAlive(server, probes, "Relight", Kind.Lighting, "lighting", () =>
        {
            lock (map.LightingTasksLock) return map.LightingTasks.Count;
        });

        object? chunkThread = AccessTools.Field(typeof(ServerMain), "chunkThread")?.GetValue(server);
        FieldInfo? requestsField = AccessTools.Field(typeof(ServerMain), "requestedChunkColumns");
        FieldInfo? requestsLockField = AccessTools.Field(typeof(ServerMain), "requestedChunkColumnsLock");
        FieldInfo? generatingField = chunkThread == null ? null : AccessTools.Field(chunkThread.GetType(), "requestedChunkColumns");
        AddIfThreadAlive(server, probes, "chunkdbthread", Kind.Chunks, "chunk loading and generation", () =>
        {
            int requested;
            lock (requestsLockField?.GetValue(server) ?? new object()) requested = CountOf(requestsField?.GetValue(server));
            return requested + CountOf(generatingField?.GetValue(chunkThread));
        });

        AddIfThreadAlive(server, probes, "ServerBlockTicks", Kind.RandomTicks, "random block ticks", queueLength: null);

        // The spawner searches on a plain thread of its own, not a server thread loop.
        ServerSystemEntitySpawner? spawner = (AccessTools.Field(typeof(ServerMain), "Systems")?.GetValue(server) as ServerSystem[])?.OfType<ServerSystemEntitySpawner>().FirstOrDefault();
        bool multithreaded = spawner != null && AccessTools.Field(typeof(ServerSystemEntitySpawner), "multithreaded")?.GetValue(spawner) is true;
        bool spawnerThreadAlive = multithreaded && server.Serverthreads.Any(t => t is { Name: "physicsManagerHelper", IsAlive: true });
        if (spawnerThreadAlive) probes.Add(new Probe("creature spawner", Kind.Spawner, null));

        if (PathfindingCounted && ThreadAlive(server, "ai-pathfinding") && PathfinderQueue(server) is { } tasks)
        {
            probes.Add(new Probe("async pathfinding", Kind.Pathfinding, () => CountOf(tasks)));
        }

        return probes;
    }

    private static void AddIfThreadAlive(ServerMain server, List<Probe> probes, string threadName, Kind kind, string name, Func<int>? queueLength)
    {
        if (ThreadAlive(server, threadName)) probes.Add(new Probe(name, kind, queueLength));
    }

    // The server's thread loops by name: Relight, ServerBlockTicks, chunkdbthread, ai-pathfinding and others.
    private static bool ThreadAlive(ServerMain server, string threadName)
    {
        if (AccessTools.Field(typeof(ServerMain), "ServerThreadLoops")?.GetValue(server) is not IEnumerable loops) return false;
        FieldInfo? nameField = AccessTools.Field(typeof(ServerThread), "threadName");
        foreach (object? loop in loops)
        {
            if (loop is ServerThread thread && thread.Alive && Equals(nameField?.GetValue(thread), threadName)) return true;
        }

        return false;
    }

    // Threads asked to pause (ShouldPause) or paused: they run no passes.
    private static string[] PausedThreads(ServerMain server)
    {
        if (AccessTools.Field(typeof(ServerMain), "ServerThreadLoops")?.GetValue(server) is not IEnumerable loops) return [];
        FieldInfo? nameField = AccessTools.Field(typeof(ServerThread), "threadName");
        FieldInfo? pausedField = AccessTools.Field(typeof(ServerThread), "paused");
        List<string> paused = [];
        foreach (object? loop in loops)
        {
            if (loop is ServerThread { Alive: true } thread && (thread.ShouldPause || pausedField?.GetValue(thread) is true))
            {
                paused.Add(nameField?.GetValue(thread) as string ?? "?");
            }
        }

        return [.. paused];
    }

    private static object? PathfinderQueue(ServerMain server)
    {
        foreach (ModSystem system in server.Api.ModLoader.Systems)
        {
            if (system.GetType().FullName == "Vintagestory.Essentials.PathfindingAsync")
            {
                return system.GetType().GetField("PathfinderTasks", BindingFlags.Instance | BindingFlags.Public)?.GetValue(system);
            }
        }

        return null;
    }

    private static int CountOf(object? collection) => collection switch
    {
        null => 0,
        ICollection c => c.Count,
        _ => collection.GetType().GetProperty("Count")?.GetValue(collection) is int n ? n : 0,
    };

    /// <summary>One background system: idle when its queue is empty and a pass ran since it last held work.</summary>
    private sealed class Probe(string name, Kind kind, Func<int>? queueLength)
    {
        private long _required = Started(kind) + 1;
        private int _queued;

        public Kind Kind { get; } = kind;

        public bool Idle { get; private set; }

        public bool Poll()
        {
            _queued = queueLength?.Invoke() ?? 0;
            if (_queued > 0) _required = Started(kind) + 1;
            Idle = _queued == 0 && Finished(kind) >= _required;
            return Idle;
        }

        public string Describe() => queueLength == null ? $"{name} (a pass has not finished)" : $"{name} ({_queued} queued)";
    }
}
