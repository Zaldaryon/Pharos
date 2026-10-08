using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>Isolation reports and the per-class log, before anything boots.</summary>
public class IsolationReportTests
{
    [Fact]
    public void Reports_DescribeThemselves()
    {
        Assert.Equal("rolled back (12 chunks, 3 listeners removed, 340 ms)", new IsolationReport(IsolationKind.RolledBack, null, 12, 3, TimeSpan.FromMilliseconds(340)).ToString());
        Assert.Equal("booted again: the client was disconnected (2000 ms)", new IsolationReport(IsolationKind.Restarted, "the client was disconnected", 0, 0, TimeSpan.FromSeconds(2)).ToString());
        Assert.Equal("booted (1500 ms)", new IsolationReport(IsolationKind.FirstBoot, null, 0, 0, TimeSpan.FromMilliseconds(1500)).ToString());
    }

    [Fact]
    public void TheLog_CarriesHowOneTestLeftTheHostsToTheNext()
    {
        Type testClass = typeof(LogSample);
        IsolationLog.Forget(testClass);

        Assert.Equal(IsolationKind.FirstBoot, IsolationLog.Prepared(testClass, reused: false, recycled: false, TimeSpan.FromSeconds(1)).Kind);

        IsolationReport rolledBack = new(IsolationKind.RolledBack, null, 4, 1, TimeSpan.FromMilliseconds(50));
        IsolationLog.Left(testClass, rolledBack, fallback: null, kept: true);
        Assert.Equal(rolledBack, IsolationLog.Prepared(testClass, reused: true, recycled: false, TimeSpan.Zero));

        IsolationLog.Left(testClass, null, "the client was disconnected");
        IsolationReport restarted = IsolationLog.Prepared(testClass, reused: false, recycled: false, TimeSpan.FromSeconds(2));
        Assert.Equal(IsolationKind.Restarted, restarted.Kind);
        Assert.Equal("the client was disconnected", restarted.Reason);

        IsolationLog.Left(testClass, rolledBack, fallback: null, kept: true);
        Assert.Equal("another test class used the pooled hosts in between", IsolationLog.Prepared(testClass, reused: false, recycled: false, TimeSpan.Zero).Reason);

        Assert.Equal("LogSample so far: 1 first boot, 2 booted again, 1 rolled back (the client was disconnected; another test class used the pooled hosts in between)", IsolationLog.Summary(testClass));
    }

    [Fact]
    public void ATestsListeners_AreTheOnesCompiledIntoItsAssembly()
    {
        IReadOnlySet<System.Reflection.Assembly> assemblies = IsolationLog.TestAssemblies(typeof(LogSample));
        int captured = 0;
        Action<float> lambda = _ => captured++;

        Assert.Contains(typeof(IsolationReportTests).Assembly, assemblies);
        Assert.DoesNotContain(typeof(IsolationLog).Assembly, assemblies);
        Assert.True(ListenerWatermark.DeclaredIn(lambda, assemblies));
        Assert.False(ListenerWatermark.DeclaredIn(new Action<string>(Console.WriteLine), assemblies));
        Assert.True(ListenerWatermark.DeclaredIn(Delegate.Combine(new Action<string>(Console.WriteLine), new Action<string>(_ => captured++))!, assemblies));
    }

    private sealed class LogSample;
}

/// <summary>
/// Rollback events, listener removal and isolation reports on a real client-server pair. Every
/// test does the same, so whichever runs later checks what the ones before it left.
/// </summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharosclientmod")]
public partial class LiveClientServerRollbackParticipationTests : ClientServerScenarioBase
{
    private static int s_serverTicks, s_clientTicks, s_busCalls, s_restoredHooks;

    protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 };

    protected override void OnRollbackRestored() => Interlocked.Increment(ref s_restoredHooks);

    [ClientServerScenario]
    public Task First() => LeavesNothingBehindAsync();

    [ClientServerScenario]
    public Task Second() => LeavesNothingBehindAsync();

    [ClientServerScenario]
    public Task Third() => LeavesNothingBehindAsync();

    private async Task LeavesNothingBehindAsync()
    {
        IsolationReport report = Isolation!;
        Assert.True(report.Kind is IsolationKind.FirstBoot or IsolationKind.RolledBack, report.ToString());

        (int serverCaptured, int serverRestored) = Counts((await ServerCommandAsync("/pharosrollbacks")));
        (int clientCaptured, int clientRestored) = Counts((await Client!.Commands.ExecuteSuccessAsync(".pharoscmd rollbacks")).Message);
        Assert.Equal(1, serverCaptured);
        Assert.Equal(1, clientCaptured);
        if (report.Kind == IsolationKind.RolledBack)
        {
            Assert.True(report.ListenersRemoved >= 3, report.ToString());
            Assert.True(serverRestored >= 1 && clientRestored >= 1, $"server {serverRestored}, client {clientRestored}");
            Assert.True(Volatile.Read(ref s_restoredHooks) >= 1);
        }

        // Nothing an earlier test registered still runs.
        int server = Volatile.Read(ref s_serverTicks), client = Volatile.Read(ref s_clientTicks), bus = Volatile.Read(ref s_busCalls);
        await Session!.StepFramesAsync(10);
        ServerHost!.RunOnGameThread(() => ((ICoreAPI)Server!.Api).Event.PushEvent("pharos:test:ping"));
        Assert.Equal(server, Volatile.Read(ref s_serverTicks));
        Assert.Equal(client, Volatile.Read(ref s_clientTicks));
        Assert.Equal(bus, Volatile.Read(ref s_busCalls));

        // This test's own run until the rollback.
        ServerHost.RunOnGameThread(() =>
        {
            ICoreAPI api = (ICoreAPI)Server!.Api;
            api.Event.RegisterGameTickListener(_ => Interlocked.Increment(ref s_serverTicks), 0);
            api.Event.RegisterEventBusListener((string name, ref EnumHandling _, Vintagestory.API.Datastructures.IAttribute data) => Interlocked.Increment(ref s_busCalls), filterByEventName: "pharos:test:ping");
        });
        Client.RunOnClientThread(() => Client.Client.api.Event.RegisterGameTickListener(_ => Interlocked.Increment(ref s_clientTicks), 0));

        await Session.StepFramesAsync(10);
        ServerHost.RunOnGameThread(() => ((ICoreAPI)Server!.Api).Event.PushEvent("pharos:test:ping"));
        Assert.True(Volatile.Read(ref s_serverTicks) > server);
        Assert.True(Volatile.Read(ref s_clientTicks) > client);
        Assert.Equal(bus + 1, Volatile.Read(ref s_busCalls));
    }

    private Task<string?> ServerCommandAsync(string command)
    {
        TaskCompletionSource<string?> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ServerHost!.RunOnGameThread(() => ((Vintagestory.API.Server.ICoreServerAPI)Server!.Api).ChatCommands.ExecuteUnparsed(
            command,
            new TextCommandCallingArgs { Caller = ServerScenarioBase.ConsoleCaller() },
            result => done.TrySetResult(result.StatusMessage)));
        return done.Task;
    }

    private static (int Captured, int Restored) Counts(string? text)
    {
        Match match = CountsPattern().Match(text ?? "");
        Assert.True(match.Success, text);
        return (int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [GeneratedRegex(@"captured=(\d+) restored=(\d+)")]
    private static partial Regex CountsPattern();
}

/// <summary>The same on a server scenario.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharosclientmod")]
public class LiveServerRollbackParticipationTests : ServerScenarioBase
{
    private static int s_ticks, s_restoredHooks;

    protected override void OnRollbackRestored() => Interlocked.Increment(ref s_restoredHooks);

    [ServerScenario]
    public Task First() => LeavesNothingBehindAsync();

    [ServerScenario]
    public Task Second() => LeavesNothingBehindAsync();

    private async Task LeavesNothingBehindAsync()
    {
        IsolationReport report = Isolation!;
        if (report.Kind == IsolationKind.RolledBack)
        {
            Assert.True(report.ListenersRemoved >= 1, report.ToString());
            Assert.True(Volatile.Read(ref s_restoredHooks) >= 1);
            Assert.Matches(@"captured=1 restored=[1-9]", (await ExecuteCommand("/pharosrollbacks")).StatusMessage);
        }

        int before = Volatile.Read(ref s_ticks);
        for (int i = 0; i < 5; i++) Host!.Tick();
        Assert.Equal(before, Volatile.Read(ref s_ticks));

        Host.RunOnGameThread(() => Api!.Event.RegisterGameTickListener(_ => Interlocked.Increment(ref s_ticks), 0));
        for (int i = 0; i < 5; i++) Host.Tick();
        Assert.True(Volatile.Read(ref s_ticks) > before);
    }
}

/// <summary>A class that wants every rollback to work, and a test that makes it impossible.</summary>
[Collection("Sequential")]
public class StrictIsolationTests
{
    [Fact]
    [Trait(PharosTraits.Category, PharosTraits.Live)]
    public async Task ATestWhoseWorldCannotBeRolledBack_FailsUnderStrictIsolation()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Disconnects), nameof(Disconnects.DisconnectsTheClient));

        Assert.Equal(nameof(IsolationException), result.FailureType.Split('.').Last());
        Assert.Contains("the client was disconnected", result.FailureMessage);
    }

    [Fact]
    [Trait(PharosTraits.Category, PharosTraits.Live)]
    public async Task EachTestsOutput_SaysHowItWasIsolated()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Quiet), nameof(Quiet.DoesNothing));

        string output = Assert.Single(result.Passed).Output;
        Assert.Contains("isolation: booted", output);
        Assert.Contains("after this test: rolled back", output);
        Assert.Contains("Quiet so far:", output);
    }

#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    [ServerWorld(StrictIsolation = true)]
    private sealed class Disconnects : ClientServerScenarioBase
    {
        protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 };

        [ClientServerScenario]
        public void DisconnectsTheClient() => Session!.Disconnect();
    }

    private sealed class Quiet : ServerScenarioBase
    {
        [ServerScenario]
        public void DoesNothing()
        {
        }
    }
#pragma warning restore xUnit1000
}
