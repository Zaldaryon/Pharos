using System.Reflection;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;
using Zaldaryon.Pharos.UI;

namespace Zaldaryon.Pharos.XUnit.V3;

/// <summary>
/// Runs a theory once per window size, and per GUI scale in GuiScales, with a WindowLayout parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WindowSizesAttribute(params string[] sizes) : DataAttribute
{
    /// <summary>The window sizes, such as "1280x720".</summary>
    public IReadOnlyList<string> Sizes { get; } = sizes;

    /// <summary>The GUI scales each size is tested at; when empty, each size's own ("1280x720@1.5"), or 1.</summary>
    public float[] GuiScales { get; set; } = [];

    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        if (Sizes.Count == 0) throw new ArgumentException("[WindowSizes] needs at least one size, such as \"1280x720\".");
        if (testMethod.GetCustomAttribute<ClientSettingsMatrixAttribute>() != null)
        {
            throw new ArgumentException(
                "[WindowSizes] and [ClientSettingsMatrix] cannot be combined on one theory: xUnit does not combine their rows. " +
                "Give the GUI scales to [WindowSizes] with GuiScales, or write them as \"1280x720@1.5\".");
        }

        List<ITheoryDataRow> rows = [];
        foreach (string size in Sizes)
        {
            WindowLayout layout = WindowLayout.Parse(size);
            if (GuiScales.Length == 0)
            {
                rows.Add(new TheoryDataRow(layout));
                continue;
            }

            if (size.Contains('@')) throw new ArgumentException($"'{size}' has a GUI scale of its own: leave it out, or leave out GuiScales.");
            foreach (float scale in GuiScales)
            {
                rows.Add(new TheoryDataRow(WindowLayout.Checked(layout.WithGuiScale(scale))));
            }
        }

        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;
}
