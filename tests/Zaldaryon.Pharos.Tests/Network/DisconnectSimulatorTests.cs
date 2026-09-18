using Xunit;
using Zaldaryon.Pharos.Network;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>
/// Pure-logic tests for DisconnectSimulator, DisconnectEvent, CrashContainmentScope, and related types.
/// These tests are headless-safe and do not require a live server.
/// </summary>
public sealed class DisconnectSimulatorTests
{
    // -------------------------------------------------------------------------
    // DisconnectReason enum tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DisconnectReason_HasExpectedValues()
    {
        Assert.Equal(0, (int)DisconnectReason.Timeout);
        Assert.Equal(1, (int)DisconnectReason.ServerShutdown);
        Assert.Equal(2, (int)DisconnectReason.Kicked);
        Assert.Equal(3, (int)DisconnectReason.NetworkError);
        Assert.Equal(4, (int)DisconnectReason.SimulatedCrash);
    }

    // -------------------------------------------------------------------------
    // DisconnectEvent record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DisconnectEvent_SimulatedCrash_IsSimulatedReturnsTrue()
    {
        var evt = new DisconnectEvent(DisconnectReason.SimulatedCrash, 100);

        Assert.True(evt.IsSimulated);
    }

    [Fact]
    public void DisconnectEvent_NonSimulated_IsSimulatedReturnsFalse()
    {
        var evt = new DisconnectEvent(DisconnectReason.Timeout, 100);

        Assert.False(evt.IsSimulated);
    }

    [Fact]
    public void DisconnectEvent_Timeout_ShouldAttemptReconnectReturnsTrue()
    {
        var evt = new DisconnectEvent(DisconnectReason.Timeout, 100);

        Assert.True(evt.ShouldAttemptReconnect);
    }

    [Fact]
    public void DisconnectEvent_Kicked_ShouldAttemptReconnectReturnsFalse()
    {
        var evt = new DisconnectEvent(DisconnectReason.Kicked, 100);

        Assert.False(evt.ShouldAttemptReconnect);
    }

    [Fact]
    public void DisconnectEvent_ServerShutdown_ShouldAttemptReconnectReturnsFalse()
    {
        var evt = new DisconnectEvent(DisconnectReason.ServerShutdown, 100);

        Assert.False(evt.ShouldAttemptReconnect);
    }

    [Fact]
    public void DisconnectEvent_WithReconnectAttempt_IncrementsCount()
    {
        var evt = new DisconnectEvent(DisconnectReason.NetworkError, 100, ReconnectAttempts: 2);

        var updated = evt.WithReconnectAttempt();

        Assert.Equal(3, updated.ReconnectAttempts);
        Assert.Equal(2, evt.ReconnectAttempts); // Original unchanged
    }

    [Fact]
    public void DisconnectEvent_Message_IsPreserved()
    {
        var evt = new DisconnectEvent(DisconnectReason.Kicked, 50, Message: "Bad behavior");

        Assert.Equal("Bad behavior", evt.Message);
    }

    // -------------------------------------------------------------------------
    // CrashContainmentResult record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CrashContainmentResult_Success_HasCorrectProperties()
    {
        var result = CrashContainmentResult.Success;

        Assert.False(result.Crashed);
        Assert.True(result.Succeeded);
        Assert.Null(result.ExceptionType);
        Assert.Null(result.ExceptionMessage);
        Assert.Null(result.StackTrace);
    }

    [Fact]
    public void CrashContainmentResult_FromException_CapturesDetails()
    {
        var exception = new InvalidOperationException("Test error");

        var result = CrashContainmentResult.FromException(exception);

        Assert.True(result.Crashed);
        Assert.False(result.Succeeded);
        Assert.Equal("System.InvalidOperationException", result.ExceptionType);
        Assert.Equal("Test error", result.ExceptionMessage);
    }

    [Fact]
    public void CrashContainmentResult_GetCrashSummary_ReturnsFormattedString()
    {
        var exception = new ArgumentNullException("param");
        var result = CrashContainmentResult.FromException(exception);

        var summary = result.GetCrashSummary();

        Assert.Contains("System.ArgumentNullException", summary);
    }

