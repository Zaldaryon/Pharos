using System.Collections.Concurrent;
using HarmonyLib;
using Vintagestory.API.Config;
using GameLang = Vintagestory.API.Config.Lang;

namespace Zaldaryon.Pharos.Translations;

/// <summary>A translation lookup that found no entry for its key.</summary>
/// <param name="Key">The key as it was looked up.</param>
/// <param name="Domain">The key's domain: the part before ':', or "game".</param>
/// <param name="Language">The language it was looked up in.</param>
/// <param name="FellBackToDefault">
/// Whether the default language (English) had the key, so the game showed that; otherwise the game
/// showed the key itself.
/// </param>
/// <param name="Count">How many times it was looked up.</param>
public sealed record MissingTranslation(string Key, string Domain, string Language, bool FellBackToDefault, int Count)
{
    /// <summary>
    /// The domain of a translation key: the part before ':' when it reads as a domain (lowercase
    /// letters, digits, '-' and '_', as mod ids are), else "game". A sentence looked up as a key,
    /// such as "Error: ...", has no domain.
    /// </summary>
    public static string DomainOf(string key)
    {
        int colon = key.IndexOf(':');
        if (colon <= 0) return "game";
        for (int i = 0; i < colon; i++)
        {
            char c = key[i];
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')) return "game";
        }

        return key[..colon];
    }
}

/// <summary>
/// The hooks on the game's translation lookups that report keys with no entry. The game's
/// <c>Lang</c> is one per process, shared by every client and server in it; each sink keeps what
/// was looked up on its own client's thread.
/// </summary>
/// <remarks>
/// Installed before the first server or client boots: the lookups are small enough to be compiled
/// into their callers, which would then go on running unpatched.
/// </remarks>
internal static class TranslationCapture
{
    private static readonly object s_lock = new();
    private static readonly Harmony s_harmony = new("zaldaryon.pharos.lang");
    private static bool s_installed;
    private static volatile Sink[] s_sinks = [];

    public static void Install()
    {
        lock (s_lock)
        {
            if (s_installed) return;
            Patch(nameof(GameLang.Get), [typeof(string), typeof(object[])], nameof(AfterGet));
            Patch(nameof(GameLang.GetUnformatted), [typeof(string)], nameof(AfterGet));
            Patch(nameof(GameLang.GetWithFallback), [typeof(string), typeof(string), typeof(object[])], nameof(AfterGetWithFallback));
            Patch(nameof(GameLang.GetMatching), [typeof(string), typeof(object[])], nameof(AfterGetMatching));
            Patch(nameof(GameLang.GetL), [typeof(string), typeof(string), typeof(object[])], nameof(AfterGetL));
            Patch(nameof(GameLang.GetMatchingL), [typeof(string), typeof(string), typeof(object[])], nameof(AfterGetMatchingL));
            s_installed = true;
        }
    }

    private static void Patch(string method, Type[] parameters, string postfix) =>
        s_harmony.Patch(AccessTools.Method(typeof(GameLang), method, parameters), postfix: new HarmonyMethod(typeof(TranslationCapture), postfix));

    /// <summary>Starts keeping the lookups made on a client's thread.</summary>
    public static Sink Open(Func<bool> onClientThread)
    {
        Sink sink = new(onClientThread);
        lock (s_lock) s_sinks = [.. s_sinks, sink];
        return sink;
    }

    public static void Close(Sink sink)
    {
        lock (s_lock) s_sinks = [.. s_sinks.Where(s => s != sink)];
    }

    /// <summary>
    /// Takes back one lookup of <paramref name="key"/> made on this thread: the game translating a
    /// command's status line, which Pharos ran.
    /// </summary>
    public static void Forget(string? key)
    {
        if (string.IsNullOrEmpty(key)) return;
        foreach (Sink sink in s_sinks)
        {
            if (sink.OnClientThread()) sink.Remove(key, GameLang.CurrentLocale);
        }
    }

    private static void AfterGet(string key) => Record(key, GameLang.CurrentLocale, wildcards: false);

    // The sink of the client whose thread this is, or null.
    private static Sink? Owner()
    {
        foreach (Sink sink in s_sinks)
        {
            if (sink.OnClientThread()) return sink;
        }

        return null;
    }

    private static void AfterGetWithFallback(string key, string fallbackKey)
    {
        if (fallbackKey == null || Owner() is not { } owner || Has(GameLang.CurrentLocale, fallbackKey, wildcards: false)) return;
        Record(owner, key, GameLang.CurrentLocale, wildcards: false);
    }

    private static void AfterGetMatching(string key) => Record(key, GameLang.CurrentLocale, wildcards: true);

    private static void AfterGetL(string langcode, string key) => Record(key, langcode, wildcards: false);

    private static void AfterGetMatchingL(string langcode, string key) => Record(key, langcode, wildcards: true);

    private static void Record(string key, string language, bool wildcards)
    {
        if (s_sinks.Length == 0 || key == null || language == null || Owner() is not { } owner) return;
        Record(owner, key, language, wildcards);
    }

    private static void Record(Sink owner, string key, string language, bool wildcards)
    {
        if (key == null || language == null || Has(language, key, wildcards)) return;
        bool fellBack = language != GameLang.DefaultLocale && Has(GameLang.DefaultLocale, key, wildcards);
        owner.Add(key, language, fellBack);
    }

    private static bool Has(string language, string key, bool wildcards)
    {
        try
        {
            return GameLang.AvailableLanguages.TryGetValue(language, out ITranslationService? service) && service.HasTranslation(key, wildcards, logErrors: false);
        }
        catch
        {
            // A hook never breaks the lookup it watches.
            return true;
        }
    }

    /// <summary>What one client looked up and did not find.</summary>
    internal sealed class Sink(Func<bool> onClientThread)
    {
        private readonly ConcurrentDictionary<(string Key, string Language), Entry> _entries = new();
        private long _order;

        public Func<bool> OnClientThread { get; } = onClientThread;

        public void Add(string key, string language, bool fellBack)
        {
            Entry entry = _entries.GetOrAdd((key, language), _ => new Entry(Interlocked.Increment(ref _order), fellBack));
            Interlocked.Increment(ref entry.Count);
        }

        public void Remove(string key, string language)
        {
            if (_entries.TryGetValue((key, language), out Entry? entry) && Interlocked.Decrement(ref entry.Count) <= 0)
            {
                _entries.TryRemove((key, language), out _);
            }
        }

        public void Clear() => _entries.Clear();

        public IReadOnlyList<MissingTranslation> Snapshot() =>
            [.. _entries.OrderBy(e => e.Value.Order).Select(e => new MissingTranslation(e.Key.Key, MissingTranslation.DomainOf(e.Key.Key), e.Key.Language, e.Value.FellBack, Volatile.Read(ref e.Value.Count)))];

        private sealed class Entry(long order, bool fellBack)
        {
            public long Order { get; } = order;

            public bool FellBack { get; } = fellBack;

            public int Count;
        }
    }
}
