using System.Collections.Concurrent;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace Zaldaryon.Pharos.XUnit.Execution;

/// <summary>
/// Runs one scenario test in-process through the scenario pipeline, outside a test runner: the
/// smoke command's way to get the watchdog, the boot check and the failure artifacts.
/// </summary>
internal static class ScenarioRunner
{
    /// <summary>What a run reported.</summary>
    /// <param name="Passed">How many tests passed.</param>
    /// <param name="Failures">The failure messages of the tests that failed.</param>
    /// <param name="Skipped">How many tests were skipped.</param>
    public sealed record Result(int Passed, IReadOnlyList<string> Failures, int Skipped)
    {
        /// <summary>Whether every test that ran passed, and at least one ran.</summary>
        public bool Succeeded => Failures.Count == 0 && Passed > 0;
    }

    /// <summary>
    /// Discovers <paramref name="methodName"/> on <paramref name="testClass"/> through the discoverer
    /// its attribute names, and runs it with <paramref name="constructorArguments"/>.
    /// </summary>
    public static async Task<Result> RunAsync(Type testClass, string methodName, params object[] constructorArguments)
    {
        MethodInfo method = testClass.GetMethod(methodName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new ArgumentException($"{testClass.Name} has no method {methodName}.", nameof(methodName));

        ITestMethod testMethod = new TestMethod(
            new TestClass(new TestCollection(new TestAssembly(Reflector.Wrap(testClass.Assembly)), null, "pharos"), Reflector.Wrap(testClass)),
            Reflector.Wrap(method));

        IAttributeInfo fact = testMethod.Method.GetCustomAttributes(typeof(FactAttribute)).Single();
        IList<object> discovererName = fact.GetCustomAttributes(typeof(XunitTestCaseDiscovererAttribute)).Single().GetConstructorArguments().ToList();
        Type discovererType = Type.GetType($"{discovererName[0]}, {discovererName[1]}", throwOnError: true)!;
        IXunitTestCaseDiscoverer discoverer = (IXunitTestCaseDiscoverer)Activator.CreateInstance(discovererType, new NullMessageSink())!;

        CollectingBus bus = new();
        foreach (IXunitTestCase testCase in discoverer.Discover(new DiscoveryOptions(), testMethod, fact))
        {
            await testCase.RunAsync(new NullMessageSink(), bus, constructorArguments, new ExceptionAggregator(), new CancellationTokenSource()).ConfigureAwait(false);
        }

        IMessageSinkMessage[] messages = [.. bus.Messages];
        return new Result(
            messages.OfType<ITestPassed>().Count(),
            [.. messages.OfType<ITestFailed>().Select(f => string.Join(Environment.NewLine, f.Messages))],
            messages.OfType<ITestSkipped>().Count());
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
