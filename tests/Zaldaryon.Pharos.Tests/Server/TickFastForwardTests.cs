using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The fast-forward's timeline, against an event manager with a clock the test sets.</summary>
public class TickFastForwardTests
{
    private static readonly BlockPos Pos = new(10, 20, 30);
    private static readonly BlockPos Elsewhere = new(11, 20, 30);

    private readonly FakeEvents _events = new() { Now = 100_000 };

    private FastForwardOutcome Run(long spanMs, long? stepMs = null, BlockPos? pos = null) =>
        TickFastForward.Run([_events], null!, pos ?? Pos, spanMs, stepMs, CancellationToken.None);

    private GameTickListenerBlock Block(long id) => ListenerWatermark.Internals.LiveBlockListeners(_events).Single(l => l?.ListenerId == id);

    [Fact]
    public void AListener_RunsEveryInterval_WithItsIntervalAsDt()
    {
        List<float> dts = [];
        _events.AddGameTickListener(dts.Add, Pos, null!, 100);

        FastForwardOutcome outcome = Run(2000, 100);

        Assert.Equal(20, dts.Count);
        Assert.All(dts, dt => Assert.Equal(0.1f, dt));
        Assert.Equal(2000, outcome.SimulatedMs);
        Assert.Equal(20, outcome.Steps);
    }

    [Fact]
    public void Listeners_RunInIntervalOrder_EachAtItsOwnInterval()
    {
        List<string> order = [];
        List<float> slowDts = [];
        _events.AddGameTickListener(dt => { order.Add("slow"); slowDts.Add(dt); }, Pos, null!, 500);
        long fast = _events.AddGameTickListener(_ => order.Add("fast"), Pos, null!, 100);

        FastForwardOutcome outcome = Run(2000, 50);

        Assert.Equal(20, order.Count(o => o == "fast"));
        Assert.Equal(4, slowDts.Count);
        Assert.All(slowDts, dt => Assert.Equal(0.5f, dt));
        Assert.Equal(["fast", "fast", "fast", "fast", "fast", "slow"], order.Take(6));
        Assert.Equal(20, outcome.Calls[(0, fast)]);
    }

    [Fact]
    public void ASpanThatIsNotAMultipleOfTheStep_RunsExactly_AndLeavesTheRestOwed()
    {
        long id = _events.AddGameTickListener(_ => { }, Pos, null!, 100);

        FastForwardOutcome outcome = Run(1050, 100);
        _events.Now += 7;

        Assert.Equal(1050, outcome.SimulatedMs);
        Assert.Equal(11, outcome.Steps);
        Assert.Equal(10, outcome.Calls[(0, id)]);
        Assert.Equal(_events.Now - 7 - 50, Block(id).LastUpdateMilliseconds);
    }

    [Fact]
    public void TheRunsOwnWallTime_IsNotOwedAfterwards()
    {
        long id = _events.AddGameTickListener(_ => _events.Now += 3, Pos, null!, 100);

        Run(1000, 100);

        // Real time moved 30 ms during the run; the listener last ran at the end of the span.
        Assert.Equal(_events.Now, Block(id).LastUpdateMilliseconds);
    }

    [Fact]
    public void ARunSplitInTwo_CallsWithTheSameDts_AsOneRun()
    {
        List<float> whole = [], split = [];
        _events.AddGameTickListener(whole.Add, Pos, null!, 300);
        _events.AddGameTickListener(split.Add, Elsewhere, null!, 300);

        Run(2000, 100);
        _events.Now += 1234;
        Run(1000, 100, Elsewhere);
        _events.Now += 99;
        Run(1000, 100, Elsewhere);

        Assert.Equal(6, whole.Count);
        Assert.Equal(whole, split);
    }

    [Fact]
    public void ARealTickBetweenRuns_StartsTheNextRunAfresh()
    {
        List<float> dts = [];
        long id = _events.AddGameTickListener(dts.Add, Pos, null!, 300);

        Run(500, 100);
        Block(id).LastUpdateMilliseconds = _events.Now; // as a real tick does
        Run(300, 100);

        Assert.Equal([0.3f, 0.3f], dts);
    }

