using System.Diagnostics;
using System.Reflection;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Performance;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Scenarios;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Metrics;

/// <summary>
/// Collects one scenario's metrics: custom readings from <see cref="Metrics"/> and the automatic
/// collectors named by <see cref="CollectMetricsAttribute"/>. Completing it writes
/// <c>result.json</c> and leaves the summary on the attempt state for <c>run.json</c>.
/// </summary>
internal sealed class ScenarioMetricsSession
{
    private readonly ScenarioTestInfo _test;
    private readonly Metric _automatic;
    private readonly MetricsCollector _collector = new();
    private readonly IDisposable _scope;
    private readonly IDisposable _bridge;
    private readonly DateTimeOffset _started = DateTimeOffset.UtcNow;
    private readonly Stopwatch _wall = Stopwatch.StartNew();
    private readonly HeadlessClient? _client;
    private readonly EmbeddedServerHost? _server;

    private readonly long _allocatedAtStart;
    private readonly long _heapAtStart;
    private readonly int[] _collectionsAtStart;
    private readonly GlCommandRecord? _glAtStart;
    private readonly bool _enabledGl;
    private readonly long _modMessageMark;

    private readonly List<double> _frameTimes = [];
    private readonly object _frameLock = new();
    private long _lastFrameTimestamp;
    private long _framesAtStart;

    private ServerTickCollector? _ticks;
    private bool _tickCollectorStarted;
    private bool _completed;

