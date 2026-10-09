using System.Diagnostics;
using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Performance;

/// <summary>
/// What one engine-mode client does over a measured window of frames, recorded on its own thread.
/// </summary>
/// <remarks>
/// Every buffer is made before the window opens, so recording allocates nothing on the client
/// thread except for a renderer registered during the window. The collector is set on the client
/// thread itself (each engine client has a thread of its own), so the game's hooks find it without
/// a lookup, and it goes with the thread if the client dies.
/// </remarks>
internal sealed class FrameCollector
{
    [ThreadStatic]
    private static FrameCollector? t_active;

    private readonly int _capacity;
    private readonly long[] _work;
    private readonly long[] _mainThreadTasks;
    private readonly long[] _gameTick;
    private readonly long[][] _stages;
    private readonly int[] _stageCalls;
    private readonly int[] _stageFrames;
    private readonly bool[] _stageRanThisFrame;
    private readonly Dictionary<(IRenderer, EnumRenderStage), RendererSamples> _renderers = new(new RendererKeyComparer());

    private long _frameStart;
    private long _allocationsAtStart;

    public FrameCollector(int frames, ClientEventManager? events)
    {
        _capacity = frames;
        _work = new long[frames];
        _mainThreadTasks = new long[frames];
        _gameTick = new long[frames];
        int stageCount = Enum.GetValues<EnumRenderStage>().Length;
        _stages = new long[stageCount][];
        for (int i = 0; i < stageCount; i++) _stages[i] = new long[frames];
        _stageCalls = new int[stageCount];
        _stageFrames = new int[stageCount];
        _stageRanThisFrame = new bool[stageCount];

        if (events == null) return;
        for (int stage = 0; stage < events.renderersByStage.Length && stage < stageCount; stage++)
        {
            foreach (RenderHandler handler in events.renderersByStage[stage])
            {
                _renderers.TryAdd((handler.Renderer, (EnumRenderStage)stage), new RendererSamples(frames));
            }
        }
    }

    /// <summary>The collector of the calling client thread, if a window is open on it.</summary>
    public static FrameCollector? Active => t_active;

    /// <summary>How many frames were recorded.</summary>
    public int Frames { get; private set; }

    /// <summary>Bytes the client thread allocated during the recorded frames.</summary>
    public long AllocatedBytes { get; private set; }

    public void Open() => t_active = this;

    public static void Close() => t_active = null;

    public void BeginFrame()
    {
        if (Frames >= _capacity) return;

        // A frame that threw part-way left its time here; it does not count.
        Array.Clear(_stageRanThisFrame);
        _mainThreadTasks[Frames] = 0;
        _gameTick[Frames] = 0;
        foreach (long[] stage in _stages) stage[Frames] = 0;
        foreach (RendererSamples samples in _renderers.Values) samples.Ticks[Frames] = 0;
        _allocationsAtStart = GC.GetAllocatedBytesForCurrentThread();
        _frameStart = Stopwatch.GetTimestamp();
    }

    public void EndFrame()
    {
        if (Frames >= _capacity) return;
        _work[Frames] = Stopwatch.GetTimestamp() - _frameStart;
        AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - _allocationsAtStart;
        for (int i = 0; i < _stageRanThisFrame.Length; i++)
        {
            if (_stageRanThisFrame[i]) _stageFrames[i]++;
        }

        Frames++;
    }

    public void MainThreadTasks(long ticks)
    {
        if (Frames < _capacity) _mainThreadTasks[Frames] += ticks;
    }

    public void GameTick(long ticks)
    {
        if (Frames < _capacity) _gameTick[Frames] += ticks;
    }

    public void Stage(EnumRenderStage stage, long ticks)
    {
        if (Frames >= _capacity || (int)stage >= _stages.Length) return;
        _stages[(int)stage][Frames] += ticks;
        _stageCalls[(int)stage]++;
        _stageRanThisFrame[(int)stage] = true;
    }

