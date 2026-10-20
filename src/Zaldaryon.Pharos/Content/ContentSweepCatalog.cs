using System.IO.Compression;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Content;

/// <summary>Enumerates live server content and the static asset codes used by theory discovery.</summary>
internal static class ContentSweepCatalog
{
    private static readonly Regex s_unquotedProperty = new(@"(?<=[{,])(\s*)([A-Za-z_][A-Za-z0-9_-]*)(\s*):", RegexOptions.Compiled);
    private static readonly Regex s_globCharacters = new("[.+^${}()|\\[\\]\\\\]", RegexOptions.Compiled);

    public static IReadOnlyList<ContentCode> FromWorld(IWorldAccessor world, string domain, ContentSweepOptions options)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(options);

        List<ContentCode> codes = [];
        codes.AddRange(world.Blocks
            .Where(block => block.Code is not null && DomainMatches(block.Code, domain))
            .Select(block => FromLocation(ContentKind.Block, block.Code!)));
        codes.AddRange(world.Items
            .Where(item => item.Code is not null && DomainMatches(item.Code, domain))
            .Select(item => FromLocation(ContentKind.Item, item.Code!)));
        codes.AddRange(world.EntityTypes
            .Where(entity => entity.Code is not null && DomainMatches(entity.Code, domain))
            .Select(entity => FromLocation(ContentKind.Entity, entity.Code)));

