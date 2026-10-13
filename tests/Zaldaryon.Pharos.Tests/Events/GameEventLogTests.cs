using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Events;

namespace Zaldaryon.Pharos.Tests.Events;

/// <summary><see cref="GameEventLog"/> and <see cref="PharosAssert.EventOrder(GameEventLog, EventMatch, EventMatch[])"/>, without a game.</summary>
public class GameEventLogTests
{
    [Fact]
    public void Records_AreIndexedAndStampedInFiringOrder()
    {
        FakeLog log = new();
        log.Add(() => new ChunkColumnLoaded(1, 2, 8));
        log.StepNumber = 5;
        log.Add(() => new WorldSaved());

        Assert.Equal(2, log.Count);
        Assert.Equal([0L, 1L], log.All.Select(r => r.Index));
        Assert.Equal([0L, 5L], log.All.Select(r => r.Step));
        Assert.All(log.All, r => Assert.Equal(EnumAppSide.Server, r.Side));
        ChunkColumnLoaded loaded = Assert.Single(log.Of<ChunkColumnLoaded>());
        Assert.Equal((1, 2, 8), (loaded.ChunkX, loaded.ChunkZ, loaded.ChunkCount));
        Assert.Equal("#1 tick 5 WorldSaved", log.All[1].ToString());
    }

    [Fact]
    public void All_IsACopy()
    {
        FakeLog log = new();
        log.Add(() => new WorldSaved());
        IReadOnlyList<GameEventRecord> before = log.All;
        log.Add(() => new WorldSaved());

        Assert.Single(before);
    }

    [Fact]
    public void Clear_KeepsIndexesCounting_AndMarkFollowsThem()
    {
        FakeLog log = new();
        Assert.Equal(-1, log.Mark());
        log.Add(() => new WorldSaved());
        log.Add(() => new WorldSaved());
        log.Clear();

        Assert.Equal(0, log.Count);
        Assert.Equal(1, log.Mark());
        log.Add(() => new WorldSaved());
        Assert.Equal(2, Assert.Single(log.All).Index);
    }

    [Fact]
    public async Task WaitForAsync_FindsARecordFromBeforeTheCall_WithoutStepping()
    {
        FakeLog log = new();
        log.Add(() => new ChunkColumnLoaded(3, 4, 8));

        ChunkColumnLoaded found = await log.WaitAsync<ChunkColumnLoaded>(c => c.ChunkX == 3);

        Assert.Equal(4, found.ChunkZ);
        Assert.Equal(0, log.Steps);
    }

    [Fact]
    public async Task WaitForAsync_StepsUntilTheRecordAppears_AndReturnsTheEarliest()
    {
        FakeLog log = new();
        log.OnStep = n =>
        {
            if (n >= 3) log.Add(() => new ChunkColumnLoaded(n, 0, 8));
        };

        ChunkColumnLoaded found = await log.WaitAsync<ChunkColumnLoaded>();

        Assert.Equal(3, found.ChunkX);
        Assert.Equal(3, log.Steps);
    }

    [Fact]
    public async Task WaitForAsync_Since_IgnoresEarlierRecords_EvenAfterClear()
    {
        FakeLog log = new();
        log.Add(() => new ChunkColumnLoaded(1, 0, 8));
        log.Add(() => new ChunkColumnLoaded(2, 0, 8));
        log.Clear();
        log.Add(() => new ChunkColumnLoaded(3, 0, 8));
        long mark = log.Mark();
        log.OnStep = n => log.Add(() => new ChunkColumnLoaded(10 + n, 0, 8));

        ChunkColumnLoaded found = await log.WaitAsync<ChunkColumnLoaded>(since: mark);

        Assert.Equal(11, found.ChunkX);
        Assert.Equal(3, found.Index);
    }

    [Fact]
    public async Task WaitForAsync_TimesOut_ListingWhatWasRecorded()
    {
        FakeLog log = new();
        log.Add(() => new WorldSaved());

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(() => log.WaitAsync<ChunkColumnUnloaded>(maxSteps: 4));

        Assert.Equal(4, log.Steps);
        Assert.Contains("No ChunkColumnUnloaded was recorded within 4 ticks", ex.Message);
        Assert.Contains("#0 tick 0 WorldSaved", ex.Message);
    }

    [Fact]
    public async Task WaitForAsync_ZeroSteps_OnlyLooks()
    {
        FakeLog log = new();

        await Assert.ThrowsAsync<TimeoutException>(() => log.WaitAsync<WorldSaved>(maxSteps: 0));

        Assert.Equal(0, log.Steps);
    }

    [Fact]
    public async Task WaitForAsync_OnADisposedLog_FailsAtOnceWhenNothingMatches()
    {
        FakeLog log = new();
        log.Add(() => new WorldSaved());
        log.Dispose();

        Assert.IsType<WorldSaved>(await log.WaitAsync<WorldSaved>());
        ObjectDisposedException ex = await Assert.ThrowsAsync<ObjectDisposedException>(() => log.WaitAsync<ChunkColumnLoaded>());
        Assert.Contains("disposed", ex.Message);
        Assert.Equal(0, log.Steps);
    }

    [Fact]
    public async Task WaitForAsync_WhenTheSourceIsGone_FailsAtOnce()
    {
        FakeLog log = new() { Alive = false };

        ObjectDisposedException ex = await Assert.ThrowsAsync<ObjectDisposedException>(() => log.WaitAsync<WorldSaved>());

        Assert.Contains("server has stopped", ex.Message);
        Assert.False(log.IsRecording);
    }

