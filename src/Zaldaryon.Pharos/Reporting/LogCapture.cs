using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.Reporting;

/// <summary>An entry the client or the server wrote to its log.</summary>
/// <param name="Side">Who logged it.</param>
/// <param name="Type">Its level: notification, warning, error, and so on.</param>
/// <param name="Message">The message, with its arguments filled in.</param>
public sealed record LogEntry(EnumAppSide Side, EnumLogType Type, string Message)
{
    /// <summary>Whether the entry is an error or a fatal error.</summary>
    public bool IsError => Type is EnumLogType.Error or EnumLogType.Fatal;

    /// <summary>Whether the entry is a warning, an error or a fatal error.</summary>
    public bool IsWarningOrWorse => Type is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal;

    /// <summary>
    /// The logger that wrote the entry: the mod id a mod's logger puts in front of its messages
    /// (<c>[mymod] ...</c>), or <c>game</c> for the engine's own loggers. It names who logged
    /// the entry, not necessarily whose fault it is: the engine reports a mod's missing asset
    /// under <c>game</c>, naming the mod in the message.
    /// </summary>
    public string Source => ParseSource(Message);

    private static string ParseSource(string message)
    {
        if (message.Length < 4 || message[0] != '[') return "game";

        int end = message.IndexOf(']', 1);
        if (end <= 1 || end + 1 >= message.Length || message[end + 1] != ' ') return "game";

        // Mod ids are made of letters, digits, '-', '_' and '.'; the engine's own bracketed
        // prefixes, such as "[Mod API]", are not mod ids.
        for (int i = 1; i < end; i++)
        {
            char c = message[i];
            if (!char.IsLetterOrDigit(c) && c is not ('-' or '_' or '.')) return "game";
        }

        return message[1..end];
    }

    /// <inheritdoc />
    public override string ToString() => $"[{Side} {Type}] {Message}";
}

/// <summary>
/// Collects what a headless client or an embedded server logs, from the moment it boots.
/// </summary>
/// <remarks>
/// Mods report most failures by logging them and carrying on: an exception in an event handler,
/// a recipe that does not resolve, an asset that does not parse. A scenario that only checks the
/// world can pass over them. <see cref="UnexpectedErrors"/> lists the errors a test did not
/// expect, and the scenario base classes can fail a test on them; see
/// <c>FailOnLoggedErrors</c>. Debug entries are not kept.
/// </remarks>
public sealed class LogCapture
{
    /// <summary>The most entries kept; older ones are dropped first.</summary>
    public const int Capacity = 10_000;

    /// <summary>The most boot diagnostics kept; later ones are counted as truncated.</summary>
    public const int BootCapacity = 10_000;

    private readonly object _lock = new();
    private readonly Queue<LogEntry> _entries = new();
    private readonly List<LogEntry> _boot = [];
    private readonly EnumAppSide _side;
    private bool _bootComplete;
    private bool _bootTruncated;

    internal LogCapture(EnumAppSide side)
    {
        _side = side;
    }

    /// <summary>Everything logged since boot or the last <see cref="Clear"/>, oldest first.</summary>
    public IReadOnlyList<LogEntry> Entries
    {
        get { lock (_lock) return [.. _entries]; }
    }

    /// <summary>The errors and fatal errors among <see cref="Entries"/>.</summary>
    public IReadOnlyList<LogEntry> Errors => Entries.Where(e => e.IsError).ToList();

    /// <summary>The warnings among <see cref="Entries"/>.</summary>
    public IReadOnlyList<LogEntry> Warnings => Entries.Where(e => e.Type == EnumLogType.Warning).ToList();

    /// <summary>
    /// The errors whose message contains none of <paramref name="allowed"/>, compared without
    /// regard to case.
    /// </summary>
    public IReadOnlyList<LogEntry> UnexpectedErrors(IEnumerable<string> allowed)
    {
        string[] fragments = [.. allowed];
        return Errors.Where(e => !fragments.Any(f => e.Message.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>
    /// Every warning, error and fatal error logged from boot until the host finished booting; see
    /// <see cref="Reporting.BootDiagnostics"/>. <see cref="Clear"/> does not touch it.
    /// </summary>
    public BootDiagnostics BootDiagnostics
    {
        get
        {
            lock (_lock) return new BootDiagnostics(_side, [.. _boot], _bootComplete, _bootTruncated);
        }
    }

    /// <summary>Ends the boot: later entries are not boot diagnostics.</summary>
    internal void CompleteBoot()
    {
        lock (_lock) _bootComplete = true;
    }

    /// <summary>Forgets everything collected so far, except the boot diagnostics.</summary>
    public void Clear()
    {
        lock (_lock) _entries.Clear();
    }

    /// <summary>Starts collecting what <paramref name="logger"/> logs.</summary>
    internal void Attach(ILogger logger) => logger.EntryAdded += OnEntryAdded;

    private void OnEntryAdded(EnumLogType type, string format, object[] args)
    {
        if (type is EnumLogType.Debug or EnumLogType.VerboseDebug or EnumLogType.Worldgen) return;

        string message;
        try
        {
            message = args is { Length: > 0 } ? string.Format(format, args) : format;
        }
        catch (FormatException)
        {
            message = format;
        }

        LogEntry entry = new(_side, type, message);
        lock (_lock)
        {
            if (_entries.Count == Capacity) _entries.Dequeue();
            _entries.Enqueue(entry);

            if (!_bootComplete && entry.IsWarningOrWorse)
            {
                if (_boot.Count < BootCapacity) _boot.Add(entry);
                else _bootTruncated = true;
            }
        }
    }
}
