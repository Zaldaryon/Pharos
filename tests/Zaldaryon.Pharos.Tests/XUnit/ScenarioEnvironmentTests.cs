using Xunit;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Tests.XUnit;

public sealed class ScenarioEnvironmentTests
{
    [Fact]
    public async Task EnvironmentsApplyBaseToDerivedThenMethodAndCreateFreshInstances()
    {
        EnvironmentOrder.Reset();
        ScenarioContext context = new(server: null, client: null, session: null);
        System.Reflection.MethodInfo method = typeof(DerivedFixture).GetMethod(nameof(DerivedFixture.Test))!;

        await ScenarioEnvironments.ApplyAsync(typeof(DerivedFixture), method, context, CancellationToken.None);
        await ScenarioEnvironments.ApplyAsync(typeof(DerivedFixture), method, context, CancellationToken.None);

        Assert.Equal(["base", "derived", "method", "base", "derived", "method"], EnvironmentOrder.Applied);
        Assert.Equal(6, EnvironmentOrder.Created);
    }

    [Fact]
    public async Task CleanupRunsInReverseOrderAndReportsAllFailures()
    {
        List<string> calls = [];
        ScenarioContext context = new(server: null, client: null, session: null);
        context.OnCleanup(() => calls.Add("first"));
        context.OnCleanup(() => { calls.Add("second"); throw new InvalidOperationException("second failed"); });
        context.OnCleanup(() => { calls.Add("third"); throw new ArgumentException("third failed"); });

        AggregateException error = await Assert.ThrowsAsync<AggregateException>(() => context.DisposeAsync());

        Assert.Equal(["third", "second", "first"], calls);
        Assert.Equal(2, error.InnerExceptions.Count);
        AggregateException retry = await Assert.ThrowsAsync<AggregateException>(() => context.DisposeAsync());
        Assert.Equal(2, retry.InnerExceptions.Count);
        Assert.Equal(3, calls.Count);
    }

    [Fact]
    public async Task PersistentCleanupFailureDisqualifiesTheHostFromPooling()
    {
        ScenarioContext context = new(server: null, client: null, session: null);
        context.OnCleanup(() => throw new InvalidOperationException("cleanup failed"));

        InvalidOperationException invokerFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.DisposeAsync());
        InvalidOperationException fallbackFailure = await Assert.ThrowsAsync<InvalidOperationException>(() => context.DisposeAsync());

        Assert.Same(invokerFailure, fallbackFailure);
        Assert.False(ScenarioHostPool.CanReuseAfterEnvironmentCleanup(fallbackFailure));
        Assert.True(ScenarioHostPool.CanReuseAfterEnvironmentCleanup(null));
    }

    [Fact]
    public async Task CleanupTimeoutCancelsRegisteredCleanup()
    {
        ScenarioContext context = new(server: null, client: null, session: null);
        TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        context.OnCleanup(async cancellationToken =>
        {
            try { await Task.Delay(Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException)
            {
                canceled.TrySetResult();
                throw;
            }
        });

        await Assert.ThrowsAsync<TimeoutException>(() => context.DisposeAsync(TimeSpan.FromMilliseconds(50)));

        Assert.True(await Task.WhenAny(canceled.Task, Task.Delay(TimeSpan.FromSeconds(2))) == canceled.Task);
    }

    [Fact]
    public void ThreadPumpPumpsAsyncCleanupCancellationOnOwnerThread()
    {
        int ownerThread = Environment.CurrentManagedThreadId;
        bool canceledOnOwner = false;
        using CancellationTokenSource cancellation = new(TimeSpan.FromMilliseconds(50));
        ScenarioThreadPump pump = new();
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(pump);
            Task cleanup = CancelAndObserveAsync();
            Assert.True(pump.Run(cleanup, cancellation.Token, TimeSpan.FromSeconds(1)));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        Assert.True(canceledOnOwner);

        async Task CancelAndObserveAsync()
        {
            try { await Task.Delay(Timeout.Infinite, cancellation.Token); }
            catch (OperationCanceledException)
            {
                canceledOnOwner = Environment.CurrentManagedThreadId == ownerThread;
            }
        }
    }

    [Fact]
    public void ThreadPumpStartsGraceAfterLateCancellation()
    {
        using CancellationTokenSource cancellation = new();
        ScenarioThreadPump pump = new();
        TaskCompletionSource resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        SynchronizationContext? previous = SynchronizationContext.Current;
        try
        {
            SynchronizationContext.SetSynchronizationContext(pump);
            Task cleanup = ResumeAfterCancellationAsync();
            // This owner-thread callback delays cancellation beyond the grace interval. It
            // makes a deadline started at Run entry observably expire before the abort arrives.
            pump.Post(_ =>
            {
                Thread.Sleep(150);
                cancellation.Cancel();
                pump.Post(_ => resume.TrySetResult(), null);
            }, null);
            Assert.True(pump.Run(cleanup, cancellation.Token, TimeSpan.FromMilliseconds(100)));
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }

        async Task ResumeAfterCancellationAsync()
        {
            cancellation.Token.ThrowIfCancellationRequested();
            await resume.Task;
        }
    }

    private static class EnvironmentOrder
    {
        private static readonly List<string> s_applied = [];
        private static int s_created;
        public static IReadOnlyList<string> Applied => s_applied;
        public static int Created => s_created;
        public static void Add(string name) => s_applied.Add(name);
        public static void CreatedOne() => s_created++;
        public static void Reset() { s_applied.Clear(); s_created = 0; }
    }

    [TestEnvironment(typeof(BaseEnvironment))]
    private abstract class BaseFixture
    {
    }

    [TestEnvironment(typeof(DerivedEnvironment))]
    private sealed class DerivedFixture : BaseFixture
    {
        [TestEnvironment(typeof(MethodEnvironment))]
        public void Test() { }
    }

    public sealed class BaseEnvironment : ITestEnvironment
    {
        public BaseEnvironment() => EnvironmentOrder.CreatedOne();
        public Task ApplyAsync(ScenarioContext context, CancellationToken cancellationToken)
        {
            EnvironmentOrder.Add("base");
            return Task.CompletedTask;
        }
    }

    public sealed class DerivedEnvironment : ITestEnvironment
    {
        public DerivedEnvironment() => EnvironmentOrder.CreatedOne();
        public Task ApplyAsync(ScenarioContext context, CancellationToken cancellationToken)
        {
            EnvironmentOrder.Add("derived");
            return Task.CompletedTask;
        }
    }

    public sealed class MethodEnvironment : ITestEnvironment
    {
        public MethodEnvironment() => EnvironmentOrder.CreatedOne();
        public Task ApplyAsync(ScenarioContext context, CancellationToken cancellationToken)
        {
            EnvironmentOrder.Add("method");
            return Task.CompletedTask;
        }
    }
}