    [Fact]
    public void CrashContainmentResult_GetCrashSummary_ReturnsNullWhenNotCrashed()
    {
        var result = CrashContainmentResult.Success;

        Assert.Null(result.GetCrashSummary());
    }

    // -------------------------------------------------------------------------
    // CrashContainmentScope tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CrashContainmentScope_RunWithContainment_SuccessfulAction_ReturnsSuccess()
    {
        var scope = new CrashContainmentScope();

        var result = scope.RunWithContainment(() => { /* success */ });

        Assert.False(result.Crashed);
        Assert.Equal(1, scope.SuccessCount);
        Assert.Equal(0, scope.CrashCount);
    }

    [Fact]
    public void CrashContainmentScope_RunWithContainment_ThrowingAction_ReturnsCrash()
    {
        var scope = new CrashContainmentScope();

        var result = scope.RunWithContainment(() => throw new InvalidOperationException("Boom"));

        Assert.True(result.Crashed);
        Assert.Equal("System.InvalidOperationException", result.ExceptionType);
        Assert.Equal(0, scope.SuccessCount);
        Assert.Equal(1, scope.CrashCount);
    }

    [Fact]
    public void CrashContainmentScope_RunWithContainment_DoesNotPropagateException()
    {
        var scope = new CrashContainmentScope();

        // Should not throw
        var result = scope.RunWithContainment(() => throw new Exception("Test"));

        Assert.True(result.Crashed);
    }

    [Fact]
    public void CrashContainmentScope_History_TracksAllExecutions()
    {
        var scope = new CrashContainmentScope();

        scope.RunWithContainment(() => { });
        scope.RunWithContainment(() => throw new Exception("Fail"));
        scope.RunWithContainment(() => { });

        var history = scope.History;

        Assert.Equal(3, history.Count);
        Assert.False(history[0].Crashed);
        Assert.True(history[1].Crashed);
        Assert.False(history[2].Crashed);
    }

    [Fact]
    public void CrashContainmentScope_LastResult_ReturnsLatest()
    {
        var scope = new CrashContainmentScope();

        scope.RunWithContainment(() => { });
        scope.RunWithContainment(() => throw new Exception("Last"));

        var last = scope.LastResult;

        Assert.NotNull(last);
        Assert.True(last.Crashed);
    }

    [Fact]
    public void CrashContainmentScope_RunIterations_ExecutesMultipleTimes()
    {
        var scope = new CrashContainmentScope();
        int counter = 0;

        var results = scope.RunIterations(() => counter++, 5);

        Assert.Equal(5, results.Count);
        Assert.Equal(5, counter);
        Assert.All(results, r => Assert.False(r.Crashed));
    }

    [Fact]
    public void CrashContainmentScope_RunIterations_StopsOnFirstCrash()
    {
        var scope = new CrashContainmentScope();
        int counter = 0;

        var results = scope.RunIterations(() =>
        {
            counter++;
            if (counter == 3) throw new Exception("Third iteration crash");
        }, 10, stopOnFirstCrash: true);

        Assert.Equal(3, results.Count);
        Assert.Equal(3, counter);
        Assert.False(results[0].Crashed);
        Assert.False(results[1].Crashed);
        Assert.True(results[2].Crashed);
    }

    [Fact]
    public void CrashContainmentScope_RunIterations_ContinuesThroughCrashes()
    {
        var scope = new CrashContainmentScope();
        int counter = 0;

        var results = scope.RunIterations(() =>
        {
            counter++;
            if (counter % 2 == 0) throw new Exception("Even iteration crash");
        }, 5, stopOnFirstCrash: false);

        Assert.Equal(5, results.Count);
        Assert.Equal(5, counter);
    }

