using System.Globalization;
using System.Reflection;

namespace Zaldaryon.Pharos;

/// <summary>An Optimum build of the game: Vintage Story with Optimum's engine patches.</summary>
/// <param name="Version">Optimum's own version, or "unknown" when the build does not say.</param>
/// <param name="LibPatched">Whether the engine library carries Optimum's patches as well as the API.</param>
public sealed record OptimumInfo(string Version, bool LibPatched);

/// <summary>Finds Optimum builds: in an install folder without loading it, or in the running game.</summary>
public static class OptimumInstall
{
    /// <summary>Optimum's diagnostics type, in its patched API.</summary>
    public const string DiagnosticsTypeName = "Vintagestory.API.Config.OptimumDiagnostics, VintagestoryAPI";

    private const string Namespace = "Vintagestory.API.Config";
    private const string TypeName = "OptimumDiagnostics";

    private static readonly Lazy<OptimumInfo?> s_loaded = new(ReadLoaded);

    // Where an Optimum build may say its version, as a constant, a static field or a static property.
    private static readonly string[] VersionNames = ["Version", "OptimumVersion", "BuildVersion"];

    /// <summary>
    /// The Optimum build in <paramref name="installDir"/>, read from its <c>VintagestoryAPI.dll</c>,
    /// or null for vanilla Vintage Story.
    /// </summary>
    public static OptimumInfo? Detect(string installDir)
    {
        string api = Path.Combine(installDir, "VintagestoryAPI.dll");
        if (!ApiMetadata.HasType(api, Namespace, TypeName)) return null;
        string version = VersionNames.Select(name => ApiMetadata.ReadStringConstant(api, Namespace, TypeName, name)).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "unknown";
        bool lib = ApiMetadata.HasMember(Path.Combine(installDir, "VintagestoryLib.dll"), "Vintagestory.Client.NoObf", "ChunkTesselatorManager", "PrimaryTesselator");
        return new OptimumInfo(version, lib);
    }

    /// <summary>
    /// The Optimum build the tests run on, from the API they loaded, or null for vanilla. This is
    /// what skips and reports use: it is what the tests exercise.
    /// </summary>
    public static OptimumInfo? Loaded => s_loaded.Value;

    // Never throws: every skip, report and output line reads this.
    private static OptimumInfo? ReadLoaded()
    {
        Type? type = OptimumDiagnostics.FindLoadedType();
        if (type == null) return null;

        bool lib;
        try
        {
            lib = Type.GetType("Vintagestory.Client.NoObf.ChunkTesselatorManager, VintagestoryLib")?.GetMember("PrimaryTesselator").Length > 0;
        }
        catch
        {
            lib = false;
        }

        return new OptimumInfo(VersionOf(type), lib);
    }

    /// <summary>The version a diagnostics type states, or "unknown"; never throws.</summary>
    internal static string VersionOf(Type type)
    {
        foreach (string name in VersionNames)
        {
            try
            {
                FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
                object? value = field == null ? type.GetProperty(name, BindingFlags.Public | BindingFlags.Static)?.GetValue(null)
                    : field.IsLiteral ? field.GetRawConstantValue()
                    : field.GetValue(null);
                if (value is string { Length: > 0 } text && !string.IsNullOrWhiteSpace(text)) return text;
            }
            catch
            {
                // A member that cannot be read says nothing; try the next.
            }
        }

        return "unknown";
    }

    /// <summary>"Optimum 1.2.3", for output lines.</summary>
    internal static string Describe(OptimumInfo info) => info.Version == "unknown" ? "Optimum, version unknown" : $"Optimum {info.Version}";
}

/// <summary>
/// Optimum's diagnostic counters: its public static numeric fields, and the <c>key=value</c> pairs
/// of each <c>Get*Summary()</c> it provides, as <c>name.key</c> (for example
/// <c>tessellation.chunks</c> from <c>GetTessellationSummary()</c>).
/// </summary>
/// <remarks>
/// The names come from Optimum and can change between its versions; <see cref="Counter"/> lists
/// the ones there are when a name is missing. The counters belong to the whole process, the
/// client's and the server's alike.
/// </remarks>
public sealed class OptimumDiagnostics
{
    // "chunks = 4" reads as "chunks=4" before the pairs are split on spaces.
    private static readonly System.Text.RegularExpressions.Regex s_aroundEquals = new(@"\s*=\s*");

