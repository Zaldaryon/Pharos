using System.Globalization;
using System.Text;
using Vintagestory.API.Client;
using Zaldaryon.Pharos.Inspection;

namespace Zaldaryon.Pharos.Performance;

/// <summary>How long one render stage took per frame.</summary>
/// <param name="Stage">The stage.</param>
/// <param name="PerFrame">Its time in each measured frame; 0 in frames it did not run.</param>
/// <param name="FramesRun">In how many frames it ran.</param>
/// <param name="Calls">How many times it ran.</param>
public sealed record StageTiming(EnumRenderStage Stage, TimingStats PerFrame, int FramesRun, int Calls);

/// <summary>How long one renderer took per frame.</summary>
/// <param name="Renderer">The renderer, as <see cref="RendererInspector"/> lists it.</param>
/// <param name="PerFrame">Its time in each measured frame; 0 in frames it was not called.</param>
/// <param name="Calls">How many times it was called.</param>
public sealed record RendererTiming(RendererInfo Renderer, TimingStats PerFrame, int Calls);

/// <summary>What an embedded server did in the measured ticks.</summary>
/// <param name="Work">Each tick's work, up to where the server sleeps out the rest of its tick.</param>
/// <param name="AllocatedBytes">Bytes the server's game thread allocated in them.</param>
/// <param name="SuspendedTicks">
/// Ticks stepped while the server was suspended, as during an autosave: it only slept in them, so
/// they are not in <paramref name="Work"/>.
/// </param>
public sealed record ServerTickTiming(TimingStats Work, long AllocatedBytes, int SuspendedTicks);

/// <summary>
/// What a client did over some frames: how long each frame's work took, where that time went,
/// and how much it allocated. See <c>docs/inspection-api.md</c>.
/// </summary>
/// <remarks>
/// <para>
/// Times are wall-clock time on the client thread, taken frame by frame as Pharos steps them: there
/// is no idle wait to leave out, but a busy machine stretches them. Work other threads do is not in them: chunk tessellation, the network
/// thread, async particles, the server. GL calls return before the GPU (or Mesa's rasteriser
/// threads) has drawn, so a renderer is charged for submitting its work, and one that waits on the
/// GPU can be charged for earlier ones.
/// </para>
/// <para>
/// <see cref="AllocatedBytes"/> counts the client thread only; the GC counts are the process's,
/// the server's included.
/// </para>
/// </remarks>
public sealed class FrameMeasurement
{
    internal FrameMeasurement(
        TimingStats work,
        long allocatedBytes,
        long processAllocatedBytes,
        int gen0,
        int gen1,
        int gen2,
        IReadOnlyList<StageTiming> stages,
        IReadOnlyList<RendererTiming> renderers,
        TimingStats gameTick,
        TimingStats mainThreadTasks,
        ServerTickTiming? serverTicks)
    {
        Work = work;
        AllocatedBytes = allocatedBytes;
        ProcessAllocatedBytes = processAllocatedBytes;
        Gen0Collections = gen0;
        Gen1Collections = gen1;
        Gen2Collections = gen2;
        Stages = stages;
        Renderers = renderers;
        GameTick = gameTick;
        MainThreadTasks = mainThreadTasks;
        ServerTicks = serverTicks;

        TimeSpan[] other = new TimeSpan[work.Count];
        for (int i = 0; i < other.Length; i++)
        {
            TimeSpan parts = gameTick.Samples[i] + mainThreadTasks.Samples[i] + stages.Aggregate(TimeSpan.Zero, (sum, s) => sum + s.PerFrame.Samples[i]);
            other[i] = work.Samples[i] > parts ? work.Samples[i] - parts : TimeSpan.Zero;
        }

        Other = TimingStats.From(other);
    }

    /// <summary>How many frames were measured.</summary>
    public int Frames => Work.Count;

    /// <summary>The client's work in each frame.</summary>
    public TimingStats Work { get; }

    /// <summary>The median frame's work.</summary>
    public TimeSpan Median => Work.Median;

