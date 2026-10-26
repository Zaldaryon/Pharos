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

    /// <summary>
    /// Sizes that should be skipped, with their reason. Use "1920x1080:reason" format.
    /// </summary>
    public string[] SkipSizes { get; set; } = [];

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

        Dictionary<string, string> skipReasons = ParseSkipSizes();

        List<ITheoryDataRow> rows = [];
        foreach (string size in Sizes)
        {
            WindowLayout layout = WindowLayout.Parse(size);
            string sizeKey = $"{layout.Width}x{layout.Height}";
            skipReasons.TryGetValue(sizeKey, out string? skipReason);

            if (GuiScales.Length == 0)
            {
                TheoryDataRow<WindowLayout> row = new(layout)
                {
                    TestDisplayName = layout.ToString(),
                    Skip = skipReason
                };
                rows.Add(row);
                continue;
            }

            if (size.Contains('@')) throw new ArgumentException($"'{size}' has a GUI scale of its own: leave it out, or leave out GuiScales.");
            foreach (float scale in GuiScales)
            {
                WindowLayout scaledLayout = WindowLayout.Checked(layout.WithGuiScale(scale));
                string scaleKey = $"{layout.Width}x{layout.Height}@{scale}";
                string? scaleSkipReason = skipReasons.TryGetValue(scaleKey, out var sr) ? sr : skipReason;

                // Skip GUI scale 3 at resolutions below 1920x1080
                if (scaleSkipReason == null && scale >= 3f && (layout.Width < 1920 || layout.Height < 1080))
                {
                    scaleSkipReason = $"GUI scale {scale} requires at least 1920x1080";
                }

                TheoryDataRow<WindowLayout> row = new(scaledLayout)
                {
                    TestDisplayName = scaledLayout.ToString(),
                    Skip = scaleSkipReason
                };
                rows.Add(row);
            }
        }

        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;

    private Dictionary<string, string> ParseSkipSizes()
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in SkipSizes)
        {
            int colonIndex = entry.IndexOf(':');
            if (colonIndex <= 0) continue;
            string key = entry[..colonIndex].Trim();
            string reason = entry[(colonIndex + 1)..].Trim();
            if (key.Length > 0 && reason.Length > 0)
            {
                result[key] = reason;
            }
        }
        return result;
    }
}