    private ScenarioMetricsSession(ScenarioTestInfo test, Metric automatic, HeadlessClient? client,
        EmbeddedServerHost? server, IsolationReport? isolation)
    {
        _test = test;
        _automatic = automatic;
        _client = client;
        _server = server;
        _scope = Metrics.BeginScope(_collector);
        try
        {
            _bridge = BridgeMetricSubscription.Subscribe(_collector);
            try
            {
                _allocatedAtStart = GC.GetTotalAllocatedBytes(precise: true);
                _heapAtStart = GC.GetTotalMemory(forceFullCollection: false);
                _collectionsAtStart = [GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2)];

                if (client != null)
                {
                    _framesAtStart = client.FrameController.TotalFrames;
                    if (automatic.HasFlag(Metric.Frame))
                    {
                        client.FrameController.OnFrameCompleted += OnFrameCompleted;
                    }

                    if (client.IsEngineMode)
                    {
                        _modMessageMark = client.ModNetwork.Mark();
                    }
                }

                if (automatic.HasFlag(Metric.Gl) && client?.GL.IsEnabled == true)
                {
                    _glAtStart = client.GL.Snapshot();
                }
                else if (automatic.HasFlag(Metric.Gl) && client is { IsEngineMode: true } engineClient)
                {
                    engineClient.GL.Enable();
                    _enabledGl = true;
                    _glAtStart = engineClient.GL.Snapshot();
                }

                if (automatic.HasFlag(Metric.Tick) && server is { IsRunning: true })
                {
                    try
                    {
                        server.RunOnGameThread(() =>
                        {
                            _ticks = new ServerTickCollector(1024);
                            _ticks.Open();
                            _tickCollectorStarted = true;
                        });
                    }
                    catch (InvalidOperationException)
                    {
                        // Another measurement owns the tick hooks; the test still gets custom metrics.
                    }
                }

                if (automatic.HasFlag(Metric.Boot) && isolation != null)
                {
                    _collector.Record("boot.isolation.ms", isolation.Duration.TotalMilliseconds,
                        MetricUnit.Milliseconds, MetricDirection.LowerIsBetter, MetricClock.Stopwatch,
                        Tags(("kind", isolation.Kind.ToString())));
                }
            }
            catch
            {
                _bridge.Dispose();
                throw;
            }
        }
        catch
        {
            _scope.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Starts collecting for <paramref name="test"/>; call before the body runs. Returns null when
    /// collecting cannot start, because metrics must never fail the test they measure.
    /// </summary>
    internal static ScenarioMetricsSession? Start(ScenarioTestInfo test, HeadlessClient? client,
        EmbeddedServerHost? server, IsolationReport? isolation = null)
    {
        ArgumentNullException.ThrowIfNull(test);
        try
        {
            Metric automatic = CollectMetricsAttribute.Resolve(test.TestClass, test.Method);
            return new ScenarioMetricsSession(test, automatic, client, server, isolation);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Pharos could not start metrics for {test.DisplayName}: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    /// <summary>The collector custom metrics are recorded into.</summary>
    internal MetricsCollector Collector => _collector;

    /// <summary>The automatic collectors requested by the test.</summary>
    internal Metric Automatic => _automatic;

    private void OnFrameCompleted(object? sender, Zaldaryon.Pharos.Timing.FrameCompletedEventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastFrameTimestamp == 0)
        {
            // The first frame only anchors the clock: the time before it is setup, not a frame.
            _lastFrameTimestamp = now;
            return;
        }

        double ms = Stopwatch.GetElapsedTime(_lastFrameTimestamp, now).TotalMilliseconds;
        _lastFrameTimestamp = now;
        lock (_frameLock)
        {
            _frameTimes.Add(ms);
        }
    }

    /// <summary>
    /// Stops collecting, writes <c>result.json</c> and leaves the summary for <c>run.json</c>.
    /// Safe to call more than once; a failure while collecting never fails the test.
    /// </summary>
    /// <param name="timedOut">Whether the body timed out, so live reads are skipped.</param>
    /// <param name="directory">
    /// The folder to write to, when a failed test already chose one for its artifacts.
    /// </param>
    internal void Complete(bool timedOut = false, string? directory = null)
    {
        if (_completed) return;
        _completed = true;

        try
        {
            if (_automatic.HasFlag(Metric.Frame) && _client != null)
            {
                _client.FrameController.OnFrameCompleted -= OnFrameCompleted;
            }

            CollectAutomatic();

            if (_automatic.HasFlag(Metric.Memory))
            {
                CollectMemory();
            }

            if (_automatic.HasFlag(Metric.Gl) && _client != null)
            {
                CollectGl();
            }

            if (_automatic.HasFlag(Metric.Network) && _client is { IsEngineMode: true })
            {
                CollectNetwork();
            }

            if (_automatic.HasFlag(Metric.World) && _server is { IsRunning: true })
            {
                CollectWorld(timedOut);
            }

            if (_server is { IsRunning: true } && _tickCollectorStarted)
            {
                StopTickCollector(timedOut);
            }

            if (_enabledGl) _client?.GL.Disable();

            _wall.Stop();
            _collector.Record("test.duration.ms", _wall.Elapsed.TotalMilliseconds,
                MetricUnit.Milliseconds, MetricDirection.LowerIsBetter, MetricClock.Stopwatch);

            MetricsWriter.Write(_test, _collector, _started, _wall.Elapsed, directory);
            ScenarioAttemptState.Current?.SetMetrics(_collector.Summaries());
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Pharos could not write metrics for {_test.DisplayName}: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            _bridge.Dispose();
            _scope.Dispose();
        }
    }

    private void CollectAutomatic()
    {
        if (!_automatic.HasFlag(Metric.Frame)) return;

        long frames = _client != null ? _client.FrameController.TotalFrames - _framesAtStart : 0;
        if (frames > 0)
        {
            _collector.Count("frame.count", frames);
        }

        double[] times;
        lock (_frameLock) times = [.. _frameTimes];
        foreach (double ms in times)
        {
            _collector.Record("frame.time.ms", ms, MetricUnit.Milliseconds,
                MetricDirection.LowerIsBetter, MetricClock.Frame);
        }
    }

    private void CollectMemory()
    {
        long allocated = GC.GetTotalAllocatedBytes(precise: true) - _allocatedAtStart;
        long heap = GC.GetTotalMemory(forceFullCollection: false);
        _collector.Bytes("memory.allocated.bytes", allocated, MetricDirection.LowerIsBetter);
        _collector.Bytes("memory.heap.bytes", heap - _heapAtStart, MetricDirection.LowerIsBetter);
        _collector.Count("memory.gc0", GC.CollectionCount(0) - _collectionsAtStart[0], MetricDirection.LowerIsBetter);
        _collector.Count("memory.gc1", GC.CollectionCount(1) - _collectionsAtStart[1], MetricDirection.LowerIsBetter);
        _collector.Count("memory.gc2", GC.CollectionCount(2) - _collectionsAtStart[2], MetricDirection.LowerIsBetter);
    }

    private void CollectGl()
    {
        if (_client!.GL.IsEnabled != true && !_enabledGl) return;
        GlCommandRecord now = _client.GL.Snapshot();
        GlCommandRecord start = _glAtStart ?? GlCommandRecord.Empty;
        _collector.Count("gl.drawcalls", now.TotalDrawCalls - start.TotalDrawCalls, MetricDirection.LowerIsBetter);
        _collector.Count("gl.drawcalls.direct", now.DrawCalls - start.DrawCalls, MetricDirection.LowerIsBetter);
        _collector.Count("gl.drawcalls.multi", now.MultiDrawCalls - start.MultiDrawCalls, MetricDirection.LowerIsBetter);
        _collector.Count("gl.drawcalls.indirect", now.IndirectDrawCalls - start.IndirectDrawCalls, MetricDirection.LowerIsBetter);
        _collector.Count("gl.buffers.created", now.BufferAllocations - start.BufferAllocations, MetricDirection.LowerIsBetter);
        _collector.Count("gl.buffers.deleted", now.BufferDeletions - start.BufferDeletions, MetricDirection.LowerIsBetter);
        _collector.Count("gl.textures.created", now.TextureAllocations - start.TextureAllocations, MetricDirection.LowerIsBetter);
        _collector.Count("gl.textures.deleted", now.TextureDeletions - start.TextureDeletions, MetricDirection.LowerIsBetter);
        _collector.Count("gl.framebuffers.created", now.FramebufferAllocations - start.FramebufferAllocations, MetricDirection.LowerIsBetter);
        _collector.Count("gl.errors", now.Errors - start.Errors, MetricDirection.LowerIsBetter);
    }

    private void CollectNetwork()
    {
        IReadOnlyList<Network.ModMessage> messages = _client!.ModNetwork.Messages()
            .Where(m => m.Sequence > _modMessageMark)
            .ToList();
        foreach (IGrouping<(Network.ModMessageDirection Direction, string Channel), Network.ModMessage> group in
                 messages.GroupBy(m => (m.Direction, m.Channel)))
        {
            string direction = group.Key.Direction == Network.ModMessageDirection.Sent ? "sent" : "received";
            IReadOnlyDictionary<string, string> tags = Tags(("channel", group.Key.Channel), ("direction", direction));
            _collector.Count($"net.channel.{group.Key.Channel}.{direction}.packets", group.Count(), MetricDirection.LowerIsBetter, tags);
            _collector.Bytes($"net.channel.{group.Key.Channel}.{direction}.bytes", group.Sum(m => m.Data.LongLength),
                MetricDirection.LowerIsBetter, tags);
        }

        _collector.Count("net.messages", messages.Count, MetricDirection.LowerIsBetter);
        _collector.Bytes("net.bytes", messages.Sum(m => m.Data.LongLength), MetricDirection.LowerIsBetter);
    }

    private void CollectWorld(bool timedOut)
    {
        if (timedOut) return;
        try
        {
            WorldCounts counts = _server!.RunOnGameThread(() =>
            {
                ICoreServerAPI api = _server.Api;
                int chunks = api.WorldManager.AllLoadedChunks.Count;
                int mapChunks = api.WorldManager.AllLoadedMapchunks.Count;
                int mapRegions = api.WorldManager.AllLoadedMapRegions.Count;
                int entities = api.World.LoadedEntities.Count;
                int blockEntities = 0;
                foreach (object loaded in api.WorldManager.AllLoadedChunks.Values)
                {
                    if (loaded is Vintagestory.API.Server.IServerChunk chunk) blockEntities += chunk.BlockEntities.Count;
                }

                Dictionary<string, int> byCode = new(StringComparer.Ordinal);
                foreach (Vintagestory.API.Common.Entities.Entity entity in api.World.LoadedEntities.Values)
                {
                    string code = entity.Code?.ToString() ?? "unknown";
                    byCode[code] = byCode.TryGetValue(code, out int seen) ? seen + 1 : 1;
                }

                int listeners = _server.TickListeners.All().Count;
                return new WorldCounts(chunks, mapChunks, mapRegions, entities, blockEntities, byCode, listeners);
            });

            _collector.Count("world.chunks", counts.Chunks);
            _collector.Count("world.mapchunks", counts.MapChunks);
            _collector.Count("world.regions", counts.MapRegions);
            _collector.Count("world.entities", counts.Entities);
            _collector.Count("world.blockentities", counts.BlockEntities);
            _collector.Count("world.listeners", counts.Listeners);
            foreach ((string code, int count) in counts.EntitiesByCode)
            {
                _collector.Count($"world.entity.{code}", count);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or TargetInvocationException)
        {
            Trace.TraceWarning($"Pharos could not read world counts: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private void StopTickCollector(bool timedOut)
    {
        try
        {
            ServerTickMeasurement? measured = _server!.RunOnGameThread(() =>
            {
                if (_ticks == null) return null;
                ServerTickMeasurement result = new(_ticks.Ticks(), _ticks.SystemTimes(), _ticks.ListenerTimes());
                ServerTickCollector.Close();
                _tickCollectorStarted = false;
                return result;
            });

            if (timedOut || measured == null) return;
            foreach (TimeSpan tick in measured.Ticks.Samples)
            {
                _collector.Record("server.tick.ms", tick.TotalMilliseconds, MetricUnit.Milliseconds,
                    MetricDirection.LowerIsBetter, MetricClock.Tick);
            }

            _collector.Count("server.ticks", measured.Ticks.Count);
            RecordTickBreakdown(measured.SystemTimes, "tick.system");
            RecordTickBreakdown(measured.ListenerTimes, "tick.listener");
        }
        catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException or TargetInvocationException)
        {
            Trace.TraceWarning($"Pharos could not read server tick metrics: {ex.GetType().Name}: {ex.Message}");
            try { _server?.RunOnGameThread(ServerTickCollector.Close); } catch { /* best effort */ }
            _tickCollectorStarted = false;
        }
    }

    private void RecordTickBreakdown(IReadOnlyDictionary<string, IReadOnlyList<double>> times, string prefix)
    {
        foreach ((string name, IReadOnlyList<double> samples) in times)
        {
            foreach (double milliseconds in samples)
            {
                _collector.Record($"{prefix}.{name}.ms", milliseconds, MetricUnit.Milliseconds,
                    MetricDirection.LowerIsBetter, MetricClock.Tick);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> Tags(params (string Key, string Value)[] tags)
    {
        Dictionary<string, string> map = new(StringComparer.Ordinal);
        foreach ((string key, string value) in tags) map[key] = value;
        return map;
    }

    private readonly record struct WorldCounts(int Chunks, int MapChunks, int MapRegions, int Entities, int BlockEntities,
        IReadOnlyDictionary<string, int> EntitiesByCode, int Listeners);

    private sealed record ServerTickMeasurement(
        TimingStats Ticks,
        IReadOnlyDictionary<string, IReadOnlyList<double>> SystemTimes,
        IReadOnlyDictionary<string, IReadOnlyList<double>> ListenerTimes);
}
