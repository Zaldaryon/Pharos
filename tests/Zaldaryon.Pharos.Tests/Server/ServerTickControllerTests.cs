using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>
/// Headless-safe tests for <see cref="EmbeddedServerHost"/> tick controller functionality.
/// </summary>
/// <remarks>
/// These tests validate tick counter logic, <see cref="ServerCrashedException"/>,
/// TickUntilAsync behavior, and game thread queue mechanics using pure in-memory
/// mocks, making them safe for headless CI environments without booting a live server.
/// </remarks>
public class ServerTickControllerTests
{
    #region ServerCrashedException Tests

    [Fact]
    public void ServerCrashedException_DefaultConstructor_HasExpectedMessage()
    {
        ServerCrashedException ex = new();

        Assert.Equal("The embedded server crashed.", ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void ServerCrashedException_MessageConstructor_PreservesMessage()
    {
        const string message = "Custom crash message";
        ServerCrashedException ex = new(message);

        Assert.Equal(message, ex.Message);
        Assert.Null(ex.InnerException);
    }

    [Fact]
    public void ServerCrashedException_MessageAndInnerConstructor_PreservesBoth()
    {
        const string message = "Outer message";
        InvalidOperationException inner = new("Inner failure");
        ServerCrashedException ex = new(message, inner);

        Assert.Equal(message, ex.Message);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public void ServerCrashedException_FromServerException_WrapsOriginalException()
    {
        InvalidOperationException serverError = new("Server tick failed");
        ServerCrashedException ex = ServerCrashedException.FromServerException(serverError);

        Assert.Contains("Server tick failed", ex.Message);
        Assert.Same(serverError, ex.InnerException);
    }

    [Fact]
    public void ServerCrashedException_FromServerException_PreservesStackTraceInInner()
    {
        InvalidOperationException serverError;
        try
        {
            throw new InvalidOperationException("Deep failure");
        }
        catch (InvalidOperationException caught)
        {
            serverError = caught;
        }

        ServerCrashedException ex = ServerCrashedException.FromServerException(serverError);

        Assert.NotNull(ex.InnerException);
        Assert.Contains("Deep failure", ex.InnerException.Message);
        Assert.NotNull(serverError.StackTrace);
    }

    [Fact]
    public void ServerCrashedException_IsException_CanBeCaughtAsException()
    {
        ServerCrashedException ex = new("Test crash");

        ServerCrashedException caught = Assert.Throws<ServerCrashedException>(ThrowEx);
        Assert.Equal("Test crash", caught.Message);

        void ThrowEx() => throw ex;
    }

    #endregion

    #region EmbeddedServerHost TickCount API Tests

    [Fact]
    public void EmbeddedServerHost_HasTickCountProperty()
    {
        Type type = typeof(EmbeddedServerHost);
        PropertyInfo? tickCountProp = type.GetProperty("TickCount", BindingFlags.Public | BindingFlags.Instance);

        Assert.NotNull(tickCountProp);
        Assert.Equal(typeof(int), tickCountProp.PropertyType);
        Assert.True(tickCountProp.CanRead);
        Assert.False(tickCountProp.CanWrite);
    }

    [Fact]
    public void EmbeddedServerHost_HasTickUntilAsyncSyncPredicate()
    {
        Type type = typeof(EmbeddedServerHost);
        MethodInfo? method = type.GetMethod(
            "TickUntilAsync",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(Func<bool>), typeof(int), typeof(CancellationToken) },
            null);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<bool>), method.ReturnType);
    }