    private readonly Type _type;
    private readonly object _lock = new();
    private IReadOnlyDictionary<string, double> _baseline = new Dictionary<string, double>();

    internal OptimumDiagnostics(Type type)
    {
        _type = type;
    }

    /// <summary>The diagnostics of the loaded Optimum build, or null for vanilla.</summary>
    internal static OptimumDiagnostics? Loaded { get; } = FindLoadedType() is { } type ? new OptimumDiagnostics(type) : null;

    // The diagnostics type of the loaded API, or null; never throws.
    internal static Type? FindLoadedType()
    {
        try
        {
            return Type.GetType(OptimumInstall.DiagnosticsTypeName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Every counter now, less what it was at the last <see cref="Reset"/>.</summary>
    public IReadOnlyDictionary<string, double> Counters
    {
        get
        {
            Dictionary<string, double> now = Raw();
            lock (_lock)
            {
                foreach ((string name, double value) in _baseline)
                {
                    if (now.TryGetValue(name, out double current)) now[name] = current - value;
                }
            }

            return now;
        }
    }

    /// <summary>One counter, since the last <see cref="Reset"/>.</summary>
    /// <exception cref="KeyNotFoundException">Optimum has no such counter; the message lists the ones it has.</exception>
    public double Counter(string name)
    {
        IReadOnlyDictionary<string, double> counters = Counters;
        return counters.TryGetValue(name, out double value)
            ? value
            : throw new KeyNotFoundException($"Optimum has no counter '{name}'. It has: {string.Join(", ", counters.Keys.Order(StringComparer.Ordinal))}.");
    }

    /// <summary>
    /// Starts the counters from zero. Optimum's own <c>Reset</c> or <c>ResetCounters</c> runs when
    /// it has one, and Optimum's state is left alone otherwise; either way the counters are then
    /// read relative to their values now, so one Optimum's reset leaves alone starts from zero too.
    /// </summary>
    /// <returns>Whether Optimum's own reset ran.</returns>
    public bool Reset()
    {
        MethodInfo? reset = _type.GetMethod("Reset", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes)
            ?? _type.GetMethod("ResetCounters", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);
        if (reset != null)
        {
            reset.Invoke(null, null);
        }

        Dictionary<string, double> now = Raw();
        lock (_lock) _baseline = now;
        return reset != null;
    }

    /// <summary>
    /// Opens a window over the counters: <see cref="OptimumWindow.Delta"/> says how much each grew
    /// since. Nothing in Optimum changes.
    /// </summary>
    public OptimumWindow Measure() => new(this, Raw());

    /// <summary>Every <c>Get*Summary()</c> Optimum provides, one per line.</summary>
    public string Summary() => string.Join("\n", Summaries().Select(s => $"{s.Name}: {s.Text}"));

    internal Dictionary<string, double> Raw()
    {
        Dictionary<string, double> counters = new(StringComparer.Ordinal);
        foreach (FieldInfo field in _type.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.IsLiteral) continue;
            try
            {
                if (Number(field.GetValue(null)) is { } value) counters[field.Name] = value;
            }
            catch (Exception ex) when (ex is TargetInvocationException or TypeInitializationException or NotSupportedException or FieldAccessException)
            {
                // A field that cannot be read is no counter; the others still are.
            }
        }

        foreach ((string name, string text) in Summaries())
        {
            foreach (string pair in s_aroundEquals.Replace(text, "=").Split([',', ';', ' ', '\t', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                int equals = pair.IndexOf('=');
                if (equals <= 0) continue;
                if (double.TryParse(WithoutUnit(pair[(equals + 1)..]), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                {
                    counters[$"{name}.{pair[..equals].Trim()}"] = value;
                }
            }
        }

        return counters;
    }

    private IEnumerable<(string Name, string Text)> Summaries()
    {
        foreach (MethodInfo method in _type.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (method.ReturnType != typeof(string) || method.GetParameters().Length != 0) continue;
            if (!method.Name.StartsWith("Get", StringComparison.Ordinal) || !method.Name.EndsWith("Summary", StringComparison.Ordinal)) continue;
            string name = method.Name[3..^7];
            if (name.Length == 0) continue;
            string text;
            try
            {
                text = method.Invoke(null, null) as string ?? "";
            }
            catch (Exception ex) when (ex is TargetInvocationException or TypeInitializationException)
            {
                // A summary that throws gives no counters; the others still do.
                continue;
            }

            yield return (char.ToLowerInvariant(name[0]) + name[1..], text);
        }
    }

    // "12.5ms", "40%" and "3KB" read as their numbers.
    private static string WithoutUnit(string text)
    {
        int end = text.Length;
        while (end > 0 && (char.IsLetter(text[end - 1]) || text[end - 1] == '%')) end--;
        return text[..end];
    }

    private static double? Number(object? value) => value switch
    {
        byte v => v,
        sbyte v => v,
        short v => v,
        ushort v => v,
        int v => v,
        long v => v,
        uint v => v,
        ulong v => v,
        float v => v,
        double v => v,
        decimal v => (double)v,
        _ => null,
    };
}

/// <summary>Optimum's counters since a window opened. See <see cref="OptimumDiagnostics.Measure"/>.</summary>
public sealed class OptimumWindow : IDisposable
{
    private readonly OptimumDiagnostics _diagnostics;
    private readonly IReadOnlyDictionary<string, double> _start;

    internal OptimumWindow(OptimumDiagnostics diagnostics, IReadOnlyDictionary<string, double> start)
    {
        _diagnostics = diagnostics;
        _start = start;
    }

    /// <summary>How much <paramref name="name"/> grew since the window opened.</summary>
    /// <exception cref="KeyNotFoundException">Optimum has no such counter.</exception>
    public double Delta(string name)
    {
        Dictionary<string, double> now = _diagnostics.Raw();
        if (!now.TryGetValue(name, out double value))
        {
            throw new KeyNotFoundException($"Optimum has no counter '{name}'. It has: {string.Join(", ", now.Keys.Order(StringComparer.Ordinal))}.");
        }

        return value - _start.GetValueOrDefault(name);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>
/// An engine-mode client's view of Optimum: its counters, and its <c>.optimum</c> command.
/// See <see cref="Core.HeadlessClient.Optimum"/>.
/// </summary>
public sealed class OptimumDriver
{
    private readonly Core.HeadlessClient _client;

    internal OptimumDriver(Core.HeadlessClient client, OptimumDiagnostics diagnostics, OptimumInfo info)
    {
        _client = client;
        Diagnostics = diagnostics;
        Info = info;
    }

    /// <summary>The build.</summary>
    public OptimumInfo Info { get; }

    /// <summary>Optimum's version, or "unknown".</summary>
    public string Version => Info.Version;

    /// <summary>The counters. See <see cref="OptimumDiagnostics"/>.</summary>
    public OptimumDiagnostics Diagnostics { get; }

    /// <inheritdoc cref="OptimumDiagnostics.Counter" />
    public double Counter(string name) => Diagnostics.Counter(name);

    /// <inheritdoc cref="OptimumDiagnostics.Counters" />
    public IReadOnlyDictionary<string, double> Counters => Diagnostics.Counters;

    /// <inheritdoc cref="OptimumDiagnostics.Reset" />
    public bool Reset() => Diagnostics.Reset();

    /// <inheritdoc cref="OptimumDiagnostics.Measure" />
    public OptimumWindow Measure() => Diagnostics.Measure();

    /// <inheritdoc cref="OptimumDiagnostics.Summary" />
    public string Summary() => Diagnostics.Summary();

    /// <summary>Runs <c>.optimum <paramref name="arguments"/></c> as the chat does, and returns its result.</summary>
    public Task<Core.ClientCommandResult> Command(string arguments = "", int maxFrames = 600, CancellationToken ct = default) =>
        _client.Commands.ExecuteAsync(string.IsNullOrWhiteSpace(arguments) ? ".optimum" : ".optimum " + arguments.Trim(), maxFrames, ct);

    internal static string NotOptimum(string? gamePath) =>
        $"This is not an Optimum build of Vintage Story (VINTAGE_STORY={gamePath ?? "not set"}): the tests loaded a vanilla API. Gate the test with [RequireOptimum].";
}
