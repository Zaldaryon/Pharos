using System.Globalization;
using System.Text;
using Zaldaryon.Pharos.Metrics;

namespace Zaldaryon.Pharos.Translations;

/// <summary>A text element whose laid-out text exceeds its bounds in a given language.</summary>
/// <param name="Dialog">The dialog class name.</param>
/// <param name="Composer">The composer holding the element.</param>
/// <param name="ElementKey">The element's key within the composer.</param>
/// <param name="ElementType">The element's class name.</param>
/// <param name="Language">The active language code when measured.</param>
/// <param name="HorizontalOverflowPx">Horizontal overflow in pixels, or 0 if it fits horizontally.</param>
/// <param name="VerticalOverflowPx">Vertical overflow in pixels, or 0 if it fits vertically.</param>
/// <param name="Text">A preview of the text that overflowed.</param>
public sealed record TextOverflowEntry(
    string Dialog,
    string Composer,
    string ElementKey,
    string ElementType,
    string Language,
    double HorizontalOverflowPx,
    double VerticalOverflowPx,
    string Text)
{
    /// <summary>The larger of horizontal and vertical overflow in pixels.</summary>
    public double MaxOverflowPx => Math.Max(HorizontalOverflowPx, VerticalOverflowPx);
}

/// <summary>A non-fatal layout or right-to-left warning observed while sweeping a dialog.</summary>
/// <param name="Dialog">The dialog class name.</param>
/// <param name="Composer">The composer holding the element.</param>
/// <param name="ElementKey">The element's key within the composer.</param>
/// <param name="ElementType">The element's class name.</param>
/// <param name="Language">The active language code when checked.</param>
/// <param name="Message">What was observed.</param>
public sealed record LanguageLayoutWarning(
    string Dialog,
    string Composer,
    string ElementKey,
    string ElementType,
    string Language,
    string Message);

/// <summary>Per-language summary of text layout checks and worst overflows.</summary>
/// <param name="Language">The language code.</param>
/// <param name="CheckedElements">Total text elements measured in this language.</param>
/// <param name="OverflowCount">How many elements overflowed their bounds.</param>
/// <param name="WorstOverflowPx">Largest overflow in pixels observed in this language.</param>
/// <param name="WorstElementKey">Element key with the largest overflow, or null if none overflowed.</param>
/// <param name="WarningCount">Number of layout/RTL warnings recorded in this language.</param>
public sealed record LanguageLayoutSummary(
    string Language,
    int CheckedElements,
    int OverflowCount,
    double WorstOverflowPx,
    string? WorstElementKey,
    int WarningCount);

/// <summary>
/// Summary table of worst text overflows and layout warnings per language across inspected dialogs.
/// </summary>
public sealed class LanguageLayoutReport : IMetricSource
{
    /// <summary>Creates a layout report from observed overflows, warnings, and checked element counts.</summary>
    public LanguageLayoutReport(
        string? domain,
        IReadOnlyList<TextOverflowEntry> entries,
        IReadOnlyList<LanguageLayoutWarning> warnings,
        IReadOnlyDictionary<string, int> checkedByLanguage)
    {
        Domain = domain;
        Entries = [.. entries.OrderByDescending(e => e.MaxOverflowPx).ThenBy(e => e.Language, StringComparer.Ordinal)];
        Warnings = [.. warnings];

        HashSet<string> languages = new(checkedByLanguage.Keys, StringComparer.Ordinal);
        foreach (TextOverflowEntry entry in Entries) languages.Add(entry.Language);
        foreach (LanguageLayoutWarning warning in Warnings) languages.Add(warning.Language);

        List<LanguageLayoutSummary> summaries = [];
        foreach (string lang in languages)
        {
            List<TextOverflowEntry> langEntries = Entries.Where(e => e.Language == lang).ToList();
            int langWarnings = Warnings.Count(w => w.Language == lang);
            checkedByLanguage.TryGetValue(lang, out int checkedCount);
            TextOverflowEntry? worst = langEntries.Count > 0 ? langEntries[0] : null;
            summaries.Add(new LanguageLayoutSummary(
                lang,
                checkedCount,
                langEntries.Count,
                worst?.MaxOverflowPx ?? 0,
                worst?.ElementKey,
                langWarnings));
        }

        ByLanguage = [.. summaries.OrderByDescending(s => s.WorstOverflowPx).ThenBy(s => s.Language, StringComparer.Ordinal)];
    }

    /// <summary>Optional domain filter supplied when generating the report.</summary>
    public string? Domain { get; }

    /// <summary>All overflow findings ordered from largest overflow to smallest.</summary>
    public IReadOnlyList<TextOverflowEntry> Entries { get; }

    /// <summary>All layout and RTL warnings recorded.</summary>
    public IReadOnlyList<LanguageLayoutWarning> Warnings { get; }

    /// <summary>Per-language summary rows ordered by worst overflow descending.</summary>
    public IReadOnlyList<LanguageLayoutSummary> ByLanguage { get; }

    /// <summary>Whether any overflow was recorded.</summary>
    public bool HasOverflows => Entries.Count > 0;

    /// <summary>Formats the report as a Markdown table of worst overflows per language.</summary>
    public string ToMarkdown()
    {
        StringBuilder sb = new();
        sb.AppendLine(Domain == null
            ? "### Language Layout Report"
            : $"### Language Layout Report ({Domain})");
        sb.AppendLine();
        sb.AppendLine("| Language | Checked | Overflows | Worst Overflow (px) | Worst Element | Warnings |");
        sb.AppendLine("|----------|---------|-----------|---------------------|---------------|----------|");

        foreach (LanguageLayoutSummary row in ByLanguage)
        {
            string worstPx = string.Create(CultureInfo.InvariantCulture, $"{row.WorstOverflowPx:0.#}");
            string worstElement = row.WorstElementKey ?? "-";
            sb.AppendLine(string.Create(CultureInfo.InvariantCulture,
                $"| {row.Language} | {row.CheckedElements} | {row.OverflowCount} | {worstPx} | {worstElement} | {row.WarningCount} |"));
        }

        return sb.ToString();
    }

    /// <inheritdoc />
    public IReadOnlyList<MetricReading> Metrics()
    {
        List<MetricReading> readings =
        [
            new("lang.layout.overflows", Entries.Count, MetricUnit.Count, MetricDirection.LowerIsBetter),
            new("lang.layout.warnings", Warnings.Count, MetricUnit.Count, MetricDirection.LowerIsBetter),
            new("lang.layout.worst_overflow_px", Entries.Count > 0 ? Entries[0].MaxOverflowPx : 0, MetricUnit.Count, MetricDirection.LowerIsBetter),
        ];

        foreach (LanguageLayoutSummary row in ByLanguage)
        {
            readings.Add(new MetricReading(
                $"lang.layout.{row.Language}.worst_overflow_px",
                row.WorstOverflowPx,
                MetricUnit.Count,
                MetricDirection.LowerIsBetter));
        }

        return readings;
    }
}
