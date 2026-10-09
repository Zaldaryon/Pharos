using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The files a test seeds into the server's and the client's data folders, read and checked, with
/// their port placeholders filled in.
/// </summary>
internal sealed partial class DataFileSet
{
    private readonly List<Entry> _entries;

    private DataFileSet(List<Entry> entries, IReadOnlyDictionary<string, int> ports, string key)
    {
        _entries = entries;
        Ports = ports;
        Key = key;
    }

    /// <summary>No files.</summary>
    public static DataFileSet Empty { get; } = new([], new Dictionary<string, int>(), "");

    /// <summary>The port each placeholder name got.</summary>
    public IReadOnlyDictionary<string, int> Ports { get; }

    /// <summary>What the files are, without their ports: equal for two tests that seed the same.</summary>
    public string Key { get; }

    /// <summary>Whether any file goes to <paramref name="side"/>.</summary>
    public bool HasFilesFor(EnumAppSide side) => _entries.Any(e => e.Side == side);

    /// <summary>Where the files of <paramref name="side"/> go, relative to its data folder.</summary>
    public IEnumerable<string> DestinationsFor(EnumAppSide side) => _entries.Where(e => e.Side == side).Select(e => e.To);

    /// <summary>The port a placeholder name got.</summary>
    /// <exception cref="ArgumentException">No file of the test has that placeholder.</exception>
    public int Port(string name) => Ports.TryGetValue(name, out int port)
        ? port
        : throw new ArgumentException(
            Ports.Count == 0
                ? $"No data file of this test has a {{{{pharos:port:{name}}}}} placeholder; it has none."
                : $"No data file of this test has a {{{{pharos:port:{name}}}}} placeholder. Known: {string.Join(", ", Ports.Keys)}.",
            nameof(name));