    [Fact]
    public void CrashContainmentScope_Reset_ClearsHistory()
    {
        var scope = new CrashContainmentScope();
        scope.RunWithContainment(() => { });
        scope.RunWithContainment(() => throw new Exception("Fail"));

        scope.Reset();

        Assert.Equal(0, scope.CrashCount);
        Assert.Equal(0, scope.SuccessCount);
        Assert.Empty(scope.History);
    }

    [Fact]
    public void CrashContainmentScope_GetStatistics_ReturnsCorrectValues()
    {
        var scope = new CrashContainmentScope();
        scope.RunWithContainment(() => { });
        scope.RunWithContainment(() => { });
        scope.RunWithContainment(() => throw new Exception("Fail"));

        var stats = scope.GetStatistics();

        Assert.Equal(3, stats.TotalExecutions);
        Assert.Equal(1, stats.CrashCount);
        Assert.Equal(2, stats.SuccessCount);
        Assert.InRange(stats.CrashRate, 0.33f, 0.34f);
    }

    [Fact]
    public void CrashContainmentScope_RunWithContainment_FuncReturnsValue()
    {
        var scope = new CrashContainmentScope();

        var (result, value) = scope.RunWithContainment(() => 42);

        Assert.False(result.Crashed);
        Assert.Equal(42, value);
    }

    [Fact]
    public void CrashContainmentScope_RunWithContainment_FuncCrashReturnsDefault()
    {
        var scope = new CrashContainmentScope();

        var (result, value) = scope.RunWithContainment<int>(() => throw new Exception("Boom"));

        Assert.True(result.Crashed);
        Assert.Equal(0, value); // default(int)
    }

    // -------------------------------------------------------------------------
    // DisconnectSimulator state tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DisconnectSimulator_InitialState_IsConnected()
    {
        var simulator = new DisconnectSimulator();

        Assert.True(simulator.IsConnected);
        Assert.False(simulator.IsDisconnected);
        Assert.Null(simulator.LastDisconnectReason);
        Assert.Equal(0, simulator.DisconnectCount);
    }

    [Fact]
    public void DisconnectSimulator_SimulateDisconnect_ChangesStateToDisconnected()
    {
        var simulator = new DisconnectSimulator();

        simulator.SimulateDisconnect(DisconnectReason.Timeout);

        Assert.False(simulator.IsConnected);
        Assert.True(simulator.IsDisconnected);
        Assert.Equal(DisconnectReason.Timeout, simulator.LastDisconnectReason);
        Assert.Equal(1, simulator.DisconnectCount);
    }

    [Fact]
    public void DisconnectSimulator_SimulateServerCrash_RecordsSimulatedCrashReason()
    {
        var simulator = new DisconnectSimulator();

        var evt = simulator.SimulateServerCrash();

        Assert.Equal(DisconnectReason.SimulatedCrash, evt.Reason);
        Assert.Contains("Simulated server crash", evt.Message);
    }

    [Fact]
    public void DisconnectSimulator_SimulateTimeout_RecordsTimeoutReason()
    {
        var simulator = new DisconnectSimulator();

        var evt = simulator.SimulateTimeout(5000);

        Assert.Equal(DisconnectReason.Timeout, evt.Reason);
        Assert.Contains("5000ms", evt.Message);
    }

    [Fact]
    public void DisconnectSimulator_SimulateKick_RecordsKickedReason()
    {
        var simulator = new DisconnectSimulator();

        var evt = simulator.SimulateKick("Cheating detected");

        Assert.Equal(DisconnectReason.Kicked, evt.Reason);
        Assert.Equal("Cheating detected", evt.Message);
    }

    [Fact]
    public void DisconnectSimulator_SimulateServerShutdown_RecordsShutdownReason()
    {
        var simulator = new DisconnectSimulator();

        var evt = simulator.SimulateServerShutdown();

        Assert.Equal(DisconnectReason.ServerShutdown, evt.Reason);
    }

    [Fact]
    public void DisconnectSimulator_SimulateNetworkError_RecordsNetworkErrorReason()
    {
        var simulator = new DisconnectSimulator();

        var evt = simulator.SimulateNetworkError("Connection reset by peer");

        Assert.Equal(DisconnectReason.NetworkError, evt.Reason);
        Assert.Contains("reset by peer", evt.Message);
    }

