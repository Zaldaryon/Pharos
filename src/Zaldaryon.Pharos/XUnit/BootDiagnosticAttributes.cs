using System.Globalization;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Fails the boot of a scenario class whose client or server logged a warning, error or fatal
/// error while booting that no <see cref="AllowBootDiagnosticAttribute"/> allows. See
/// <c>docs/boot-diagnostics.md</c>.
/// </summary>
/// <remarks>
/// The boot is checked once, when a host is freshly booted; a host reused from the previous test
/// was checked then. When it fails, the test that booted it fails with the list and its failure
/// artifacts, and every later test of the class fails at once with the same list, without
/// booting again.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = false, Inherited = true)]
public sealed class StrictBootAttribute : Attribute;

/// <summary>
/// Allows a warning, error or fatal error that a client or server logs while booting, for
/// <see cref="StrictBootAttribute"/> and <c>UnexpectedBootDiagnostics</c>.
/// </summary>
/// <remarks>
/// An entry is allowed when any allowance matches it, and counts toward every allowance it
/// matches. <see cref="Pattern"/> is a regular expression searched for anywhere in the message,
/// ignoring case (unlike <c>AllowedLoggedErrors</c>, which takes plain fragments).
/// </remarks>
/// <param name="pattern">A regular expression the message must contain a match for.</param>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true, Inherited = true)]
public sealed class AllowBootDiagnosticAttribute(string pattern) : Attribute
{
    private Regex? _regex;

    /// <summary>The regular expression the message must contain a match for.</summary>
    public string Pattern { get; } = pattern;

    /// <summary>
    /// The level the entry must have: <see cref="EnumLogType.Warning"/>, <see cref="EnumLogType.Error"/>
    /// or <see cref="EnumLogType.Fatal"/>. Any of them when not set (shown as
    /// <see cref="EnumLogType.Notification"/>); other levels are refused. The engine logs
    /// <c>Fatal(exception)</c> at the error level.
    /// </summary>
    public EnumLogType Level { get; set; } = EnumLogType.Notification;

    /// <summary>
    /// The logger that must have written the entry, compared without regard to case: a mod id, or
    /// <c>game</c> for the engine. Any logger when not set. See <see cref="LogEntry.Source"/>.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// How many times the entry must be logged, exactly; any other number fails the boot. Any
    /// number when not set (-1). A count above 0 makes the entry required; 0 means it must never
    /// be logged.
    /// </summary>
    public int Count { get; set; } = -1;

    /// <summary>Whether the entry must be logged at least once; a fixed warning then shows as a stale allowance.</summary>
    public bool Required { get; set; }

    /// <summary>Throws when the allowance cannot work: a bad pattern, level or count.</summary>
    /// <exception cref="ArgumentException">It cannot.</exception>
    internal void Validate()
    {
        _ = Regex;
        if (Level is not (EnumLogType.Notification or EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal))
        {
            throw new ArgumentException($"{this}: Level must be Warning, Error or Fatal; boot diagnostics are nothing else.");
        }

        if (Count < -1)
        {
            throw new ArgumentException($"{this}: Count must be 0 or more, or -1 for any number.");
        }

        if (Count == 0 && Required)
        {
            throw new ArgumentException($"{this}: Count = 0 and Required = true contradict each other.");
        }
    }

    /// <summary>The compiled pattern.</summary>
    /// <exception cref="ArgumentException">The pattern is not a valid regular expression.</exception>
    internal Regex Regex => _regex ??= Compile(Pattern);

    internal bool Matches(LogEntry entry, Regex regex, out bool timedOut)
    {
        timedOut = false;
        if (Level != EnumLogType.Notification && entry.Type != Level) return false;
        if (Source != null && !string.Equals(entry.Source, Source, StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            return regex.IsMatch(entry.Message);
        }
        catch (RegexMatchTimeoutException)
        {
            timedOut = true;
            return false;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        List<string> parts = [$"\"{Pattern}\""];
        if (Level != EnumLogType.Notification) parts.Add("Level = " + Level);
        if (Source != null) parts.Add($"Source = \"{Source}\"");
        if (Count >= 0) parts.Add("Count = " + Count.ToString(CultureInfo.InvariantCulture));
        if (Required) parts.Add("Required = true");
        return $"[AllowBootDiagnostic({string.Join(", ", parts)})]";
    }

    private static Regex Compile(string pattern)
    {
        try
        {
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"[AllowBootDiagnostic(\"{pattern}\")] is not a valid regular expression: {ex.Message}", ex);
        }
    }
}