    /// <summary>The 95th percentile frame's work.</summary>
    public TimeSpan P95 => Work.P95;

    /// <summary>The 99th percentile frame's work.</summary>
    public TimeSpan P99 => Work.P99;

    /// <summary>The fastest frame's work.</summary>
    public TimeSpan Min => Work.Min;

    /// <summary>The slowest frame's work.</summary>
    public TimeSpan Max => Work.Max;

    /// <summary>The average frame's work.</summary>
    public TimeSpan Mean => Work.Mean;

    /// <summary>All the frames' work.</summary>
    public TimeSpan Total => Work.Total;

    /// <summary>Bytes the client thread allocated during the frames.</summary>
    public long AllocatedBytes { get; }

    /// <summary>Bytes the client thread allocated per frame, on average.</summary>
    public double AllocatedBytesPerFrame => Frames == 0 ? 0 : AllocatedBytes / (double)Frames;

    /// <summary>Bytes the whole process allocated during the frames, every thread's.</summary>
    public long ProcessAllocatedBytes { get; }

    /// <summary>Generation 0 collections during the frames, in the whole process.</summary>
    public int Gen0Collections { get; }

    /// <summary>Generation 1 collections during the frames, in the whole process.</summary>
    public int Gen1Collections { get; }

    /// <summary>Generation 2 collections during the frames, in the whole process.</summary>
    public int Gen2Collections { get; }

    /// <summary>Each render stage that ran, in stage order.</summary>
    public IReadOnlyList<StageTiming> Stages { get; }

    /// <summary>Each renderer that was called, mods' and the engine's.</summary>
    public IReadOnlyList<RendererTiming> Renderers { get; }

    /// <summary>The client's game tick listeners, mods' included, in each frame.</summary>
    public TimingStats GameTick { get; }

    /// <summary>Tasks queued for the client thread, received packets among them, in each frame.</summary>
    public TimingStats MainThreadTasks { get; }

    /// <summary>The rest of each frame's work: input, GUI, culling, what lies between stages.</summary>
    public TimingStats Other { get; }

    /// <summary>The server's ticks when the measurement stepped one, else null.</summary>
    public ServerTickTiming? ServerTicks { get; }

    /// <summary>The time of <paramref name="stage"/>, or null when it did not run.</summary>
    public StageTiming? Stage(EnumRenderStage stage) => Stages.FirstOrDefault(s => s.Stage == stage);

    /// <summary>The renderers of the mod <paramref name="modId"/>.</summary>
    public IReadOnlyList<RendererTiming> Of(string modId) => Renderers.Where(r => r.Renderer.Mod == modId).ToList();

    /// <inheritdoc/>
    public override string ToString()
    {
        StringBuilder text = new();
        text.AppendLine(CultureInfo.InvariantCulture, $"{Frames} frames: {Work}");
        text.AppendLine(CultureInfo.InvariantCulture, $"allocated {AllocatedBytes:N0} bytes on the client thread ({AllocatedBytesPerFrame:N0} per frame), {ProcessAllocatedBytes:N0} in the process; GCs {Gen0Collections}/{Gen1Collections}/{Gen2Collections}");
        foreach (StageTiming stage in Stages) text.AppendLine(CultureInfo.InvariantCulture, $"  {stage.Stage}: {stage.PerFrame} ({stage.FramesRun} frames)");
        text.AppendLine(CultureInfo.InvariantCulture, $"  game tick: {GameTick}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  main thread tasks: {MainThreadTasks}");
        text.AppendLine(CultureInfo.InvariantCulture, $"  other: {Other}");
        foreach (RendererTiming renderer in Renderers.OrderByDescending(r => r.PerFrame.Total).Take(10))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  {renderer.Renderer.Stage} {renderer.Renderer.ProfilingName} ({renderer.Renderer.Mod ?? "engine"}): {renderer.PerFrame}");
        }

        if (ServerTicks != null) text.AppendLine(CultureInfo.InvariantCulture, $"server ticks: {ServerTicks.Work}; allocated {ServerTicks.AllocatedBytes:N0} bytes");
        return text.ToString();
    }
}
