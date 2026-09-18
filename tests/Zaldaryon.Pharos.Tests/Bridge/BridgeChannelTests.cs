using Zaldaryon.Pharos.Bridge;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Bridge;

/// <summary>
/// Collection definition for Bridge tests that modifies shared static state.
/// </summary>
[CollectionDefinition("Bridge", DisableParallelization = true)]
public class BridgeTestCollection : ICollectionFixture<BridgeTestFixture> { }

/// <summary>
/// Fixture that ensures BridgeChannel.Active is reset between tests.
/// </summary>
public class BridgeTestFixture : IDisposable
{
    public void Dispose()
    {
        BridgeChannel.Deactivate();
    }
}

/// <summary>
/// Unit tests for <see cref="BridgeChannel"/> event pipeline.
/// </summary>
[Collection("Bridge")]
public sealed class BridgeChannelTests : IDisposable
{
    public BridgeChannelTests()
    {
        // Ensure clean state before each test
        BridgeChannel.Deactivate();
    }

    public void Dispose()
    {
        BridgeChannel.Deactivate();
    }

    [Fact]
    public void BridgeChannel_ActiveIsNullByDefault()
    {
        Assert.Null(BridgeChannel.Active);
    }

    [Fact]
    public void BridgeChannel_Activate_SetsActive()
    {
        var channel = new BridgeChannel();

        BridgeChannel.Activate(channel);

        Assert.Same(channel, BridgeChannel.Active);
    }

    [Fact]
    public void BridgeChannel_Deactivate_ClearsActive()
    {
        var channel = new BridgeChannel();
        BridgeChannel.Activate(channel);

        BridgeChannel.Deactivate();

        Assert.Null(BridgeChannel.Active);
    }

    [Fact]
    public void BridgeChannel_PublishFrameStart_WhenActive_EnqueuesEvent()
    {
        var channel = new BridgeChannel();
        BridgeChannel.Activate(channel);

        channel.PublishFrameStart(0.016);

        var ev = channel.Drain();
        Assert.NotNull(ev);
        Assert.Equal(BridgeEventKind.FrameStart, ev.Kind);
        Assert.Equal(0.016, ev.Dt);
    }

    [Fact]
    public void BridgeChannel_Drain_ReturnsEnqueuedEvent()
    {
        var channel = new BridgeChannel();
        BridgeChannel.Activate(channel);
        channel.PublishFrameEnd(0.033);

        var ev = channel.Drain();

        Assert.NotNull(ev);
        Assert.Equal(BridgeEventKind.FrameEnd, ev.Kind);
        Assert.Equal(0.033, ev.Dt);

        // Queue is empty now
        Assert.Null(channel.Drain());
    }

    [Fact]
    public void BridgeChannel_DrainAll_ReturnsAllPending()
    {
        var channel = new BridgeChannel();
        BridgeChannel.Activate(channel);
        channel.PublishFrameStart(0.016);
        channel.PublishChunkTessellated(1, 2, 3);
        channel.PublishGuiStateChanged("MainMenu");
        channel.PublishFrameEnd(0.016);

        var events = channel.DrainAll();

        Assert.Equal(4, events.Count);
        Assert.Equal(BridgeEventKind.FrameStart, events[0].Kind);
        Assert.Equal(BridgeEventKind.ChunkTessellated, events[1].Kind);
        Assert.Equal(1, events[1].ChunkX);
        Assert.Equal(2, events[1].ChunkY);
        Assert.Equal(3, events[1].ChunkZ);
        Assert.Equal(BridgeEventKind.GuiStateChanged, events[2].Kind);
        Assert.Equal("MainMenu", events[2].ScreenName);
        Assert.Equal(BridgeEventKind.FrameEnd, events[3].Kind);

        // Queue is empty now
        Assert.Empty(channel.DrainAll());
    }

    [Fact]
    public void BridgeChannel_PublishWhenInactive_DoesNotEnqueue()
    {
        var channel = new BridgeChannel();
        // Not activated

        channel.PublishFrameStart(0.016);
        channel.PublishFrameEnd(0.016);
        channel.PublishChunkTessellated(0, 0, 0);
        channel.PublishGuiStateChanged("Test");

        Assert.Null(channel.Drain());
        Assert.Empty(channel.DrainAll());
    }

    [Fact]
    public void BridgeEventKind_HasFourValues()
    {
        var values = Enum.GetValues<BridgeEventKind>();

        Assert.Equal(4, values.Length);
        Assert.Contains(BridgeEventKind.FrameStart, values);
        Assert.Contains(BridgeEventKind.FrameEnd, values);
        Assert.Contains(BridgeEventKind.ChunkTessellated, values);
        Assert.Contains(BridgeEventKind.GuiStateChanged, values);
    }

    [Fact]
    public void BridgeChannel_Activate_ThrowsOnNull()
    {
        Assert.Throws<ArgumentNullException>(() => BridgeChannel.Activate(null!));
    }

    [Fact]
    public void BridgeChannel_Activate_ReplacesExistingActive()
    {
        var channel1 = new BridgeChannel();
        var channel2 = new BridgeChannel();
        BridgeChannel.Activate(channel1);

        BridgeChannel.Activate(channel2);

        Assert.Same(channel2, BridgeChannel.Active);
    }

    [Fact]
    public void BridgeChannel_OtherChannelActive_DoesNotEnqueue()
    {
        var channel1 = new BridgeChannel();
        var channel2 = new BridgeChannel();
        BridgeChannel.Activate(channel1);

        // channel2 is not the active channel
        channel2.PublishFrameStart(0.016);

        Assert.Null(channel2.Drain());
        // channel1 did not receive the event either
        Assert.Null(channel1.Drain());
    }
}
