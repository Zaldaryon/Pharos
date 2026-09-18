using System.Text;
using static System.FormattableString;

namespace Zaldaryon.Pharos.Reporting;

/// <summary>
/// Immutable test report containing frame metrics, test results, and duration.
/// Can generate HTML and Markdown output formats.
/// </summary>
public sealed record TestReport
{
    /// <summary>
    /// Report title (typically the test or scenario name).
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Per-frame metrics captured during the test.
    /// </summary>
    public required IReadOnlyList<FrameMetricsRecord> Frames { get; init; }

    /// <summary>
    /// Number of passing test assertions.
    /// </summary>
    public int PassCount { get; init; }

    /// <summary>
    /// Number of failing test assertions.
    /// </summary>
    public int FailCount { get; init; }

    /// <summary>
    /// Total test duration in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// List of passing test names.
    /// </summary>
    public IReadOnlyList<string> PassedTests { get; init; } = [];

    /// <summary>
    /// List of failing test names with their failure reasons.
    /// </summary>
    public IReadOnlyList<(string TestName, string Reason)> FailedTests { get; init; } = [];

    /// <summary>
    /// Total number of tests (PassCount + FailCount).
    /// </summary>
    public int TotalTests => PassCount + FailCount;

    /// <summary>
    /// Pass rate as a percentage (0-100).
    /// Returns 100 if no tests were run.
    /// </summary>
    public float PassRate => TotalTests > 0 ? (float)PassCount / TotalTests * 100f : 100f;

    /// <summary>
    /// Average frame time in milliseconds across all captured frames.
    /// Returns 0 if no frames were captured.
    /// </summary>
    public float AverageFrameTimeMs => Frames.Count > 0 ? Frames.Average(f => f.FrameTimeMs) : 0f;

    /// <summary>
    /// Maximum frame time in milliseconds across all captured frames.
    /// Returns 0 if no frames were captured.
    /// </summary>
    public float MaxFrameTimeMs => Frames.Count > 0 ? Frames.Max(f => f.FrameTimeMs) : 0f;

    /// <summary>
    /// Total draw calls across all frames.
    /// </summary>
    public int TotalDrawCalls => Frames.Sum(f => f.DrawCalls);

    /// <summary>
    /// Total indirect draw calls across all frames.
    /// </summary>
    public int TotalIndirectDrawCalls => Frames.Sum(f => f.IndirectDrawCalls);

    /// <summary>
    /// Generates an HTML report string.
    /// </summary>
    public string GenerateHtml()
    {
        StringBuilder sb = new();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html lang=\"en\">");
        sb.AppendLine("<head>");
        sb.AppendLine("  <meta charset=\"UTF-8\">");
        sb.AppendLine($"  <title>{EscapeHtml(Title)}</title>");
        sb.AppendLine("  <style>");
        sb.AppendLine("    body { font-family: system-ui, sans-serif; margin: 2rem; }");
        sb.AppendLine("    h1, h2, h3 { color: #333; }");
        sb.AppendLine("    table { border-collapse: collapse; width: 100%; margin: 1rem 0; }");
        sb.AppendLine("    th, td { border: 1px solid #ddd; padding: 0.5rem; text-align: left; }");
        sb.AppendLine("    th { background: #f5f5f5; }");
        sb.AppendLine("    .pass { color: #28a745; }");
        sb.AppendLine("    .fail { color: #dc3545; }");
        sb.AppendLine("    .summary { background: #f8f9fa; padding: 1rem; border-radius: 4px; }");
        sb.AppendLine("  </style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");

        // Header
        sb.AppendLine($"  <h1>{EscapeHtml(Title)}</h1>");

        // Summary
        sb.AppendLine("  <div class=\"summary\">");
        sb.AppendLine("    <h2>Summary</h2>");
        sb.AppendLine($"    <p><strong>Duration:</strong> {DurationMs} ms</p>");
        sb.AppendLine(Invariant($"    <p><strong>Tests:</strong> <span class=\"pass\">{PassCount} passed</span>, <span class=\"fail\">{FailCount} failed</span> ({PassRate:F1}% pass rate)</p>"));
        sb.AppendLine($"    <p><strong>Frames:</strong> {Frames.Count}</p>");
        if (Frames.Count > 0)
        {
            sb.AppendLine(Invariant($"    <p><strong>Avg Frame Time:</strong> {AverageFrameTimeMs:F2} ms</p>"));
            sb.AppendLine(Invariant($"    <p><strong>Max Frame Time:</strong> {MaxFrameTimeMs:F2} ms</p>"));
            sb.AppendLine($"    <p><strong>Total Draw Calls:</strong> {TotalDrawCalls} ({TotalIndirectDrawCalls} indirect)</p>");
        }
        sb.AppendLine("  </div>");

