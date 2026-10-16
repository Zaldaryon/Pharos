using System.Globalization;
using System.Text;
using Zaldaryon.Pharos.Performance;
using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// What a soak run measured: the readings it took, the breaches it recorded, and the totals.
/// </summary>
/// <remarks>
/// <see cref="TickTime"/> is wall-clock time for every tick, so it is a measurement of the machine,
/// not a deterministic result.
/// </remarks>
public sealed class SoakReport
{
    private readonly List<SoakSample> _samples = [];
    private readonly List<SoakBreach> _breaches = [];

    internal SoakReport(double days, float speed, int tickMs)
    {
        Days = days;
        Speed = speed;
        TickMs = tickMs;
        TickTime = TimingStats.From([]);
    }

    /// <summary>The game days the run asked for.</summary>
    public double Days { get; }

    /// <summary>The calendar's time speed modifier for the run.</summary>
    public float Speed { get; }

    /// <summary>The virtual clock's tick, in milliseconds.</summary>
    public int TickMs { get; }

    /// <summary>The ticks the run processed.</summary>
    public long Ticks { get; internal set; }

    /// <summary>The calendar days the run moved, from its first reading to its last.</summary>
    public double CalendarDays { get; internal set; }

    /// <summary>The game time the virtual clock moved.</summary>
    public TimeSpan Simulated { get; internal set; }

    /// <summary>The wall-clock time the run took.</summary>
    public TimeSpan WallTime { get; internal set; }

    /// <summary>The wall-clock time of every tick, in the order measured.</summary>
    public TimingStats TickTime { get; internal set; }

    /// <summary>The random block tick passes the background thread finished during the run.</summary>
    public long RandomTickPasses { get; internal set; }

    /// <summary>The entity spawner passes the background thread finished during the run.</summary>
    public long SpawnerPasses { get; internal set; }

    /// <summary>Whether the run reached its target day, and did not stop early.</summary>
    public bool Completed { get; internal set; }

    /// <summary>The readings, in the order taken.</summary>
    public IReadOnlyList<SoakSample> Samples => _samples;

    /// <summary>The breaches, gating and advisory, in the order they happened.</summary>
    public IReadOnlyList<SoakBreach> Breaches => _breaches;

    internal void AddSample(SoakSample sample) => _samples.Add(sample);

    internal void AddBreach(SoakBreach breach) => _breaches.Add(breach);

    /// <summary>The readings as CSV, with a header line. A value the reading did not take is an empty cell.</summary>
    public string ToCsv()
    {
        StringBuilder csv = new();
        csv.Append("tick,total_days,game_seconds,wall_seconds,tick_p50_ms,tick_p99_ms,tick_max_ms,")
            .Append("managed_bytes,managed_bytes_after_gc,entities,loaded_chunks,tick_listeners,callbacks,")
            .Append("random_tick_passes,checkpoint\n");
        foreach (SoakSample sample in _samples)
        {
            csv.Append(CultureInfo.InvariantCulture, $"{sample.Tick},{sample.TotalDays:0.######},{sample.Simulated.TotalSeconds:0.###},{sample.Wall.TotalSeconds:0.###},")
                .Append(Ms(sample.TickP50)).Append(',')
                .Append(Ms(sample.TickP99)).Append(',')
                .Append(Ms(sample.TickMax)).Append(',')
                .Append(CultureInfo.InvariantCulture, $"{sample.ManagedBytes},")
                .Append(sample.ManagedBytesAfterGc?.ToString(CultureInfo.InvariantCulture) ?? "").Append(',')
                .Append(CultureInfo.InvariantCulture, $"{sample.Entities},{sample.LoadedChunks},{sample.TickListeners},{sample.Callbacks},{sample.RandomTickPasses},")
                .Append(sample.Checkpoint ? "true" : "false").Append('\n');
        }

        return csv.ToString();
    }

    /// <summary>
    /// A path for a file a soak test saves: under the failure artifact root, in the <c>soak</c> folder,
    /// which the nightly run uploads with the other artifacts.
    /// </summary>
    public static string ArtifactPath(string fileName) => Path.Combine(FailureArtifactWriter.Root, "soak", fileName);

    /// <summary>Writes <see cref="ToCsv"/> to <paramref name="path"/>, creating its folder.</summary>
    public void SaveCsv(string path)
    {
        string full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, ToCsv(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string Ms(TimeSpan? time) =>
        time is { } value ? value.TotalMilliseconds.ToString("0.###", CultureInfo.InvariantCulture) : "";
}
