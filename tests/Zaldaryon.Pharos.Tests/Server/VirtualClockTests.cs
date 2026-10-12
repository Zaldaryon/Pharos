using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;
using Xunit;
using Xunit.Abstractions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.XUnit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The virtual clock's arithmetic and its IL rewrite, before anything boots.</summary>
public class VirtualClockUnitTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1000, 250, new long[] { 250, 250, 250, 250 })]
    [InlineData(1100, 250, new long[] { 250, 250, 250, 250, 100 })]
    [InlineData(0, 250, new long[0])]
    [InlineData(7, 1000, new long[] { 7 })]
    public void Steps_SplitTheSpanIntoMaxSteps(long total, long max, long[] expected)
    {
        Assert.Equal(expected, VirtualClock.Steps(total, max));
    }

    [Fact]
    public void Steps_RejectNegativeSpansAndEmptySteps()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VirtualClock.Steps(-1, 250).ToList());
        Assert.Throws<ArgumentOutOfRangeException>(() => VirtualClock.Steps(100, 0).ToList());
    }

    [Theory]
    [InlineData(10_000)]
    [InlineData(-3_000)]
    public void State_EnterAndExit_CarryOnWithoutAJump(long virtualAheadOfRealMs)
    {
        long real = 5_000;
        ServerClockState state = new(null, () => real);
        Assert.Equal(5_000, state.Read());

        state.Enter(tickMs: 50);
        Assert.Equal(5_000, state.Read());
        real += 1_000;
        Assert.Equal(5_000, state.Read());
        Assert.Equal(5_050, state.BeginTick());

        // Virtual time ends ahead of or behind the wall clock.
        state.SetPendingStep(1_000 + virtualAheadOfRealMs - 50);
        long atExit = state.BeginTick();
        Assert.False(state.HasPendingStep);
        state.Exit();
        Assert.Equal(atExit, state.Read());

        real += 20;
        Assert.Equal(atExit + 20, state.Read());
    }

    [Fact]
    public void State_OnTheWallClock_OnlyReads()
    {
        long real = 100;
        ServerClockState state = new(null, () => real);
        Assert.Equal(100, state.BeginTick());
        real = 160;
        Assert.Equal(160, state.BeginTick());
    }

    [Fact]
    public void State_FollowedCalendar_BanksSimulatedTimeOnly()
    {
        long real = 0;
        Stopwatch calendarWatch = new();
        calendarWatch.Start();
        ServerClockState state = new(null, () => real);
        state.Enter(tickMs: 50);
        state.FollowCalendar(calendarWatch);

        state.BeginTick();
        state.BeginTick();
        real += 10_000;
        Assert.Equal(100, state.PendingCalendarElapsed(calendarWatch));
        Assert.Equal(100, state.TakeCalendarElapsed(calendarWatch));
        Assert.Equal(0, state.TakeCalendarElapsed(calendarWatch));
        Assert.Null(state.TakeCalendarElapsed(new Stopwatch()));

        // A stopped calendar watch lets no time pass for the calendar.
        calendarWatch.Stop();
        state.BeginTick();
        calendarWatch.Start();
        Assert.Equal(0, state.TakeCalendarElapsed(calendarWatch));
    }

    [Fact]
    public void Rewrite_RoutesOnlyTheStopwatchReads()
    {
        Harmony harmony = new("zaldaryon.pharos.tests.virtualclock");
        try
        {
            HarmonyMethod transpiler = new(typeof(VirtualClockUnitTests), nameof(TranspileSample));
            harmony.Patch(AccessTools.Method(typeof(Sample), nameof(Sample.ReadMs)), transpiler: transpiler);
            harmony.Patch(AccessTools.Method(typeof(Sample), nameof(Sample.ReadElapsed)), transpiler: transpiler);
            harmony.Patch(AccessTools.Method(typeof(Sample), nameof(Sample.StartAndRead)), transpiler: transpiler);

            Sample sample = new();
            s_routed = sample.Watch;
            Assert.Equal(4242, sample.ReadMs());
            Assert.Equal(TimeSpan.FromMilliseconds(4242), sample.ReadElapsed());
            Assert.Equal(4242, sample.StartAndRead());
            Assert.True(sample.Watch.IsRunning, "Start() was rewritten too");

            Sample other = new();
            Assert.Equal(other.Watch.ElapsedMilliseconds, other.ReadMs());
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            s_routed = null;
        }
    }

    [Fact]
    public void Discovery_FindsTheServerReaders()
    {
        Stopwatch took = Stopwatch.StartNew();
        List<string> readers = ServerClockPatches.FindReaders(typeof(ServerMain).Assembly, AccessTools.Field(typeof(ServerMain), nameof(ServerMain.totalUnpausedTime)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();
        output.WriteLine($"Found {readers.Count} readers in {took.ElapsedMilliseconds} ms: {string.Join(", ", readers)}");

        Assert.Contains("ServerMain.Process", readers);
        Assert.Contains("ServerMain.get_ElapsedMilliseconds", readers);
        Assert.Contains("ServerSystemCalendar.OnServerTick", readers);
        Assert.Contains("ServerAPI.get_ServerUptimeMilliseconds", readers);
        Assert.Contains("ServerAPI.get_ServerUptimeSeconds", readers);
        Assert.DoesNotContain("ServerMain.Suspend", readers);
    }

    [Fact]
    public void Install_SupportsThisGameVersion()
    {
        ServerClockPatches.Install();
        output.WriteLine($"Installed in {ServerClockPatches.InstallTime.TotalMilliseconds:0} ms: {string.Join(", ", ServerClockPatches.Rewritten)}");

        Assert.True(ServerClockPatches.Supported);
        Assert.Contains("Vintagestory.Common.GameCalendar.Tick", ServerClockPatches.Rewritten);
    }

    private static Stopwatch? s_routed;

    private static IEnumerable<CodeInstruction> TranspileSample(IEnumerable<CodeInstruction> instructions) =>
        ServerClockPatches.Rewrite(instructions, AccessTools.Field(typeof(Sample), nameof(Sample.Watch)),
            AccessTools.Method(typeof(VirtualClockUnitTests), nameof(RoutedMs)),
            AccessTools.Method(typeof(VirtualClockUnitTests), nameof(RoutedElapsed)));

    private static long RoutedMs(Stopwatch watch) => ReferenceEquals(watch, s_routed) ? 4242 : watch.ElapsedMilliseconds;

    private static TimeSpan RoutedElapsed(Stopwatch watch) => ReferenceEquals(watch, s_routed) ? TimeSpan.FromMilliseconds(4242) : watch.Elapsed;

    private sealed class Sample
    {
        public Stopwatch Watch = new();

        [MethodImpl(MethodImplOptions.NoInlining)]
        public long ReadMs() => Watch.ElapsedMilliseconds;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public TimeSpan ReadElapsed() => Watch.Elapsed;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public long StartAndRead()
        {
            Watch.Start();
            return Watch.ElapsedMilliseconds;
        }
    }
}

/// <summary>A live server's time, driven by ticks.</summary>
[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class LiveVirtualClockTests : ServerScenarioBase
{
    private ICoreServerAPI ServerApi => Api!;

    private long ServerMs => Host!.RunOnGameThread(() => Server!.ElapsedMilliseconds);

    [ServerScenario]
    public void Ticks_AdvanceTheServersTimeExactly()
    {
        using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
        (long world, long events, long uptime) before = Readings();

        Host.Ticks(600);

        (long world, long events, long uptime) after = Readings();
        Assert.Equal(30_000, after.world - before.world);
        Assert.Equal(30_000, after.events - before.events);
        Assert.Equal(30_000, after.uptime - before.uptime);
        Assert.Equal(TimeSpan.FromSeconds(30), clock.Elapsed);
    }

    [ServerScenario]
    public void Getters_StillFollow_OnceTheirCallersAreHot()
    {
        // Thousands of calls get the callers recompiled at the top tier, where small getters are
        // inlined; the patch must hold there too.
        long sink = 0;
        Host!.RunOnGameThread(() =>
        {
            for (int i = 0; i < 200_000; i++) sink += ServerApi.World.ElapsedMilliseconds + Server!.EventManager.InWorldEllapsedMs + ServerApi.Server.ServerUptimeMilliseconds;
        });
        Host.Ticks(30);

        using VirtualClock clock = Host.UseVirtualClock(tickMs: 20);
        (long world, long events, long uptime) before = Readings();
        Host.Ticks(50);
        (long world, long events, long uptime) after = Readings();
        Assert.Equal((1000L, 1000L, 1000L), (after.world - before.world, after.events - before.events, after.uptime - before.uptime));
        Assert.NotEqual(0, sink);
    }

    [ServerScenario]
    public void ListenersAndCallbacks_RunOnSimulatedTime()
    {
        using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
        List<float> listenerDts = [];
        int callbackTick = -1;
        int tick = 0;
        (long listener, long callback) = Host.RunOnGameThread(() => (
            ServerApi.Event.RegisterGameTickListener(dt => listenerDts.Add(dt), 100),
            ServerApi.Event.RegisterCallback(_ => callbackTick = tick, 1000)));
        try
        {
            for (tick = 1; tick <= 600; tick++) Host.Tick();

            // A listener runs once more than its interval has passed: every third 50 ms tick.
            Assert.Equal(200, listenerDts.Count);
            Assert.All(listenerDts, dt => Assert.Equal(0.15f, dt, 5));
            Assert.Equal(20, callbackTick);
        }
        finally
        {
            Host.RunOnGameThread(() =>
            {
                ServerApi.Event.UnregisterGameTickListener(listener);
                ServerApi.Event.UnregisterCallback(callback);
            });
        }
    }

    [ServerScenario]
    public void Advance_TicksInStepsOfAQuarterSecond()
    {
        using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
        float maxDt = 0;
        long listener = Host.RunOnGameThread(() => ServerApi.Event.RegisterGameTickListener(dt => maxDt = Math.Max(maxDt, dt), 20));
        try
        {
            long before = ServerMs;
            Assert.Equal(40, clock.Advance(TimeSpan.FromSeconds(10)));
            Assert.Equal(10_000, ServerMs - before);
            Assert.Equal(0.25f, maxDt, 5);

            Assert.Equal(5, clock.Advance(TimeSpan.FromSeconds(1), TimeSpan.FromMilliseconds(200)));
            Assert.Equal(11_000, ServerMs - before);
            Assert.Equal(0, clock.Advance(TimeSpan.Zero));
        }
        finally
        {
            Host.RunOnGameThread(() => ServerApi.Event.UnregisterGameTickListener(listener));
        }
    }

    [ServerScenario]
    public void Calendar_FollowsTheClock()
    {
        // The calendar runs only while players are connected, unless the server passes time when empty.
        bool passTime = Host!.RunOnGameThread(() => Server!.Config.PassTimeWhenEmpty);
        Host.RunOnGameThread(() => Server!.Config.PassTimeWhenEmpty = true);
        try
        {
            using VirtualClock clock = Host.UseVirtualClock(tickMs: 50);
            Assert.True(clock.FollowsCalendar);
            Host.Ticks(10);
            Assert.True(Host.Calendar.IsRunning, "The calendar does not run");

            (double before, double rate) = Host.RunOnGameThread(() =>
            {
                GameCalendar calendar = (GameCalendar)Server!.Calendar;
                calendar.Tick();
                return (CalendarDriver.Internals.Timespan(calendar).TotalSeconds, (double)calendar.SpeedOfTime * calendar.CalendarSpeedMul);
            });

            Host.Ticks(200);

            double after = Host.RunOnGameThread(() =>
            {
                GameCalendar calendar = (GameCalendar)Server!.Calendar;
                calendar.Tick();
                return CalendarDriver.Internals.Timespan(calendar).TotalSeconds;
            });

            // Ten simulated seconds, within a tick.
            Assert.InRange(after - before, (10 - 0.05) * rate, (10 + 0.05) * rate);
        }
        finally
        {
            Host.RunOnGameThread(() => Server!.Config.PassTimeWhenEmpty = passTime);
        }
    }

    [ServerScenario]
    public void Dispose_GoesBackToTheWallClockWithoutAJump()
    {
        long end;
        using (VirtualClock clock = Host!.UseVirtualClock(tickMs: 50))
        {
            clock.Advance(TimeSpan.FromMinutes(2));
            end = clock.ElapsedMilliseconds;
            clock.Dispose();
            Assert.False(clock.IsActive);
            Assert.Throws<ObjectDisposedException>(() => clock.Advance(TimeSpan.FromSeconds(1)));
        }

        Assert.Null(Host.VirtualClock);
        long first = ServerMs;
        Host.Ticks(5);
        long second = ServerMs;
        Assert.InRange(first, end, end + 60_000);
        Assert.True(second >= first, $"The server's time went back from {first} to {second}");
    }

    [ServerScenario]
    public void WallClock_IsUnchangedWithoutAClock()
    {
        Assert.Null(Host!.VirtualClock);
        (long read, long watch) = Host.RunOnGameThread(() => (Server!.ElapsedMilliseconds, Server.totalUnpausedTime.ElapsedMilliseconds));

        // Earlier tests on this pooled server may have left it ahead of its stopwatch, never behind.
        Assert.True(read >= watch - 5, $"The server reads {read} ms, its stopwatch {watch} ms");
    }

    [ServerScenario]
    public void Misuse_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Host!.UseVirtualClock(tickMs: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Host!.UseVirtualClock(tickMs: 1001));
        using VirtualClock clock = Host!.UseVirtualClock();
        Assert.Same(clock, Host.VirtualClock);
        Assert.Throws<InvalidOperationException>(() => Host.UseVirtualClock());
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromSeconds(-1)));
        Assert.Throws<ArgumentOutOfRangeException>(() => clock.Advance(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)));
        Assert.Throws<InvalidOperationException>(() => Host.RunOnGameThread(() => { Host.WaitForBackgroundWorkAsync(); }));
    }

    [ServerScenario]
    public async Task WaitForBackgroundWork_SettlesLighting()
    {
        Assert.True(BackgroundWork.PathfindingCounted, "The async pathfinder's passes are not counted");

        using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
        BlockPos torch = Host.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos).AddCopy(3, 0, 3);
        BlockPos beside = torch.AddCopy(1, 0, 0);
        Assert.Equal(0, BlockLight(beside));

        this.SetBlock(torch, "game:torch-basic-lit-up");

        // The game thread hands the change to the relight thread on its next block update.
        clock.Advance(TimeSpan.FromSeconds(1));
        await Host.WaitForBackgroundWorkAsync();

        Assert.Equal(0, Host.RunOnGameThread(() => { lock (Server!.WorldMap.LightingTasksLock) return Server.WorldMap.LightingTasks.Count; }));
        Assert.True(BlockLight(beside) > 0, "The torch lit nothing once the relight thread was idle");
    }

    private int BlockLight(BlockPos pos) => Host!.RunOnGameThread(() => ServerApi.World.BlockAccessor.GetLightLevel(pos, EnumLightLevelType.OnlyBlockLight));

    private (long World, long Events, long Uptime) Readings() =>
        Host!.RunOnGameThread(() => (ServerApi.World.ElapsedMilliseconds, Server!.EventManager.InWorldEllapsedMs, ServerApi.Server.ServerUptimeMilliseconds));
}

