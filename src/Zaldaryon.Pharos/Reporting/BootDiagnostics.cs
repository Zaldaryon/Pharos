using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Reporting;

/// <summary>
/// The warnings, errors and fatal errors a client or a server logged while it booted. See
/// <c>docs/boot-diagnostics.md</c>.
/// </summary>
/// <remarks>
/// A server's boot ends when <c>EmbeddedServerHost.Boot</c> returns. A fixture-mode client's boot
/// ends when it has booted; an engine-mode client's, when it has joined a server, since its mods
/// start during the join. Until then <see cref="IsComplete"/> is false and the list keeps growing.
/// </remarks>
public sealed class BootDiagnostics : IReadOnlyList<LogEntry>
{
    private readonly IReadOnlyList<LogEntry> _entries;

    internal BootDiagnostics(EnumAppSide side, IReadOnlyList<LogEntry> entries, bool isComplete, bool isTruncated)
    {
        Side = side;
        _entries = entries;
        IsComplete = isComplete;
        IsTruncated = isTruncated;
    }

    /// <summary>Whose boot this is.</summary>
    public EnumAppSide Side { get; }

    /// <summary>Whether the boot has ended. A client that has not joined yet is still booting.</summary>
    public bool IsComplete { get; }

    /// <summary>Whether more than <see cref="LogCapture.BootCapacity"/> entries were logged and the rest dropped.</summary>
    public bool IsTruncated { get; }

    /// <summary>Every entry, in order: <see cref="BootDiagnostics"/> itself is the list.</summary>
    public IReadOnlyList<LogEntry> Entries => _entries;

    /// <inheritdoc />
    public int Count => _entries.Count;

    /// <inheritdoc />
    public LogEntry this[int index] => _entries[index];

    /// <summary>
    /// Judges the entries against <paramref name="allowances"/>, and the entries Pharos itself
    /// always allows. A scenario class's own allowances are applied by its
    /// <c>UnexpectedBootDiagnostics</c>.
    /// </summary>
    /// <exception cref="ArgumentException">An allowance is invalid.</exception>
    public BootDiagnosticsResult Check(IEnumerable<AllowBootDiagnosticAttribute> allowances) =>
        BootDiagnosticsResult.Of([this], [.. allowances, .. BootCheck.HarnessAllowances]);

    /// <inheritdoc />
    public IEnumerator<LogEntry> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Boot diagnostics judged against a set of allowances.</summary>
public sealed class BootDiagnosticsResult
{
    private BootDiagnosticsResult(IReadOnlyList<LogEntry> unexpected, IReadOnlyList<string> unmet, IReadOnlyList<string> incomplete)
    {
        Unexpected = unexpected;
        Unmet = unmet;
        Incomplete = incomplete;
    }

    /// <summary>The entries no allowance matched.</summary>
    public IReadOnlyList<LogEntry> Unexpected { get; }

    /// <summary>
    /// The allowances that were not met: a required entry that never came, or an exact count
    /// that was not reached or was passed. A fixed warning shows up here as a stale allowance.
    /// </summary>
    public IReadOnlyList<string> Unmet { get; }

    /// <summary>What could not be judged: a boot that had not ended, or a list that was cut short.</summary>
    public IReadOnlyList<string> Incomplete { get; }

    /// <summary>Whether nothing is unexpected and every allowance is met.</summary>
    public bool Passed => Unexpected.Count == 0 && Unmet.Count == 0;

    /// <summary>A report of what failed, for a failure message.</summary>
    public string Describe()
    {
        StringBuilder text = new();
        if (Unexpected.Count > 0)
        {
            text.Append(CultureInfo.InvariantCulture, $"{Unexpected.Count} unexpected boot diagnostic(s):");
            foreach (LogEntry entry in Unexpected.Take(50))
            {
                text.AppendLine().Append(CultureInfo.InvariantCulture, $"  [{entry.Side} {entry.Type}, {entry.Source}] {entry.Message}");
            }

            if (Unexpected.Count > 50) text.AppendLine().Append(CultureInfo.InvariantCulture, $"  ... and {Unexpected.Count - 50} more");
        }

        foreach (string unmet in Unmet)
        {
            if (text.Length > 0) text.AppendLine();
            text.Append("Unmet allowance: ").Append(unmet);
        }

        foreach (string incomplete in Incomplete)
        {
            if (text.Length > 0) text.AppendLine();
            text.Append("Note: ").Append(incomplete);
        }

        return text.ToString();
    }

    internal static BootDiagnosticsResult Of(IEnumerable<BootDiagnostics> boots, IEnumerable<AllowBootDiagnosticAttribute> allowances)
    {
        AllowBootDiagnosticAttribute[] allowed = [.. allowances];
        foreach (AllowBootDiagnosticAttribute allowance in allowed) allowance.Validate();
        Regex[] patterns = [.. allowed.Select(a => a.Regex)];
        int[] matches = new int[allowed.Length];
        bool[] timedOut = new bool[allowed.Length];
        List<LogEntry> unexpected = [];
        List<string> incomplete = [];

        foreach (BootDiagnostics boot in boots)
        {
            if (!boot.IsComplete) incomplete.Add($"the {boot.Side.ToString().ToLowerInvariant()} had not finished booting when it was checked; later diagnostics are not included");
            if (boot.IsTruncated) incomplete.Add($"the {boot.Side.ToString().ToLowerInvariant()} logged more than {LogCapture.BootCapacity} diagnostics; the rest were dropped");

            foreach (LogEntry entry in boot)
            {
                bool matched = false;
                for (int i = 0; i < allowed.Length; i++)
                {
                    if (allowed[i].Matches(entry, patterns[i], out bool slow))
                    {
                        matches[i]++;
                        matched = true;
                    }

                    timedOut[i] |= slow;
                }

                if (!matched) unexpected.Add(entry);
            }
        }

        foreach (AllowBootDiagnosticAttribute allowance in allowed.Where((_, i) => timedOut[i]))
        {
            incomplete.Add($"{allowance} took longer than a second to match an entry, which then counted as not matched");
        }

        List<string> unmet = [];
        for (int i = 0; i < allowed.Length; i++)
        {
            AllowBootDiagnosticAttribute allowance = allowed[i];
            if (allowance.Count >= 0 && matches[i] != allowance.Count)
            {
                unmet.Add($"{allowance} expected {allowance.Count}, saw {matches[i]}");
            }
            else if (allowance.Required && matches[i] == 0)
            {
                unmet.Add($"{allowance} is required but never logged");
            }
        }

        // A list cut short cannot prove that nothing else was logged.
        if (boots.Any(b => b.IsTruncated) && unexpected.Count == 0)
        {
            unmet.Add("the boot diagnostics were cut short, so they cannot be judged clean");
        }

        return new BootDiagnosticsResult(unexpected, unmet, incomplete);
    }
}
