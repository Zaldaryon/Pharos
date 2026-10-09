using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Zaldaryon.Pharos;

/// <summary>
/// A Vintage Story version such as <c>1.22.7</c> or <c>1.22.0-rc.3</c>. A pre-release sorts
/// before its release, and pre-release parts compare as numbers when both are numbers.
/// </summary>
public readonly record struct PharosGameVersion(int Major, int Minor, int Patch, string? Prerelease = null) : IComparable<PharosGameVersion>
{
    /// <summary>Reads <c>MAJOR.MINOR[.PATCH][-PRERELEASE]</c>; a missing patch is 0.</summary>
    public static bool TryParse(string? text, out PharosGameVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string core = text.Trim();
        string? prerelease = null;
        int dash = core.IndexOf('-');
        if (dash >= 0)
        {
            prerelease = core[(dash + 1)..];
            core = core[..dash];
            if (prerelease.Length == 0 || prerelease.Split('.').Any(p => p.Length == 0)) return false;
        }

        string[] parts = core.Split('.');
        if (parts.Length is < 2 or > 3) return false;
        int[] numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i])) return false;
        }

        version = new PharosGameVersion(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    /// <summary>Reads a version.</summary>
    /// <exception cref="FormatException">The text is not a version.</exception>
    public static PharosGameVersion Parse(string text) =>
        TryParse(text, out PharosGameVersion version) ? version : throw new FormatException($"'{text}' is not a game version such as 1.22.7 or 1.22.0-rc.3.");

    /// <inheritdoc />
    public int CompareTo(PharosGameVersion other)
    {
        int byNumbers = (Major, Minor, Patch).CompareTo((other.Major, other.Minor, other.Patch));
        if (byNumbers != 0) return byNumbers;
        if (Prerelease == other.Prerelease) return 0;
        if (Prerelease == null) return 1;
        if (other.Prerelease == null) return -1;

        string[] mine = Prerelease.Split('.'), theirs = other.Prerelease.Split('.');
        for (int i = 0; i < Math.Min(mine.Length, theirs.Length); i++)
        {
            bool a = int.TryParse(mine[i], NumberStyles.None, CultureInfo.InvariantCulture, out int x);
            bool b = int.TryParse(theirs[i], NumberStyles.None, CultureInfo.InvariantCulture, out int y);
            int part = a && b ? x.CompareTo(y) : a ? -1 : b ? 1 : string.CompareOrdinal(mine[i], theirs[i]);
            if (part != 0) return part;
        }

        return mine.Length.CompareTo(theirs.Length);
    }

    public static bool operator <(PharosGameVersion a, PharosGameVersion b) => a.CompareTo(b) < 0;

    public static bool operator >(PharosGameVersion a, PharosGameVersion b) => a.CompareTo(b) > 0;

    public static bool operator <=(PharosGameVersion a, PharosGameVersion b) => a.CompareTo(b) <= 0;

    public static bool operator >=(PharosGameVersion a, PharosGameVersion b) => a.CompareTo(b) >= 0;

    /// <inheritdoc />
    public override string ToString() => Prerelease == null ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}

/// <summary>
/// A range of game versions: comparisons joined by commas, all of which must hold, and
/// alternatives joined by <c>||</c>.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>&gt;=1.22.0</c>, <c>&gt;1.21.6</c>, <c>&lt;=1.22.7</c>, <c>&lt;1.23.0</c>, <c>=1.22.7</c>; a bare version means <c>=</c>.</item>
/// <item><c>1.22.x</c>, <c>1.22.*</c> and a bare <c>1.22</c> mean any 1.22 version, pre-releases included.</item>
/// <item>Pre-releases compare as versions: <c>&gt;=1.22.0</c> leaves out <c>1.22.0-rc.3</c>, <c>&gt;=1.22.0-rc.1</c> takes it.</item>
/// </list>
/// </remarks>
public sealed class GameVersionRange
{
    private readonly string _text;
    private readonly IReadOnlyList<IReadOnlyList<Func<PharosGameVersion, bool>>> _alternatives;

    private GameVersionRange(string text, IReadOnlyList<IReadOnlyList<Func<PharosGameVersion, bool>>> alternatives)
    {
        _text = text;
        _alternatives = alternatives;
    }

    /// <summary>Reads a range.</summary>
    /// <exception cref="FormatException">The range cannot be read; the message names the part.</exception>
    public static GameVersionRange Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new FormatException("A game version range cannot be empty.");