    /// <summary>Writes the files of <paramref name="side"/> into <paramref name="dataPath"/>.</summary>
    public void WriteTo(string dataPath, EnumAppSide side)
    {
        foreach (Entry entry in _entries.Where(e => e.Side == side))
        {
            string path = Path.Combine(dataPath, entry.To);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, entry.Content);
        }
    }

    /// <summary>
    /// Reads, checks and fills in the files <paramref name="attributes"/> name, for the sides
    /// <paramref name="sides"/> a scenario has. A test's attributes replace its class's for the
    /// same side and destination.
    /// </summary>
    /// <exception cref="FileNotFoundException">A source file does not exist.</exception>
    /// <exception cref="ArgumentException">A destination leaves the data folder or is not allowed.</exception>
    /// <exception cref="InvalidOperationException">Two files go to the same place at the same level.</exception>
    /// <exception cref="FormatException">A placeholder is malformed.</exception>
    public static DataFileSet Create(IEnumerable<DataFilesAttribute> classLevel, IEnumerable<DataFilesAttribute> methodLevel, IReadOnlyCollection<EnumAppSide> sides) =>
        Create([classLevel, methodLevel], sides);

    /// <summary>
    /// As <see cref="Create(IEnumerable{DataFilesAttribute}, IEnumerable{DataFilesAttribute}, IReadOnlyCollection{EnumAppSide})"/>,
    /// with any number of levels, from the base class down to the test: each level's files replace
    /// the earlier levels' for the same side and destination.
    /// </summary>
    public static DataFileSet Create(IEnumerable<IEnumerable<DataFilesAttribute>> levels, IReadOnlyCollection<EnumAppSide> sides)
    {
        // Keyed by side and upper-cased destination, so paths compare as on Windows.
        Dictionary<(EnumAppSide, string), (string To, string Source)> targets = [];
        foreach (IEnumerable<DataFilesAttribute> level in levels) Collect(level, sides, targets);
        if (targets.Count == 0) return Empty;

        // Read once per source, so a file seeded on both sides is the same bytes with the same ports.
        Dictionary<string, byte[]> contents = targets.Values.Select(t => t.Source).Distinct(StringComparer.Ordinal)
            .ToDictionary(s => s, File.ReadAllBytes, StringComparer.Ordinal);
        SortedSet<string> names = new(StringComparer.Ordinal);
        foreach ((string source, byte[] bytes) in contents) FindPlaceholders(source, bytes, names);

        IReadOnlyList<int> taken = FreePorts.Take(names.Count);
        Dictionary<string, int> ports = names.Zip(taken).ToDictionary(p => p.First, p => p.Second, StringComparer.Ordinal);

        List<Entry> entries = targets
            .Select(t => new Entry(t.Key.Item1, t.Value.To, t.Value.Source, Fill(contents[t.Value.Source], ports)))
            .OrderBy(e => e.Side).ThenBy(e => e.To, StringComparer.Ordinal)
            .ToList();
        string key = string.Join("|", entries.Select(e => $"{e.Side}:{e.To}<{e.Source}:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(contents[e.Source]))}"));
        return new DataFileSet(entries, ports, key);
    }

    // One level's files: they replace earlier levels' files for the same side and destination, and
    // two of them going to the same place is an error.
    private static void Collect(IEnumerable<DataFilesAttribute> attributes, IReadOnlyCollection<EnumAppSide> sides, Dictionary<(EnumAppSide, string), (string, string)> targets)
    {
        HashSet<(EnumAppSide, string)> atThisLevel = [];
        foreach (DataFilesAttribute attribute in attributes)
        {
            string source = ResolveSource(attribute.Source);
            string to = Destination(attribute.To ?? "ModConfig/" + Path.GetFileName(source));
            EnumAppSide[] attributeSides = attribute.Side == EnumAppSide.Universal ? [EnumAppSide.Server, EnumAppSide.Client] : [attribute.Side];
            foreach (EnumAppSide side in attributeSides.Where(sides.Contains))
            {
                CheckAllowed(side, to);
                (EnumAppSide, string) target = (side, to.ToUpperInvariant());
                if (!atThisLevel.Add(target))
                {
                    throw new InvalidOperationException($"Two data files go to {to} on the {side.ToString().ToLowerInvariant()} side at the same level.");
                }

                targets[target] = (to, source);
            }
        }
    }

    // Relative to the working folder, as [ServerMods] is, or else to the test assembly's folder.
    private static string ResolveSource(string source) => ResolveFixture(source, "data file");

    /// <summary>
    /// The full path of a fixture file: relative to the working folder, as <c>[ServerMods]</c> is,
    /// or else to the test assembly's folder.
    /// </summary>
    /// <exception cref="FileNotFoundException">Neither exists.</exception>
    internal static string ResolveFixture(string source, string what)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        string full = Path.GetFullPath(source);
        if (File.Exists(full)) return full;

        string beside = Path.GetFullPath(source, AppContext.BaseDirectory);
        if (File.Exists(beside)) return beside;

        throw new FileNotFoundException(
            $"The {what} '{source}' does not exist: looked at {full} and {beside}. Copy fixtures to the test output with " +
            "<None Include=\"Fixtures\\**\" CopyToOutputDirectory=\"PreserveNewest\" />.", source);
    }

    // A relative path that stays inside the data folder, with forward slashes.
    private static string Destination(string to)
    {
        if (string.IsNullOrWhiteSpace(to)) throw new ArgumentException("A data file needs a destination.");
        string normalized = to.Replace('\\', '/');
        if (Path.IsPathRooted(to) || normalized.StartsWith('/') || normalized.Contains(':') || normalized.EndsWith('/')
            || normalized.Split('/').Any(part => part is ".." or "." or "" || part.EndsWith('.') || part.EndsWith(' ') || IsDeviceName(part)))
        {
            throw new ArgumentException($"The data file destination '{to}' must be a file path inside the data folder, such as ModConfig/mymod.json.");
        }

        return normalized;
    }

    // Names Windows keeps for devices, with or without an extension.
    private static bool IsDeviceName(string part)
    {
        string name = part.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (name.Length == 4 && (name.StartsWith("COM", StringComparison.Ordinal) || name.StartsWith("LPT", StringComparison.Ordinal)) && char.IsDigit(name[3]));
    }

    // Folders the game or Pharos fill themselves, and files the game reads only once per process.
    private static void CheckAllowed(EnumAppSide side, string to)
    {
        string first = to.Split('/')[0];
        if (first.Equals("Mods", StringComparison.OrdinalIgnoreCase) || first.Equals("ModsByServer", StringComparison.OrdinalIgnoreCase)
            || (side == EnumAppSide.Server && first.Equals("ClientMods", StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Data files cannot go into {first}/: stage mods with [ServerMods] or [PharosMods].");
        }

        // The game holds these open or writes them as it runs: putting them back under a running
        // host would fight it.
        if (first.ToUpperInvariant() is "SAVES" or "BACKUPSAVES" or "BACKUPS" or "OLDSAVES" or "LOGS" or "CACHE" or "PLAYERDATA"
            || to.Equals("serverconfig.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Data files cannot go to {to}: the game keeps it open or writes it itself. Configure the world with [ServerWorld].");
        }

        if (to.Equals("clientsettings.json", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("clientsettings.json cannot be seeded: the game loads it once per process. Use [ClientSetting] instead.");
        }
    }

    private static void FindPlaceholders(string source, byte[] bytes, SortedSet<string> names)
    {
        string text = Encoding.UTF8.GetString(bytes);
        foreach (Match token in AnyToken().Matches(text))
        {
            Match port = PortToken().Match(token.Value);
            if (!port.Success || port.Length != token.Length)
            {
                int line = text.AsSpan(0, token.Index).Count('\n') + 1;
                throw new FormatException($"{Path.GetFileName(source)} line {line}: '{token.Value}' is not a Pharos placeholder. Use {{{{pharos:port:NAME}}}} with letters, digits, '.', '_' or '-'.");
            }

            names.Add(port.Groups[1].Value);
        }
    }

    private static byte[] Fill(byte[] bytes, IReadOnlyDictionary<string, int> ports)
    {
        if (ports.Count == 0) return bytes;
        string text = Encoding.UTF8.GetString(bytes);
        if (!text.Contains("pharos", StringComparison.OrdinalIgnoreCase)) return bytes;

        string filled = PortToken().Replace(text, m => ports[m.Groups[1].Value].ToString(CultureInfo.InvariantCulture));
        bool bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        byte[] body = new UTF8Encoding(false).GetBytes(filled.TrimStart('﻿'));
        return bom ? [0xEF, 0xBB, 0xBF, .. body] : body;
    }

    // Loose, so a misspelt placeholder is reported rather than left in the file.
    [GeneratedRegex(@"\{\{\s*pharos\s*:[^}]*\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex AnyToken();

    [GeneratedRegex(@"\{\{pharos:port:([A-Za-z0-9_.\-]+)\}\}")]
    private static partial Regex PortToken();

    private sealed record Entry(EnumAppSide Side, string To, string Source, byte[] Content);
}

/// <summary>
/// The seeded files and everything under <c>ModConfig</c> in one data folder, as they were once the
/// side had booted, to put back after a test changed them.
/// </summary>
/// <remarks>
/// Taken after boot rather than from the sources: many mods rewrite their config when they start,
/// adding defaults, and that rewritten file is what every test starts from.
/// </remarks>
internal sealed class DataFileBaseline
{
    private readonly string _root;
    private readonly IReadOnlyList<string> _destinations;
    private readonly Dictionary<string, byte[]> _files;

    private DataFileBaseline(string root, IReadOnlyList<string> destinations, Dictionary<string, byte[]> files)
    {
        _root = root;
        _destinations = destinations;
        _files = files;
    }

    /// <summary>Records the files of <paramref name="set"/> for <paramref name="side"/> in <paramref name="dataPath"/>, or null when there are none.</summary>
    public static DataFileBaseline? Capture(string dataPath, DataFileSet set, EnumAppSide side)
    {
        if (!set.HasFilesFor(side)) return null;

        List<string> destinations = set.DestinationsFor(side).ToList();
        Dictionary<string, byte[]> files = new(PathComparer);
        foreach (string path in Watched(dataPath, destinations))
        {
            if (File.Exists(path)) files[path] = File.ReadAllBytes(path);
        }

        return new DataFileBaseline(dataPath, destinations, files);
    }

    /// <summary>
    /// Puts back what changed: rewrites changed files, recreates deleted ones, and deletes files
    /// added under <c>ModConfig</c>. Returns how many files it touched.
    /// </summary>
    public int Restore()
    {
        int touched = 0;
        foreach (string path in Watched(_root, _destinations).Where(p => !_files.ContainsKey(p)).ToList())
        {
            if (!File.Exists(path)) continue;
            File.Delete(path);
            touched++;
        }

        foreach ((string path, byte[] saved) in _files)
        {
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(saved)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, saved);
            touched++;
        }

        return touched;
    }

    private static IEnumerable<string> Watched(string dataPath, IEnumerable<string> destinations)
    {
        string modConfig = Path.Combine(dataPath, "ModConfig");
        IEnumerable<string> underModConfig = Directory.Exists(modConfig) ? Directory.EnumerateFiles(modConfig, "*", SearchOption.AllDirectories) : [];
        return underModConfig.Concat(destinations.Select(d => Path.GetFullPath(Path.Combine(dataPath, d))))
            .Select(Path.GetFullPath).Distinct(PathComparer);
    }

    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
