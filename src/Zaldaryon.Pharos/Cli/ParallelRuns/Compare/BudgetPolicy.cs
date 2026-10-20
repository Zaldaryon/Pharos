using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Zaldaryon.Pharos.Statistics;

namespace Zaldaryon.Pharos.Cli.ParallelRuns.Compare;

/// <summary>
/// The thresholds from <c>pharos.compare.json</c>: how far a metric may move in the worse
/// direction before the comparison fails.
/// </summary>
/// <remarks>
/// Two shapes are accepted. A list of rules:
/// <code>
/// { "budgets": [ { "metric": "tick.modsystem.mymod:*.ms", "limit": "+5%" } ] }
/// </code>
/// and a plain map of pattern to limit:
/// <code>
/// { "net.channel.mymod.bytes": "+0%", "gl.drawcalls.*": "+10%" }
/// </code>
/// A limit with a percent sign is relative to the baseline value; a bare number is an absolute
/// change in the metric's own unit. The first rule whose pattern matches a metric applies.
/// </remarks>
internal sealed class BudgetPolicy
{
    private readonly IReadOnlyList<BudgetRule> _rules;

    private BudgetPolicy(IReadOnlyList<BudgetRule> rules) => _rules = rules;

    /// <summary>No budgets.</summary>
    public static BudgetPolicy Empty { get; } = new([]);

    /// <summary>Whether any rule was configured.</summary>
    public bool IsEmpty => _rules.Count == 0;

    /// <summary>The rules, in file order.</summary>
    public IReadOnlyList<BudgetRule> Rules => _rules;

    /// <summary>Loads a budgets file.</summary>
    /// <param name="path">The JSON file.</param>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="FormatException">The file is not a budgets file, or a limit is not a number.</exception>
    public static BudgetPolicy Load(string path)
    {
        string json = File.ReadAllText(path);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            List<BudgetRule> rules = [];
            switch (root.ValueKind)
            {
                case JsonValueKind.Array:
                    foreach (JsonElement rule in root.EnumerateArray()) rules.Add(ReadRule(rule));
                    break;
                case JsonValueKind.Object when root.TryGetProperty("budgets", out JsonElement budgets):
                    if (budgets.ValueKind != JsonValueKind.Array) throw new FormatException($"{path}: 'budgets' must be an array of rules.");
                    foreach (JsonElement rule in budgets.EnumerateArray()) rules.Add(ReadRule(rule));
                    break;
                case JsonValueKind.Object:
                    foreach (JsonProperty property in root.EnumerateObject()) rules.Add(new BudgetRule(property.Name, ParseLimit(property.Value, property.Name)));
                    break;
                default:
                    throw new FormatException($"{path}: expected an object of patterns or a list of rules.");
            }

            if (rules.Count == 0) throw new FormatException($"{path}: no budgets were listed.");
            return new BudgetPolicy(rules);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"{path} is not valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>The text of the first rule that matches a metric, or null when none does.</summary>
    /// <param name="name">The metric's name.</param>
    public string? Match(string name) => Evaluate(name, 0, 0, "neutral").Budget;

    /// <summary>
    /// Applies the first matching rule to a change. A neutral metric has no worse direction, so its
    /// budget can never be exceeded.
    /// </summary>
    /// <param name="name">The metric's name.</param>
    /// <param name="baseline">The baseline value.</param>
    /// <param name="candidate">The candidate value.</param>
    /// <param name="direction">Which way is better.</param>
    /// <returns>The rule's text, if it matched, and whether the change is worse than it allows.</returns>
    public (string? Budget, bool OverBudget) Evaluate(string name, double baseline, double candidate, string direction)
    {
        foreach (BudgetRule rule in _rules)
        {
            if (!rule.Matches(name)) continue;
            return (rule.Text, WorseThan(baseline, candidate, direction, rule));
        }
        return (null, false);
    }

    // How far the candidate moved in the worse direction, against the rule's allowed worsening.
    private static bool WorseThan(double baseline, double candidate, string direction, BudgetRule rule)
    {
        if (direction == "neutral") return false;
        bool lowerIsBetter = direction == "lowerIsBetter";
        double worsening = lowerIsBetter ? candidate - baseline : baseline - candidate;
        if (rule.Percent)
        {
            if (baseline == 0) return worsening > 0;
            return worsening / Math.Abs(baseline) > rule.Value;
        }
        return worsening > rule.Value;
    }

    private static BudgetRule ReadRule(JsonElement rule)
    {
        if (rule.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException("Every budget must be an object with 'metric' and 'limit'.");
        }
        if (!rule.TryGetProperty("metric", out JsonElement metric) || metric.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(metric.GetString()))
        {
            throw new FormatException("Every budget needs a 'metric' name or pattern.");
        }
        string name = metric.GetString()!;
        return new BudgetRule(name, ParseLimit(rule.TryGetProperty("limit", out JsonElement limit) ? limit : default, name));
    }

    private static (double Value, bool Percent) ParseLimit(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.String && value.ValueKind != JsonValueKind.Number)
        {
            throw new FormatException($"The budget for '{name}' must be a number or a percentage.");
        }
        string text = (value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText()).Trim();
        bool percent = text.EndsWith('%');
        string number = percent ? text[..^1] : text;
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) || !double.IsFinite(parsed))
        {
            throw new FormatException($"The budget for '{name}' is not a number: '{text}'.");
        }
        if (parsed < 0) throw new FormatException($"The budget for '{name}' cannot be negative: '{text}'.");
        return (percent ? parsed / 100 : parsed, percent);
    }
}

/// <summary>One budget rule: a metric pattern and how far it may move.</summary>
/// <param name="Pattern">The glob pattern, with <c>*</c> and <c>?</c>, matched against the whole name.</param>
/// <param name="Limit">The allowed worsening: a fraction for a percentage, or an absolute value.</param>
/// <param name="Percent">Whether the limit was written as a percentage.</param>
internal sealed record BudgetRule(string Pattern, (double Value, bool Percent) Limit)
{
    /// <summary>The allowed worsening.</summary>
    public double Value => Limit.Value;

    /// <summary>Whether the limit was written as a percentage.</summary>
    public bool Percent => Limit.Percent;

    /// <summary>The rule as it is written in the report, such as <c>+5%</c>.</summary>
    public string Text => Percent
        ? "+" + (Value * 100).ToString("0.##", CultureInfo.InvariantCulture) + "%"
        : RankStatistics.Format(Value);

    /// <summary>Whether a metric name matches the pattern.</summary>
    /// <param name="name">The metric's name.</param>
    public bool Matches(string name) => Glob.IsMatch(Pattern, name);
}

/// <summary>Glob matching for metric names: <c>*</c> for any run of characters, <c>?</c> for one.</summary>
internal static class Glob
{
    /// <summary>Whether the whole value matches the pattern.</summary>
    /// <param name="pattern">The pattern; without wildcards it is an exact match.</param>
    /// <param name="value">The value to test.</param>
    public static bool IsMatch(string pattern, string value)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(value);
        if (!pattern.Contains('*') && !pattern.Contains('?')) return string.Equals(pattern, value, StringComparison.Ordinal);
        string expression = "^" + Regex.Escape(pattern).Replace("\\*", ".*", StringComparison.Ordinal).Replace("\\?", ".", StringComparison.Ordinal) + "$";
        return Regex.IsMatch(value, expression, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}