        List<IReadOnlyList<Func<PharosGameVersion, bool>>> alternatives = [];
        foreach (string alternative in text.Split("||"))
        {
            List<Func<PharosGameVersion, bool>> clauses = [];
            foreach (string raw in alternative.Split(','))
            {
                string clause = raw.Trim();
                if (clause.Length == 0) throw new FormatException($"'{text}' has an empty comparison.");
                clauses.AddRange(Clause(clause, text));
            }

            alternatives.Add(clauses);
        }

        return new GameVersionRange(text.Trim(), alternatives);
    }

    /// <summary>Whether <paramref name="version"/> is in the range.</summary>
    public bool Includes(PharosGameVersion version) => _alternatives.Any(all => all.All(holds => holds(version)));

    /// <inheritdoc />
    public override string ToString() => _text;

    private static IEnumerable<Func<PharosGameVersion, bool>> Clause(string clause, string range)
    {
        string op = clause.StartsWith(">=", StringComparison.Ordinal) || clause.StartsWith("<=", StringComparison.Ordinal) ? clause[..2]
            : clause[0] is '>' or '<' or '=' ? clause[..1]
            : "";
        string operand = clause[op.Length..].Trim();

        // 1.22.x, 1.22.* and 1.22: the whole minor version.
        string[] parts = operand.Split('.');
        bool wildcard = parts.Length == 3 && parts[2] is "x" or "X" or "*";
        if (op is "" or "=" && (wildcard || (parts.Length == 2 && !operand.Contains('-'))))
        {
            PharosGameVersion low = Version(parts[0] + "." + parts[1] + ".0-0", clause, range);
            PharosGameVersion high = new(low.Major, low.Minor + 1, 0, "0");
            return [v => v >= low, v => v < high];
        }

        PharosGameVersion bound = Version(operand, clause, range);
        return op switch
        {
            ">=" => [v => v >= bound],
            "<=" => [v => v <= bound],
            ">" => [v => v > bound],
            "<" => [v => v < bound],
            _ => [v => v.CompareTo(bound) == 0],
        };
    }

    private static PharosGameVersion Version(string text, string clause, string range) =>
        PharosGameVersion.TryParse(text, out PharosGameVersion version)
            ? version
            : throw new FormatException($"'{clause}' in the game version range '{range}' is not a comparison such as >=1.22.0.");
}

/// <summary>The Vintage Story the tests run against.</summary>
public static class InstalledGame
{
    private static readonly Lazy<string> s_loaded = new(ReadLoaded);

    // A method of its own, so an API that cannot load fails inside the try, not when the caller compiles.
    private static string ReadLoaded()
    {
        try
        {
            return LoadedConstant();
        }
        catch
        {
            return "unknown";
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static string LoadedConstant()
    {
        Type type = typeof(Vintagestory.API.Config.GameVersion);
        return type.GetField("ShortGameVersion", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string
            ?? type.GetField("OverallVersion", BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string
            ?? "unknown";
    }

    /// <summary>
    /// The version of the game's API the tests loaded, read from the API's constants rather than
    /// the value Pharos was compiled with, or "unknown".
    /// </summary>
    public static string Version => s_loaded.Value;

    /// <summary><see cref="Version"/>, read as a version, or null.</summary>
    public static PharosGameVersion? ParsedVersion => PharosGameVersion.TryParse(Version, out PharosGameVersion version) ? version : null;

    /// <summary>
    /// The version of the install in <paramref name="installDir"/>, read from its
    /// <c>VintagestoryAPI.dll</c> without loading it, or null. The file version of that DLL says
    /// only the minor version, so the API's own constant is read.
    /// </summary>
    public static string? ReadVersion(string installDir)
    {
        string path = Path.Combine(installDir, "VintagestoryAPI.dll");
        if (!File.Exists(path)) return null;

        try
        {
            using FileStream stream = File.OpenRead(path);
            using PEReader pe = new(stream);
            MetadataReader metadata = pe.GetMetadataReader();
            foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
            {
                TypeDefinition type = metadata.GetTypeDefinition(handle);
                if (metadata.GetString(type.Name) != "GameVersion" || metadata.GetString(type.Namespace) != "Vintagestory.API.Config") continue;

                foreach (FieldDefinitionHandle fieldHandle in type.GetFields())
                {
                    FieldDefinition field = metadata.GetFieldDefinition(fieldHandle);
                    if (metadata.GetString(field.Name) != "ShortGameVersion") continue;
                    ConstantHandle constant = field.GetDefaultValue();
                    if (constant.IsNil) return null;
                    BlobReader blob = metadata.GetBlobReader(metadata.GetConstant(constant).Value);
                    return blob.ReadUTF16(blob.Length);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException or InvalidOperationException)
        {
        }

        return null;
    }
}
