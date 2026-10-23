using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Tests.Server;

public sealed class MultiplayerContractTests
{
    [Fact]
    public void RingAndGrid_AreDeterministic_AndCopyTheirCenter()
    {
        var center = new Vec3d(10, 20, 30);
        var ring = PlayerLayout.Ring(center, 8);
        var grid = PlayerLayout.Grid(center, spacing: 4, columns: 3);
        center.X = 1000;
        Assert.Equal(18, ring.Position(0, 12).X);
        Assert.All(Enumerable.Range(0, 12), i => Assert.Equal(8, ring.Position(i, 12).DistanceTo(new Vec3d(10, 20, 30)), 10));
        Assert.Equal(new Vec3d(6, 20, 28), grid.Position(0, 6));
        Assert.Equal(new Vec3d(14, 20, 32), grid.Position(5, 6));
        Assert.Equal(12, Enumerable.Range(0, 12).Select(i => grid.Position(i, 12).ToString()).Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.Position(12, 12));
        Assert.Throws<ArgumentOutOfRangeException>(() => ring.Position(0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerLayout.Grid(center, columns: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerLayout.Ring(new Vec3d(double.MaxValue, 0, 0), double.MaxValue).Position(0, 2));
    }

    [Fact]
    public void SendAsyncCompletion_MatchesOnlyTheSamePlayerTransportMessageAndPayload()
    {
        var sent = new BotModNetworkDriver.Prepared(Udp: true, ChannelId: 3, MessageId: 1, Data: [1, 2]);
        var received = new ServerModMessage(1, 0, ModMessageDirection.Received, "uid", "name", null, "dual", true, 1, null, [1, 2]);
        Assert.True(BotModNetworkDriver.Matches(received, "uid", sent));
        Assert.False(BotModNetworkDriver.Matches(received with { Udp = false }, "uid", sent));
        Assert.False(BotModNetworkDriver.Matches(received with { MessageId = 2 }, "uid", sent));
        Assert.False(BotModNetworkDriver.Matches(received with { Data = [1, 3] }, "uid", sent));
        Assert.False(BotModNetworkDriver.Matches(received with { Direction = ModMessageDirection.Sent }, "uid", sent));
        Assert.False(BotModNetworkDriver.Matches(received, "other", sent));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void Layout_RejectsInvalidRadius(double radius) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PlayerLayout.Ring(new Vec3d(), radius));

    [Fact]
    public void NearbyAssertion_IsInclusiveNonVacuousAndRequiresSentFinitePositions()
    {
        var sent = new ServerModMessage(1, 1, ModMessageDirection.Sent, "test", "Test", new Vec3d(3, 4, 0),
            "channel", false, 0, "Test", []);
        PharosAssert.OnlyNearbyReceived(new[] { sent }, new Vec3d(), 5);
        Assert.Throws<PharosAssertException>(() => PharosAssert.OnlyNearbyReceived(new[] { sent }, new Vec3d(), 4.99));
        Assert.Throws<PharosAssertException>(() => PharosAssert.OnlyNearbyReceived(Array.Empty<ServerModMessage>(), new Vec3d(), 5));
        Assert.Throws<PharosAssertException>(() => PharosAssert.OnlyNearbyReceived(new[] { sent with { Position = null } }, new Vec3d(), 5));
        Assert.Throws<PharosAssertException>(() => PharosAssert.OnlyNearbyReceived(new[] { sent with { Direction = ModMessageDirection.Received } }, new Vec3d(), 5));
        Assert.Throws<PharosAssertException>(() => PharosAssert.OnlyNearbyReceived(new[] { sent with { Position = new Vec3d(double.NaN, 0, 0) } }, new Vec3d(), 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => PharosAssert.OnlyNearbyReceived(new[] { sent }, new Vec3d(), double.NaN));
        Assert.Throws<ArgumentException>(() => PharosAssert.OnlyNearbyReceived(new[] { sent }, new Vec3d(double.NaN, 0, 0), 5));
    }
}