/// <summary>A clock a test leaves in use, and a failure's run.json, through the real scenario pipeline.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public sealed class LiveVirtualClockLifecycleTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-clock-live-" + Guid.NewGuid().ToString("N")[..8]);

    public LiveVirtualClockLifecycleTests()
    {
        FailureArtifactWriter.RootOverride = _root;
    }

    public void Dispose()
    {
        FailureArtifactWriter.RootOverride = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AForgottenClock_EndsWithTheTest_AndAFailureRecordsIt()
    {
        ScenarioRunnerHarness.RunResult leaves = await ScenarioRunnerHarness.RunAsync(typeof(ForgetfulClass), nameof(ForgetfulClass.LeavesAClockRunning));
        Assert.True(leaves.Passed.Count == 1, leaves.Failed.Count > 0 ? leaves.FailureMessage : "The test did not run");

        ScenarioRunnerHarness.RunResult next = await ScenarioRunnerHarness.RunAsync(typeof(ForgetfulClass), nameof(ForgetfulClass.StartsOnTheWallClock));
        Assert.True(next.Passed.Count == 1, next.Failed.Count > 0 ? next.FailureMessage : "The test did not run");

        ScenarioRunnerHarness.RunResult fails = await ScenarioRunnerHarness.RunAsync(typeof(ForgetfulClass), nameof(ForgetfulClass.FailsOnAClock));
        Assert.Single(fails.Failed);
        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, nameof(ForgetfulClass), nameof(ForgetfulClass.FailsOnAClock), "run.json")));
        JsonElement clock = run.RootElement.GetProperty("serverClock");
        Assert.Equal("virtual", clock.GetProperty("mode").GetString());
        Assert.Equal(40, clock.GetProperty("tickMs").GetInt32());
        Assert.False(clock.GetProperty("active").GetBoolean());
        Assert.Equal(120, clock.GetProperty("simulatedMs").GetInt64());
    }

