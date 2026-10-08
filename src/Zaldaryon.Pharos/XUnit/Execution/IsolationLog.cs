using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using Vintagestory.API.Datastructures;

namespace Zaldaryon.Pharos.XUnit.Execution;

/// <summary>
/// Remembers, per test class, how the last test's hosts were left, so the next test knows how its
/// own were made ready, and collects each test's lines for its output.
/// </summary>
internal static class IsolationLog
{
    private static readonly AsyncLocal<Notes?> s_current = new();
    private static readonly ConcurrentDictionary<Type, Tally> s_classes = new();

    /// <summary>The notes of the test running on this flow, set by the test runner.</summary>
    public static Notes? Current
    {
        get => s_current.Value;
        set => s_current.Value = value;
    }

    /// <summary>
    /// Records how a test of <paramref name="testClass"/> left its hosts: rolled back, with what it
    /// took, or not kept, with why when a rollback was expected.
    /// </summary>
    /// <param name="testClass">The test's class.</param>
    /// <param name="rolledBack">What the rollback did, when it did.</param>
    /// <param name="fallback">Why the hosts were not kept, when keeping them was expected.</param>
    /// <param name="kept">Whether the hosts were pooled for the next test.</param>
    public static void Left(Type testClass, IsolationReport? rolledBack, string? fallback, bool kept = false)
    {
        Tally tally = s_classes.GetOrAdd(testClass, _ => new Tally());
        lock (tally)
        {
            tally.LastRollback = rolledBack;
            tally.LastFallback = fallback;
            tally.LastKept = kept;
        }

        Current?.SetAfter(rolledBack != null ? "after this test: " + rolledBack : fallback != null ? "after this test: not rolled back: " + fallback : null);
    }

    /// <summary>
    /// The report for a test of <paramref name="testClass"/> whose hosts were taken from the pool
    /// (<paramref name="reused"/>) or booted, the boot having taken <paramref name="boot"/>.
    /// </summary>
    public static IsolationReport Prepared(Type testClass, bool reused, bool recycled, TimeSpan boot)
    {
        Tally tally = s_classes.GetOrAdd(testClass, _ => new Tally());
        IsolationReport report;
        lock (tally)
        {
            if (reused && recycled) report = new IsolationReport(IsolationKind.Recycled, null, 0, 0, TimeSpan.Zero);
            else if (reused) report = tally.LastRollback ?? new IsolationReport(IsolationKind.RolledBack, null, 0, 0, TimeSpan.Zero);
            else if (tally.Tests == 0) report = new IsolationReport(IsolationKind.FirstBoot, null, 0, 0, boot);
            else
            {
                // Hosts that were kept and still not found mean another class took them.
                string? reason = tally.LastFallback ?? (tally.LastKept ? "another test class used the pooled hosts in between" : null);
                report = new IsolationReport(IsolationKind.Restarted, reason, 0, 0, boot);
            }

            tally.Tests++;
            tally.Counts[report.Kind] = tally.Counts.GetValueOrDefault(report.Kind) + 1;
            if (report.Kind == IsolationKind.Restarted && report.Reason != null) tally.Reasons.Add(report.Reason);
            tally.LastRollback = null;
            tally.LastFallback = null;
            tally.LastKept = false;
        }

        Current?.SetPrepared("isolation: " + report, Summary(testClass));
        return report;
    }

    /// <summary>Forgets <paramref name="testClass"/>, for tests of the log itself.</summary>
    internal static void Forget(Type testClass) => s_classes.TryRemove(testClass, out _);

    /// <summary>The class's tests so far, by how their hosts were made ready.</summary>
    public static string Summary(Type testClass)
    {
        Tally tally = s_classes.GetOrAdd(testClass, _ => new Tally());
        lock (tally)
        {
            string counts = string.Join(", ", tally.Counts.OrderBy(c => c.Key).Select(c => $"{c.Value} {Describe(c.Key)}"));
            string reasons = tally.Reasons.Count > 0 ? $" ({string.Join("; ", tally.Reasons.Distinct())})" : "";
            return $"{testClass.Name} so far: {counts}{reasons}";
        }
    }

    private static string Describe(IsolationKind kind) => kind switch
    {
        IsolationKind.FirstBoot => "first boot",
        IsolationKind.Restarted => "booted again",
        IsolationKind.RolledBack => "rolled back",
        _ => "recycled",
    };

    /// <summary>The event data Pharos sends with a rollback event.</summary>
    public static TreeAttribute EventData(string? test, int? chunks)
    {
        TreeAttribute data = new();
        data.SetString("test", test ?? "");
        if (chunks is { } count) data.SetInt("chunks", count);
        return data;
    }

    /// <summary>
    /// The assemblies a scenario class's test code lives in: its own and its base classes', less
    /// Pharos's and the game's.
    /// </summary>
    public static IReadOnlySet<Assembly> TestAssemblies(Type testClass)
    {
        HashSet<Assembly> assemblies = [];
        for (Type? type = testClass; type != null && type != typeof(object); type = type.BaseType)
        {
            assemblies.Add(type.Assembly);
        }

        assemblies.Remove(typeof(IsolationLog).Assembly);
        assemblies.RemoveWhere(a => a.GetName().Name is { } name && name.StartsWith("Vintagestory", StringComparison.Ordinal));
        return assemblies;
    }

    /// <summary>Times something.</summary>
    public static TimeSpan Time(Action action)
    {
        long start = Stopwatch.GetTimestamp();
        action();
        return Stopwatch.GetElapsedTime(start);
    }

    private sealed class Tally
    {
        public int Tests;
        public IsolationReport? LastRollback;
        public string? LastFallback;
        public bool LastKept;
        public Dictionary<IsolationKind, int> Counts { get; } = [];
        public List<string> Reasons { get; } = [];
    }

    /// <summary>One test's isolation lines, appended to its output once it has torn down.</summary>
    internal sealed class Notes
    {
        private string? _prepared, _summary, _after;

        public void SetPrepared(string line, string summary)
        {
            _prepared = line;
            _summary = summary;
        }

        public void SetAfter(string? line) => _after = line;

        public string Text => string.Join(Environment.NewLine, new[] { _prepared, _after, _summary }.Where(l => l != null)) is { Length: > 0 } text
            ? text + Environment.NewLine
            : "";
    }
}
