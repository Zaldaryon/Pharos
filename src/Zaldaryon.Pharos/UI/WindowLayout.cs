using System.Globalization;
using Xunit.Abstractions;

namespace Zaldaryon.Pharos.UI;

/// <summary>A window size and a GUI scale to lay the client's GUI out at.</summary>
public sealed record WindowLayout : IXunitSerializable
{
    /// <summary>A layout of the given size and scale.</summary>
    public WindowLayout(int width, int height, float guiScale = 1f)
    {
        Width = width;
        Height = height;
        GuiScale = guiScale;
    }

    /// <summary>For xUnit, which rebuilds theory rows from their serialized form.</summary>
    [Obsolete("For xUnit's serialization only.")]
    public WindowLayout()
    {
    }

    /// <summary>The window's width in pixels; at least 600, the game's minimum.</summary>
    public int Width { get; private set; }

    /// <summary>The window's height in pixels; at least 400.</summary>
    public int Height { get; private set; }

    /// <summary>The GUI scale, as the game's <c>guiScale</c> setting.</summary>
    public float GuiScale { get; private set; } = 1f;

    /// <summary>This layout at another GUI scale.</summary>
    public WindowLayout WithGuiScale(float guiScale) => new(Width, Height, guiScale);

    void IXunitSerializable.Serialize(IXunitSerializationInfo info)
    {
        info.AddValue(nameof(Width), Width);
        info.AddValue(nameof(Height), Height);
        info.AddValue(nameof(GuiScale), GuiScale);
    }

    void IXunitSerializable.Deserialize(IXunitSerializationInfo info)
    {
        Width = info.GetValue<int>(nameof(Width));
        Height = info.GetValue<int>(nameof(Height));
        GuiScale = info.GetValue<float>(nameof(GuiScale));
    }

    /// <summary>The smallest window the game allows.</summary>
    public const int MinWidth = 600, MinHeight = 400;

    /// <summary>A size such as "1280x720", optionally with a GUI scale: "1280x720@1.5".</summary>
    /// <exception cref="FormatException">The text is not such a size.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The size is below the game's minimum, or the scale is not positive.</exception>
    public static WindowLayout Parse(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        string[] scaleParts = text.Trim().Split('@');
        string[] sizeParts = scaleParts[0].Split('x', 'X');
        if (scaleParts.Length > 2 || sizeParts.Length != 2
            || !int.TryParse(sizeParts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int width)
            || !int.TryParse(sizeParts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int height))
        {
            throw new FormatException($"'{text}' is not a window size such as 1280x720 or 1280x720@1.5.");
        }

        float scale = 1f;
        if (scaleParts.Length == 2 && !float.TryParse(scaleParts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out scale))
        {
            throw new FormatException($"'{text}' has no GUI scale after '@'.");
        }

        return Checked(new WindowLayout(width, height, scale));
    }

    /// <summary>Throws when the layout cannot be applied.</summary>
    public static WindowLayout Checked(WindowLayout layout)
    {
        if (layout.Width < MinWidth || layout.Height < MinHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(layout), $"{layout.Width}x{layout.Height} is below the game's smallest window, {MinWidth}x{MinHeight}.");
        }

        if (!(layout.GuiScale > 0)) throw new ArgumentOutOfRangeException(nameof(layout), $"A GUI scale must be positive, not {layout.GuiScale}.");
        return layout;
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Width}x{Height}@{GuiScale}");
}