        return Filter(codes, domain, options.Skip, options.Variants);
    }

    public static IReadOnlyList<ContentCode> DiscoverAssetCodes(
        Type testClass,
        string domain,
        SweepSteps steps,
        IReadOnlyList<string> skip,
        IReadOnlyList<string> variants)
    {
        ArgumentNullException.ThrowIfNull(testClass);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);

        string assemblyDirectory = Path.GetDirectoryName(testClass.Assembly.Location)
            ?? AppContext.BaseDirectory;
        IEnumerable<string> declaredPaths = ScenarioAttributes.ServerMods(testClass);
        List<string> paths = declaredPaths
            .Select(path => ModStager.ResolvePath(path, assemblyDirectory))
            .ToList();

        if (paths.Count == 0)
        {
            foreach (string fallback in new[] { "TestMods", "Mods" })
            {
                string directory = Path.Combine(assemblyDirectory, fallback);
                if (Directory.Exists(directory)) paths.Add(directory);
            }
        }

        List<ContentCode> codes = [];
        foreach (string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(path))
            {
                foreach (string modRoot in MatchingModDirectories(path, domain))
                {
                    codes.AddRange(DirectoryAssetCodes(modRoot, domain, steps));
                }
            }
            else if (File.Exists(path) && Path.GetExtension(path).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                codes.AddRange(ArchiveAssetCodes(path, domain, steps));
            }
        }

        IReadOnlyList<ContentCode> filtered = Filter(codes, domain, skip, variants);
        if (filtered.Count > 0) return filtered;
        if (codes.Count == 0)
            throw new InvalidOperationException($"[ContentSweep(\"{domain}\")] found no blocktypes, itemtypes or entities JSON assets. For content registered in code, call ContentSweep.RunAsync(host, \"{domain}\") from a server scenario.");
        throw new InvalidOperationException($"[ContentSweep(\"{domain}\")] filters selected no content codes.");
    }

    public static bool MatchesPattern(string pattern, string value, string defaultDomain)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return false;
        string normalized = pattern.Contains(':', StringComparison.Ordinal) ? pattern : defaultDomain + ":" + pattern;
        string regex = "^" + s_globCharacters.Replace(normalized, "\\$&")
            .Replace("*", ".*")
            .Replace("?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IEnumerable<ContentCode> DirectoryAssetCodes(string modRoot, string domain, SweepSteps steps)
    {
        string assets = Path.Combine(modRoot, "assets", domain);
        if (!Directory.Exists(assets)) yield break;

        foreach ((string folder, ContentKind kind) in ContentFolders())
        {
            string category = Path.Combine(assets, folder);
            if (!Directory.Exists(category)) continue;
            foreach (string file in Directory.EnumerateFiles(category, "*.json", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(file); }
                catch (IOException) { continue; }

                string relative = Path.GetRelativePath(category, file);
                string fallbackCode = Path.ChangeExtension(relative, null)!.Replace(Path.DirectorySeparatorChar, '-').Replace(Path.AltDirectorySeparatorChar, '-');
                foreach (ContentCode code in ParseCodes(text, domain, kind, fallbackCode, steps)) yield return code;
            }
        }
    }

    private static IEnumerable<ContentCode> ArchiveAssetCodes(string path, string domain, SweepSteps steps)
    {
        using FileStream file = File.OpenRead(path);
        using ZipArchive archive = new(file, ZipArchiveMode.Read, leaveOpen: false);
        string? modPrefix = null;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!Path.GetFileName(entry.FullName).Equals("modinfo.json", StringComparison.OrdinalIgnoreCase)) continue;
            using StreamReader reader = new(entry.Open());
            if (ModId(reader.ReadToEnd()) != domain) continue;
            modPrefix = entry.FullName[..^"modinfo.json".Length];
            break;
        }

        if (modPrefix is null) yield break;
        string assetPrefix = modPrefix + "assets/" + domain + "/";
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.StartsWith(assetPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            string relative = entry.FullName[assetPrefix.Length..];
            foreach ((string folder, ContentKind kind) in ContentFolders())
            {
                string categoryPrefix = folder + "/";
                if (!relative.StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase)
                    || !relative.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                using StreamReader reader = new(entry.Open());
                string fallbackCode = Path.ChangeExtension(relative[categoryPrefix.Length..], null)!.Replace('/', '-');
                foreach (ContentCode code in ParseCodes(reader.ReadToEnd(), domain, kind, fallbackCode, steps)) yield return code;
                break;
            }
        }
    }

    private static IEnumerable<string> MatchingModDirectories(string root, string domain)
    {
        string[] infoFiles;
        try
        {
            infoFiles = Directory.EnumerateFiles(root, "modinfo.json", SearchOption.AllDirectories).ToArray();
        }
        catch (IOException)
        {
            infoFiles = [];
        }
        if (infoFiles.Length == 0) yield break;

        foreach (string infoFile in infoFiles)
        {
            string? modId;
            try { modId = ModId(File.ReadAllText(infoFile)); }
            catch (Exception exception) when (exception is IOException or ArgumentException or Newtonsoft.Json.JsonException) { continue; }
            if (!string.Equals(modId, domain, StringComparison.OrdinalIgnoreCase)) continue;
            yield return Path.GetDirectoryName(infoFile)!;
        }
    }

    private static string? ModId(string text)
    {
        try { return ParseObject(text)["modid"]?.Value<string>(); }
        catch (Exception exception) when (exception is Newtonsoft.Json.JsonException or ArgumentException) { return null; }
    }

    private static IEnumerable<ContentCode> ParseCodes(string json, string domain, ContentKind kind, string fallbackCode, SweepSteps steps)
    {
        JObject? root;
        try { root = ParseObject(json); }
        catch (Exception exception) when (exception is Newtonsoft.Json.JsonException or ArgumentException)
        {
            root = null;
        }

        if (root is null)
        {
            yield return new ContentCode(kind, domain + ":" + fallbackCode, steps);
            yield break;
        }

        string rawCode = root["code"]?.Value<string>() ?? fallbackCode;
        List<VariantGroup> groups = ReadVariantGroups(root["variantgroups"] as JArray);
        List<(string Code, List<string> Variants)> expanded = [(rawCode, [])];
        foreach (VariantGroup group in groups)
        {
            List<(string Code, List<string> Variants)> next = [];
            foreach ((string currentCode, List<string> currentVariants) in expanded)
            {
                foreach (string state in group.States)
                {
                    string token = "{" + group.Name + "}";
                    string nextCode = currentCode.Contains(token, StringComparison.OrdinalIgnoreCase)
                        ? ReplaceIgnoreCase(currentCode, token, state)
                        : currentCode + "-" + state;
                    next.Add((nextCode, [.. currentVariants, group.Name + "=" + state]));
                }
            }
            if (next.Count > 0) expanded = next;
        }

        foreach ((string rawVariantCode, List<string> values) in expanded)
        {
            string code = rawVariantCode.Contains(':', StringComparison.Ordinal) ? rawVariantCode : domain + ":" + rawVariantCode;
            yield return new ContentCode(kind, code, steps, string.Join(',', values));
        }
    }

    private static JObject ParseObject(string text)
    {
        string normalized = s_unquotedProperty.Replace(text, match => match.Groups[1].Value + "\"" + match.Groups[2].Value + "\"" + match.Groups[3].Value + ":");
        return JObject.Parse(normalized);
    }

    private static List<VariantGroup> ReadVariantGroups(JArray? groups)
    {
        if (groups is null) return [];
        List<VariantGroup> result = [];
        foreach (JObject group in groups.OfType<JObject>())
        {
            string? name = group["code"]?.Value<string>();
            if (string.IsNullOrWhiteSpace(name) || group["states"] is not JArray states) continue;
            string[] values = states.Select(state => state.Type == JTokenType.String
                    ? state.Value<string>()
                    : (state as JObject)?["code"]?.Value<string>())
                .Where(state => !string.IsNullOrWhiteSpace(state))
                .Select(state => state!)
                .ToArray();
            if (values.Length > 0) result.Add(new VariantGroup(name, values));
        }
        return result;
    }

    private static IReadOnlyList<ContentCode> Filter(
        IEnumerable<ContentCode> codes,
        string domain,
        IReadOnlyList<string> skip,
        IReadOnlyList<string> variants)
    {
        return codes
            .Where(code => !skip.Any(pattern => MatchesPattern(pattern, code.Code, domain)))
            .Where(code => variants.Count == 0 || variants.Any(pattern => MatchesVariantPattern(pattern, code.Variants)))
            .GroupBy(code => (code.Kind, code.Code), new ContentCodeKeyComparer())
            .Select(group => group.First())
            .OrderBy(code => code.Kind)
            .ThenBy(code => code.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool MatchesVariantPattern(string pattern, string variants)
    {
        foreach (string value in variants.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] expected = pattern.Split('=', 2);
            string[] actual = value.Split('=', 2);
            if (expected.Length == 2 && actual.Length == 2
                && GlobMatches(expected[0], actual[0])
                && GlobMatches(expected[1], actual[1])) return true;
            if (expected.Length == 1 && GlobMatches(expected[0], value)) return true;
        }
        return false;
    }

    private static bool GlobMatches(string pattern, string value)
    {
        string regex = "^" + s_globCharacters.Replace(pattern, "\\$&")
            .Replace("*", ".*")
            .Replace("?", ".") + "$";
        return Regex.IsMatch(value, regex, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static ContentCode FromLocation(ContentKind kind, AssetLocation location)
    {
        string variant = location.EndVariant();
        return new ContentCode(kind, location.ToString(), SweepSteps.All, variant.Length == 0 ? "" : "*=" + variant);
    }

    private static bool DomainMatches(AssetLocation code, string domain) =>
        string.Equals(code.Domain, domain, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<(string Folder, ContentKind Kind)> ContentFolders()
    {
        yield return ("blocktypes", ContentKind.Block);
        yield return ("itemtypes", ContentKind.Item);
        yield return ("entities", ContentKind.Entity);
    }

    private static string ReplaceIgnoreCase(string text, string oldValue, string newValue)
    {
        int index = text.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? text : text[..index] + newValue + text[(index + oldValue.Length)..];
    }

    private sealed record VariantGroup(string Name, IReadOnlyList<string> States);

    private sealed class ContentCodeKeyComparer : IEqualityComparer<(ContentKind Kind, string Code)>
    {
        public bool Equals((ContentKind Kind, string Code) x, (ContentKind Kind, string Code) y) =>
            x.Kind == y.Kind && string.Equals(x.Code, y.Code, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((ContentKind Kind, string Code) value) =>
            HashCode.Combine(value.Kind, StringComparer.OrdinalIgnoreCase.GetHashCode(value.Code));
    }
}
