namespace Zaldaryon.Pharos.Visual;

/// <summary>Pure managed region-aware golden image comparison.</summary>
public static class GoldenImageComparator
{
    /// <summary>Compares straight-alpha sRGB snapshots after compositing RGB over black; alpha error is also counted independently.</summary>
    public static GoldenImageComparison Compare(
        byte[] actual,
        byte[] expected,
        int width,
        int height,
        GoldenImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        options ??= new GoldenImageOptions();
        Validate(width, height, actual.Length, expected.Length, options);

        int pixels = checked(width * height);
        bool[] included = new bool[pixels];
        if (options.Regions.Count == 0)
        {
            Array.Fill(included, true);
        }
        else
        {
            foreach (GoldenImageRect region in options.Regions)
                Fill(included, width, region, true);
        }
        foreach (GoldenImageRect mask in options.Masks)
            Fill(included, width, mask, false);

        int compared = 0, failed = 0;
        double sum = 0, max = 0;
        for (int p = 0; p < pixels; p++)
        {
            if (!included[p]) continue;
            compared++;
            double error = PixelError(actual, expected, p * 4, options.Metric);
            sum += error;
            max = Math.Max(max, error);
            if (error > options.LocalTolerance) failed++;
        }

        if (compared == 0)
            throw new ArgumentException("Regions and masks leave no pixels to compare.", nameof(options));

        double fraction = (double)failed / compared;
        return new GoldenImageComparison(
            fraction <= options.GlobalTolerance, compared, failed, fraction,
            sum / compared, max, options.LocalTolerance, options.GlobalTolerance, options.Metric);
    }

    /// <summary>Returns an RGBA heatmap. Included matching pixels are green, local errors are red, and masks are transparent.</summary>
    public static byte[] Heatmap(byte[] actual, byte[] expected, int width, int height, GoldenImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(expected);
        options ??= new GoldenImageOptions();
        Validate(width, height, actual.Length, expected.Length, options);
        bool[] included = BuildIncluded(width, height, options);
        byte[] map = new byte[checked(width * height * 4)];
        for (int p = 0; p < width * height; p++)
        {
            int i = p * 4;
            if (!included[p]) continue;
            double error = PixelError(actual, expected, i, options.Metric);
            byte intensity = (byte)Math.Clamp((int)Math.Round(error * 255 / Math.Max(options.LocalTolerance, 1d / 255)), 0, 255);
            map[i] = intensity;
            map[i + 1] = (byte)(255 - intensity);
            map[i + 2] = 0;
            map[i + 3] = 255;
        }
        return map;
    }

    private static bool[] BuildIncluded(int width, int height, GoldenImageOptions options)
    {
        bool[] included = new bool[checked(width * height)];
        if (options.Regions.Count == 0) Array.Fill(included, true);
        else foreach (GoldenImageRect region in options.Regions) Fill(included, width, region, true);
        foreach (GoldenImageRect mask in options.Masks) Fill(included, width, mask, false);
        return included;
    }

    internal static void ValidateOptions(int width, int height, int pixelLength, GoldenImageOptions options) =>
        Validate(width, height, pixelLength, pixelLength, options);

    private static void Validate(int width, int height, int actualLength, int expectedLength, GoldenImageOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        int bytes;
        try { bytes = checked(checked(width * height) * 4); }
        catch (OverflowException) { throw new ArgumentOutOfRangeException(nameof(width), "Image dimensions are too large."); }
        if (actualLength != bytes) throw new ArgumentException($"Actual RGBA length must be {bytes}, got {actualLength}.", "actual");
        if (expectedLength != bytes) throw new ArgumentException($"Expected RGBA length must be {bytes}, got {expectedLength}.", "expected");
        if (!double.IsFinite(options.LocalTolerance) || options.LocalTolerance < 0 || options.LocalTolerance > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "LocalTolerance must be finite and in [0, 1].");
        if (!double.IsFinite(options.GlobalTolerance) || options.GlobalTolerance < 0 || options.GlobalTolerance > 1)
            throw new ArgumentOutOfRangeException(nameof(options), "GlobalTolerance must be finite and in [0, 1].");
        if (!Enum.IsDefined(options.Metric)) throw new ArgumentOutOfRangeException(nameof(options), "Unknown image metric.");
        if (options.Regions is null || options.Masks is null) throw new ArgumentException("Regions and Masks cannot be null.", nameof(options));
        foreach (GoldenImageRect rect in options.Regions.Concat(options.Masks)) ValidateRect(rect, width, height);
        if (!BuildIncluded(width, height, options).Any(included => included))
            throw new ArgumentException("Regions and masks leave no pixels to compare.", nameof(options));
    }

    private static void ValidateRect(GoldenImageRect r, int width, int height)
    {
        if (r.X < 0 || r.Y < 0 || r.Width <= 0 || r.Height <= 0 ||
            (long)r.X + r.Width > width || (long)r.Y + r.Height > height)
            throw new ArgumentOutOfRangeException(nameof(r), $"Rectangle {r} must have positive area and fit inside {width}×{height}.");
    }

