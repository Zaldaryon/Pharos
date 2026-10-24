using System.Text.Json;

// Path and IdOverride are written to obj/ by the PharosModUnderTest MSBuild target.
internal static partial class ModUnderTest
{
    public static string Id => string.IsNullOrEmpty(IdOverride) ? ReadId() : IdOverride;

    private static string ReadId()
    {
        string manifest = System.IO.Path.Combine(Path, "modinfo.json");
        if (File.Exists(manifest))
        {
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(manifest),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            foreach (JsonProperty property in json.RootElement.EnumerateObject().Reverse())
            {
                if (property.Name.Equals("modid", StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(property.Value.GetString()))
                {
                    return property.Value.GetString()!;
                }
            }
        }

        throw new InvalidOperationException($"Found no top-level modid in '{manifest}'. Copy modinfo.json to the mod's output, or set PharosModId in the test project (also required for a mod zip).");
    }
}