    // -------------------------------------------------------------------------
    // DisconnectSimulator reconnect tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DisconnectSimulator_AttemptReconnect_IncrementsCounter()
    {
        var simulator = new DisconnectSimulator();
        simulator.SimulateDisconnect(DisconnectReason.Timeout);

        simulator.AttemptReconnect();
        simulator.AttemptReconnect();

        Assert.Equal(2, simulator.ReconnectAttempts);
    }

    [Fact]
    public void DisconnectSimulator_AttemptReconnect_UpdatesHistoryEvent()
    {
        var simulator = new DisconnectSimulator();
        simulator.SimulateDisconnect(DisconnectReason.NetworkError);

        simulator.AttemptReconnect();
        simulator.AttemptReconnect();

        var history = simulator.GetDisconnectHistory();
        Assert.Single(history);
        Assert.Equal(2, history[0].ReconnectAttempts);
    }

    [Fact]
    public void DisconnectSimulator_AttemptReconnect_ExhaustedReturnsFalse()
    {
        var simulator = new DisconnectSimulator();
        simulator.MaxReconnectAttempts = 3;
        simulator.SimulateDisconnect(DisconnectReason.Timeout);

        Assert.True(simulator.AttemptReconnect());
        Assert.True(simulator.AttemptReconnect());
        Assert.False(simulator.AttemptReconnect()); // 3rd attempt exhausts
        Assert.True(simulator.ReconnectAttemptsExhausted);
    }

    [Fact]
    public void DisconnectSimulator_AttemptReconnect_WhenConnected_ReturnsTrue()
    {
        var simulator = new DisconnectSimulator();

        var result = simulator.AttemptReconnect();

        Assert.True(result);
        Assert.Equal(0, simulator.ReconnectAttempts);
    }

    [Fact]
    public void DisconnectSimulator_SimulateReconnectSuccess_RestoresConnectedState()
    {
        var simulator = new DisconnectSimulator();
        simulator.SimulateDisconnect(DisconnectReason.NetworkError);
        simulator.AttemptReconnect();

        simulator.SimulateReconnectSuccess();

        Assert.True(simulator.IsConnected);
        Assert.Null(simulator.LastDisconnectReason);
        Assert.Equal(1, simulator.ReconnectAttempts); // Preserved for diagnostics
    }

    [Fact]
    public void DisconnectSimulator_MaxReconnectAttempts_CanBeConfigured()
    {
        var simulator = new DisconnectSimulator();

        simulator.MaxReconnectAttempts = 10;

        Assert.Equal(10, simulator.MaxReconnectAttempts);
    }

    [Fact]
    public void DisconnectSimulator_MaxReconnectAttempts_NegativeThrows()
    {
        var simulator = new DisconnectSimulator();

        Assert.Throws<ArgumentOutOfRangeException>(() => simulator.MaxReconnectAttempts = -1);
    }

    // -------------------------------------------------------------------------
    // DisconnectSimulator history tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DisconnectSimulator_GetDisconnectHistory_ReturnsAllEvents()
    {
        var simulator = new DisconnectSimulator();

        simulator.SimulateTimeout();
        simulator.SimulateReconnectSuccess();
        simulator.SimulateServerCrash();
        simulator.SimulateReconnectSuccess();
        simulator.SimulateKick();

        var history = simulator.GetDisconnectHistory();

        Assert.Equal(3, history.Count);
        Assert.Equal(DisconnectReason.Timeout, history[0].Reason);
        Assert.Equal(DisconnectReason.SimulatedCrash, history[1].Reason);
        Assert.Equal(DisconnectReason.Kicked, history[2].Reason);
    }

    [Fact]
    public void DisconnectSimulator_Events_HaveIncreasingTimestamps()
    {
        var simulator = new DisconnectSimulator();

        simulator.SimulateDisconnect(DisconnectReason.Timeout);
        Thread.Sleep(10);
        simulator.SimulateDisconnect(DisconnectReason.NetworkError);

        var history = simulator.GetDisconnectHistory();

        Assert.True(history[1].TimestampMs >= history[0].TimestampMs);
    }