    [Fact]
    public void ListenersElsewhere_AreLeftAlone()
    {
        int calls = 0;
        long id = _events.AddGameTickListener(_ => calls++, Elsewhere, null!, 100);
        long before = Block(id).LastUpdateMilliseconds;
        _events.AddGameTickListener(_ => { }, Pos, null!, 100);

        Run(1000, 100);

        Assert.Equal(0, calls);
        Assert.Equal(before, Block(id).LastUpdateMilliseconds);
    }

    [Fact]
    public void AListenerRemovedDuringTheRun_IsNotCalledAgain()
    {
        int selfCalls = 0, laterCalls = 0;
        long self = 0, later = 0;
        self = _events.AddGameTickListener(_ =>
        {
            selfCalls++;
            _events.RemoveGameTickListener(self);
            _events.RemoveGameTickListener(later);
        }, Pos, null!, 100);
        later = _events.AddGameTickListener(_ => laterCalls++, Pos, null!, 100);

        FastForwardOutcome outcome = Run(1000, 100);

        Assert.Equal(1, selfCalls);
        Assert.Equal(0, laterCalls);
        Assert.Equal(1, outcome.Steps);
    }

    [Fact]
    public void AListenerAddedDuringTheRun_KeepsItsInitialDelay()
    {
        List<long> calledAt = [];
        long elapsed = 0;
        bool added = false;
        _events.AddGameTickListener(_ =>
        {
            elapsed += 100;
            if (elapsed == 200 && !added)
            {
                added = true;
                _events.AddGameTickListener(_ => calledAt.Add(elapsed), Pos, null!, 100, 300);
            }
        }, Pos, null!, 100);

        Run(1000, 100);

        // Registered at 200: first due 300 + 100 later; the 100 ms listener runs first in each step.
        Assert.Equal([600, 700, 800, 900, 1000], calledAt);
    }

    [Fact]
    public void Callbacks_RunWhenDue_AndTheRestKeepTheirDelay()
    {
        StubBlockEntity be = new() { Pos = Pos.Copy() };
        List<string> ran = [];
        float blockDt = -1, plainDt = -1;
        _events.AddDelayedCallback((_, _, dt) => { ran.Add("block"); blockDt = dt; }, Pos, 250);
        _events.AddDelayedCallback(be.Handler(dt => { ran.Add("plain"); plainDt = dt; }), 150);
        ListenerWatermark.Internals.LiveSingleBlockCallbacks(_events)[Pos.Copy()] =
            new DelayedCallbackBlock(1000, _events.Now + 420, Pos.Copy(), (_, _, _) => ran.Add("single"));
        _events.AddDelayedCallback((_, _, _) => ran.Add("late"), Pos, 5000);
        _events.AddDelayedCallback((_, _, _) => ran.Add("elsewhere"), Elsewhere, 100);

        FastForwardOutcome outcome = Run(1000, 100);
        _events.Now += 40;

        Assert.Equal(["plain", "block", "single"], ran);
        Assert.Equal(0.05f, blockDt);
        Assert.Equal(0.05f, plainDt);
        Assert.Equal(3, outcome.CallbacksRun);
        Assert.Empty(ListenerWatermark.Internals.SingleBlockCallbacks(_events));
        DelayedCallbackBlock late = Assert.Single(ListenerWatermark.Internals.BlockCallbacks(_events), c => c.Pos.Equals(Pos));
        Assert.Equal(_events.Now - 40 + 4000, late.CallAtEllapsedMilliseconds);
        Assert.Single(ListenerWatermark.Internals.BlockCallbacks(_events), c => c.Pos.Equals(Elsewhere));
        Assert.Empty(ListenerWatermark.Internals.EntityCallbacks(_events));
    }

