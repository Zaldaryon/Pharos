using Vintagestory.API.Config;
using Zaldaryon.Pharos.Core;
using GameLang = Vintagestory.API.Config.Lang;

namespace Zaldaryon.Pharos.Translations;

/// <summary>
/// An engine-mode client's translations: the lookups that found no entry, and the language it shows.
/// </summary>
/// <remarks>
/// <para>
/// The game keeps one set of translations and one current language per process, so a server
/// booted in the same process shares them: <see cref="Use"/> switches the server's language too.
/// </para>
/// <para>
/// <see cref="MissingKeys"/> lists the lookups made on the client's own thread, from its boot or
/// the last <see cref="Reset"/>; the scenario base classes reset it before each test. The game's
/// translation of the status line of a command Pharos runs is left out. The game also looks up
/// some plain English sentences as keys, so filter by your mod's domain with
/// <see cref="MissingKeysOf"/>.
/// </para>
/// </remarks>
public sealed class LanguageDriver : IDisposable
{
    private readonly HeadlessClient _client;
    private readonly TranslationCapture.Sink _sink;
    private readonly string _bootLanguage;
    private readonly List<Restore> _uses = [];

    internal LanguageDriver(HeadlessClient client, Func<bool> onClientThread)
    {
        _client = client;
        _sink = TranslationCapture.Open(onClientThread);
        _bootLanguage = Vintagestory.Client.NoObf.ClientSettings.Language ?? GameLang.DefaultLocale;
    }

    /// <summary>The language the game shows now.</summary>
    public string Current => GameLang.CurrentLocale;

    /// <summary>
    /// The lookups that found no entry in their language: the game showed the key itself. Each
    /// key and language once, in the order first seen.
    /// </summary>
    public IReadOnlyList<MissingTranslation> MissingKeys => [.. _sink.Snapshot().Where(m => !m.FellBackToDefault)];

    /// <summary>
    /// Every lookup that found no entry in its language, also the ones the game showed in the
    /// default language (English) instead: what a translation lacks.
    /// </summary>
    public IReadOnlyList<MissingTranslation> MissingKeysIncludingFallbacks => _sink.Snapshot();

    /// <summary>The <see cref="MissingKeys"/> of one domain, such as your mod's id.</summary>
    public IReadOnlyList<MissingTranslation> MissingKeysOf(string domain, bool includeFallbacks = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        return [.. (includeFallbacks ? MissingKeysIncludingFallbacks : MissingKeys).Where(m => m.Domain == domain)];
    }

    /// <summary>Forgets the lookups so far.</summary>
    public void Reset() => _sink.Clear();

    /// <summary>
    /// Shows <paramref name="language"/> until the result is disposed, or until the test ends in a
    /// scenario class. Dialogs already open keep the text they were composed with.
    /// </summary>
    /// <param name="language">A language code the game has, such as "de" or "pt-br".</param>
    /// <exception cref="ArgumentException">The game has no such language.</exception>
    public IDisposable Use(string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        return _client.RunOnClientThread<IDisposable>(() =>
        {
            if (!GameLang.AvailableLanguages.TryGetValue(language, out ITranslationService? service))
            {
                throw new ArgumentException($"The game has no language '{language}'. It has: {string.Join(", ", GameLang.AvailableLanguages.Keys.Order())}.", nameof(language));
            }

            // A language loads lazily, through the asset manager it was last given: the client's,
            // so mods' translations are in it.
            service.UseAssetManager(_client.Client.Platform.AssetManager);
            service.Load(lazyload: false);
            Restore restore = new(this, GameLang.CurrentLocale, language);
            _uses.Add(restore);
            GameLang.ChangeLanguage(language);
            Active = language;
            return restore;
        });
    }

    /// <summary>The language <see cref="Use"/> set last and has not restored, or null.</summary>
    internal string? Active { get; private set; }

    /// <summary>Puts back the language the client booted with, as at the end of a test.</summary>
    internal void RestoreBootLanguage()
    {
        foreach (Restore use in _uses) use.Forget();
        _uses.Clear();
        Active = null;
        if (GameLang.CurrentLocale != _bootLanguage) GameLang.ChangeLanguage(_bootLanguage);
    }

    /// <summary>After the game reloaded its translations: the other languages load again when next used, and a language <see cref="Use"/> set is shown again.</summary>
    internal void AfterReload()
    {
        foreach ((string code, ITranslationService service) in GameLang.AvailableLanguages)
        {
            if (code != GameLang.CurrentLocale) service.Invalidate();
        }

        if (Active != null) GameLang.ChangeLanguage(Active);
    }

    public void Dispose() => TranslationCapture.Close(_sink);

    // Puts back the language shown before its Use. Disposed out of order, it leaves the language a
    // later Use shows alone; after the test that made it has ended, it does nothing.
    private sealed class Restore(LanguageDriver driver, string previous, string language) : IDisposable
    {
        private bool _done;

        public void Forget() => _done = true;

        public void Dispose()
        {
            if (_done) return;
            driver._client.RunOnClientThread(() =>
            {
                if (_done) return;
                _done = true;
                int index = driver._uses.IndexOf(this);
                if (index < 0) return;
                driver._uses.RemoveAt(index);
                if (index < driver._uses.Count)
                {
                    // A later Use is still on: it goes back to what this one replaced.
                    driver._uses[index].Previous = previous;
                    return;
                }

                GameLang.ChangeLanguage(previous);
                driver.Active = driver._uses.Count == 0 ? null : driver._uses[^1].Language;
            });
        }

        public string Previous { get; set; } = previous;

        public string Language { get; } = language;
    }
}
