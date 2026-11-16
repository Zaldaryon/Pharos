using Newtonsoft.Json.Linq;
using Zaldaryon.Pharos.Platform;
using GameLang = Vintagestory.API.Config.Lang;

namespace Zaldaryon.Pharos.Translations;

/// <summary>
/// Discovers the languages shipped by Vintage Story and identifies right-to-left locales.
/// </summary>
public static class LanguageCatalog
{
    /// <summary>Synthetic locale code for pseudo-localised long text testing.</summary>
    public const string PseudoLongLocale = "pseudo-long";

    private static readonly string[] s_fallbackLanguages =
    [
        "en", "ar", "be", "bg", "cs", "da", "nl", "fi", "fr", "de", "hu", "eo",
        "is", "it", "ja", "ko", "lt", "no", "pl", "pt-pt", "pt-br", "ru", "sr",
        "es-es", "es-419", "sk", "sv-se", "th", "tr", "uk", "vi", "zh-cn", "zh-tw",
    ];

    private static readonly HashSet<string> s_rtlBaseSubtags = new(StringComparer.OrdinalIgnoreCase)
    {
        "ar", "dv", "fa", "he", "iw", "ps", "sd", "ug", "ur", "yi",
    };

    /// <summary>
    /// Returns every language code shipped by the game, from <see cref="GameLang.AvailableLanguages"/>
    /// when booted or from <c>assets/game/lang/languages.json</c> during test discovery.
    /// </summary>
    public static IReadOnlyList<string> AllLanguages()
    {
        if (GameLang.AvailableLanguages.Count > 0)
        {
            return [.. GameLang.AvailableLanguages.Keys.Where(code => !string.Equals(code, PseudoLongLocale, StringComparison.OrdinalIgnoreCase))];
        }

        string gamePath = HeadlessPlatformResolver.ResolveGamePath();
        string languagesFile = Path.Combine(gamePath, "assets", "game", "lang", "languages.json");
        if (!File.Exists(languagesFile))
        {
            return s_fallbackLanguages;
        }

        return ParseLanguageCodes(File.ReadAllText(languagesFile));
    }

    /// <summary>Parses distinct language codes from the JSON content of <c>languages.json</c>.</summary>
    internal static IReadOnlyList<string> ParseLanguageCodes(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        JArray array = JArray.Parse(json);
        List<string> codes = [];
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        foreach (JToken token in array)
        {
            string? code = token["code"]?.Value<string>();
            if (!string.IsNullOrWhiteSpace(code) && seen.Add(code))
            {
                codes.Add(code);
            }
        }

        return codes;
    }

    /// <summary>Whether <paramref name="languageCode"/> is written right-to-left.</summary>
    public static bool IsRightToLeft(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode)) return false;
        int dash = languageCode.IndexOf('-');
        string primary = dash > 0 ? languageCode[..dash] : languageCode;
        return s_rtlBaseSubtags.Contains(primary);
    }

    internal static IReadOnlyList<string> SelectSweepLanguages(bool includePseudoLong, string[]? exclude)
    {
        HashSet<string> excluded = new(exclude ?? [], StringComparer.OrdinalIgnoreCase);
        List<string> selected = [.. AllLanguages().Where(lang => !excluded.Contains(lang))];
        if (includePseudoLong && !excluded.Contains(PseudoLongLocale))
        {
            selected.Add(PseudoLongLocale);
        }

        return selected;
    }

    internal static IReadOnlyList<string> ValidateExplicitLanguages(string[] languages)
    {
        ArgumentNullException.ThrowIfNull(languages);
        if (languages.Length == 0)
        {
            throw new ArgumentException("[Languages] needs at least one language code, such as \"de\".", nameof(languages));
        }
        if (languages.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A language code of [Languages] cannot be empty.", nameof(languages));
        }

        return Array.AsReadOnly((string[])languages.Clone());
    }

    internal static void EnsureStringParameter(System.Reflection.MethodInfo testMethod, string attributeName)
    {
        ArgumentNullException.ThrowIfNull(testMethod);
        if (testMethod.GetParameters() is not [{ ParameterType: var type }] || type != typeof(string))
        {
            throw new ArgumentException($"[{attributeName}] requires a theory method with one string parameter.");
        }
    }
}
