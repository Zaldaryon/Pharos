using System.Xml.Linq;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

/// <summary>
/// <c>pharos merge</c>: merges test run TRX files or run manifest JSON files from parallel shards.
/// </summary>
public static class MergeCommand
{
    /// <summary>Merged successfully.</summary>
    public const int ExitSuccess = 0;

    /// <summary>Error merging files or invalid arguments.</summary>
    public const int ExitError = 2;

    /// <summary>Help text for <c>pharos merge</c>.</summary>
    public const string HelpText = """
        pharos merge - merge test run results or manifests from parallel shards

        Usage: pharos merge <manifest1.json ...> -o <output.json>
               pharos merge <shard1.trx ...> -o <output.trx>

        Options:
          -o, --output <path>    The merged output file path (required)
          --trx <path>           Where the merged TRX goes when merging TRX files
          -v, --verbose          Print verbose progress

        Examples:
          pharos merge artifacts/run-1.json artifacts/run-2.json -o artifacts/run.json
          pharos merge artifacts/*.trx -o artifacts/merged.trx
        """;

    /// <summary>Runs the merge command with the supplied arguments.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        string? output = null;
        string? trxPath = null;
        bool verbose = false;
        List<string> inputs = [];

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");

            switch (arg)
            {
                case "-h" or "--help":
                    stdout.WriteLine(HelpText);
                    return ExitSuccess;
                case "-o" or "--output" or "--out":
                    output = Value();
                    break;
                case "--trx":
                    trxPath = Value();
                    break;
                case "-v" or "--verbose":
                    verbose = true;
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        stderr.WriteLine($"Error: Unknown option: {arg}");
                        stderr.WriteLine();
                        stderr.WriteLine(HelpText);
                        return ExitError;
                    }
                    // Handle glob patterns expanded by shell or passed directly
                    if (arg.Contains('*') || arg.Contains('?'))
                    {
                        string dir = Path.GetDirectoryName(arg);
                        if (string.IsNullOrEmpty(dir)) dir = ".";
                        string pattern = Path.GetFileName(arg);
                        if (Directory.Exists(dir))
                        {
                            inputs.AddRange(Directory.GetFiles(dir, pattern));
                        }
                    }
                    else
                    {
                        inputs.Add(arg);
                    }
                    break;
            }
        }

        if (output == null)
        {
            stderr.WriteLine("Error: Missing required --output <path> argument.");
            stderr.WriteLine();
            stderr.WriteLine(HelpText);
            return ExitError;
        }

        if (inputs.Count == 0)
        {
            stderr.WriteLine("Error: Name at least one manifest or TRX file to merge.");
            stderr.WriteLine();
            stderr.WriteLine(HelpText);
            return ExitError;
        }

        try
        {
            bool allTrx = inputs.All(f => string.Equals(Path.GetExtension(f), ".trx", StringComparison.OrdinalIgnoreCase));
            bool allJson = inputs.All(f => string.Equals(Path.GetExtension(f), ".json", StringComparison.OrdinalIgnoreCase));

            if (allTrx)
            {
                List<XDocument> docs = [];
                foreach (string file in inputs)
                {
                    if (!File.Exists(file)) throw new FileNotFoundException($"File not found: {file}", file);
                    docs.Add(XDocument.Load(file));
                }

                XDocument merged = Trx.Merge(docs, [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Merged Run");
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
                merged.Save(output);
                stdout.WriteLine($"Merged {inputs.Count} TRX files into {output}");
                return ExitSuccess;
            }

            if (allJson)
            {
                List<ScenarioRunManifest> manifests = [];
                List<string> existingTrx = [];
                foreach (string file in inputs)
                {
                    if (!File.Exists(file)) throw new FileNotFoundException($"File not found: {file}", file);
                    ScenarioRunManifest manifest = ScenarioRunManifestWriter.Load(file);
                    manifests.Add(manifest);
                    if (!string.IsNullOrEmpty(manifest.Trx) && File.Exists(manifest.Trx))
                    {
                        existingTrx.Add(manifest.Trx);
                    }
                    else
                    {
                        string candidateTrx = Path.ChangeExtension(file, ".trx");
                        if (File.Exists(candidateTrx)) existingTrx.Add(candidateTrx);
                    }
                }

                if (trxPath != null && existingTrx.Count > 0)
                {
                    List<XDocument> docs = existingTrx.Select(XDocument.Load).ToList();
                    XDocument mergedTrx = Trx.Merge(docs, [], manifests.Min(m => m.StartedUtc), manifests.Max(m => m.FinishedUtc), "Merged Run");
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(trxPath))!);
                    mergedTrx.Save(trxPath);
                    if (verbose) stdout.WriteLine($"Merged {existingTrx.Count} TRX files into {trxPath}");
                }

                ScenarioRunManifest mergedManifest = ScenarioRunManifestWriter.Merge(manifests, trxPath);
                ScenarioRunManifestWriter.Save(output, mergedManifest);
                stdout.WriteLine($"Merged {manifests.Count} shard manifests ({mergedManifest.Tests.Count} total tests) into {output}");
                return ExitSuccess;
            }

            stderr.WriteLine("Error: All input files must be .json manifests or all must be .trx files.");
            return ExitError;
        }
        catch (Exception ex) when (ex is IOException or FormatException or System.Text.Json.JsonException or System.Xml.XmlException)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return ExitError;
        }
    }
}