    [Fact]
    public async Task WaitForAsync_HonoursCancellation()
    {
        FakeLog log = new();
        using CancellationTokenSource cts = new();
        log.OnStep = _ => cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => log.WaitAsync<WorldSaved>(ct: cts.Token));
    }

    [Fact]
    public void Dispose_UnsubscribesOnce_AndStopsRecording()
    {
        FakeLog log = new();
        log.Dispose();
        log.Dispose();
        log.Add(() => new WorldSaved());

        Assert.Equal(1, log.Unsubscribes);
        Assert.False(log.IsRecording);
        Assert.Equal(0, log.Count);
    }

    [Fact]
    public void AHandlerThatFails_DoesNotThrowIntoTheGame_ButTheNextReadDoes()
    {
        FakeLog log = new();
        log.Add(() => throw new FormatException("boom"));

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => log.All);
        Assert.IsType<FormatException>(ex.InnerException);
    }

    [Fact]
    public async Task ConcurrentAppends_GetUniqueGapFreeIndexes()
    {
        FakeLog log = new();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < 500; i++) log.Add(() => new WorldSaved());
        })));

        Assert.Equal(Enumerable.Range(0, 4000).Select(i => (long)i), log.All.Select(r => r.Index));
    }

    [Fact]
    public void EventOrder_PassesForAnInOrderSubsequence()
    {
        FakeLog log = Sample();

        PharosAssert.EventOrder(log, typeof(ChunkColumnLoaded), typeof(WorldSaved));
        PharosAssert.EventOrder(log, EventMatch.Of<ChunkColumnLoaded>(c => c.ChunkX == 2), typeof(ChunkColumnUnloaded));
        PharosAssert.EventOrder(log.All, EventMatch.Where(r => r.Index == 0, "the first"), EventMatch.Of<ChunkColumnLoaded>(c => c.ChunkX == 2));
    }

    [Fact]
    public void EventOrder_MatchesSubclasses()
    {
        FakeLog log = Sample();

        PharosAssert.EventOrder(log, typeof(GameEventRecord), typeof(GameEventRecord), typeof(GameEventRecord), typeof(GameEventRecord));
    }

    [Fact]
    public void EventOrder_FailsWhenOutOfOrder()
    {
        FakeLog log = Sample();

        PharosAssertException ex = Assert.Throws<PharosAssertException>(() => PharosAssert.EventOrder(log, typeof(ChunkColumnUnloaded), EventMatch.Of<ChunkColumnLoaded>(c => c.ChunkX == 1)));

        Assert.Contains("Expected ChunkColumnLoaded (with a condition) (#2 of 2) after #3 tick 0 ChunkColumnUnloaded 2,0,2, but it came only before that.", ex.Message);
        Assert.Contains("#0 tick 0 ChunkColumnLoaded 1,1", ex.Message);
    }

    [Fact]
    public void EventOrder_FailsWhenMissing()
    {
        FakeLog log = Sample();

        PharosAssertException ex = Assert.Throws<PharosAssertException>(() => PharosAssert.EventOrder(log, typeof(MapRegionLoaded)));

        Assert.Contains("Expected MapRegionLoaded (#1 of 1), but none was recorded.", ex.Message);
    }

    [Fact]
    public void EventMatch_RejectsATypeThatIsNotARecord()
    {
        Assert.Throws<ArgumentException>(() => EventMatch.Of(typeof(string)));
    }

    [Fact]
    public void FilterGroups_CoverTheirEvents()
    {
        Assert.Equal(ServerEvents.BlockPlaced | ServerEvents.BlockBroken | ServerEvents.BlockUsed, ServerEvents.Blocks);
        ServerEvents everyServerEvent = Enum.GetValues<ServerEvents>().Where(e => e != 0 && (e & (e - 1)) == 0).Aggregate((a, b) => a | b);
        Assert.Equal(ServerEvents.All, everyServerEvent);
        Assert.Equal(ServerEvents.All, ServerEvents.Blocks | ServerEvents.Players | ServerEvents.Entities | ServerEvents.Chunks | ServerEvents.WorldSaved);
        ClientEvents everyClientEvent = Enum.GetValues<ClientEvents>().Where(e => e != 0 && (e & (e - 1)) == 0).Aggregate((a, b) => a | b);
        Assert.Equal(ClientEvents.All, everyClientEvent);
    }

    private static FakeLog Sample()
    {
        FakeLog log = new();
        log.Add(() => new ChunkColumnLoaded(1, 1, 8));
        log.Add(() => new ChunkColumnLoaded(2, 2, 8));
        log.Add(() => new WorldSaved());
        log.Add(() => new ChunkColumnUnloaded(2, 0, 2));
        return log;
    }

    private sealed class FakeLog : GameEventLog
    {
        public FakeLog()
            : base(EnumAppSide.Server)
        {
        }

        public long StepNumber { get; set; }

        public int Steps { get; private set; }

        public int Unsubscribes { get; private set; }

        public bool Alive { get; set; } = true;

        public Action<int>? OnStep { get; set; }

        public Task<T> WaitAsync<T>(System.Func<T, bool>? predicate = null, long? since = null, int maxSteps = 600, CancellationToken ct = default) where T : GameEventRecord =>
            WaitCoreAsync(predicate, since, maxSteps, ct);

        internal override bool SourceAlive => Alive;

        internal override long CurrentStep => StepNumber;

        internal override string StepUnit => "ticks";

        internal override Task StepAsync(CancellationToken ct)
        {
            Steps++;
            OnStep?.Invoke(Steps);
            return Task.CompletedTask;
        }

        internal override void Unsubscribe() => Unsubscribes++;
    }
}