    public void Renderer(IRenderer renderer, EnumRenderStage stage, long ticks)
    {
        if (Frames >= _capacity) return;
        if (!_renderers.TryGetValue((renderer, stage), out RendererSamples? samples))
        {
            samples = new RendererSamples(_capacity);
            _renderers[(renderer, stage)] = samples;
        }

        samples.Ticks[Frames] += ticks;
        samples.Calls++;
    }

    public TimingStats Work() => TimingStats.FromTicks(_work, Frames);

    public TimingStats MainThreadTaskTimes() => TimingStats.FromTicks(_mainThreadTasks, Frames);

    public TimingStats GameTickTimes() => TimingStats.FromTicks(_gameTick, Frames);

    public IEnumerable<(EnumRenderStage Stage, TimingStats PerFrame, int FramesRun, int Calls)> Stages()
    {
        for (int i = 0; i < _stages.Length; i++)
        {
            if (_stageCalls[i] > 0) yield return ((EnumRenderStage)i, TimingStats.FromTicks(_stages[i], Frames), _stageFrames[i], _stageCalls[i]);
        }
    }

    public IEnumerable<(IRenderer Renderer, EnumRenderStage Stage, TimingStats PerFrame, int Calls)> Renderers() =>
        _renderers.Where(r => r.Value.Calls > 0).Select(r => (r.Key.Item1, r.Key.Item2, TimingStats.FromTicks(r.Value.Ticks, Frames), r.Value.Calls));

    private sealed class RendererSamples(int frames)
    {
        public long[] Ticks { get; } = new long[frames];

        public int Calls { get; set; }
    }

    private sealed class RendererKeyComparer : IEqualityComparer<(IRenderer Renderer, EnumRenderStage Stage)>
    {
        public bool Equals((IRenderer Renderer, EnumRenderStage Stage) x, (IRenderer Renderer, EnumRenderStage Stage) y) =>
            ReferenceEquals(x.Renderer, y.Renderer) && x.Stage == y.Stage;

        public int GetHashCode((IRenderer Renderer, EnumRenderStage Stage) key) => HashCode.Combine(RuntimeHelpers.GetHashCode(key.Renderer), key.Stage);
    }
}

/// <summary>What an embedded server does in each measured tick, recorded on its game thread.</summary>
internal sealed class ServerTickCollector
{
    [ThreadStatic]
    private static ServerTickCollector? t_active;

    [ThreadStatic]
    private static long t_tickStart;

    [ThreadStatic]
    private static long t_allocationsAtStart;

    [ThreadStatic]
    private static bool t_pending;

    private readonly List<long> _ticks;

    public ServerTickCollector(int expectedTicks)
    {
        _ticks = new List<long>(expectedTicks);
    }

    /// <summary>Bytes the server thread allocated in the measured ticks.</summary>
    public long AllocatedBytes { get; private set; }

    /// <summary>Ticks the server spent suspended, as during an autosave, which are not measured.</summary>
    public int SuspendedTicks { get; private set; }

    public void Open()
    {
        if (t_active != null) throw new InvalidOperationException("This server's ticks are already being measured.");
        t_active = this;
    }

    public static void Close()
    {
        t_active = null;
        t_pending = false;
    }

    public TimingStats Ticks() => TimingStats.FromTicks([.. _ticks], _ticks.Count);

    // ServerMain.Process starts. A suspended server only sleeps: no tick to count.
    internal static void TickStarting(bool suspended)
    {
        if (t_active is not { } collector) return;
        if (suspended)
        {
            collector.SuspendedTicks++;
            return;
        }

        t_allocationsAtStart = GC.GetAllocatedBytesForCurrentThread();
        t_tickStart = Stopwatch.GetTimestamp();
        t_pending = true;
    }

    // The tick's work is done: at its sleep, or at its end when it does not sleep.
    internal static void TickWorkDone()
    {
        if (!t_pending || t_active is not { } collector) return;
        t_pending = false;
        collector._ticks.Add(Stopwatch.GetTimestamp() - t_tickStart);
        collector.AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - t_allocationsAtStart;
    }
}
