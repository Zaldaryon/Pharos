using System.Reflection;
using Xunit.Sdk;
using Zaldaryon.Pharos.Content;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Supplies one <see cref="ContentCode"/> theory row for each block, item and entity code found
/// in a mod's content assets. Pair it with <see cref="ServerTheoryAttribute"/>.
/// </summary>
/// <remarks>
/// <para>
/// Put this attribute on a method in a <see cref="ServerScenarioBase"/> class. The mod should be
/// staged with <see cref="ServerModsAttribute"/>. Relative mod paths are resolved beside the test
/// assembly. The attribute also scans the assembly's <c>TestMods</c> and <c>Mods</c> directories
/// when no server mod path is declared.
/// </para>
/// <para>
/// The theory rows come from <c>blocktypes</c>, <c>itemtypes</c> and <c>entities</c> JSON assets.
/// If the mod registers content only from code, call the domain overload of
/// <see cref="ContentSweep.RunAsync(Zaldaryon.Pharos.Server.EmbeddedServerHost,string,ContentSweepOptions?,CancellationToken)"/>
/// from a server scenario.
/// </para>
/// <code>
/// [ServerWorld(Preset = WorldPreset.SurvivalFlat)]
/// [ServerMods("TestMods/mymod")]
/// public class MyModContent : ServerScenarioBase
/// {
///     [ServerTheory]
///     [ContentSweep("mymod", Steps = SweepSteps.All, SkipCodes = ["mymod:debugblock-*"])]
///     public Task Sweep(ContentCode code) => ContentSweep.RunAsync(Host!, code);
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ContentSweepAttribute(string domain) : DataAttribute
{
    /// <summary>The mod domain whose assets supply the theory rows.</summary>
    public string Domain { get; } = string.IsNullOrWhiteSpace(domain)
        ? throw new ArgumentException("A mod domain is required.", nameof(domain))
        : domain;

    /// <summary>The content steps to run for each code.</summary>
    public SweepSteps Steps { get; set; } = SweepSteps.All;

    /// <summary>Code globs to omit from the theory rows. The name differs from xUnit's reserved <c>Skip</c> property.</summary>
    public string[] SkipCodes { get; set; } = [];

    /// <summary>
    /// Variant selectors to include, written as <c>key=value</c> pairs. An empty array includes
    /// every variant.
    /// </summary>
    public string[] Variants { get; set; } = [];

    /// <inheritdoc />
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        Type testClass = testMethod.DeclaringType
            ?? throw new InvalidOperationException($"[ContentSweep] method {testMethod.Name} has no declaring test class.");
        ParameterInfo[] parameters = testMethod.GetParameters();
        if (parameters.Length != 1 || parameters[0].ParameterType != typeof(ContentCode))
            throw new InvalidOperationException($"[ContentSweep] method {testClass.FullName}.{testMethod.Name} must have one ContentCode parameter.");

        return ContentSweepCatalog.DiscoverAssetCodes(testClass, Domain, Steps, SkipCodes, Variants)
            .Select(code => new object[] { code });
    }
}
