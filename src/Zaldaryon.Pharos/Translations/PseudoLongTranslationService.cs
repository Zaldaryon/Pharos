using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace Zaldaryon.Pharos.Translations;

/// <summary>
/// A synthetic <see cref="ITranslationService"/> that wraps the default English service and expands
/// translated strings by roughly 40% while preserving format placeholders and VTML tags.
/// </summary>
internal sealed class PseudoLongTranslationService(ITranslationService inner) : ITranslationService
{
    private static readonly Regex s_tokenPattern = new(@"(\{[^{}]*\}|<[^>]*>|\\n|\\r|\\t)", RegexOptions.Compiled);

    public EnumLinebreakBehavior LineBreakBehavior => inner.LineBreakBehavior;

    public string LanguageCode => LanguageCatalog.PseudoLongLocale;

    public void Load(bool lazyload = false) => inner.Load(lazyload);

    public void PreLoad(string assetsPath, bool lazyLoad = false) => inner.PreLoad(assetsPath, lazyLoad);

    public void PreLoadModWorldConfig(string modPath, string modDomain, bool lazyLoad = false) =>
        inner.PreLoadModWorldConfig(modPath, modDomain, lazyLoad);

    public string? GetIfExists(string key, params object[] args) =>
        inner.GetIfExists(key, args) is { } value ? Expand(value) : null;

    public string Get(string key, params object[] args) => Expand(inner.Get(key, args));

    public IDictionary<string, string> GetAllEntries() =>
        inner.GetAllEntries().ToDictionary(pair => pair.Key, pair => Expand(pair.Value));

    public string GetUnformatted(string key) => Expand(inner.GetUnformatted(key));

    public string GetMatching(string key, params object[] args) => Expand(inner.GetMatching(key, args));

    public string? GetMatchingIfExists(string key, params object[] args) =>
        inner.GetMatchingIfExists(key, args) is { } value ? Expand(value) : null;

    public bool HasTranslation(string key, bool findWildcarded = true) =>
        inner.HasTranslation(key, findWildcarded);

    public bool HasTranslation(string key, bool findWildcarded, bool logErrors) =>
        inner.HasTranslation(key, findWildcarded, logErrors);

    public void UseAssetManager(IAssetManager assetManager) => inner.UseAssetManager(assetManager);

    public void InitialiseSearch() => inner.InitialiseSearch();

    public void Invalidate() => inner.Invalidate();

    /// <summary>
    /// Expands plain text segments while keeping format placeholders and VTML tags intact so
    /// translated dialogs still parse and format arguments.
    /// </summary>
    internal static string Expand(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        StringBuilder expanded = new(text.Length + 16);
        expanded.Append("[!! ");

        int index = 0;
        foreach (Match match in s_tokenPattern.Matches(text))
        {
            if (match.Index > index)
            {
                AppendExpandedSegment(expanded, text.AsSpan(index, match.Index - index));
            }

            expanded.Append(match.Value);
            index = match.Index + match.Length;
        }

        if (index < text.Length)
        {
            AppendExpandedSegment(expanded, text.AsSpan(index));
        }

        expanded.Append(" !!]");
        return expanded.ToString();
    }

    private static void AppendExpandedSegment(StringBuilder target, ReadOnlySpan<char> segment)
    {
        foreach (char c in segment)
        {
            target.Append(c);
            if (c is 'a' or 'e' or 'i' or 'o' or 'u' or 'A' or 'E' or 'I' or 'O' or 'U')
            {
                target.Append(c);
            }
        }
    }
}