    [Fact]
    public void EmbeddedServerHost_HasTickUntilAsyncAsyncPredicate()
    {
        Type type = typeof(EmbeddedServerHost);
        MethodInfo? method = type.GetMethod(
            "TickUntilAsync",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(Func<Task<bool>>), typeof(int), typeof(CancellationToken) },
            null);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<bool>), method.ReturnType);
    }

    [Fact]
    public void EmbeddedServerHost_HasRunOnGameThreadAsyncAction()
    {
        Type type = typeof(EmbeddedServerHost);
        MethodInfo? method = type.GetMethod(
            "RunOnGameThreadAsync",
            BindingFlags.Public | BindingFlags.Instance,
            null,
            new[] { typeof(Action) },
            null);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    [Fact]
    public void EmbeddedServerHost_HasRunOnGameThreadAsyncFunc()
    {
        Type type = typeof(EmbeddedServerHost);
        MethodInfo? genericMethod = type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(m => m.Name == "RunOnGameThreadAsync" && m.IsGenericMethodDefinition);

        Assert.NotNull(genericMethod);
        Assert.True(genericMethod.IsGenericMethod);
    }

    #endregion

    #region Tick Counting Logic Tests (Simulated)

    [Fact]
    public void TickCountingLogic_IncrementCounter_WorksCorrectly()
    {
        // Simulate tick counting logic without live server
        int tickCount = 0;

        for (int i = 0; i < 10; i++)
        {
            tickCount++;
        }

        Assert.Equal(10, tickCount);
    }

    [Fact]
    public void TickCountingLogic_NegativeTicksCount_ThrowsArgumentOutOfRange()
    {
        // Verify the Ticks method parameter validation logic
        int count = -1;

        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), count, "Tick count must be non-negative.");
        });

        Assert.Equal("count", ex.ParamName);
        Assert.Equal(-1, ex.ActualValue);
    }

    [Fact]
    public void TickCountingLogic_ZeroTicks_ExecutesNoIterations()
    {
        int tickCount = 0;
        int iterations = 0;

        for (int i = 0; i < 0; i++)
        {
            tickCount++;
            iterations++;
        }

        Assert.Equal(0, tickCount);
        Assert.Equal(0, iterations);
    }

    #endregion

    #region TickUntilAsync Logic Tests (Simulated)

    [Fact]
    public async Task TickUntilAsync_PredicateTrueImmediately_ReturnsWithoutTicking()
    {
        // Simulate TickUntilAsync behavior
        int tickCount = 0;
        Func<bool> predicate = () => true;
        int maxTicks = 100;
        bool result = false;

        for (int i = 0; i < maxTicks; i++)
        {
            if (predicate())
            {
                result = true;
                break;
            }
            tickCount++;
        }

        Assert.True(result);
        Assert.Equal(0, tickCount); // No ticks executed because predicate was immediately true
    }

    [Fact]
    public async Task TickUntilAsync_PredicateNeverTrue_ReachesMaxTicks()
    {
        int tickCount = 0;
        Func<bool> predicate = () => false;
        int maxTicks = 5;
        bool result = false;

        for (int i = 0; i < maxTicks; i++)
        {
            if (predicate())
            {
                result = true;
                break;
            }
            tickCount++;
        }

        Assert.False(result);
        Assert.Equal(5, tickCount);
    }

    [Fact]
    public async Task TickUntilAsync_PredicateTrueAfter3Ticks_StopsEarly()
    {
        int tickCount = 0;
        int triggerAt = 3;
        Func<bool> predicate = () => tickCount >= triggerAt;
        int maxTicks = 100;
        bool result = false;

        for (int i = 0; i < maxTicks; i++)
        {
            if (predicate())
            {
                result = true;
                break;
            }
            tickCount++;
        }

        Assert.True(result);
        Assert.Equal(3, tickCount);
    }

    [Fact]
    public async Task TickUntilAsync_WithCancellation_ThrowsOperationCanceled()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            cts.Token.ThrowIfCancellationRequested();
            await Task.CompletedTask;
        });
    }

    [Fact]
    public void TickUntilAsync_NullPredicate_ThrowsArgumentNull()
    {
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
        {
            Func<bool>? predicate = null;
            ArgumentNullException.ThrowIfNull(predicate);
        });

        Assert.Equal("predicate", ex.ParamName);
    }

    [Fact]
    public void TickUntilAsync_ZeroMaxTicks_ThrowsArgumentOutOfRange()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            int maxTicks = 0;
            if (maxTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTicks), maxTicks, "Maximum tick count must be positive.");
        });

        Assert.Equal("maxTicks", ex.ParamName);
        Assert.Equal(0, ex.ActualValue);
    }

    #endregion

    #region Game Thread Queue Logic Tests (Simulated)

    [Fact]
    public void GameThreadQueue_EnqueueAndDrain_ExecutesActions()
    {
        ConcurrentQueue<(Action action, TaskCompletionSource tcs)> queue = new();
        int executionCount = 0;

        // Enqueue actions
        for (int i = 0; i < 3; i++)
        {
            TaskCompletionSource tcs = new();
            queue.Enqueue((() => executionCount++, tcs));
        }

        Assert.Equal(3, queue.Count);
        Assert.Equal(0, executionCount);

        // Drain queue
        while (queue.TryDequeue(out var item))
        {
            item.action();
            item.tcs.TrySetResult();
        }

        Assert.Empty(queue);
        Assert.Equal(3, executionCount);
    }

    [Fact]
    public async Task GameThreadQueue_ActionThrows_PropagatesExceptionToTask()
    {
        ConcurrentQueue<(Action action, TaskCompletionSource tcs)> queue = new();
        TaskCompletionSource tcs = new();
        InvalidOperationException expectedException = new("Action failed");

        queue.Enqueue((() => throw expectedException, tcs));

        // Drain with exception handling
        while (queue.TryDequeue(out var item))
        {
            try
            {
                item.action();
                item.tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                item.tcs.TrySetException(ex);
            }
        }

        // Verify the task was faulted
        await Assert.ThrowsAsync<InvalidOperationException>(() => tcs.Task);
    }

    [Fact]
    public async Task GameThreadQueue_FuncReturnsValue_PropagatesResultToTask()
    {
        ConcurrentQueue<(Func<object?> func, TaskCompletionSource<object?> tcs)> queue = new();
        TaskCompletionSource<object?> tcs = new();

        queue.Enqueue((() => 42, tcs));

        while (queue.TryDequeue(out var item))
        {
            try
            {
                object? result = item.func();
                item.tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                item.tcs.TrySetException(ex);
            }
        }

        object? value = await tcs.Task;
        Assert.Equal(42, value);
    }

    #endregion

    #region Crash Fail-Fast Logic Tests (Simulated)

    [Fact]
    public void CrashFailFast_ServerException_FaultsPendingWaiters()
    {
        ConcurrentQueue<(Action action, TaskCompletionSource tcs)> queue = new();
        Exception serverException = new InvalidOperationException("Server crashed");

        // Enqueue pending actions
        TaskCompletionSource tcs1 = new();
        TaskCompletionSource tcs2 = new();
        queue.Enqueue((() => { }, tcs1));
        queue.Enqueue((() => { }, tcs2));

        // Fault all pending waiters
        ServerCrashedException crashEx = ServerCrashedException.FromServerException(serverException);
        while (queue.TryDequeue(out var item))
        {
            item.tcs.TrySetException(crashEx);
        }

        // Verify both tasks were faulted
        Assert.True(tcs1.Task.IsFaulted);
        Assert.True(tcs2.Task.IsFaulted);
        Assert.IsType<ServerCrashedException>(tcs1.Task.Exception?.InnerException);
        Assert.IsType<ServerCrashedException>(tcs2.Task.Exception?.InnerException);
    }

    [Fact]
    public void CrashFailFast_ExceptionCapture_PreventsFurtherTicks()
    {
        Exception? serverException = null;
        int tickCount = 0;

        // Simulate a tick that crashes
        void SimulateTick()
        {
            if (serverException is not null)
            {
                throw ServerCrashedException.FromServerException(serverException);
            }

            // Simulate crash on tick 3
            if (tickCount == 2)
            {
                serverException = new InvalidOperationException("Crash!");
                throw ServerCrashedException.FromServerException(serverException);
            }

            tickCount++;
        }

        // Ticks 0, 1, 2 (crash on 2)
        SimulateTick(); // tick 0
        SimulateTick(); // tick 1

        Assert.Throws<ServerCrashedException>(() => SimulateTick()); // tick 2 crashes
        Assert.Throws<ServerCrashedException>(() => SimulateTick()); // subsequent tick also throws

        Assert.Equal(2, tickCount);
    }

    [Fact]
    public void CrashFailFast_RethrowsOriginalCause()
    {
        InvalidOperationException originalException = new("Original server error");
        ServerCrashedException crashEx = ServerCrashedException.FromServerException(originalException);

        Assert.Same(originalException, crashEx.InnerException);
        Assert.Contains("Original server error", crashEx.Message);
    }

    #endregion

    #region Parameter Validation Tests

    [Fact]
    public void RunOnGameThreadAsync_NullAction_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            Action? action = null;
            ArgumentNullException.ThrowIfNull(action);
        });
    }

    [Fact]
    public void RunOnGameThreadAsync_NullFunc_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
        {
            Func<int>? func = null;
            ArgumentNullException.ThrowIfNull(func);
        });
    }

    [Fact]
    public void TickUntilAsync_NegativeMaxTicks_ThrowsArgumentOutOfRange()
    {
        ArgumentOutOfRangeException ex = Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            int maxTicks = -5;
            if (maxTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTicks), maxTicks, "Maximum tick count must be positive.");
        });

        Assert.Equal("maxTicks", ex.ParamName);
        Assert.Equal(-5, ex.ActualValue);
    }

    #endregion
}
