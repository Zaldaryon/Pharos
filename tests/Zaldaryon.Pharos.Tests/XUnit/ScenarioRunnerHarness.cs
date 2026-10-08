using System.Collections.Concurrent;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>
/// Discovers and runs one test method in-process, through whatever discoverer its attribute
/// names, and collects the messages xUnit reports for it.
/// </summary>
internal static class ScenarioRunnerHarness
{
    public static IReadOnlyList<IXunitTestCase> Discover(Type testClass, string methodName)
    {
        MethodInfo method = testClass.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new ArgumentException($"{testClass.Name} has no method {methodName}");

        ITestMethod testMethod = new TestMethod(
            new TestClass(new TestCollection(new TestAssembly(Reflector.Wrap(testClass.Assembly)), null, "pharos-harness"), Reflector.Wrap(testClass)),
            Reflector.Wrap(method));

        IAttributeInfo fact = testMethod.Method.GetCustomAttributes(typeof(FactAttribute)).Single();
        IAttributeInfo discovererAttribute = fact.GetCustomAttributes(typeof(XunitTestCaseDiscovererAttribute)).Single();
        IList<object> args = discovererAttribute.GetConstructorArguments().ToList();
        Type discovererType = Type.GetType($"{args[0]}, {args[1]}", throwOnError: true)!;
        IXunitTestCaseDiscoverer discoverer = (IXunitTestCaseDiscoverer)Activator.CreateInstance(discovererType, new NullMessageSink())!;

        return discoverer.Discover(new DiscoveryOptions(), testMethod, fact).ToList();
    }

    public static async Task<RunResult> RunAsync(Type testClass, string methodName)
    {
        CollectingBus bus = new();
        foreach (IXunitTestCase testCase in Discover(testClass, methodName))
        {
            await testCase.RunAsync(new NullMessageSink(), bus, [], new ExceptionAggregator(), new CancellationTokenSource());
        }

        return new RunResult([.. bus.Messages]);
    }

    public sealed record RunResult(IReadOnlyList<IMessageSinkMessage> Messages)
    {
        public IReadOnlyList<ITestFailed> Failed => Messages.OfType<ITestFailed>().ToList();

        public IReadOnlyList<ITestPassed> Passed => Messages.OfType<ITestPassed>().ToList();

        public ITestFailed SingleFailure => Assert.Single(Failed);

        public string FailureMessage => string.Join(Environment.NewLine, SingleFailure.Messages);

        public string FailureType => SingleFailure.ExceptionTypes[0];
    }

    private sealed class DiscoveryOptions : ITestFrameworkDiscoveryOptions
    {
        private readonly Dictionary<string, object?> _values = [];

        public TValue GetValue<TValue>(string name) => _values.TryGetValue(name, out object? value) && value is TValue typed ? typed : default!;

        public void SetValue<TValue>(string name, TValue value) => _values[name] = value;
    }

    private sealed class CollectingBus : IMessageBus
    {
        public ConcurrentQueue<IMessageSinkMessage> Messages { get; } = new();

        public bool QueueMessage(IMessageSinkMessage message)
        {
            Messages.Enqueue(message);
            return true;
        }

        public void Dispose()
        {
        }
    }
}
