namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Marks a test method as a client theory (Theory-based, data-driven).
/// The test class should inherit from <see cref="ClientScenarioBase"/> for lifecycle management.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ClientTheoryAttribute : Xunit.TheoryAttribute;