    // -------------------------------------------------------------------------
    // DisconnectSimulator crash containment tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DisconnectSimulator_RunTickWithContainment_ContainsException()
    {
        var simulator = new DisconnectSimulator();

        var result = simulator.RunTickWithContainment(() => throw new InvalidOperationException("Tick crash"));

        Assert.True(result.Crashed);
        Assert.Contains("InvalidOperationException", result.ExceptionType);
    }

    [Fact]
    public void DisconnectSimulator_RunTicksWithContainment_ExecutesMultipleTicks()
    {
        var simulator = new DisconnectSimulator();
        int tickCount = 0;

        var results = simulator.RunTicksWithContainment(() => tickCount++, 10);

        Assert.Equal(10, results.Count);
        Assert.Equal(10, tickCount);
    }

    [Fact]
    public void DisconnectSimulator_CrashScope_ExposedForDirectAccess()
    {
        var simulator = new DisconnectSimulator();

        Assert.NotNull(simulator.CrashScope);
        Assert.IsType<CrashContainmentScope>(simulator.CrashScope);
    }

    // -------------------------------------------------------------------------
    // DisconnectSimulator statistics tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DisconnectSimulator_GetStatistics_CountsByReason()
    {
        var simulator = new DisconnectSimulator();

        simulator.SimulateTimeout();
        simulator.SimulateReconnectSuccess();
        simulator.SimulateTimeout();
        simulator.SimulateReconnectSuccess();
        simulator.SimulateKick();
        simulator.SimulateNetworkError();
        simulator.SimulateServerShutdown();
        simulator.SimulateServerCrash();

        var stats = simulator.GetStatistics();

        Assert.Equal(6, stats.TotalDisconnects);
        Assert.Equal(2, stats.TimeoutCount);
        Assert.Equal(1, stats.KickCount);
        Assert.Equal(1, stats.NetworkErrorCount);
        Assert.Equal(1, stats.ServerShutdownCount);
        Assert.Equal(1, stats.SimulatedCrashCount);
    }

    [Fact]
    public void DisconnectSimulator_GetStatistics_IncludesReconnectAttempts()
    {
        var simulator = new DisconnectSimulator();
        simulator.MaxReconnectAttempts = 10;

        simulator.SimulateTimeout();
        simulator.AttemptReconnect();
        simulator.AttemptReconnect();
        simulator.SimulateReconnectSuccess();

        simulator.SimulateNetworkError();
        simulator.AttemptReconnect();
        simulator.SimulateReconnectSuccess();

        var stats = simulator.GetStatistics();

        Assert.Equal(3, stats.TotalReconnectAttempts);
    }

    [Fact]
    public void DisconnectSimulator_GetStatistics_IncludesCrashStatistics()
    {
        var simulator = new DisconnectSimulator();

        simulator.RunTickWithContainment(() => { });
        simulator.RunTickWithContainment(() => throw new Exception("Crash"));

        var stats = simulator.GetStatistics();

        Assert.Equal(2, stats.CrashStatistics.TotalExecutions);
        Assert.Equal(1, stats.CrashStatistics.CrashCount);
    }

    // -------------------------------------------------------------------------
    // DisconnectSimulator reset tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DisconnectSimulator_Reset_ClearsAllState()
    {
        var simulator = new DisconnectSimulator();
        simulator.SimulateDisconnect(DisconnectReason.Timeout);
        simulator.AttemptReconnect();
        simulator.RunTickWithContainment(() => throw new Exception("Crash"));

        simulator.Reset();

        Assert.True(simulator.IsConnected);
        Assert.Null(simulator.LastDisconnectReason);
        Assert.Equal(0, simulator.ReconnectAttempts);
        Assert.Equal(0, simulator.DisconnectCount);
        Assert.Equal(0, simulator.CrashScope.CrashCount);
    }
}