    [Fact]
    public void APlainListenerOfABlockEntityAtThePosition_Runs()
    {
        StubBlockEntity be = new() { Pos = Pos.Copy() };
        int calls = 0;
        _events.AddGameTickListener(be.Handler(_ => calls++), (Action<Exception>)null!, 100);

        Run(500, 100);

        Assert.Equal(5, calls);
    }

    [Fact]
    public void AThrowingListenerWithAnErrorHandler_IsCalledEveryStep()
    {
        int errors = 0;
        _events.AddGameTickListener(_ => throw new InvalidDataException(), Pos, _ => errors++, 300);

        Run(1000, 100);

        // A block listener that throws keeps its last run time, as in the game, so it stays due.
        Assert.Equal(8, errors);
    }

    [Fact]
    public void AThrowingListenerWithoutAnErrorHandler_StopsTheRun_AndTimeIsStillPutBack()
    {
        int calls = 0;
        long ok = _events.AddGameTickListener(_ => calls++, Pos, null!, 100);
        _events.AddGameTickListener(_ => throw new InvalidDataException(), Pos, null!, 300);

        Assert.Throws<InvalidDataException>(() => Run(1000, 100));

        Assert.Equal(3, calls);
        Assert.Equal(_events.Now, Block(ok).LastUpdateMilliseconds);
    }

    [Fact]
    public void AThrowingCallback_IsTakenOutFirst_AndTheErrorPropagates()
    {
        _events.AddDelayedCallback((_, _, _) => throw new InvalidDataException(), Pos, 100);
        _events.AddGameTickListener(_ => { }, Pos, null!, 100);

        Assert.Throws<InvalidDataException>(() => Run(1000, 100));

        Assert.Empty(ListenerWatermark.Internals.BlockCallbacks(_events));
    }

    [Fact]
    public void AFastForwardInsideAHandler_IsRefused()
    {
        Exception? error = null;
        _events.AddGameTickListener(_ => Run(100, 100), Pos, e => error = e, 100);

        Run(100, 100);

        Assert.IsType<InvalidOperationException>(error);
    }

    [Fact]
    public void BadArguments_AreRefused()
    {
        _events.AddGameTickListener(_ => { }, Pos, null!, 100);

        Assert.Throws<ArgumentOutOfRangeException>(() => Run(1000, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Run(-1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() => Run(2_000_000, 1));
    }

    [Fact]
    public void TheDefaultStep_IsTheSmallestInterval()
    {
        _events.AddGameTickListener(_ => { }, Pos, null!, 250);
        _events.AddGameTickListener(_ => { }, Pos, null!, 40);

        Assert.Equal(25, Run(1000).Steps);
    }

    [Fact]
    public void NothingAtThePosition_RunsNoSteps()
    {
        Assert.Equal(0, Run(1000).Steps);
    }

    [Fact]
    public void TheInspectorsReadings_FollowTheHandlersOwner()
    {
        StubBlockEntity be = new() { Pos = Pos.Copy() };
        Action<float> handler = be.Handler(_ => { });

        Assert.Equal(Pos, TickItems.PosOf(new GameTickListener(1, 100, 0, handler, null!)));
        Assert.Equal(typeof(StubBlockEntity).FullName, TickItems.OwnerTypeOf(handler));
        Assert.Null(TickItems.EntityIdOf(handler));
        Assert.Null(TickItems.PosOf(new GameTickListener(2, 100, 0, _ => { }, null!)));
    }

    private sealed class FakeEvents : EventManager
    {
        public long Now;

        public override ILogger Logger => throw new NotSupportedException();

        public override string CommandPrefix => "/";

        public override long InWorldEllapsedMs => Now;

        public override bool HasPrivilege(string playerUid, string privilegecode) => true;
    }

    private sealed class StubBlockEntity : BlockEntity
    {
        // A handler bound to this block entity, as a block entity's own methods are.
        public Action<float> Handler(Action<float> body)
        {
            _body = body;
            return OnTick;
        }

        private Action<float>? _body;

        private void OnTick(float dt) => _body!(dt);
    }
}
