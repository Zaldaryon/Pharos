using System;
using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>
/// Tests for client isolation modes, context capture, and manager behavior.
/// </summary>
public class IsolationModeTests
{
    [Fact]
    public void IsolationOverhead_Zero_HasZeroDuration()
    {
        var overhead = IsolationOverhead.Zero;

        Assert.Equal(TimeSpan.Zero, overhead.Duration);
    }

    [Fact]
    public void IsolationOverhead_Zero_HasSharedClientMode()
    {
        var overhead = IsolationOverhead.Zero;

        Assert.Equal(IsolationMode.SharedClient, overhead.Mode);
    }

    [Fact]
    public void ClientIsolationManager_DefaultMode_IsShared()
    {
        var manager = new ClientIsolationManager(IsolationMode.SharedClient);

        Assert.Equal(IsolationMode.SharedClient, manager.Mode);
    }

    [Fact]
    public void ClientIsolationManager_PrepareForTest_SharedMode_NoOp()
    {
        var manager = new ClientIsolationManager(IsolationMode.SharedClient);

        var ex = Record.Exception(() => manager.PrepareForTest(null));

        Assert.Null(ex);
        Assert.Null(manager.ActiveClient);
    }

    [Fact]
    public void ClientIsolationManager_MeasureOverhead_ReturnsTimedResult()
    {
        var manager = new ClientIsolationManager(IsolationMode.SharedClient);
        bool executed = false;

        var overhead = manager.MeasureOverhead(() => executed = true);

        Assert.True(executed);
        Assert.True(overhead.Duration >= TimeSpan.Zero);
        Assert.Equal(IsolationMode.SharedClient, overhead.Mode);
    }

    [Fact]
    public void ClientIsolationManager_CaptureState_WithNullClient_ReturnsNull()
    {
        var manager = new ClientIsolationManager(IsolationMode.RollbackState);

        var context = manager.CaptureState(null!);

        Assert.Null(context);
    }

    [Fact]
    public void IsolationMode_FreshClient_HasHighestValue()
    {
        Assert.True((int)IsolationMode.FreshClient > (int)IsolationMode.SharedClient);
        Assert.True((int)IsolationMode.FreshClient > (int)IsolationMode.RollbackState);
    }

    [Fact]
    public void ClientScenarioBase_GetIsolationManager_ReturnsSameInstance()
    {
        var scenario = new TestableScenario();

        var first = scenario.GetIsolationManagerExposed();
        var second = scenario.GetIsolationManagerExposed();

        Assert.Same(first, second);
    }

    [Fact]
    public void ClientIsolationManager_FreshClientMode_SetsRequiresFreshClient()
    {
        var manager = new ClientIsolationManager(IsolationMode.FreshClient);
        Assert.False(manager.RequiresFreshClient);

        manager.PrepareForTest(null);

        Assert.True(manager.RequiresFreshClient);
    }

    [Fact]
    public void ClientIsolationManager_ClearFreshClientRequirement_ResetsFlag()
    {
        var manager = new ClientIsolationManager(IsolationMode.FreshClient);
        manager.PrepareForTest(null);
        Assert.True(manager.RequiresFreshClient);

        manager.ClearFreshClientRequirement();

        Assert.False(manager.RequiresFreshClient);
    }

    [Fact]
    public void IsolationContext_Capture_WithNullPlayer_ReturnsNull()
    {
        var context = IsolationContext.Capture(null, null);

        Assert.Null(context);
    }

    [Fact]
    public void ClientIsolationManager_RollbackMode_StoresMode()
    {
        var manager = new ClientIsolationManager(IsolationMode.RollbackState);

        Assert.Equal(IsolationMode.RollbackState, manager.Mode);
    }

    /// <summary>
    /// Testable scenario subclass that exposes the protected GetIsolationManager method.
    /// </summary>
    private sealed class TestableScenario : ClientScenarioBase
    {
        public ClientIsolationManager GetIsolationManagerExposed() => GetIsolationManager();
    }
}