#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    [ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
    private sealed class ForgetfulClass : ServerScenarioBase
    {
        private static EmbeddedServerHost? s_host;
        private static long s_left;

        [ServerScenario]
        public void LeavesAClockRunning()
        {
            VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
            clock.Advance(TimeSpan.FromSeconds(5));
            s_left = clock.ElapsedMilliseconds;
            s_host = Host;
        }

        [ServerScenario]
        public void StartsOnTheWallClock()
        {
            Assert.Same(s_host, Host);
            Assert.Null(Host!.VirtualClock);
            Assert.True(Host.RunOnGameThread(() => Server!.ElapsedMilliseconds) >= s_left, "The rollback took the server's time back");
        }

        [ServerScenario]
        public void FailsOnAClock()
        {
            using VirtualClock clock = Host!.UseVirtualClock(tickMs: 40);
            Host.Ticks(3);
            throw new InvalidOperationException("failed on purpose");
        }
    }
#pragma warning restore xUnit1000
}

/// <summary>An engine-mode client stepped in lockstep with a server on a virtual clock.</summary>
[Collection("Sequential")]
public class LiveVirtualClockLockstepTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public void EachFrame_AdvancesTheServerByItsTicks()
    {
        using VirtualClock clock = ServerHost!.UseVirtualClock(tickMs: 50);
        long start = clock.ElapsedMilliseconds;

        Session!.StepFrames(20, dt: 0.05f);
        Assert.Equal(start + 1_000, clock.ElapsedMilliseconds);

        Session.StepFrames(10, dt: 0.05f, serverTicksPerFrame: 2);
        Assert.Equal(start + 2_000, clock.ElapsedMilliseconds);
    }
}
