using Xunit;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The state hash file format and the comparison of two recordings, without a game.</summary>
public class StateHashFileTests
{
    [Fact]
    public void Parse_ReadsAWrittenFile()
    {
        const string json = """
            {
              "schema": 1, "kind": "pharos-state-hashes", "startTick": 100, "every": 1, "tickMs": 50,
              "region": [0, 0, 0, 1, 1, 1], "parts": "Blocks, Entities, Players", "truncated": false,
              "ticks": [ { "tick": 101, "blocks": "00000000000000ff", "entities": "0000000000000001", "players": "0000000000000002" } ]
            }
            """;

        StateHashFile file = StateHashFile.Parse(json);

        Assert.Equal(100, file.StartTick);
        Assert.Equal(new[] { 0, 0, 0, 1, 1, 1 }, file.Region);
        Assert.Equal(StateHashParts.All, file.Parts);
        StateHashTick tick = Assert.Single(file.Ticks);
        Assert.Equal(0xffUL, tick.Blocks);
        Assert.Equal(1UL, tick.Entities);
    }

    [Fact]
    public void Parse_RefusesOtherJson()
    {
        Assert.Throws<InvalidDataException>(() => StateHashFile.Parse("""{ "kind": "something-else", "schema": 1 }"""));
    }

    [Fact]
    public void Compare_OfIdenticalRecordings_IsNull()
    {
        StateHashFile a = Make(new StateHashTick(1, 10, 20, 30), new StateHashTick(2, 11, 21, 31));
        StateHashFile b = Make(new StateHashTick(1, 10, 20, 30), new StateHashTick(2, 11, 21, 31));

        Assert.Null(StateHashFile.Compare(a, b));
    }

    [Fact]
    public void Compare_NamesTheFirstDivergentTickAndItsParts()
    {
        StateHashFile a = Make(new StateHashTick(1, 10, 20, 30), new StateHashTick(2, 11, 21, 31), new StateHashTick(3, 12, 22, 32));
        StateHashFile b = Make(new StateHashTick(1, 10, 20, 30), new StateHashTick(2, 11, 99, 31), new StateHashTick(3, 77, 22, 99));

        StateHashDivergence divergence = Assert.IsType<StateHashDivergence>(StateHashFile.Compare(a, b));

        Assert.Equal("diverged", divergence.Kind);
        Assert.Equal(1, divergence.Index);
        Assert.Equal(2, divergence.Tick);
        Assert.Equal(new[] { "entities" }, divergence.Parts);
    }

    [Fact]
    public void Compare_OfARecordingThatEndsFirst_IsALengthDifference()
    {
        StateHashFile a = Make(new StateHashTick(1, 10, 20, 30));
        StateHashFile b = Make(new StateHashTick(1, 10, 20, 30), new StateHashTick(2, 11, 21, 31));

        StateHashDivergence divergence = Assert.IsType<StateHashDivergence>(StateHashFile.Compare(a, b));

        Assert.Equal("length", divergence.Kind);
        Assert.Equal(1, divergence.Compared);
        Assert.Equal(2, divergence.Tick);
    }

    [Fact]
    public void Compare_OfDifferentRegions_IsRefused()
    {
        StateHashFile a = Make(new StateHashTick(1, 10, 20, 30)) with { Region = [0, 0, 0, 1, 1, 1] };
        StateHashFile b = Make(new StateHashTick(1, 10, 20, 30)) with { Region = [0, 0, 0, 2, 2, 2] };

        Assert.Throws<ArgumentException>(() => StateHashFile.Compare(a, b));
    }

    private static StateHashFile Make(params StateHashTick[] ticks) =>
        new(StartTick: 0, Every: 1, TickMs: 50, Region: null, Parts: StateHashParts.All, Truncated: false, Ticks: ticks);
}
