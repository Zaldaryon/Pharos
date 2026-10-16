using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>The running game instances and test-scoped cleanup for one scenario.</summary>
public sealed class ScenarioContext
{
    private readonly List<Func<CancellationToken, Task>> _cleanups = [];
    private bool _disposed;
    private Exception? _cleanupFailure;

    internal ScenarioContext(EmbeddedServerHost? server, HeadlessClient? client, ClientServerLoopbackSession? session)
    {
        Server = server;
        Client = client;
        Session = session;
    }

    /// <summary>The embedded server, when this scenario has one.</summary>
    public EmbeddedServerHost? Server { get; }

    /// <summary>The headless client, when this scenario has one.</summary>
    public HeadlessClient? Client { get; }

    /// <summary>The connected client-server session, when this scenario has one.</summary>
    public ClientServerLoopbackSession? Session { get; }

    /// <summary>Registers test-scoped cleanup. Cleanups run once in reverse registration order, including when ApplyAsync fails.</summary>
    public void OnCleanup(Func<CancellationToken, Task> cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cleanups.Add(cleanup);
    }

    /// <summary>Registers a short synchronous test-scoped cleanup. It runs on the game-owning thread and cannot be preempted if it blocks.</summary>
    public void OnCleanup(Action cleanup)
    {
        ArgumentNullException.ThrowIfNull(cleanup);
        OnCleanup(_ => { cleanup(); return Task.CompletedTask; });
    }

    internal Task DisposeAsync() => DisposeAsync(CancellationToken.None);

    internal Task DisposeAsync(CancellationToken cancellationToken) => DisposeAsyncCore(cancellationToken);

    internal async Task DisposeAsync(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(timeout));
        using CancellationTokenSource cancellation = new();
        // Fallback disposal should be bounded for async cleanup. Fixture-thread cleanup is
        // normally drained by the scenario invoker with its owner-thread pump.
        Task cleanup = DisposeAsyncCore(cancellation.Token);
        try
        {
            await cleanup.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            cancellation.Cancel();
            _ = cleanup.ContinueWith(
                static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }
    }

    private async Task DisposeAsyncCore(CancellationToken cancellationToken)
    {
        if (_disposed)
        {
            if (_cleanupFailure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_cleanupFailure).Throw();
            return;
        }
        _disposed = true;
        List<Exception> failures = [];
        for (int i = _cleanups.Count - 1; i >= 0; i--)
        {
            try { await _cleanups[i](cancellationToken); }
            catch (Exception ex) { failures.Add(ex); }
        }
        _cleanups.Clear();
        if (failures.Count == 1) _cleanupFailure = failures[0];
        else if (failures.Count > 1) _cleanupFailure = new AggregateException("Scenario environment cleanup failed.", failures);
        if (_cleanupFailure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(_cleanupFailure).Throw();
    }
}

/// <summary>Applies reusable test state after the world baseline is ready and before the scenario body.</summary>
public interface ITestEnvironment
{
    /// <summary>Applies this environment for one scenario test.</summary>
    Task ApplyAsync(ScenarioContext context, CancellationToken cancellationToken);
}

/// <summary>Applies an <see cref="ITestEnvironment"/> to each scenario test in a class or method.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class TestEnvironmentAttribute : Attribute
{
    /// <summary>Creates an environment attribute.</summary>
    public TestEnvironmentAttribute(Type environmentType)
    {
        ArgumentNullException.ThrowIfNull(environmentType);
        if (!typeof(ITestEnvironment).IsAssignableFrom(environmentType) || environmentType.IsAbstract || environmentType.GetConstructor(Type.EmptyTypes) is null)
            throw new ArgumentException($"{environmentType.FullName} must be a concrete ITestEnvironment with a public parameterless constructor.", nameof(environmentType));
        EnvironmentType = environmentType;
    }

    /// <summary>The environment type created for each test.</summary>
    public Type EnvironmentType { get; }
}

internal static class ScenarioEnvironments
{
    public static async Task ApplyAsync(Type testClass, System.Reflection.MethodInfo? method, ScenarioContext context, CancellationToken cancellationToken)
    {
        Stack<Type> hierarchy = new();
        for (Type? type = testClass; type != null && type != typeof(object); type = type.BaseType) hierarchy.Push(type);
        foreach (Type type in hierarchy)
        foreach (TestEnvironmentAttribute attribute in type.GetCustomAttributes(typeof(TestEnvironmentAttribute), inherit: false).Cast<TestEnvironmentAttribute>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ITestEnvironment environment = (ITestEnvironment)Activator.CreateInstance(attribute.EnvironmentType)!;
            await environment.ApplyAsync(context, cancellationToken);
        }

        if (method != null)
        foreach (TestEnvironmentAttribute attribute in method.GetCustomAttributes(typeof(TestEnvironmentAttribute), inherit: false).Cast<TestEnvironmentAttribute>())
        {
            cancellationToken.ThrowIfCancellationRequested();
            ITestEnvironment environment = (ITestEnvironment)Activator.CreateInstance(attribute.EnvironmentType)!;
            await environment.ApplyAsync(context, cancellationToken);
        }
    }
}
