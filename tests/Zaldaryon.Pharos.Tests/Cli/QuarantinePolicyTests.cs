using Zaldaryon.Pharos.Cli.ParallelRuns;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Cli;

public sealed class QuarantinePolicyTests
{
    [Theory]
    [InlineData(true, "body", false, true)]
    [InlineData(true, "setup", false, false)]
    [InlineData(true, "cleanup", false, false)]
    [InlineData(true, "timeout", false, false)]
    [InlineData(true, null, false, false)]
    [InlineData(true, "body", true, false)]
    [InlineData(false, "body", false, false)]
    public void Quarantine_IsLimitedToKnownBodyFailures(bool rawFailure, string? phase, bool infrastructureFailure, bool expected) =>
        Assert.Equal(expected, ScenarioRunManifestWriter.CanQuarantine(rawFailure, phase, infrastructureFailure));

    [Fact]
    public void ValidEntry_MatchesExactFqnAndOptionalTheoryDisplayName()
    {
        using TemporaryFile file = new("""
            {"schemaVersion":1,"entries":[
              {"fullyQualifiedName":"Ns.Facts.Flaky","owner":"alice","expiresOn":"2026-10-17"},
              {"fullyQualifiedName":"Ns.Theory.Row","displayName":"Ns.Theory.Row(value: 2)","owner":"bob","expiresOn":"2026-10-18"}
            ]}
            """);

        QuarantinePolicy policy = QuarantinePolicy.Load(file.Path, new DateOnly(2026, 10, 17));

        Assert.Equal("alice", policy.Match("Ns.Facts.Flaky", "Ns.Facts.Flaky")?.Owner);
        Assert.Equal("alice", policy.Match("Ns.Facts.Flaky", "Ns.Facts.Other")?.Owner);
        Assert.Equal("bob", policy.Match("Ns.Theory.Row", "Ns.Theory.Row(value: 2)")?.Owner);
        Assert.Null(policy.Match("Ns.Theory.Row", "Ns.Theory.Row(value: 1)"));
    }

    [Theory]
    [InlineData("{\"schemaVersion\":2,\"entries\":[]}")]
    [InlineData("{\"schemaVersion\":\"1\",\"entries\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"entries\":[{\"fullyQualifiedName\":\"Ns.F\",\"owner\":\" \",\"expiresOn\":\"2026-10-17\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"entries\":[{\"fullyQualifiedName\":\"Ns.F\",\"owner\":\"a\",\"expiresOn\":\"2026-10-17T00:00:00Z\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"entries\":[{\"fullyQualifiedName\":\"Ns.F\",\"owner\":\"a\",\"expiresOn\":\"2026-10-17\"},{\"fullyQualifiedName\":\"Ns.F\",\"owner\":\"b\",\"expiresOn\":\"2026-10-18\"}]}")]
    public void MalformedOrOverlappingEntries_AreRejected(string json)
    {
        using TemporaryFile file = new(json);

        Assert.Throws<InvalidDataException>(() => QuarantinePolicy.Load(file.Path, new DateOnly(2026, 10, 17)));
    }

    [Fact]
    public void ExpiredEntry_IsRejectedBeforeExecution()
    {
        using TemporaryFile file = new("""
            {"schemaVersion":1,"entries":[{"fullyQualifiedName":"Ns.F","owner":"alice","expiresOn":"2026-10-16"}]}
            """);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(() => QuarantinePolicy.Load(file.Path, new DateOnly(2026, 10, 17)));
        Assert.Contains("expired", exception.Message);
    }

    private sealed class TemporaryFile : IDisposable
    {
        public TemporaryFile(string contents)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(Path, contents);
        }

        public string Path { get; }

        public void Dispose() => File.Delete(Path);
    }
}
