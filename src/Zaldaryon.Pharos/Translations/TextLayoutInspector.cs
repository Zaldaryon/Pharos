using System.Globalization;
using System.Reflection;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.UI;
using GameLang = Vintagestory.API.Config.Lang;

namespace Zaldaryon.Pharos.Translations;

/// <summary>
/// Pure layout measurement of a single text-bearing GUI element.
/// </summary>
internal readonly record struct TextElementMetrics(
    string Dialog,
    string Composer,
    string Key,
    string Type,
    string Text,
    double AvailableWidth,
    double AvailableHeight,
    double MeasuredWidth,
    double MeasuredHeight,
    EnumTextOrientation Orientation,
    bool HasRtlFlag);

/// <summary>Result of checking RTL alignment and opt-in flags on a dialog.</summary>
internal sealed record RtlEvaluationResult(
    IReadOnlyList<string> Failures,
    IReadOnlyList<LanguageLayoutWarning> Warnings);

/// <summary>
/// Measures text extents against GUI element bounds using Vintage Story's <see cref="TextDrawUtil"/>
/// and <see cref="CairoFont"/> on the client thread, and evaluates overflow and RTL alignment.
/// </summary>
internal static class TextLayoutInspector
{
    private static readonly FieldInfo? s_interactive = typeof(GuiComposer).GetField("interactiveElements", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_static = typeof(GuiComposer).GetField("staticElements", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_rightPadding = typeof(GuiElementTextBase).GetField("RightPadding", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_staticOrientation = typeof(GuiElementStaticText).GetField("orientation", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_dynamicOrientation = typeof(GuiElementDynamicText).GetField("orientation", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? s_buttonNormalText = typeof(GuiElementTextButton).GetField("normalText", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Captures measured text extents and orientations for every visible, non-clipped text element
    /// of the open dialog <paramref name="dialogNameOrType"/>.
    /// </summary>
    public static (string Language, IReadOnlyList<TextElementMetrics> Elements) InspectDialog(HeadlessClient client, string dialogNameOrType)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(dialogNameOrType);

        GuiDriver ui = client.Ui ?? throw new InvalidOperationException("The client has no GUI driver: it is not in engine mode.");
        return client.RunOnClientThread(() =>
        {
            GuiDialog dialog = ui.RequireDialog(dialogNameOrType);
            string language = GameLang.CurrentLocale;
            List<TextElementMetrics> results = [];
            HashSet<(string Composer, string Key)> seen = [];

            foreach (KeyValuePair<string, GuiComposer> entry in ((IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers).ToList())
            {
                (string composerName, GuiComposer composer) = (entry.Key, entry.Value);
                Dictionary<string, GuiElement>? interactive = s_interactive?.GetValue(composer) as Dictionary<string, GuiElement>;
                Dictionary<string, GuiElement>? statics = s_static?.GetValue(composer) as Dictionary<string, GuiElement>;

                HashSet<ElementBounds> clips = [];
                foreach (GuiElement element in (statics?.Values ?? Enumerable.Empty<GuiElement>()).Concat(interactive?.Values ?? Enumerable.Empty<GuiElement>()))
                {
                    if (IsClip(element)) clips.Add(element.Bounds);
                }

                Collect(results, seen, dialog.GetType().Name, composerName, interactive, clips);
                Collect(results, seen, dialog.GetType().Name, composerName, statics, clips);
            }

            return (language, (IReadOnlyList<TextElementMetrics>)results);
        });
    }

    private static void Collect(
        List<TextElementMetrics> into,
        HashSet<(string Composer, string Key)> seen,
        string dialogName,
        string composerName,
        Dictionary<string, GuiElement>? elements,
        HashSet<ElementBounds> clips)
    {
        if (elements == null) return;

        foreach ((string key, GuiElement element) in elements)
        {
            if (!seen.Add((composerName, key))) continue;
            if (IsClip(element) || InClip(element.Bounds, clips)) continue;
            if (element.Bounds.InnerWidth <= 0 || element.Bounds.InnerHeight <= 0) continue;

            if (MeasureElement(dialogName, composerName, key, element) is { } metrics)
            {
                into.Add(metrics);
            }
        }
    }

    // Must run on the client thread: CairoFont.FontMeasuringContext is static and not thread-safe.
    private static TextElementMetrics? MeasureElement(string dialogName, string composerName, string key, GuiElement element)
    {
        switch (element)
        {
            case GuiElementTextButton button:
            {
                if (s_buttonNormalText?.GetValue(button) is not GuiElementStaticText normalText) return null;
                string? text = normalText.GetText();
                if (string.IsNullOrWhiteSpace(text) || normalText.Font == null) return null;

                double width = Math.Max(normalText.Bounds.InnerWidth, button.Bounds.InnerWidth);
                double height = Math.Max(normalText.Bounds.InnerHeight, button.Bounds.InnerHeight);
                (double measuredW, double measuredH) = MeasureMultiline(normalText.textUtil, normalText.Font, text, width);
                EnumTextOrientation orientation = ReadOrientation(normalText, normalText.Font);
                return new TextElementMetrics(dialogName, composerName, key, element.GetType().Name, text, width, height, measuredW, measuredH, orientation, ReadRtlFlag(button) || ReadRtlFlag(normalText) || ReadRtlFlag(normalText.Font));
            }
            case GuiElementDialogTitleBar titleBar:
            {
                string? text = titleBar.GetText();
                if (string.IsNullOrWhiteSpace(text) || titleBar.Font == null) return null;

                double leftOffset = GuiElement.scaled(GuiStyle.ElementToDialogPadding);
                double iconsReserve = GuiElement.scaled(GuiElementDialogTitleBar.unscaledCloseIconSize * 2 + 34);
                double availableW = Math.Max(1.0, titleBar.Bounds.InnerWidth - leftOffset - iconsReserve);
                double availableH = titleBar.Bounds.InnerHeight;
                TextExtents extents = titleBar.Font.GetTextExtents(text);
                double lineHeight = titleBar.textUtil.GetLineHeight(titleBar.Font);
                return new TextElementMetrics(dialogName, composerName, key, element.GetType().Name, text, availableW, availableH, extents.Width, lineHeight, titleBar.Font.Orientation, ReadRtlFlag(titleBar) || ReadRtlFlag(titleBar.Font));
            }
            case GuiElementToggleButton toggle:
            {
                string? text = toggle.GetText();
                if (string.IsNullOrWhiteSpace(text) || toggle.Font == null) return null;

                double iconReserve = string.IsNullOrEmpty(toggle.icon) ? 0.0 : toggle.Bounds.InnerHeight;
                double availableW = Math.Max(1.0, toggle.Bounds.InnerWidth - iconReserve);
                (double measuredW, double measuredH) = MeasureMultiline(toggle.textUtil, toggle.Font, text, availableW);
                return new TextElementMetrics(dialogName, composerName, key, element.GetType().Name, text, availableW, toggle.Bounds.InnerHeight, measuredW, measuredH, EnumTextOrientation.Center, ReadRtlFlag(toggle) || ReadRtlFlag(toggle.Font));
            }
            case GuiElementEditableTextBase:
                // Editable inputs hold user-entered values and scroll internally; skip by default.
                return null;
            case GuiElementTextBase textBase:
            {
                string? text = textBase.GetText();
                if (string.IsNullOrWhiteSpace(text) || textBase.Font == null) return null;

                float rightPadding = s_rightPadding?.GetValue(textBase) is float rp ? rp : 0f;
                double availableW = Math.Max(1.0, textBase.Bounds.InnerWidth - rightPadding);
                (double measuredW, double measuredH) = MeasureMultiline(textBase.textUtil, textBase.Font, text, availableW);
                EnumTextOrientation orientation = ReadOrientation(textBase, textBase.Font);
                return new TextElementMetrics(dialogName, composerName, key, element.GetType().Name, text, availableW, textBase.Bounds.InnerHeight, measuredW, measuredH, orientation, ReadRtlFlag(textBase) || ReadRtlFlag(textBase.Font));
            }
            case GuiElementRichtext rich:
            {
                string text = GuiDriver.RichText(rich.Components);
                if (string.IsNullOrWhiteSpace(text)) return null;

                RichTextComponent? firstText = rich.Components?.OfType<RichTextComponent>().FirstOrDefault();
                EnumTextOrientation orientation = firstText?.Font?.Orientation ?? EnumTextOrientation.Left;
                bool hasRtl = ReadRtlFlag(rich) || (firstText?.Font != null && ReadRtlFlag(firstText.Font));
                return new TextElementMetrics(dialogName, composerName, key, element.GetType().Name, text, rich.Bounds.InnerWidth, rich.Bounds.InnerHeight, rich.MaxLineWidth, rich.TotalHeight, orientation, hasRtl);
            }
            default:
                return null;
        }
    }

    private static (double Width, double Height) MeasureMultiline(TextDrawUtil util, CairoFont font, string text, double boxWidth)
    {
        TextLine[] lines = util.Lineize(font, text, boxWidth);
        double maxLineWidth = lines.Length == 0 ? 0.0 : lines.Max(l => l.Bounds.Width);
        double totalHeight = lines.Length * util.GetLineHeight(font);
        return (maxLineWidth, totalHeight);
    }

    private static EnumTextOrientation ReadOrientation(GuiElement element, CairoFont font) => element switch
    {
        GuiElementStaticText staticText when s_staticOrientation?.GetValue(staticText) is EnumTextOrientation o => o,
        GuiElementDynamicText dynamicText when s_dynamicOrientation?.GetValue(dynamicText) is EnumTextOrientation o => o,
        _ => font.Orientation,
    };

    private static bool ReadRtlFlag(object target)
    {
        Type type = target.GetType();
        PropertyInfo? prop = type.GetProperty("RightToLeft", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? type.GetProperty("IsRtl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop?.PropertyType == typeof(bool) && prop.GetValue(target) is true) return true;

        FieldInfo? field = type.GetField("RightToLeft", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? type.GetField("rightToLeft", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? type.GetField("IsRtl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.FieldType == typeof(bool) && field.GetValue(target) is true;
    }

    private static bool IsClip(GuiElement element) => element.GetType().Name == "GuiElementClip";

    private static bool InClip(ElementBounds bounds, HashSet<ElementBounds> clips)
    {
        if (clips.Count == 0) return false;
        for (ElementBounds? parent = bounds.ParentBounds; parent != null && parent != parent.ParentBounds; parent = parent.ParentBounds)
        {
            if (clips.Contains(parent)) return true;
        }

        return false;
    }

    /// <summary>Evaluates whether a measured text element overflows its bounds beyond <paramref name="tolerance"/>.</summary>
    internal static TextOverflowEntry? EvaluateOverflow(in TextElementMetrics element, string language, double tolerance)
    {
        double overflowX = Math.Max(0.0, element.MeasuredWidth - element.AvailableWidth);
        double overflowY = Math.Max(0.0, element.MeasuredHeight - element.AvailableHeight);
        if (overflowX <= tolerance && overflowY <= tolerance) return null;

        string preview = element.Text.Length <= 60 ? element.Text : element.Text[..57] + "...";
        return new TextOverflowEntry(
            element.Dialog,
            element.Composer,
            element.Key,
            element.Type,
            language,
            overflowX > tolerance ? overflowX : 0.0,
            overflowY > tolerance ? overflowY : 0.0,
            preview.Replace('\n', ' ').Replace('\r', ' '));
    }

    /// <summary>
    /// Evaluates right-to-left rendering alignment and RTL opt-in flags for <paramref name="elements"/>.
    /// When the dialog has opted in (or <paramref name="strict"/> is true), left-aligned elements fail;
    /// otherwise they are returned as warnings.
    /// </summary>
    internal static RtlEvaluationResult EvaluateRtl(IReadOnlyList<TextElementMetrics> elements, string language, bool strict)
    {
        if (!LanguageCatalog.IsRightToLeft(language) || elements.Count == 0)
        {
            return new RtlEvaluationResult([], []);
        }

        bool localeHasRtlFlag = GameLang.AvailableLanguages.TryGetValue(language, out ITranslationService? svc) && ReadRtlFlag(svc);
        bool anyElementOptedIn = localeHasRtlFlag || elements.Any(e => e.Orientation == EnumTextOrientation.Right || e.HasRtlFlag);
        bool anyRtlFlagUsed = localeHasRtlFlag || elements.Any(e => e.HasRtlFlag);

        List<string> failures = [];
        List<LanguageLayoutWarning> warnings = [];

        foreach (TextElementMetrics element in elements)
        {
            if (element.Orientation == EnumTextOrientation.Left)
            {
                string detail = $"'{element.Key}' ({element.Type}) in '{element.Composer}' uses Left alignment in RTL language '{language}'";
                if (strict || anyElementOptedIn)
                {
                    failures.Add(detail);
                }
                else
                {
                    warnings.Add(new LanguageLayoutWarning(element.Dialog, element.Composer, element.Key, element.Type, language, detail));
                }
            }
            else if (anyRtlFlagUsed && !element.HasRtlFlag && element.Orientation == EnumTextOrientation.Right)
            {
                string detail = $"'{element.Key}' ({element.Type}) in '{element.Composer}' is Right-aligned without the RTL flag in '{language}'";
                if (strict)
                {
                    failures.Add(detail);
                }
                else
                {
                    warnings.Add(new LanguageLayoutWarning(element.Dialog, element.Composer, element.Key, element.Type, language, detail));
                }
            }
        }

        return new RtlEvaluationResult(failures, warnings);
    }

    internal static string FormatOverflowMessage(string dialogName, string language, IReadOnlyList<TextOverflowEntry> overflows)
    {
        IEnumerable<string> lines = overflows.Take(30).Select(o =>
        {
            List<string> axes = [];
            if (o.HorizontalOverflowPx > 0) axes.Add(string.Create(CultureInfo.InvariantCulture, $"{o.HorizontalOverflowPx:0.#} px horizontally"));
            if (o.VerticalOverflowPx > 0) axes.Add(string.Create(CultureInfo.InvariantCulture, $"{o.VerticalOverflowPx:0.#} px vertically"));
            return $"  {o.ElementType} '{o.ElementKey}' in '{o.Composer}' ({o.Language}) overflows by {string.Join(" and ", axes)} (\"{o.Text}\")";
        });

        string extra = overflows.Count > 30 ? $"\n  ... and {overflows.Count - 30} more" : "";
        return $"Dialog '{dialogName}' has {overflows.Count} text overflow(s) in '{language}':\n{string.Join("\n", lines)}{extra}";
    }
}
