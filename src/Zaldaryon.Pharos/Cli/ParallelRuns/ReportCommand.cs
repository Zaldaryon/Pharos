using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

/// <summary>Where a run report was written.</summary>
/// <param name="Html">The self-contained HTML page.</param>
/// <param name="Markdown">The short Markdown summary.</param>
/// <param name="Timings">The per-test timings, as JSON.</param>
public sealed record ReportPaths(string Html, string Markdown, string Timings);

/// <summary>
/// <c>pharos report</c>: reads a merged TRX and the run manifest beside it, with the failure
/// artifacts, and writes an HTML page, a Markdown summary and the timings as JSON. See
/// <c>docs/reports.md</c>.
/// </summary>
public static class ReportCommand
{
    /// <summary>The help for <c>pharos report</c>.</summary>
    public const string HelpText = """
        pharos report - write an HTML page, a Markdown summary and timings from a run

        Usage: pharos report <pharos.trx | pharos-run.json> [options]

        Options:
          --manifest <json>    The run manifest; defaults to pharos-run.json beside the TRX
          --artifacts <dir>    The failure artifacts; defaults to artifacts beside the TRX
          --html <path>        Where the HTML page goes (default: report.html beside the TRX)
          --md <path>          Where the Markdown summary goes (default: report.md beside the TRX)
          --timings <path>     Where the timings JSON goes (default: timings.json beside the TRX)
          -h, --help           Show this help

        The report reads what the run already wrote. It never runs a test or changes a result.

        Examples:
          pharos report pharos-run/pharos.trx
          pharos report pharos-run/pharos-run.json --md "$GITHUB_STEP_SUMMARY"
        """;

    /// <summary>The summary a report states at its top.</summary>
    internal sealed record Summary(int Passed, int Failed, int Skipped, TimeSpan Duration)
    {
        /// <summary>The total number of tests.</summary>
        public int Total => Passed + Failed + Skipped;

        /// <summary>The line both outputs start with.</summary>
        public string Line => string.Create(CultureInfo.InvariantCulture,
            $"{Total} tests: {Passed} passed, {Failed} failed, {Skipped} skipped ({DurationText(Duration)})");
    }

