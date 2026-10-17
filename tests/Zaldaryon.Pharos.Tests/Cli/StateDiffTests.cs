using System.Text.Json;
using Xunit;
using Zaldaryon.Pharos.Cli.ParallelRuns;

namespace Zaldaryon.Pharos.Tests.Cli;

/// <summary>`pharos diff` on two state hash recordings: where they diverge, and the exit codes.</summary>
public sealed class StateDiffTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pharos-statediff-" + Guid.NewGuid().ToString("N")[..8]);

    public StateDiffTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void IdenticalRecordings_ExitZero()
    {
        string a = Write("a.json", Recording(region: "[0,0,0,1,1,1]", ticks: [(1, "10", "20", "30"), (2, "11", "21", "31")]));
        string b = Write("b.json", Recording(region: "[0,0,0,1,1,1]", ticks: [(1, "10", "20", "30"), (2, "11", "21", "31")]));

        (int exit, string output) = Run(a, b);

        Assert.Equal(DiffCommand.ExitNoRegressions, exit);
        Assert.Contains("identical over 2 ticks", output);
    }

    [Fact]
    public void ADivergence_NamesTheTickAndTheParts()
    {
        string a = Write("a.json", Recording(region: "[0,0,0,1,1,1]", ticks: [(1, "10", "20", "30"), (2, "11", "21", "31"), (3, "12", "22", "32")]));
        string b = Write("b.json", Recording(region: "[0,0,0,1,1,1]", ticks: [(1, "10", "20", "30"), (2, "11", "99", "31"), (3, "77", "22", "99")]));

        (int exit, string output) = Run(a, b);

        Assert.Equal(DiffCommand.ExitRegressions, exit);
        Assert.Contains("diverge at tick 2", output);
        Assert.Contains("entities differ", output);
    }

    [Fact]
    public void ARunThatEndsFirst_IsReportedAsALength()
    {
        string a = Write("a.json", Recording(region: "[0,0,0,1,1,1]", ticks: [(1, "10", "20", "30")]));
        string b = Write("b.json", Recording(region: "[0,0,0,1,1,1]", ticks: [(1, "10", "20", "30"), (2, "11", "21", "31")]));

        (int exit, string output) = Run(a, b);

        Assert.Equal(DiffCommand.ExitRegressions, exit);
        Assert.Contains("then one run ends", output);
    }

    [Fact]
    public void DifferentRegions_AreNotComparable()
    {
        string a = Write("a.json", Recording(region: "[0,0,0,1,1,1]", ticks: [(1, "10", "20", "30")]));
        string b = Write("b.json", Recording(region: "[0,0,0,2,2,2]", ticks: [(1, "10", "20", "30")]));

        (int exit, string output) = Run(a, b);

        Assert.Equal(DiffCommand.ExitError, exit);
        Assert.Contains("cannot be compared", output);
    }

    [Fact]
    public void JsonWithTrx_IsRefused()
    {
        string a = Write("a.json", Recording(region: "[0,0,0,1,1,1]", ticks: [(1, "10", "20", "30")]));
        string b = Write("b.trx", "<TestRun />");

        (int exit, _) = Run(a, b);

        Assert.Equal(DiffCommand.ExitError, exit);
    }

    [Fact]
    public void JsonOutput_NamesTheDivergence()
    {
        string a = Write("a.json", Recording(region: "[0,0,0,1,1,1]", ticks: [(1, "10", "20", "30")]));
        string b = Write("b.json", Recording(region: "[0,0,0,1,1,1]", ticks: [(1, "10", "20", "31")]));

        (int exit, string output) = Run(a, b, "--json");

        Assert.Equal(DiffCommand.ExitRegressions, exit);
        using JsonDocument doc = JsonDocument.Parse(output);
        Assert.Equal("players", doc.RootElement.GetProperty("divergence").GetProperty("parts")[0].GetString());
    }

    private string Write(string name, string json)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, json);
        return path;
    }

    private static (int Exit, string Output) Run(string before, string after, params string[] extra)
    {
        StringWriter stdout = new();
        StringWriter stderr = new();
        int exit = DiffCommand.Run([before, after, .. extra], stdout, stderr);
        return (exit, stdout.ToString() + stderr.ToString());
    }

    private static string Recording(string region, (long Tick, string Blocks, string Entities, string Players)[] ticks)
    {
        string samples = string.Join(",", ticks.Select(t =>
            $"{{\"tick\":{t.Tick},\"blocks\":\"{Hex(t.Blocks)}\",\"entities\":\"{Hex(t.Entities)}\",\"players\":\"{Hex(t.Players)}\"}}"));
        return $"{{\"schema\":1,\"kind\":\"pharos-state-hashes\",\"startTick\":0,\"every\":1,\"tickMs\":50,\"region\":{region},\"parts\":\"All\",\"truncated\":false,\"ticks\":[{samples}]}}";
    }

    // The test samples are small numbers written as 16 hex digits, so each one stays readable.
    private static string Hex(string value) => Convert.ToUInt64(value).ToString("x16");
}
