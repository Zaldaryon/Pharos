using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.UI;

/// <summary>A page of the survival handbook.</summary>
/// <param name="Code">
/// The page's code, which <see cref="HandbookDriver.OpenPageAsync"/> and <c>handbook://</c> links
/// take: a guide's <c>pageCode</c>, or <c>item-</c>/<c>block-</c> and the short code for a stack.
/// </param>
/// <param name="Title">The page's title in the current language.</param>
/// <param name="Category">The handbook category the page is listed under, such as <c>guide</c>, <c>tutorial</c> or <c>stack</c>.</param>
/// <param name="Kind">
/// What the page shows: <c>text</c> for a guide from <c>config/handbook</c>, <c>stack</c> for an
/// item or a block, <c>meal</c> for a cooking recipe, or the page's class name otherwise.
/// </param>
/// <param name="Domain">
/// The mod the page comes from: the domain of the guide's asset, or of the stack's code.
/// </param>
public sealed record HandbookPageInfo(string Code, string Title, string Category, string Kind, string Domain);

/// <summary>
/// Drives the survival handbook of an engine-mode client: opens and closes it with its hotkey,
/// searches it through its search field, opens pages by their code and reads them.
/// </summary>
/// <remarks>
/// <para>
/// The handbook belongs to the survival mod, which Pharos reaches by name: without it,
/// <see cref="IsAvailable"/> is false and the rest throws. The game builds the pages of every
/// item and block on a worker thread after the world loads; every call that reads pages waits for
/// that first (<see cref="WaitUntilLoadedAsync"/>), since the handbook lists nothing until then.
/// </para>
/// <para>
/// <see cref="OpenPageAsync"/> follows a <c>handbook://</c> link the way a click on one does: it
/// calls the link protocol the survival mod registers, which opens the handbook on the page.
/// Searching types into the search field. The handbook does not pause a client joined to a server.
/// </para>
/// </remarks>
public sealed class HandbookDriver
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const string SystemTypeName = "Vintagestory.GameContent.ModSystemSurvivalHandbook";

    private readonly HeadlessClient _client;
    private readonly GuiDriver _gui;
    private Dictionary<string, string>? _guideDomains;

    internal HandbookDriver(HeadlessClient client, GuiDriver gui)
    {
        _client = client;
        _gui = gui;
    }

    /// <summary>Whether the client has a survival handbook: the survival mod is loaded and the world is in.</summary>
    public bool IsAvailable => _client.RunOnClientThread(() => DialogOrNull() != null);

    /// <summary>Whether the handbook is open.</summary>
    public bool IsOpen => _client.RunOnClientThread(() => DialogOrNull()?.IsOpened() == true);

    /// <summary>Whether the game has finished building the handbook's pages.</summary>
    public bool IsLoaded => _client.RunOnClientThread(() => !Loading(RequireDialog()));

    /// <summary>
    /// The code of the page the handbook shows, or null while it is closed or shows its list.
    /// </summary>
    public string? CurrentPageCode => _client.RunOnClientThread(() => CurrentPage(RequireDialog()) is { } page ? Code(page) : null);

    /// <summary>The title of the page the handbook shows, or null while it shows no page.</summary>
    public string? PageTitle => _client.RunOnClientThread(() => CurrentPage(RequireDialog()) is { } page ? Title(page) : null);

    /// <summary>
    /// The text of the page the handbook shows, as its rich text displays it without the markup:
    /// links give their label, item stacks and images nothing. Null while it shows no page.
    /// </summary>
    public string? PageText => _client.RunOnClientThread(() =>
    {
        GuiDialog dialog = RequireDialog();
        if (CurrentPage(dialog) == null) return null;
        return Field<GuiComposer>(dialog, "detailViewGui")?.GetElement("richtext") is GuiElementRichtext rich
            ? GuiDriver.RichText(rich.Components)
            : null;
    });

    /// <summary>
    /// Waits until the game has built the handbook's pages, stepping frames meanwhile, for at most
    /// <paramref name="timeout"/> (60 seconds by default, scaled by <c>PHAROS_TIMEOUT_SCALE</c>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The client has no handbook.</exception>
    /// <exception cref="TimeoutException">The pages were not built in time.</exception>
    public async Task WaitUntilLoadedAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        TimeSpan limit = timeout ?? TimeSpan.FromSeconds(60 * ScenarioTimeouts.Scale);
        Stopwatch watch = Stopwatch.StartNew();
        while (_client.RunOnClientThread(() => Loading(RequireDialog())))
        {
            if (watch.Elapsed > limit) throw new TimeoutException($"The handbook's pages were not built within {limit.TotalSeconds:0} seconds.");
            await _client.StepAsync(ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Opens the handbook with its hotkey, once its pages are built, on its list of pages.
    /// Does nothing when it is open.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client has no handbook, or the hotkey did not open it.</exception>
    public async Task OpenAsync(CancellationToken ct = default)
    {
        await WaitUntilLoadedAsync(ct: ct).ConfigureAwait(false);
        if (IsOpen) return;

        await _client.Hotkeys.TriggerAsync("handbook", ct).ConfigureAwait(false);
        if (!await _gui.StepUntilAsync(() => RequireDialog().IsOpened(), 30, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The handbook hotkey did not open the handbook.");
        }

        // The handbook picks the composer it shows, list or page, when it renders.
        await _client.StepAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Closes the handbook, as its close button does, and steps a frame. Does nothing when it is closed.</summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (!_client.RunOnClientThread(() => RequireDialog() is { } dialog && dialog.IsOpened() && dialog.TryClose())) return;
        await _client.StepAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Types <paramref name="text"/> into the handbook's search field, opening the handbook or
    /// going back to its list first, and returns the pages it lists, best match first. The
    /// "everything" category is selected first by a click on its tab, so the search covers every
    /// page, whichever category the handbook last showed.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client has no handbook.</exception>
    public async Task<IReadOnlyList<HandbookPageInfo>> SearchAsync(string text, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        await OpenAsync(ct).ConfigureAwait(false);

        string dialogName = _client.RunOnClientThread(() => RequireDialog().GetType().Name);
        if (CurrentPageCode != null)
        {
            // The overview button of the page view.
            string overview = _client.RunOnClientThread(() => Lang.Get("handbook-overview"));
            GuiElementInfo button = _gui.Elements(dialogName).FirstOrDefault(e => e.Type == "GuiElementTextButton" && e.Text == overview)
                ?? throw new InvalidOperationException("The handbook's page view has no overview button.");
            _gui.Click(dialogName, button.Key);
            await _gui.StepUntilAsync(() => CurrentPage(RequireDialog()) == null, 10, ct).ConfigureAwait(false);
            await _client.StepAsync(ct).ConfigureAwait(false);
        }

        // The search covers only the category shown.
        if (_client.RunOnClientThread(() => Field<object>(RequireDialog(), "currentCatgoryCode") != null))
        {
            string everything = _client.RunOnClientThread(() => Lang.Get("handbook-category-everything"));
            _gui.ClickTab(dialogName, "verticalTabs", everything);
            await _client.StepAsync(ct).ConfigureAwait(false);
        }

        _gui.ClearInput(dialogName, "searchField");
        _client.Input.TypeText(text);
        await _client.StepAsync(ct).ConfigureAwait(false);

        return _client.RunOnClientThread(() =>
        {
            GuiDialog dialog = RequireDialog();
            return (IReadOnlyList<HandbookPageInfo>)(Field<IList>(dialog, "shownHandbookPages") ?? throw GameChanged())
                .Cast<object>().Select(Info).ToList();
        });
    }

    /// <summary>
    /// Opens the page <paramref name="code"/> the way a click on a <c>handbook://code</c> link
    /// does, opening the handbook first, and waits until the page is shown.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No page has that code; the message lists close ones.</exception>
    /// <exception cref="InvalidOperationException">The client has no handbook, or the page did not show.</exception>
    public async Task OpenPageAsync(string code, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        await WaitUntilLoadedAsync(ct: ct).ConfigureAwait(false);

        _client.RunOnClientThread(() =>
        {
            GuiDialog dialog = RequireDialog();
            Dictionary<string, int> codes = Field<Dictionary<string, int>>(dialog, "pageNumberByPageCode") ?? throw GameChanged();
            if (!codes.ContainsKey(code))
            {
                string[] close = Close(code, codes.Keys);
                throw new KeyNotFoundException($"The handbook has no page '{code}'.{(close.Length > 0 ? $" Close: {string.Join(", ", close)}." : "")}");
            }

            if (!_client.Client.api.LinkProtocols.TryGetValue("handbook", out Action<LinkTextComponent>? protocol))
            {
                throw new InvalidOperationException("No handbook:// link protocol is registered.");
            }

            protocol(new LinkTextComponent("handbook://" + code));
        });

        bool shown = await _gui.StepUntilAsync(() =>
        {
            GuiDialog dialog = RequireDialog();
            return dialog.IsOpened() && CurrentPage(dialog) is { } page && Code(page) == code
                && Field<GuiComposer>(dialog, "detailViewGui")?.GetElement("richtext") != null;
        }, 30, ct).ConfigureAwait(false);
        if (!shown) throw new InvalidOperationException($"The handbook did not show page '{code}'.");
        await _client.StepAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Every page of the handbook, or those from <paramref name="domain"/>, once the pages are built.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client has no handbook.</exception>
    public async Task<IReadOnlyList<HandbookPageInfo>> PagesAsync(string? domain = null, CancellationToken ct = default)
    {
        await WaitUntilLoadedAsync(ct: ct).ConfigureAwait(false);
        return _client.RunOnClientThread(() =>
        {
            GuiDialog dialog = RequireDialog();
            return (IReadOnlyList<HandbookPageInfo>)(Field<IList>(dialog, "allHandbookPages") ?? throw GameChanged())
                .Cast<object>().Select(Info)
                .Where(p => domain == null || string.Equals(p.Domain, domain, StringComparison.OrdinalIgnoreCase))
                .ToList();
        });
    }

    // Client thread.
    private GuiDialog? DialogOrNull()
    {
        ModSystem? system = _client.Client.api?.ModLoader.GetModSystem(SystemTypeName);
        return system?.GetType().GetField("dialog", AnyInstance)?.GetValue(system) as GuiDialog;
    }

    // Client thread.
    private GuiDialog RequireDialog() => DialogOrNull()
        ?? throw new InvalidOperationException("The client has no survival handbook: the survival mod is not loaded, or the world has not finished loading.");

    private static bool Loading(GuiDialog dialog) => Field<object>(dialog, "loadingPagesAsync") as bool? ?? throw GameChanged();

    // The page on top of the browsing history, unless the top is a search or the handbook is closed.
    private static object? CurrentPage(GuiDialog dialog)
    {
        if (!dialog.IsOpened() || Field<IEnumerable>(dialog, "browseHistory") is not { } history) return null;
        object? top = history.Cast<object>().FirstOrDefault();
        if (top == null || top.GetType().GetField("SearchText")?.GetValue(top) != null) return null;
        return top.GetType().GetField("Page")?.GetValue(top);
    }

    private HandbookPageInfo Info(object page)
    {
        string code = Code(page);
        string category = page.GetType().GetProperty("CategoryCode")?.GetValue(page) as string ?? "";
        string kind = page.GetType().Name switch
        {
            "GuiHandbookTextPage" => "text",
            "GuiHandbookItemStackPage" => "stack",
            "GuiHandbookMealRecipePage" => "meal",
            string other => other,
        };

        string domain = kind switch
        {
            "text" => GuideDomains().GetValueOrDefault(code, GlobalConstants.DefaultDomain),
            "stack" => (page.GetType().GetField("Stack")?.GetValue(page) as ItemStack)?.Collectible?.Code?.Domain ?? GlobalConstants.DefaultDomain,
            _ => GlobalConstants.DefaultDomain,
        };

        return new HandbookPageInfo(code, Title(page), category, kind, domain);
    }

    private static string Code(object page) => page.GetType().GetProperty("PageCode")?.GetValue(page) as string ?? "";

    private static string Title(object page)
    {
        if (page.GetType().GetField("Stack")?.GetValue(page) is ItemStack stack) return stack.GetName();
        if (page.GetType().GetField("Title")?.GetValue(page) is string title) return Lang.Get(title);

        // Other pages only keep the lowercase title the search matches against.
        object? text = page.GetType().GetMethod("GetPageText", Type.EmptyTypes)?.Invoke(page, null);
        return text?.GetType().GetField("Title")?.GetValue(text) as string ?? "";
    }

    // Guides do not keep where they came from: their codes are read back from the assets they were made from.
    private Dictionary<string, string> GuideDomains()
    {
        if (_guideDomains != null) return _guideDomains;

        Dictionary<string, string> domains = [];
        foreach (IAsset asset in _client.Client.api.Assets.GetMany("config/handbook"))
        {
            try
            {
                if (JObject.Parse(asset.ToText())["pageCode"]?.Value<string>() is { } code) domains[code] = asset.Location.Domain;
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // The game logs guides it cannot read.
            }
        }

        return _guideDomains = domains;
    }

    // Codes containing the asked one, or contained in it, then those sharing the longest start.
    internal static string[] Close(string code, IEnumerable<string> codes)
    {
        List<string> all = codes.ToList();
        string[] containing = all
            .Where(c => c.Contains(code, StringComparison.OrdinalIgnoreCase) || code.Contains(c, StringComparison.OrdinalIgnoreCase))
            .OrderBy(c => Math.Abs(c.Length - code.Length)).ThenBy(c => c, StringComparer.Ordinal)
            .Take(5).ToArray();
        if (containing.Length > 0) return containing;

        static int Shared(string a, string b)
        {
            int n = 0;
            while (n < a.Length && n < b.Length && char.ToLowerInvariant(a[n]) == char.ToLowerInvariant(b[n])) n++;
            return n;
        }

        return all.Select(c => (Code: c, Shared: Shared(c, code)))
            .Where(c => c.Shared >= Math.Min(4, code.Length))
            .OrderByDescending(c => c.Shared).ThenBy(c => c.Code, StringComparer.Ordinal)
            .Take(5).Select(c => c.Code).ToArray();
    }

    private static T? Field<T>(object target, string name) where T : class
    {
        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            if (type.GetField(name, AnyInstance | BindingFlags.DeclaredOnly) is { } field) return field.GetValue(target) as T;
        }

        return null;
    }

    private static InvalidOperationException GameChanged() =>
        new("Pharos cannot read the survival handbook: the game changed.");
}
