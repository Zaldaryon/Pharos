namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Marks a test method as a client scenario (Fact-based).
/// The test class should inherit from <see cref="ClientScenarioBase"/> for lifecycle management.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ClientScenarioAttribute : Xunit.FactAttribute;