    private static void Fill(bool[] included, int width, GoldenImageRect rect, bool value)
    {
        for (int y = rect.Y; y < rect.Y + rect.Height; y++)
            Array.Fill(included, value, y * width + rect.X, rect.Width);
    }

    private static double PixelError(byte[] a, byte[] b, int i, GoldenImageMetric metric)
    {
        double alpha = Math.Abs(a[i + 3] - b[i + 3]) / 255d;
        double ar = a[i] * a[i + 3] / 255d, ag = a[i + 1] * a[i + 3] / 255d, ab = a[i + 2] * a[i + 3] / 255d;
        double br = b[i] * b[i + 3] / 255d, bg = b[i + 1] * b[i + 3] / 255d, bb = b[i + 2] * b[i + 3] / 255d;
        if (metric == GoldenImageMetric.Rgb)
            return Math.Max(alpha, Math.Max(Math.Abs(ar - br), Math.Max(Math.Abs(ag - bg), Math.Abs(ab - bb))) / 255d);
        Lab x = ToLab(ar, ag, ab);
        Lab y = ToLab(br, bg, bb);
        return Math.Max(alpha, Math.Min(1d, DeltaE2000(x, y) / 100d));
    }

    private readonly record struct Lab(double L, double A, double B);

    internal static double Ciede2000(double l1, double a1, double b1, double l2, double a2, double b2) =>
        DeltaE2000(new Lab(l1, a1, b1), new Lab(l2, a2, b2));

    private static Lab ToLab(double r8, double g8, double b8)
    {
        static double Linear(double c) { double v = c / 255d; return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4); }
        double r = Linear(r8), g = Linear(g8), b = Linear(b8);
        double x = (r * .4124564 + g * .3575761 + b * .1804375) / .95047;
        double y = (r * .2126729 + g * .7151522 + b * .0721750);
        double z = (r * .0193339 + g * .1191920 + b * .9503041) / 1.08883;
        static double F(double t) => t > .008856451679 ? Math.Cbrt(t) : 7.787037037 * t + 16d / 116;
        double fx = F(x), fy = F(y), fz = F(z);
        return new Lab(116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    // CIEDE2000, with the standard kL=kC=kH=1 weighting.
    private static double DeltaE2000(Lab p, Lab q)
    {
        const double rad = Math.PI / 180d;
        double c1 = Math.Sqrt(p.A * p.A + p.B * p.B), c2 = Math.Sqrt(q.A * q.A + q.B * q.B);
        double cBar = (c1 + c2) / 2;
        double c7 = Math.Pow(cBar, 7), twentyFive7 = Math.Pow(25, 7);
        double g = .5 * (1 - Math.Sqrt(c7 / (c7 + twentyFive7)));
        double a1 = (1 + g) * p.A, a2 = (1 + g) * q.A;
        double cp1 = Math.Sqrt(a1 * a1 + p.B * p.B), cp2 = Math.Sqrt(a2 * a2 + q.B * q.B);
        static double Hue(double a, double b) { double h = Math.Atan2(b, a) * 180 / Math.PI; return h < 0 ? h + 360 : h; }
        double h1 = Hue(a1, p.B), h2 = Hue(a2, q.B);
        double dL = q.L - p.L, dC = cp2 - cp1;
        double dh = cp1 * cp2 == 0 ? 0 : h2 - h1;
        if (dh > 180) dh -= 360; else if (dh < -180) dh += 360;
        double dH = 2 * Math.Sqrt(cp1 * cp2) * Math.Sin(dh * rad / 2);
        double lBar = (p.L + q.L) / 2, cpBar = (cp1 + cp2) / 2;
        double hBar = cp1 * cp2 == 0 ? h1 + h2 : Math.Abs(h1 - h2) <= 180 ? (h1 + h2) / 2 : h1 + h2 < 360 ? (h1 + h2 + 360) / 2 : (h1 + h2 - 360) / 2;
        double t = 1 - .17 * Math.Cos((hBar - 30) * rad) + .24 * Math.Cos(2 * hBar * rad) + .32 * Math.Cos((3 * hBar + 6) * rad) - .20 * Math.Cos((4 * hBar - 63) * rad);
        double sl = 1 + .015 * Math.Pow(lBar - 50, 2) / Math.Sqrt(20 + Math.Pow(lBar - 50, 2));
        double sc = 1 + .045 * cpBar, sh = 1 + .015 * cpBar * t;
        double deltaTheta = 30 * Math.Exp(-Math.Pow((hBar - 275) / 25, 2));
        double rc = 2 * Math.Sqrt(Math.Pow(cpBar, 7) / (Math.Pow(cpBar, 7) + twentyFive7));
        double rt = -Math.Sin(2 * deltaTheta * rad) * rc;
        double x = dL / sl, y = dC / sc, z = dH / sh;
        return Math.Sqrt(x * x + y * y + z * z + rt * y * z);
    }
}
