using System.Diagnostics;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Checks that server teardown waits for started callbacks before releasing its logger.</summary>
[Collection("Sequential")]
[ServerWorld(seed: 4242, Isolation = Isolation.Restart)]
public sealed class ThreadPoolDrainTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task DisposeWaitsForStartedCallbackAndPreservesItsWarning()
    {
        const string caller = "Pharos thread-pool drain test";
        const string exceptionMessage = "Expected delayed callback failure during teardown";
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var host = Host!;

        TyronThreadPool.QueueTask(async () =>
        {
            started.TrySetResult();
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            while (!host.Logs.Entries.Any(entry => entry.Message.Contains("Stopped the server!", StringComparison.Ordinal)))
            {
                await Task.Delay(10, timeout.Token);
            }

            // ServerMain.Stop clears its log watchers immediately after this final event.
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            finished.TrySetResult();
            throw new InvalidOperationException(exceptionMessage);
        }, caller);

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await host.DisposeAsync();

        Assert.True(finished.Task.IsCompleted, "The embedded server returned before its running callback finished.");
        Assert.Contains(host.Logs.Warnings, entry => entry.Message.Contains(caller, StringComparison.Ordinal));
        Assert.Contains(host.Logs.Warnings, entry => entry.Message.Contains(exceptionMessage, StringComparison.Ordinal));
        Assert.DoesNotContain(host.Logs.Warnings, entry => entry.Message.StartsWith("Pharos thread pool did not become idle", StringComparison.Ordinal));
    }

    [ServerScenario]
    public async Task DisposeWaitsForQueuedCallbackBeforeReleasingThreadPool()
    {
        var host = Host!;
        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread orchestrator = new(() =>
        {
            try
            {
                VerifySaturatedAsyncDisposal(host);
                completed.TrySetResult();
            }
            catch (Exception error)
            {
                completed.TrySetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "Pharos thread-pool drain test orchestrator"
        };

        orchestrator.Start();
        await completed.Task;
        orchestrator.Join();
    }

    private static void VerifySaturatedAsyncDisposal(EmbeddedServerHost host)
    {
        const string caller = "Pharos queued thread-pool drain test";
        ThreadPool.GetMinThreads(out int oldMinWorkers, out int oldMinIo);
        ThreadPool.GetMaxThreads(out int oldMaxWorkers, out int oldMaxIo);
        int workerLimit = Math.Min(10, oldMaxWorkers);
        int blockerCount = workerLimit;
        Assert.True(blockerCount > 0, "The test needs at least one thread-pool worker.");
        ManualResetEventSlim releaseBlockers = new(false);
        ManualResetEventSlim callbackStarted = new(false);
        CountdownEvent blockersStarted = new(blockerCount);
        CountdownEvent blockersFinished = new(blockerCount);
        Task? dispose = null;

        try
        {
            Assert.True(ThreadPool.SetMinThreads(workerLimit, oldMinIo));
            Assert.True(ThreadPool.SetMaxThreads(workerLimit, oldMaxIo));

            Stopwatch waitForQuiet = Stopwatch.StartNew();
            int quietMs = 0;
            while (waitForQuiet.Elapsed < TimeSpan.FromSeconds(15) && quietMs < 250)
            {
                if (TyronThreadPool.Inst.RunningTasks.IsEmpty && ThreadPool.PendingWorkItemCount == 0)
                {
                    quietMs += 10;
                }
                else
                {
                    quietMs = 0;
                }

                Thread.Sleep(10);
            }

            Assert.True(
                quietMs >= 250,
                $"The thread pool did not become quiet: {TyronThreadPool.Inst.ListAllRunningTasks()}; " +
                $"{ThreadPool.PendingWorkItemCount} queued.");

            for (int i = 0; i < blockerCount; i++)
            {
                bool blockerQueued = ThreadPool.QueueUserWorkItem(_ =>
                {
                    blockersStarted.Signal();
                    releaseBlockers.Wait();
                    blockersFinished.Signal();
                }, null);
                Assert.True(blockerQueued, "A thread-pool blocker could not be queued.");
            }

            Stopwatch waitForSaturation = Stopwatch.StartNew();
            ThreadPool.GetAvailableThreads(out int availableWorkers, out _);
            int startedBlockers = blockerCount - blockersStarted.CurrentCount;
            while (waitForSaturation.Elapsed < TimeSpan.FromSeconds(15)
                && (availableWorkers > 0 || startedBlockers < workerLimit - 1))
            {
                Thread.Sleep(10);
                ThreadPool.GetAvailableThreads(out availableWorkers, out _);
                startedBlockers = blockerCount - blockersStarted.CurrentCount;
            }

            Assert.True(
                startedBlockers >= workerLimit - 1,
                $"The thread-pool workers did not start enough blockers ({startedBlockers}/{blockerCount} started).");
            Assert.True(
                TyronThreadPool.Inst.RunningTasks.IsEmpty,
                $"Unexpected Tyron callbacks were already running before the test callback was queued: " +
                TyronThreadPool.Inst.ListAllRunningTasks());
            Assert.Equal(0, availableWorkers);

            long pendingBeforeCallback = ThreadPool.PendingWorkItemCount;
            TyronThreadPool.QueueTask(() => callbackStarted.Set(), caller);
            Assert.False(callbackStarted.IsSet, "The callback should remain queued behind the blockers.");
            Assert.True(
                ThreadPool.PendingWorkItemCount > pendingBeforeCallback,
                "The callback was not left pending in the worker queue.");

            dispose = host.DisposeAsync().AsTask();
            Stopwatch waitForStop = Stopwatch.StartNew();
            while (waitForStop.Elapsed < TimeSpan.FromSeconds(10)
                && !host.Logs.Entries.Any(entry => entry.Message.Contains("Stopped the server!", StringComparison.Ordinal)))
            {
                Thread.Sleep(10);
            }

            bool teardownStartedWhileWorkerWasBlocked = host.Logs.Entries.Any(
                entry => entry.Message.Contains("Stopped the server!", StringComparison.Ordinal));
            bool teardownReturnedBeforeQueuedCallbackCouldStart = dispose.Wait(TimeSpan.FromSeconds(2));
            bool callbackStartedBeforeBlockersWereReleased = callbackStarted.IsSet;
            ThreadPool.GetAvailableThreads(out int workersAvailableBeforeRelease, out _);
            ThreadPool.GetMaxThreads(out int maxWorkersBeforeRelease, out _);
            long pendingItemsBeforeRelease = ThreadPool.PendingWorkItemCount;
            int startedBlockersBeforeRelease = blockerCount - blockersStarted.CurrentCount;
            string runningCallbacksBeforeRelease = TyronThreadPool.Inst.ListAllRunningTasks();
            releaseBlockers.Set();
            dispose.GetAwaiter().GetResult();

            Assert.True(
                teardownStartedWhileWorkerWasBlocked,
                "Asynchronous teardown did not begin until a thread-pool worker became available.");
            Assert.False(
                teardownReturnedBeforeQueuedCallbackCouldStart,
                $"The embedded server returned while its callback remained queued behind blocked workers " +
                $"(available workers: {workersAvailableBeforeRelease}/{maxWorkersBeforeRelease}, " +
                $"pending items: {pendingItemsBeforeRelease}, started blockers: {startedBlockersBeforeRelease}/{blockerCount}, " +
                $"running callbacks: {runningCallbacksBeforeRelease}).");
            Assert.False(
                callbackStartedBeforeBlockersWereReleased,
                "The test callback unexpectedly started before the blockers were released.");
            Assert.True(
                callbackStarted.IsSet,
                "The embedded server returned while a queued callback was still waiting to start.");
        }
        finally
        {
            releaseBlockers.Set();
            if (dispose is not null)
            {
                try
                {
                    dispose.GetAwaiter().GetResult();
                }
                catch
                {
                    // Preserve the assertion or disposal failure from the test body.
                }
            }

            blockersFinished.Wait(TimeSpan.FromSeconds(10));
            callbackStarted.Wait(TimeSpan.FromSeconds(10));
            ThreadPool.SetMaxThreads(oldMaxWorkers, oldMaxIo);
            ThreadPool.SetMinThreads(oldMinWorkers, oldMinIo);
        }
    }

    [ServerScenario]
    public async Task DisposeDrainsCallbacksQueuedByServerSystems()
    {
        TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Thread orchestrator = new(() =>
        {
            try
            {
                VerifyServerSystemDisposeCallbackDrain(Host!);
                completed.TrySetResult();
            }
            catch (Exception error)
            {
                completed.TrySetException(error);
            }
        })
        {
            IsBackground = true,
            Name = "Pharos server-system disposal drain test orchestrator"
        };

        orchestrator.Start();
        await completed.Task;
        orchestrator.Join();
    }

    private static void VerifyServerSystemDisposeCallbackDrain(EmbeddedServerHost host)
    {
        const string caller = "Pharos server-system dispose callback test";
        const string warning = "Pharos callback queued while server systems were disposed";
        ThreadPool.GetMinThreads(out int oldMinWorkers, out int oldMinIo);
        ThreadPool.GetMaxThreads(out int oldMaxWorkers, out int oldMaxIo);
        int workerLimit = Math.Min(10, oldMaxWorkers);

        TaskCompletionSource releaseCallbacks = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ManualResetEventSlim callbackStarted = new(false);
        CountdownEvent callbacksFinished = new(1);
        int callbacksStarted = 0;
        Task? dispose = null;

        try
        {
            Assert.True(ThreadPool.SetMinThreads(Math.Min(oldMinWorkers, workerLimit), oldMinIo));
            Assert.True(ThreadPool.SetMaxThreads(workerLimit, oldMaxIo));
            Assert.True(ThreadPool.SetMinThreads(workerLimit, oldMinIo));

            Stopwatch waitForQuiet = Stopwatch.StartNew();
            int quietMs = 0;
            while (waitForQuiet.Elapsed < TimeSpan.FromSeconds(15) && quietMs < 250)
            {
                if (TyronThreadPool.Inst.RunningTasks.IsEmpty && ThreadPool.PendingWorkItemCount == 0)
                {
                    quietMs += 10;
                }
                else
                {
                    quietMs = 0;
                }

                Thread.Sleep(10);
            }

            Assert.True(quietMs >= 250, "The thread pool did not become quiet before system disposal.");
            ThreadPool.GetAvailableThreads(out int availableWorkers, out _);
            Assert.True(availableWorkers > 0, "The test needs an available worker before system disposal.");

            ILogger logger = ServerMain.Logger!;
            AddDisposeCallbackSystem(host.Server, () =>
            {
                TyronThreadPool.QueueTask(async () =>
                {
                    Interlocked.Increment(ref callbacksStarted);
                    callbackStarted.Set();
                    await releaseCallbacks.Task;
                    logger.Warning(warning);
                    callbacksFinished.Signal();
                }, caller);
            });

            dispose = host.DisposeAsync().AsTask();
            Assert.True(callbackStarted.Wait(TimeSpan.FromSeconds(15)), "Server-system disposal did not start its queued callbacks.");
            bool returnedBeforeCallbackWasReleased = dispose.Wait(TimeSpan.FromSeconds(2));
            ThreadPool.GetAvailableThreads(out int workersAvailableBeforeRelease, out _);
            ThreadPool.GetMaxThreads(out int maxWorkersBeforeRelease, out _);
            long pendingItemsBeforeRelease = ThreadPool.PendingWorkItemCount;
            int startedCallbacksBeforeRelease = Volatile.Read(ref callbacksStarted);
            int unfinishedCallbacksBeforeRelease = callbacksFinished.CurrentCount;
            string runningCallbacksBeforeRelease = TyronThreadPool.Inst.ListAllRunningTasks();
            releaseCallbacks.TrySetResult();
            dispose.GetAwaiter().GetResult();

            Assert.False(
                returnedBeforeCallbackWasReleased,
                $"The host returned while a callback queued from ServerSystem.Dispose was still running " +
                $"(available workers: {workersAvailableBeforeRelease}/{maxWorkersBeforeRelease}, " +
                $"pending items: {pendingItemsBeforeRelease}, started callbacks: {startedCallbacksBeforeRelease}, " +
                $"unfinished callbacks: {unfinishedCallbacksBeforeRelease}, " +
                $"running callbacks: {runningCallbacksBeforeRelease}).");
            Assert.True(callbacksFinished.Wait(TimeSpan.FromSeconds(10)), "The server-system dispose callbacks did not finish.");
            Assert.Contains(host.Logs.Warnings, entry => entry.Message.Contains(warning, StringComparison.Ordinal));
        }
        finally
        {
            releaseCallbacks.TrySetResult();
            if (dispose is not null)
            {
                try
                {
                    dispose.GetAwaiter().GetResult();
                }
                catch
                {
                    // Preserve the assertion or disposal failure from the test body.
                }
            }

            callbacksFinished.Wait(TimeSpan.FromSeconds(10));
            ThreadPool.SetMaxThreads(oldMaxWorkers, oldMaxIo);
            ThreadPool.SetMinThreads(oldMinWorkers, oldMinIo);
        }
    }

    private static void AddDisposeCallbackSystem(ServerMain server, Action onDispose)
    {
        FieldInfo systemsField = typeof(ServerMain).GetField("Systems", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("ServerMain.Systems was not found.");
        ServerSystem[] systems = (ServerSystem[]?)systemsField.GetValue(server)
            ?? throw new InvalidOperationException("ServerMain.Systems was null.");
        Array.Resize(ref systems, systems.Length + 1);
        systems[^1] = new QueueCallbackOnDisposeSystem(server, onDispose);
        systemsField.SetValue(server, systems);
    }

    private sealed class QueueCallbackOnDisposeSystem(ServerMain server, Action onDispose) : ServerSystem(server)
    {
        public override void Dispose() => onDispose();
    }

    [ServerScenario]
    public void DisposeDoesNotWaitForUnrelatedThreadPoolWorkers()
    {
        var host = Host!;
        using ManualResetEventSlim releaseWorker = new(false);
        using ManualResetEventSlim workerStarted = new(false);
        Assert.True(ThreadPool.QueueUserWorkItem(_ =>
        {
            workerStarted.Set();
            releaseWorker.Wait();
        }), "The unrelated worker could not be queued.");
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(10)), "The unrelated worker did not start.");

        try
        {
            host.Dispose();

            Assert.DoesNotContain(
                host.Logs.Warnings,
                entry => entry.Message.StartsWith("Pharos thread pool did not become idle", StringComparison.Ordinal));
        }
        finally
        {
            releaseWorker.Set();
        }
    }

    [ServerScenario]
    public async Task DisposeLogsWhenThreadPoolDrainTimesOut()
    {
        const string caller = "Pharos blocked thread-pool drain timeout test";
        const string timeoutWarning = "Pharos thread pool did not become idle within 12000ms after Stop";
        var host = Host!;
        TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        ManualResetEventSlim disposeReturned = new(false);
        Thread watchdog = new(() =>
        {
            if (!disposeReturned.Wait(TimeSpan.FromSeconds(20))) release.TrySetResult();
        })
        {
            IsBackground = true,
            Name = "Pharos thread-pool timeout test watchdog"
        };

        try
        {
            TyronThreadPool.QueueTask(async () =>
            {
                started.TrySetResult();
                await release.Task;
            }, caller);

            await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            watchdog.Start();

            Stopwatch elapsed = Stopwatch.StartNew();
            host.Dispose();
            disposeReturned.Set();

            Assert.True(elapsed.Elapsed >= TimeSpan.FromSeconds(11), "The teardown did not exercise the 12-second thread-pool timeout.");
            Assert.Contains(host.Logs.Warnings, entry => entry.Message.StartsWith(timeoutWarning, StringComparison.Ordinal));
            Assert.Contains(host.Logs.Warnings, entry => entry.Message.Contains(caller, StringComparison.Ordinal));
        }
        finally
        {
            release.TrySetResult();
            disposeReturned.Set();
            if (watchdog.IsAlive) watchdog.Join(TimeSpan.FromSeconds(5));
            disposeReturned.Dispose();
        }
    }
}