        // Test Results
        if (PassedTests.Count > 0 || FailedTests.Count > 0)
        {
            sb.AppendLine("  <h2>Test Results</h2>");
            sb.AppendLine("  <table>");
            sb.AppendLine("    <tr><th>Test</th><th>Result</th><th>Details</th></tr>");

            foreach (string test in PassedTests)
            {
                sb.AppendLine($"    <tr><td>{EscapeHtml(test)}</td><td class=\"pass\">PASS</td><td></td></tr>");
            }
            foreach ((string testName, string reason) in FailedTests)
            {
                sb.AppendLine($"    <tr><td>{EscapeHtml(testName)}</td><td class=\"fail\">FAIL</td><td>{EscapeHtml(reason)}</td></tr>");
            }

            sb.AppendLine("  </table>");
        }

        // Frame Metrics
        if (Frames.Count > 0)
        {
            sb.AppendLine("  <h2>Frame Metrics</h2>");
            sb.AppendLine("  <table>");
            sb.AppendLine("    <tr><th>Frame</th><th>Time (ms)</th><th>Draw Calls</th><th>Indirect</th><th>Visible</th><th>Culled</th><th>Memory</th></tr>");

            foreach (FrameMetricsRecord frame in Frames)
            {
                sb.AppendLine($"    <tr>" +
                    $"<td>{frame.FrameIndex}</td>" +
                    Invariant($"<td>{frame.FrameTimeMs:F2}</td>") +
                    $"<td>{frame.DrawCalls}</td>" +
                    $"<td>{frame.IndirectDrawCalls}</td>" +
                    $"<td>{frame.VisibleChunks}</td>" +
                    $"<td>{frame.CulledChunks}</td>" +
                    $"<td>{FormatBytes(frame.MemoryBytes)}</td>" +
                    $"</tr>");
            }

            sb.AppendLine("  </table>");
        }

        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    /// <summary>
    /// Generates a Markdown report string.
    /// </summary>
    public string GenerateMarkdown()
    {
        StringBuilder sb = new();

        // Header
        sb.AppendLine($"# {Title}");
        sb.AppendLine();

        // Summary
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Duration:** {DurationMs} ms");
        sb.AppendLine(Invariant($"- **Tests:** {PassCount} passed, {FailCount} failed ({PassRate:F1}% pass rate)"));
        sb.AppendLine($"- **Frames:** {Frames.Count}");
        if (Frames.Count > 0)
        {
            sb.AppendLine(Invariant($"- **Avg Frame Time:** {AverageFrameTimeMs:F2} ms"));
            sb.AppendLine(Invariant($"- **Max Frame Time:** {MaxFrameTimeMs:F2} ms"));
            sb.AppendLine($"- **Total Draw Calls:** {TotalDrawCalls} ({TotalIndirectDrawCalls} indirect)");
        }
        sb.AppendLine();

        // Test Results
        if (PassedTests.Count > 0 || FailedTests.Count > 0)
        {
            sb.AppendLine("## Test Results");
            sb.AppendLine();
            sb.AppendLine("| Test | Result | Details |");
            sb.AppendLine("|------|--------|---------|");

            foreach (string test in PassedTests)
            {
                sb.AppendLine($"| {EscapeMarkdown(test)} | ✅ PASS | |");
            }
            foreach ((string testName, string reason) in FailedTests)
            {
                sb.AppendLine($"| {EscapeMarkdown(testName)} | ❌ FAIL | {EscapeMarkdown(reason)} |");
            }
            sb.AppendLine();
        }

        // Frame Metrics
        if (Frames.Count > 0)
        {
            sb.AppendLine("## Frame Metrics");
            sb.AppendLine();
            sb.AppendLine("| Frame | Time (ms) | Draw Calls | Indirect | Visible | Culled | Memory |");
            sb.AppendLine("|-------|-----------|------------|----------|---------|--------|--------|");

            foreach (FrameMetricsRecord frame in Frames)
            {
                sb.AppendLine(Invariant($"| {frame.FrameIndex} | {frame.FrameTimeMs:F2} | {frame.DrawCalls} | {frame.IndirectDrawCalls} | {frame.VisibleChunks} | {frame.CulledChunks} | {FormatBytes(frame.MemoryBytes)} |"));
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Saves the HTML report to a file.
    /// </summary>
    /// <param name="path">File path to write to.</param>
    public void SaveHtml(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, GenerateHtml());
    }

    /// <summary>
    /// Saves the Markdown report to a file.
    /// </summary>
    /// <param name="path">File path to write to.</param>
    public void SaveMarkdown(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        File.WriteAllText(path, GenerateMarkdown());
    }

    private static string EscapeHtml(string text)
    {
        return text
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static string EscapeMarkdown(string text)
    {
        return text
            .Replace("|", "\\|")
            .Replace("\n", " ")
            .Replace("\r", "");
    }

    private static string FormatBytes(long bytes)
    {
        return bytes switch
        {
            >= 1024 * 1024 * 1024 => Invariant($"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"),
            >= 1024 * 1024 => Invariant($"{bytes / (1024.0 * 1024.0):F2} MB"),
            >= 1024 => Invariant($"{bytes / 1024.0:F2} KB"),
            _ => $"{bytes} B"
        };
    }
}
