using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Visual;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Visual;

public sealed class GoldenImageV2Tests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-golden-v2-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void Compare_DefaultsToFullImage_AndCountsOverlappingRegionsOnce()
    {
        byte[] expected = Pixels(4, 1, (0, 0, 0, 255));
        byte[] actual = (byte[])expected.Clone();
        Set(actual, 4, 1, 1, (20, 0, 0, 255));
        Set(actual, 4, 1, 2, (40, 0, 0, 255));

        GoldenImageComparison result = GoldenImageComparator.Compare(actual, expected, 4, 1, new()
        {
            Metric = GoldenImageMetric.Rgb,
            LocalTolerance = 0,
            GlobalTolerance = 1d / 3,
            Regions = [new(0, 0, 3, 1), new(1, 0, 3, 1)],
            Masks = [new(2, 0, 1, 1)],
        });

        Assert.True(result.IsMatch); // exactly at global limit
        Assert.Equal(3, result.ComparedPixels); // x=2 masked; overlap is not counted twice
        Assert.Equal(1, result.FailedPixels);
    }

    [Fact]
    public void Compare_UsesFullImageWhenRegionsOmitted_AndLocalEqualityPasses()
    {
        byte[] expected = Pixels(2, 1, (10, 20, 30, 255));
        byte[] actual = (byte[])expected.Clone();
        Set(actual, 2, 1, 0, (61, 20, 30, 255));

        GoldenImageComparison result = GoldenImageComparator.Compare(actual, expected, 2, 1, new()
        {
            Metric = GoldenImageMetric.Rgb,
            LocalTolerance = 0.2, // 51/255 equals the boundary
            GlobalTolerance = 0,
        });

        Assert.True(result.IsMatch);
        Assert.Equal(2, result.ComparedPixels);
        Assert.Equal(0, result.FailedPixels);
    }

    [Fact]
    public void Compare_TransparentRgbIsInvisibleButAlphaIsCompared()
    {
        byte[] expected = Pixels(1, 1, (0, 0, 0, 0));
        byte[] invisibleRed = Pixels(1, 1, (255, 0, 0, 0));
        Assert.Equal(0, GoldenImageComparator.Compare(invisibleRed, expected, 1, 1, new() { LocalTolerance = 0 }).MaxError);

        byte[] visible = Pixels(1, 1, (0, 0, 0, 255));
        Assert.Equal(1, GoldenImageComparator.Compare(visible, expected, 1, 1, new() { LocalTolerance = 0 }).MaxError);
    }

    [Fact]
    public void PerceptualMetricUsesCiede2000AndHeatmapHonorsMasks()
    {
        byte[] a = Pixels(2, 1, (0, 0, 0, 255));
        byte[] b = (byte[])a.Clone();
        Set(b, 2, 1, 1, (255, 255, 255, 255));
        GoldenImageOptions options = new() { LocalTolerance = 0, GlobalTolerance = 0, Masks = [new(1, 0, 1, 1)] };

        GoldenImageComparison result = GoldenImageComparator.Compare(a, b, 2, 1, options);
        byte[] heatmap = GoldenImageComparator.Heatmap(a, b, 2, 1, options);

        Assert.True(result.IsMatch);
        Assert.Equal(1, result.ComparedPixels);
        Assert.Equal(GoldenImageMetric.Perceptual, result.Metric);
        Assert.Equal((byte)0, heatmap[4 + 3]);
    }

    [Theory]
    [InlineData(2.6772, -79.7751, 0, -82.7485, 2.0425)]
    [InlineData(3.1571, -77.2803, 0, -82.7485, 2.8615)]
    [InlineData(2.8361, -74.0200, 0, -82.7485, 3.4412)]
    [InlineData(-1.3802, -84.2814, 0, -82.7485, 1.0000)]
    [InlineData(-0.9009, -85.5211, 0, -82.7485, 1.0000)]
    [InlineData(0, 0, -1, 2, 2.3669)]
    public void Ciede2000_MatchesPublishedReferencePairs(double a1, double b1, double a2, double b2, double expected)
    {
        double difference = GoldenImageComparator.Ciede2000(50, a1, b1, 50, a2, b2);
        Assert.Equal(expected, difference, precision: 4);
    }

    [Fact]
    public void Compare_RejectsEmptyAreaBadRectanglesDimensionsAndTolerances()
    {
        byte[] pixels = Pixels(2, 2, (0, 0, 0, 255));
        Assert.Throws<ArgumentException>(() => GoldenImageComparator.Compare(pixels, pixels, 2, 2, new() { Masks = [new(0, 0, 2, 2)] }));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoldenImageComparator.Compare(pixels, pixels, 2, 2, new() { Regions = [new(1, 1, 2, 1)] }));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoldenImageComparator.Compare(pixels, pixels, 2, 2, new() { LocalTolerance = double.NaN }));
        Assert.Throws<ArgumentOutOfRangeException>(() => GoldenImageComparator.Compare(pixels, pixels, 2, 2, new() { GlobalTolerance = 1.1 }));
        Assert.Throws<ArgumentException>(() => GoldenImageComparator.Compare(pixels, pixels[..^4], 2, 2));

        GoldenImageStore.UpdateOverride = true;
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => GoldenImageMatcher.AssertMatches(
                new FramebufferSnapshot(Pixels(2, 2, (0, 0, 0, 255)), 2, 2),
                "invalid-options",
                new() { GoldenDirectory = _root, Regions = [new(1, 1, 2, 2)] },
                "linux"));
            Assert.False(File.Exists(Path.Combine(_root, "invalid-options.linux.png")));
        }
        finally { GoldenImageStore.UpdateOverride = null; }
    }

    [Fact]
    public void MissingFullyMaskedGoldenIsRejectedBeforeAutoCreate()
    {
        GoldenImageStore.UpdateOverride = true;
        try
        {
            Assert.Throws<ArgumentException>(() => GoldenImageMatcher.AssertMatches(
                new FramebufferSnapshot(Pixels(2, 2, (0, 0, 0, 255)), 2, 2), "empty", new()
                {
                    GoldenDirectory = _root,
                    Masks = [new(0, 0, 2, 2)],
                }, "linux"));
        }
        finally { GoldenImageStore.UpdateOverride = null; }
        Assert.False(File.Exists(Path.Combine(_root, "empty.linux.png")));
    }

    [Fact]
    public void ResolverPrefersExactPlatformThenGenericAndUpdatesExactPlatform()
    {
        string exact = Path.Combine(_root, "lantern.linux.png");
        string generic = Path.Combine(_root, "lantern.png");
        Directory.CreateDirectory(_root);
        File.WriteAllBytes(generic, [1]);

        ResolvedGolden fallback = GoldenImageStore.Resolve("lantern.png", _root, "linux");
        Assert.True(fallback.UsedGenericFallback);
        Assert.Equal(generic, fallback.ComparisonPath);
        Assert.Equal(exact, fallback.UpdatePath);
        Assert.EndsWith(".linux.png", fallback.UpdatePath);

        File.WriteAllBytes(exact, [2]);
        ResolvedGolden specific = GoldenImageStore.Resolve("lantern", _root, "linux");
        Assert.False(specific.UsedGenericFallback);
        Assert.Equal(exact, specific.ComparisonPath);
        Assert.Equal(exact, specific.UpdatePath);
        Assert.Throws<ArgumentException>(() => GoldenImageStore.Resolve("../escape", _root, "linux"));
    }

    [Fact]
    public void UpdateFromGenericFallbackWritesAtomicPlatformFileWithoutChangingGeneric()
    {
        string generic = Path.Combine(_root, "lamp.png");
        byte[] initial = Pixels(1, 1, (1, 2, 3, 255));
        byte[] changed = Pixels(1, 1, (200, 30, 50, 255));
        new FramebufferSnapshot(initial, 1, 1).SaveToPng(generic);
        GoldenImageStore.UpdateOverride = true;
        try
        {
            GoldenImageMatcher.AssertMatches(new FramebufferSnapshot(changed, 1, 1), "lamp", new() { GoldenDirectory = _root }, "linux");
        }
        finally { GoldenImageStore.UpdateOverride = null; }

        Assert.Equal(initial, FramebufferSnapshot.FromFile(generic).RawRgba);
        Assert.Equal(changed, FramebufferSnapshot.FromFile(Path.Combine(_root, "lamp.linux.png")).RawRgba);
        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.TopDirectoryOnly));
    }

    [Fact]
    public void MissingGoldenUpdateCreatesPlatformPngAndNeverDuplicatesExtension()
    {
        GoldenImageStore.UpdateOverride = true;
        try
        {
            GoldenImageMatcher.AssertMatches(new FramebufferSnapshot(Pixels(1, 1, (1, 2, 3, 255)), 1, 1), "new.png", new() { GoldenDirectory = _root }, "windows");
        }
        finally { GoldenImageStore.UpdateOverride = null; }
        Assert.True(File.Exists(Path.Combine(_root, "new.windows.png")));
        Assert.Empty(Directory.GetFiles(_root, "*.png.png", SearchOption.AllDirectories));
    }

    [Fact]
    public void CorruptGoldenIsNotReplacedEvenWhenUpdateIsEnabled()
    {
        Directory.CreateDirectory(_root);
        string path = Path.Combine(_root, "broken.linux.png");
        byte[] corrupt = [10, 20, 30];
        File.WriteAllBytes(path, corrupt);
        GoldenImageStore.UpdateOverride = true;
        try
        {
            Assert.Throws<PharosAssertException>(() => GoldenImageMatcher.AssertMatches(
                new FramebufferSnapshot(Pixels(1, 1, (1, 2, 3, 255)), 1, 1), "broken", new() { GoldenDirectory = _root }, "linux"));
        }
        finally { GoldenImageStore.UpdateOverride = null; }
        Assert.Equal(corrupt, File.ReadAllBytes(path));
    }

    [Fact]
    public void UpdateIoFailureRemainsAFailureAndIsReportedWithActualArtifact()
    {
        string blockingRoot = Path.Combine(_root, "root-is-a-file");
        Directory.CreateDirectory(_root);
        File.WriteAllText(blockingRoot, "block parent directory creation");
        FailureArtifactWriter.RootOverride = _root;
        GoldenImageStore.UpdateOverride = true;
        PharosAssertException error;
        try
        {
            error = Assert.Throws<PharosAssertException>(() => GoldenImageMatcher.AssertMatches(
                new FramebufferSnapshot(Pixels(1, 1, (8, 9, 10, 255)), 1, 1),
                "lamp",
                new() { GoldenDirectory = blockingRoot },
                "linux"));
            Assert.IsAssignableFrom<IOException>(error.InnerException);
            FailureReport report = FailureArtifactWriter.Write(new ScenarioFailure
            {
                DisplayName = "Visual.UpdateIoFailure",
                TestClass = typeof(GoldenImageV2Tests),
                MethodName = "UpdateIoFailure",
                Exception = error,
                Artifacts = FailureArtifacts.None,
            });
            Assert.Contains("golden-lamp-1-actual.png", report.Files);
            Assert.DoesNotContain(report.Files, f => f.Contains("expected", StringComparison.Ordinal));
        }
        finally
        {
            GoldenImageStore.UpdateOverride = null;
            FailureArtifactWriter.RootOverride = null;
        }
    }

    [Fact]
    public void FailureWriterSavesDetachedActualExpectedHeatmapForWrappedAggregate()
    {
        FailureArtifactWriter.RootOverride = _root;
        byte[] actualBytes = Pixels(1, 1, (255, 0, 0, 255));
        FramebufferSnapshot actual = new(actualBytes, 1, 1);
        byte[] expectedBytes = Pixels(1, 1, (0, 0, 0, 255));
        new FramebufferSnapshot(expectedBytes, 1, 1).SaveToPng(Path.Combine(_root, "lamp.linux.png"));
        try
        {
            PharosAssertException comparisonFailure = Assert.Throws<PharosAssertException>(() => GoldenImageMatcher.AssertMatches(
                actual, "lamp", new() { GoldenDirectory = _root, Metric = GoldenImageMetric.Rgb, LocalTolerance = 0, GlobalTolerance = 0 }, "linux"));
            actualBytes[0] = 0; // the artifact capture is a detached copy

            Exception failure = new AggregateException(
                new TimeoutException("SucceedWhenAsync exceeded its tick budget"),
                new ScenarioFailedException(comparisonFailure, "wrapped"));
            FailureReport report = FailureArtifactWriter.Write(new ScenarioFailure
            {
                DisplayName = "Visual.Wrapped",
                TestClass = typeof(GoldenImageV2Tests),
                MethodName = "Wrapped",
                Exception = failure,
                Artifacts = FailureArtifacts.None,
            });

            Assert.Contains("golden-lamp-1-actual.png", report.Files);
            Assert.Contains("golden-lamp-1-expected.png", report.Files);
            Assert.Contains("golden-lamp-1-heatmap.png", report.Files);
            Assert.Equal((byte)255, FramebufferSnapshot.FromFile(Path.Combine(report.Directory, "golden-lamp-1-actual.png")).GetPixel(0, 0).R);
            Assert.Empty(report.Problems);
        }
        finally { FailureArtifactWriter.RootOverride = null; }
    }

    [Fact]
    public async Task SwallowedAndRetriedComparisonsDoNotLeakIntoUnrelatedFailure()
    {
        FailureArtifactWriter.RootOverride = _root;
        string goldens = Path.Combine(_root, "goldens");
        FramebufferSnapshot actual = new(Pixels(1, 1, (9, 8, 7, 255)), 1, 1);
        try
        {
            TimeoutException timeout = await Assert.ThrowsAsync<TimeoutException>(() => ScenarioTickWait.SucceedWhenAsync(
                () => GoldenImageMatcher.AssertMatches(actual, "retry", new() { GoldenDirectory = goldens }, "linux"),
                20, (_, _) => Task.CompletedTask, CancellationToken.None));
            Assert.IsType<PharosAssertException>(timeout.InnerException);
            FailureReport retried = FailureArtifactWriter.Write(new ScenarioFailure
            {
                DisplayName = "Visual.RetriedComparison",
                TestClass = typeof(GoldenImageV2Tests),
                MethodName = "RetriedComparison",
                Exception = timeout,
                Artifacts = FailureArtifacts.None,
            });
            Assert.Contains("golden-retry-1-actual.png", retried.Files);
            FailureReport unrelated = FailureArtifactWriter.Write(new ScenarioFailure
            {
                DisplayName = "Visual.UnrelatedAfterRetry",
                TestClass = typeof(GoldenImageV2Tests),
                MethodName = "UnrelatedAfterRetry",
                Exception = new InvalidOperationException("unrelated failure"),
                Artifacts = FailureArtifacts.None,
            });
            Assert.DoesNotContain(unrelated.Files, f => f.StartsWith("golden-", StringComparison.Ordinal));

            Assert.Throws<PharosAssertException>(() => GoldenImageMatcher.AssertMatches(actual, "caught", new() { GoldenDirectory = goldens }, "linux"));
            FailureReport report = FailureArtifactWriter.Write(new ScenarioFailure
            {
                DisplayName = "Visual.UnrelatedAfterCaught",
                TestClass = typeof(GoldenImageV2Tests),
                MethodName = "UnrelatedAfterCaught",
                Exception = new InvalidOperationException("another unrelated failure"),
                Artifacts = FailureArtifacts.None,
            });
            Assert.DoesNotContain(report.Files, f => f.StartsWith("golden-", StringComparison.Ordinal));
        }
        finally { FailureArtifactWriter.RootOverride = null; }
    }

    [Fact]
    public void MissingGoldenArtifactsContainActualOnly()
    {
        FailureArtifactWriter.RootOverride = _root;
        try
        {
            PharosAssertException missing = Assert.Throws<PharosAssertException>(() => GoldenImageMatcher.AssertMatches(
                new FramebufferSnapshot(Pixels(1, 1, (4, 5, 6, 255)), 1, 1), "absent", new() { GoldenDirectory = Path.Combine(_root, "goldens") }, "linux"));
            FailureReport report = FailureArtifactWriter.Write(new ScenarioFailure
            {
                DisplayName = "Visual.Missing",
                TestClass = typeof(GoldenImageV2Tests),
                MethodName = "Missing",
                Exception = missing,
                Artifacts = FailureArtifacts.None,
            });
            Assert.Contains("golden-absent-1-actual.png", report.Files);
            Assert.DoesNotContain(report.Files, f => f.Contains("expected", StringComparison.Ordinal));
            Assert.DoesNotContain(report.Files, f => f.Contains("heatmap", StringComparison.Ordinal));
        }
        finally { FailureArtifactWriter.RootOverride = null; }
    }

    [Fact]
    public void LongNestedGoldenNameStillWritesFailureImage()
    {
        FailureArtifactWriter.RootOverride = _root;
        try
        {
            string name = string.Join('/', Enumerable.Repeat(new string('a', 70), 5));
            PharosAssertException missing = Assert.Throws<PharosAssertException>(() => GoldenImageMatcher.AssertMatches(
                new FramebufferSnapshot(Pixels(1, 1, (4, 5, 6, 255)), 1, 1), name, new() { GoldenDirectory = Path.Combine(_root, "goldens") }, "linux"));
            FailureReport report = FailureArtifactWriter.Write(new ScenarioFailure
            {
                DisplayName = "Visual.LongName",
                TestClass = typeof(GoldenImageV2Tests),
                MethodName = "LongName",
                Exception = missing,
                Artifacts = FailureArtifacts.None,
            });
            Assert.Contains(report.Files, f => f.StartsWith("golden-", StringComparison.Ordinal) && f.EndsWith("-actual.png", StringComparison.Ordinal));
            Assert.Empty(report.Problems);
        }
        finally { FailureArtifactWriter.RootOverride = null; }
    }

    private static byte[] Pixels(int width, int height, (byte R, byte G, byte B, byte A) color)
    {
        byte[] pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = color.R; pixels[i + 1] = color.G; pixels[i + 2] = color.B; pixels[i + 3] = color.A;
        }
        return pixels;
    }

    private static void Set(byte[] pixels, int width, int height, int x, (byte R, byte G, byte B, byte A) color)
    {
        int i = x * 4;
        pixels[i] = color.R; pixels[i + 1] = color.G; pixels[i + 2] = color.B; pixels[i + 3] = color.A;
    }

    public void Dispose()
    {
        GoldenImageStore.UpdateOverride = null;
        FailureArtifactWriter.RootOverride = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
