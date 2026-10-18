using System.Reflection;
using Xunit.Sdk;
using Zaldaryon.Pharos.UI;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Runs a theory once per window size, and per GUI scale in <see cref="GuiScales"/>, with a
/// <see cref="WindowLayout"/> parameter to apply with <c>client.Window.UseAsync(layout)</c>.
/// </summary>
/// <remarks>
/// xUnit runs the rows of several data attributes one after the other, not their combinations: put
/// the GUI scales here, in <see cref="GuiScales"/>, to test every size at every scale.
/// </remarks>
/// <example>
/// <code>
/// [ClientTheory]
/// [WindowSizes("1280x720", "1920x1080", GuiScales = [1f, 1.5f])]
/// public async Task PanelFitsOnScreen(WindowLayout layout)
/// {
///     await using IAsyncDisposable _ = await Client!.Window.UseAsync(layout);
///     ...
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WindowSizesAttribute(params string[] sizes) : DataAttribute
{
    /// <summary>The window sizes, such as "1280x720".</summary>
    public IReadOnlyList<string> Sizes { get; } = sizes;

    /// <summary>The GUI scales each size is tested at; when empty, each size's own ("1280x720@1.5"), or 1.</summary>
    public float[] GuiScales { get; set; } = [];

    /// <inheritdoc />
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        if (Sizes.Count == 0) throw new ArgumentException("[WindowSizes] needs at least one size, such as \"1280x720\".");
        if (testMethod.GetCustomAttribute<ClientSettingsMatrixAttribute>() != null)
        {
            throw new ArgumentException(
                "[WindowSizes] and [ClientSettingsMatrix] cannot be combined on one theory: xUnit does not combine their rows. " +
                "Give the GUI scales to [WindowSizes] with GuiScales, or write them as \"1280x720@1.5\".");
        }

        foreach (string size in Sizes)
        {
            WindowLayout layout = WindowLayout.Parse(size);
            if (GuiScales.Length == 0)
            {
                yield return [layout];
                continue;
            }

            if (size.Contains('@')) throw new ArgumentException($"'{size}' has a GUI scale of its own: leave it out, or leave out GuiScales.");
            foreach (float scale in GuiScales)
            {
                yield return [WindowLayout.Checked(layout.WithGuiScale(scale))];
            }
        }
    }
}
