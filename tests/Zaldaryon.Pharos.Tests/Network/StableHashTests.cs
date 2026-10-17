using Xunit;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>The stable hash, pinned to vectors computed by an independent FNV-1a implementation, so it cannot change silently.</summary>
public class StableHashTests
{
    [Fact]
    public void Of_MatchesTheFixedVector()
    {
        Assert.Equal(0x89bbf0ee7da728baUL, StableHash.Of("pharos"));
    }

    [Fact]
    public void Add_MatchesTheFixedVector()
    {
        Assert.Equal(0x89cd31291d2aefa4UL, StableHash.Add(StableHash.Seed, 1));
    }

    [Fact]
    public void AddDouble_HashesTheBitPattern()
    {
        Assert.Equal(0xaa95e93229a27c80UL, StableHash.AddDouble(StableHash.Seed, 1.5));
        Assert.NotEqual(StableHash.AddDouble(StableHash.Seed, 0.0), StableHash.AddDouble(StableHash.Seed, -0.0));
    }

    [Fact]
    public void Finish_OfZeroIsZero()
    {
        Assert.Equal(0UL, StableHash.Finish(0));
    }

    [Fact]
    public void SameInput_GivesTheSameHash_AndOneCharacterChangeDoesNot()
    {
        Assert.Equal(StableHash.Of("block"), StableHash.Of("block"));
        Assert.NotEqual(StableHash.Of("block"), StableHash.Of("blocl"));
    }
}
