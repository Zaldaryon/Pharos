using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.XUnit;
using System.Security.Cryptography;
using System.Text;

namespace Zaldaryon.Pharos.Visual;

internal sealed record GoldenImageFailureCapture(
    string Name,
    FramebufferSnapshot Actual,
    FramebufferSnapshot? Expected,
    byte[]? Heatmap,
    string Detail);

/// <summary>Attaches visual evidence to its causal exception and writes it when that failure is reported.</summary>
internal static class GoldenImageFailureArtifacts
{
    private const string DataKey = "Zaldaryon.Pharos.GoldenImageFailureCapture";
    private const int MaxCaptures = 8;

    internal static PharosAssertException Attach(PharosAssertException exception, GoldenImageFailureCapture capture)
    {
        exception.Data[DataKey] = capture;
        return exception;
    }

    internal static void WriteFrom(Exception failure, string directory, List<string> files, List<string> problems)
    {
        HashSet<Exception> visited = new(ReferenceEqualityComparer.Instance);
        List<GoldenImageFailureCapture> captures = [];
        Visit(failure);
        for (int i = 0; i < captures.Count; i++)
        {
            GoldenImageFailureCapture capture = captures[i];
            string safe = Safe(capture.Name);
            string prefix = $"golden-{safe}-{i + 1}";
            Step(prefix + "-actual.png", () => capture.Actual.SaveToPng(Path.Combine(directory, prefix + "-actual.png")));
            if (capture.Expected is { } expected)
                Step(prefix + "-expected.png", () => expected.SaveToPng(Path.Combine(directory, prefix + "-expected.png")));
            if (capture.Heatmap is { } heatmap)
                Step(prefix + "-heatmap.png", () => new FramebufferSnapshot((byte[])heatmap.Clone(), capture.Actual.Width, capture.Actual.Height).SaveToPng(Path.Combine(directory, prefix + "-heatmap.png")));
            Step(prefix + "-comparison.txt", () => File.WriteAllText(Path.Combine(directory, prefix + "-comparison.txt"), capture.Detail));
        }

        void Visit(Exception? exception)
        {
            if (exception == null || !visited.Add(exception)) return;
            if (exception.Data[DataKey] is GoldenImageFailureCapture capture && captures.Count < MaxCaptures)
                captures.Add(capture);
            if (exception is AggregateException aggregate)
                foreach (Exception inner in aggregate.InnerExceptions) Visit(inner);
            if (exception is ScenarioFailedException scenario) Visit(scenario.Failure);
            Visit(exception.InnerException);
        }

        void Step(string label, Action write)
        {
            try { write(); files.Add(label); }
            catch (Exception ex) { problems.Add($"{label} ({ex.Message})"); }
        }
    }

    private static string Safe(string name)
    {
        string safe = string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-'));
        if (safe.Length == 0) return "image";
        if (safe.Length <= 64) return safe;
        string suffix = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..12].ToLowerInvariant();
        return safe[..48] + "-" + suffix;
    }
}
