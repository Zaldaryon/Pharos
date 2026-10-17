using System.Globalization;
using System.Text.Json;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

internal sealed record QuarantineEntry(string FullyQualifiedName, string? DisplayName, string Owner, DateOnly ExpiresOn);

internal sealed class QuarantinePolicy(IReadOnlyList<QuarantineEntry> entries)
{
    public IReadOnlyList<QuarantineEntry> Entries { get; } = entries;

    public QuarantineEntry? Match(string fullyQualifiedName, string displayName) => Entries.FirstOrDefault(entry =>
        entry.FullyQualifiedName == fullyQualifiedName && (entry.DisplayName == null || entry.DisplayName == displayName));

    public static QuarantinePolicy Load(string path, DateOnly utcDate)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out JsonElement version) || version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out int schemaVersion) || schemaVersion != 1)
            throw new InvalidDataException("pharos.quarantine.json must have schemaVersion 1.");
        if (!root.TryGetProperty("entries", out JsonElement array) || array.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("pharos.quarantine.json needs an entries array.");

        List<QuarantineEntry> entries = [];
        foreach (JsonElement element in array.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Every quarantine entry must be a JSON object.");
            string fqn = RequiredString(element, "fullyQualifiedName");
            string owner = RequiredString(element, "owner");
            string? display = null;
            if (element.TryGetProperty("displayName", out JsonElement displayElement) && displayElement.ValueKind != JsonValueKind.Null)
            {
                if (displayElement.ValueKind != JsonValueKind.String) throw new InvalidDataException("Quarantine displayName must be a string or null.");
                display = displayElement.GetString();
            }
            if (display != null && string.IsNullOrWhiteSpace(display)) throw new InvalidDataException("Quarantine displayName must be omitted or non-empty.");
            string expiry = RequiredString(element, "expiresOn");
            if (!DateOnly.TryParseExact(expiry, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly expiresOn) || expiresOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) != expiry)
                throw new InvalidDataException($"Invalid quarantine expiresOn '{expiry}'; use YYYY-MM-DD.");
            if (expiresOn < utcDate) throw new InvalidDataException($"Quarantine entry for {fqn} expired on {expiry} (UTC).");
            if (entries.Any(existing => existing.FullyQualifiedName == fqn &&
                (existing.DisplayName == null || display == null || existing.DisplayName == display)))
                throw new InvalidDataException($"Duplicate or overlapping quarantine entry for {fqn}{(display == null ? "" : " (" + display + ")")}.");
            entries.Add(new QuarantineEntry(fqn, display, owner, expiresOn));
        }
        return new QuarantinePolicy(entries);
    }

    private static string RequiredString(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new InvalidDataException($"Quarantine entry needs a non-empty {name}.");
        return value.GetString()!;
    }
}