    /// <summary>Runs <c>pharos report</c> with the arguments that follow <c>report</c>.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        try
        {
            string? input = null;
            string? manifest = null;
            string? artifacts = null;
            string? html = null;
            string? markdown = null;
            string? timings = null;

            for (int i = 0; i < args.Count; i++)
            {
                string arg = args[i];
                string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.", nameof(args));
                switch (arg)
                {
                    case "-h" or "--help":
                        stdout.WriteLine(HelpText);
                        return 0;
                    case "--manifest":
                        manifest = Value();
                        break;
                    case "--artifacts":
                        artifacts = Value();
                        break;
                    case "--html":
                        html = Value();
                        break;
                    case "--md" or "--markdown":
                        markdown = Value();
                        break;
                    case "--timings":
                        timings = Value();
                        break;
                    default:
                        if (arg.StartsWith('-')) throw new ArgumentException($"Unknown option '{arg}'.", nameof(args));
                        if (input != null) throw new ArgumentException("Name one TRX or run manifest.", nameof(args));
                        input = arg;
                        break;
                }
            }

            if (input == null)
            {
                input = File.Exists(Path.Combine("pharos-run", "pharos.trx")) ? Path.Combine("pharos-run", "pharos.trx") : null;
                if (input == null) throw new ArgumentException("Name a TRX file or a pharos-run.json.", nameof(args));
            }
            if (!File.Exists(input)) throw new FileNotFoundException($"The run file '{input}' does not exist.", input);

            string trx = input;
            if (input.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                ScenarioRunManifest loaded = ScenarioRunManifestWriter.Load(input);
                trx = loaded.Trx;
                manifest ??= input;
                if (!File.Exists(trx)) throw new FileNotFoundException($"The TRX of the manifest, '{trx}', does not exist.", trx);
            }

            string directory = Path.GetDirectoryName(Path.GetFullPath(trx))!;
            string htmlPath = Path.GetFullPath(html ?? Path.Combine(directory, "report.html"));
            string markdownPath = Path.GetFullPath(markdown ?? Path.Combine(directory, "report.md"));
            string timingsPath = Path.GetFullPath(timings ?? Path.Combine(directory, "timings.json"));
            string manifestPath = manifest ?? ScenarioRunManifestWriter.PathFor(trx);
            string artifactsRoot = artifacts ?? Path.Combine(directory, "artifacts");

            ReportPaths paths = Generate(trx, manifestPath, artifactsRoot, htmlPath, markdownPath, timingsPath);
            stdout.WriteLine($"HTML: {paths.Html}");
            stdout.WriteLine($"Markdown: {paths.Markdown}");
            stdout.WriteLine($"Timings: {paths.Timings}");
            return 0;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or FormatException or JsonException or InvalidDataException or System.Xml.XmlException)
        {
            stderr.WriteLine($"pharos report: {ex.Message}");
            return 2;
        }
    }

    /// <summary>Writes the three report files from a run and returns where they went.</summary>
    /// <param name="trxPath">The merged TRX of the run.</param>
    /// <param name="manifestPath">The run manifest, or null when the run wrote none.</param>
    /// <param name="artifactsRoot">The folder holding the failure artifacts.</param>
    /// <param name="htmlPath">Where the HTML page goes.</param>
    /// <param name="markdownPath">Where the Markdown summary goes.</param>
    /// <param name="timingsPath">Where the timings JSON goes.</param>
    public static ReportPaths Generate(string trxPath, string? manifestPath, string artifactsRoot,
        string htmlPath, string markdownPath, string timingsPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(trxPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(htmlPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(markdownPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(timingsPath);

        XDocument trx = XDocument.Load(trxPath);
        IReadOnlyList<TrxResult> results = Trx.Results(trx);
        ScenarioRunManifest? manifest = null;
        List<string> warnings = [];
        if (manifestPath != null && File.Exists(manifestPath))
        {
            try
            {
                manifest = ScenarioRunManifestWriter.Load(manifestPath);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or FormatException)
            {
                warnings.Add($"The run manifest '{manifestPath}' could not be read: {ex.Message}");
            }
        }

        Summary summary = Summarize(results, manifest);
        string artifacts = Path.GetFullPath(artifactsRoot);
        Dictionary<(string FullyQualifiedName, string DisplayName), ScenarioRunTest> tests = manifest?.Tests
            .GroupBy(test => (test.FullyQualifiedName, test.DisplayName))
            .ToDictionary(group => group.Key, group => group.First())
            ?? [];

        string htmlDirectory = Path.GetDirectoryName(Path.GetFullPath(htmlPath))!;
        string markdownDirectory = Path.GetDirectoryName(Path.GetFullPath(markdownPath))!;
        Directory.CreateDirectory(htmlDirectory);
        Directory.CreateDirectory(markdownDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(timingsPath))!);

        string html = BuildHtml(results, tests, manifest, summary, artifacts, htmlDirectory, warnings);
        string markdown = BuildMarkdown(results, tests, manifest, summary, artifacts, markdownDirectory, warnings);
        File.WriteAllText(htmlPath, html);
        File.WriteAllText(markdownPath, markdown);
        File.WriteAllText(timingsPath, BuildTimings(results, tests, manifest, summary));

        return new ReportPaths(Path.GetFullPath(htmlPath), Path.GetFullPath(markdownPath), Path.GetFullPath(timingsPath));
    }

    private static Summary Summarize(IReadOnlyList<TrxResult> results, ScenarioRunManifest? manifest)
    {
        int passed = results.Count(result => result.Passed);
        int failed = results.Count(result => result.Failed);
        int skipped = results.Count - passed - failed;
        TimeSpan duration = manifest == null ? TimeSpan.FromTicks(results.Sum(result => result.Duration.Ticks))
            : manifest.FinishedUtc - manifest.StartedUtc;
        if (duration < TimeSpan.Zero) duration = TimeSpan.FromTicks(results.Sum(result => result.Duration.Ticks));
        return new Summary(passed, failed, skipped, duration);
    }

    private static string BuildHtml(IReadOnlyList<TrxResult> results,
        IReadOnlyDictionary<(string FullyQualifiedName, string DisplayName), ScenarioRunTest> tests,
        ScenarioRunManifest? manifest, Summary summary, string artifactsRoot, string outputDirectory,
        IReadOnlyList<string> warnings)
    {
        StringBuilder html = new();
        html.AppendLine("<!DOCTYPE html>");
        html.AppendLine("<html lang=\"en\"><head><meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine("<title>Pharos run report</title>");
        html.AppendLine("<style>");
        html.AppendLine(":root{color-scheme:light dark}body{font:15px/1.5 system-ui,sans-serif;max-width:1100px;margin:2rem auto;padding:0 1rem}");
        html.AppendLine("h1{margin-bottom:.2rem}h2{margin-top:2rem;border-bottom:1px solid #8884;padding-bottom:.3rem}");
        html.AppendLine(".summary{font-size:1.1rem}.failed{color:#c62828}.passed{color:#2e7d32}.skipped{color:#757575}");
        html.AppendLine("table{border-collapse:collapse;width:100%}th,td{text-align:left;padding:.35rem .5rem;border-bottom:1px solid #8883;vertical-align:top}");
        html.AppendLine("th{background:#8881}code,pre{font-family:ui-monospace,monospace;font-size:.88rem}pre{white-space:pre-wrap;background:#8881;padding:.6rem;border-radius:4px}");
        html.AppendLine("section{border-left:4px solid #c62828;padding-left:.8rem;margin:1.2rem 0}img{max-width:100%;border:1px solid #8884;border-radius:4px}");
        html.AppendLine("details{margin:.4rem 0}.muted{color:#777;font-size:.9rem}.links a{margin-right:.8rem}");
        html.AppendLine("</style></head><body>");
        html.AppendLine("<h1>Pharos run report</h1>");
        html.Append("<p class=\"summary\"><span class=\"passed\">")
            .Append(summary.Passed).Append(" passed</span>, <span class=\"failed\">")
            .Append(summary.Failed).Append(" failed</span>, <span class=\"skipped\">")
            .Append(summary.Skipped).Append(" skipped</span> of ").Append(summary.Total)
            .Append(" in ").Append(Html(DurationText(summary.Duration))).AppendLine("</p>");
        if (manifest != null)
        {
            html.Append("<p class=\"muted\">Run ").Append(Html(manifest.RunId)).Append(" started ")
                .Append(Html(manifest.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)))
                .Append(", ").Append(WorkerText(manifest.Workers.Count)).Append(", assembly ")
                .Append(Html(Path.GetFileName(manifest.Assembly))).AppendLine(".</p>");
        }
        foreach (string warning in warnings)
        {
            html.Append("<p class=\"muted\">Warning: ").Append(Html(warning)).AppendLine("</p>");
        }

        html.AppendLine("<h2>Failures</h2>");
        List<TrxResult> failed = results.Where(result => result.Failed)
            .OrderByDescending(result => result.Duration).ToList();
        if (failed.Count == 0)
        {
            html.AppendLine("<p class=\"passed\">No failures.</p>");
        }
        foreach (TrxResult result in failed)
        {
            ScenarioRunTest? test = Find(tests, result);
            string? directory = ArtifactDirectory(test, artifactsRoot, result);
            html.AppendLine("<section>");
            html.Append("<h3 class=\"failed\">").Append(Html(result.TestName)).AppendLine("</h3>");
            html.Append("<p class=\"muted\">").Append(Html(result.FullyQualifiedName)).Append(" in ")
                .Append(Html(DurationText(result.Duration))).AppendLine("</p>");
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                html.Append("<pre>").Append(Html(result.Message)).AppendLine("</pre>");
            }
            AppendHtmlArtifacts(html, directory, artifactsRoot, outputDirectory);
            if (!string.IsNullOrWhiteSpace(result.StackTrace))
            {
                html.Append("<details><summary>Stack trace</summary><pre>")
                    .Append(Html(result.StackTrace)).AppendLine("</pre></details>");
            }
            html.AppendLine("</section>");
        }

        html.AppendLine("<h2>Slowest tests</h2>");
        AppendHtmlTable(html, results.OrderByDescending(result => result.Duration).Take(15), tests, artifactsRoot, outputDirectory);

        html.AppendLine("<h2>All tests</h2>");
        AppendHtmlTable(html, results.OrderBy(result => result.FullyQualifiedName).ThenBy(result => result.TestName), tests, artifactsRoot, outputDirectory);

        List<TrxResult> skipped = results.Where(result => result.Skipped).ToList();
        html.AppendLine("<h2>Skipped</h2>");
        if (skipped.Count == 0)
        {
            html.AppendLine("<p>None.</p>");
        }
        else
        {
            html.AppendLine("<ul>");
            foreach (TrxResult result in skipped)
            {
                html.Append("<li><code>").Append(Html(result.TestName)).Append("</code>");
                if (!string.IsNullOrWhiteSpace(result.Message)) html.Append(": ").Append(Html(FirstLine(result.Message)));
                html.AppendLine("</li>");
            }
            html.AppendLine("</ul>");
        }

        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static void AppendHtmlTable(StringBuilder html, IEnumerable<TrxResult> results,
        IReadOnlyDictionary<(string FullyQualifiedName, string DisplayName), ScenarioRunTest> tests,
        string artifactsRoot, string outputDirectory)
    {
        html.AppendLine("<table><thead><tr><th>Test</th><th>Outcome</th><th>Duration</th><th>Artifacts</th></tr></thead><tbody>");
        foreach (TrxResult result in results)
        {
            string css = result.Passed ? "passed" : result.Failed ? "failed" : "skipped";
            html.Append("<tr><td>").Append(Html(result.TestName)).Append("</td><td class=\"").Append(css).Append("\">")
                .Append(Html(result.Outcome)).Append("</td><td>").Append(Html(DurationText(result.Duration))).Append("</td><td>");
            string? directory = ArtifactDirectory(Find(tests, result), artifactsRoot, result);
            if (directory != null)
            {
                string relative = Relative(directory, outputDirectory);
                html.Append("<a href=\"").Append(Html(relative)).AppendLine("\">files</a>");
            }
            html.AppendLine("</td></tr>");
        }
        html.AppendLine("</tbody></table>");
    }

    private static void AppendHtmlArtifacts(StringBuilder html, string? directory, string artifactsRoot, string outputDirectory)
    {
        if (directory == null) return;

        html.AppendLine("<div class=\"links\">");
        string relative = Relative(directory, outputDirectory);
        html.Append("<a href=\"").Append(Html(relative)).AppendLine("\">All files</a>");
        string screenshot = Path.Combine(directory, "screenshot.png");
        if (File.Exists(screenshot))
        {
            html.Append("<a href=\"").Append(Html(Path.Combine(relative, "screenshot.png"))).AppendLine("\">Screenshot</a>");
        }
        foreach (string name in new[] { "server.log", "client.log", "run.json" })
        {
            if (File.Exists(Path.Combine(directory, name)))
            {
                html.Append("<a href=\"").Append(Html(Path.Combine(relative, name))).Append("\">")
                    .Append(Html(name)).AppendLine("</a>");
            }
        }
        html.AppendLine("</div>");

        if (File.Exists(screenshot) && new FileInfo(screenshot).Length is > 0 and <= 4_000_000)
        {
            byte[] bytes = File.ReadAllBytes(screenshot);
            html.Append("<p><img alt=\"Screenshot of the failure\" src=\"data:image/png;base64,")
                .Append(Convert.ToBase64String(bytes)).AppendLine("\"></p>");
        }
        foreach (string name in new[] { "server.log", "client.log" })
        {
            string path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;
            string tail = Tail(path, 25);
            if (tail.Length == 0) continue;
            html.Append("<details><summary>Last lines of ").Append(Html(name)).Append("</summary><pre>")
                .Append(Html(tail)).AppendLine("</pre></details>");
        }
    }

    private static string BuildMarkdown(IReadOnlyList<TrxResult> results,
        IReadOnlyDictionary<(string FullyQualifiedName, string DisplayName), ScenarioRunTest> tests,
        ScenarioRunManifest? manifest, Summary summary, string artifactsRoot, string outputDirectory,
        IReadOnlyList<string> warnings)
    {
        StringBuilder markdown = new();
        markdown.AppendLine("## Pharos: " + summary.Line);
        if (manifest != null)
        {
            markdown.AppendLine();
            markdown.Append("Run `").Append(manifest.RunId).Append("` started ")
                .Append(manifest.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture))
                .Append(" with ").Append(WorkerText(manifest.Workers.Count)).AppendLine(".");
        }
        foreach (string warning in warnings) markdown.AppendLine().Append("> Warning: ").AppendLine(warning);

        List<TrxResult> failed = results.Where(result => result.Failed)
            .OrderByDescending(result => result.Duration).ToList();
        markdown.AppendLine();
        markdown.AppendLine("### Failed");
        if (failed.Count == 0)
        {
            markdown.AppendLine();
            markdown.AppendLine("None.");
        }
        else
        {
            markdown.AppendLine();
            markdown.AppendLine("| Test | Failure | Artifacts |");
            markdown.AppendLine("| --- | --- | --- |");
            foreach (TrxResult result in failed)
            {
                string? directory = ArtifactDirectory(Find(tests, result), artifactsRoot, result);
                string artifacts = directory == null ? "" : $"[files]({Relative(directory, outputDirectory)})";
                markdown.Append("| `").Append(Cell(result.TestName)).Append("` | ")
                    .Append(Cell(FirstLine(result.Message ?? result.Outcome))).Append(" | ")
                    .Append(artifacts).AppendLine(" |");
            }
        }

        List<TrxResult> slow = results.OrderByDescending(result => result.Duration).Take(10).ToList();
        markdown.AppendLine();
        markdown.AppendLine("### Slowest");
        markdown.AppendLine();
        markdown.AppendLine("| Test | Duration |");
        markdown.AppendLine("| --- | --- |");
        foreach (TrxResult result in slow)
        {
            markdown.Append("| `").Append(Cell(result.TestName)).Append("` | ")
                .Append(DurationText(result.Duration)).AppendLine(" |");
        }

        List<TrxResult> skipped = results.Where(result => result.Skipped).ToList();
        markdown.AppendLine();
        markdown.AppendLine("### Skipped");
        if (skipped.Count == 0)
        {
            markdown.AppendLine();
            markdown.AppendLine("None.");
        }
        else
        {
            markdown.AppendLine();
            foreach (TrxResult result in skipped)
            {
                markdown.Append("- `").Append(result.TestName).Append('`');
                if (!string.IsNullOrWhiteSpace(result.Message)) markdown.Append(": ").Append(FirstLine(result.Message));
                markdown.AppendLine();
            }
        }

        return markdown.ToString();
    }

    private static string BuildTimings(IReadOnlyList<TrxResult> results,
        IReadOnlyDictionary<(string FullyQualifiedName, string DisplayName), ScenarioRunTest> tests,
        ScenarioRunManifest? manifest, Summary summary)
    {
        var testsJson = results.Select(result =>
        {
            ScenarioRunTest? test = Find(tests, result);
            return new
            {
                fullyQualifiedName = result.FullyQualifiedName,
                displayName = result.TestName,
                outcome = result.Outcome,
                durationMs = (long)result.Duration.TotalMilliseconds,
                attempts = test?.Attempts?.Select(attempt => new
                {
                    number = attempt.Number,
                    phase = attempt.Phase,
                    outcome = attempt.Outcome,
                    durationMs = (long)(attempt.DurationSeconds * 1000),
                    artifacts = attempt.ArtifactDirectories,
                }).ToArray(),
            };
        }).ToArray();

        var document = new
        {
            schemaVersion = 1,
            trx = manifest?.Trx,
            runId = manifest?.RunId,
            startedUtc = manifest?.StartedUtc,
            finishedUtc = manifest?.FinishedUtc,
            durationSeconds = summary.Duration.TotalSeconds,
            passed = summary.Passed,
            failed = summary.Failed,
            skipped = summary.Skipped,
            tests = testsJson,
        };
        return JsonSerializer.Serialize(document, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        });
    }

    private static ScenarioRunTest? Find(
        IReadOnlyDictionary<(string FullyQualifiedName, string DisplayName), ScenarioRunTest> tests,
        TrxResult result) =>
        tests.GetValueOrDefault((result.FullyQualifiedName, result.TestName));

    private static string? ArtifactDirectory(ScenarioRunTest? test, string artifactsRoot, TrxResult result)
    {
        if (test?.Attempts != null)
        {
            foreach (ScenarioAttemptEntry attempt in test.Attempts.Reverse())
            {
                foreach (string directory in attempt.ArtifactDirectories.Reverse())
                {
                    if (Directory.Exists(directory)) return directory;
                }
            }
        }

        string classFolder = FailureArtifactWriter.Sanitize(ClassName(result.FullyQualifiedName));
        string methodFolder = MethodFolder(result.FullyQualifiedName, result.TestName);
        foreach (string root in Candidates(artifactsRoot))
        {
            string path = Path.Combine(root, classFolder, methodFolder);
            if (Directory.Exists(path)) return path;
        }

        try
        {
            return Directory.EnumerateDirectories(artifactsRoot, methodFolder, SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileName(Path.GetDirectoryName(path)) == classFolder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<string> Candidates(string artifactsRoot)
    {
        yield return artifactsRoot;
        IEnumerable<string> children;
        try
        {
            children = Directory.EnumerateDirectories(artifactsRoot);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }
        foreach (string child in children) yield return child;
    }

    private static string ClassName(string fullyQualifiedName)
    {
        int method = fullyQualifiedName.LastIndexOf('.');
        string type = method < 0 ? fullyQualifiedName : fullyQualifiedName[..method];
        return type[(type.LastIndexOf('.') + 1)..];
    }

    private static string MethodFolder(string fullyQualifiedName, string displayName)
    {
        string method = fullyQualifiedName[(fullyQualifiedName.LastIndexOf('.') + 1)..];
        string folder = FailureArtifactWriter.Sanitize(method);
        if (displayName.EndsWith("." + method, StringComparison.Ordinal) || displayName == method) return folder;

        // A theory row's folder carries a hash of its display name, as FailureArtifactWriter makes it.
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(displayName)))[..8].ToLowerInvariant();
        return folder[..Math.Min(folder.Length, 51)] + "-" + hash;
    }

    private static string Relative(string path, string outputDirectory) =>
        Path.GetRelativePath(outputDirectory, path).Replace('\\', '/');

    private static string WorkerText(int count) => count == 1 ? "1 worker" : string.Create(CultureInfo.InvariantCulture, $"{count} workers");

    private static string DurationText(TimeSpan duration) => duration.TotalHours >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalHours}h {duration.Minutes:00}m {duration.Seconds:00}s")
        : duration.TotalMinutes >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)duration.TotalMinutes}m {duration.Seconds:00}s")
            : string.Create(CultureInfo.InvariantCulture, $"{duration.TotalSeconds:0.0}s");

    private static string Tail(string path, int lines)
    {
        try
        {
            return string.Join("\n", File.ReadLines(path).TakeLast(lines));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal).Replace("\r", "", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);

    private static string Html(string text) => WebUtility.HtmlEncode(text);
}
