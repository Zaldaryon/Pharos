using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Visual;

/// <summary>Resolves and compares named, optionally platform-specific golden images.</summary>
public static class GoldenImageMatcher
{
    /// <summary>
    /// Asserts that a snapshot matches the named golden. With PHAROS_UPDATE_GOLDENS=1, missing or
    /// mismatching comparisons atomically write the actual snapshot to the platform-specific golden.
    /// </summary>
    public static void AssertMatches(FramebufferSnapshot actual, string name, GoldenImageOptions? options = null)
        => AssertMatches(actual, name, options, GoldenImageStore.CurrentPlatform);

    internal static void AssertMatches(FramebufferSnapshot actual, string name, GoldenImageOptions? options, string platform)
    {
        ArgumentNullException.ThrowIfNull(actual);
        options ??= new GoldenImageOptions();
        byte[] actualPixels = (byte[])actual.RawRgba.Clone();
        GoldenImageComparator.ValidateOptions(actual.Width, actual.Height, actualPixels.Length, options);
        FramebufferSnapshot actualCopy = new(actualPixels, actual.Width, actual.Height);
        ResolvedGolden resolved = GoldenImageStore.Resolve(name, options.GoldenDirectory, platform);
        bool update = GoldenImageStore.UpdateEnabled;

        if (!File.Exists(resolved.ComparisonPath))
        {
            if (update)
            {
                try
                {
                    GoldenImageStore.SaveAtomically(actualCopy, resolved.UpdatePath);
                    return;
                }
                catch (Exception ex)
                {
                    throw Failure(name, $"Golden image '{name}' is missing and could not be created at '{resolved.UpdatePath}'.", actualCopy, null, null, "Golden missing; auto-update failed: " + ex, ex);
                }
            }

            throw Failure(name, $"Golden image '{name}' was not found. Expected '{resolved.ComparisonPath}'. Set {GoldenImageStore.UpdateVariable}=1 to create it.", actualCopy, null, null, "Golden image is missing: " + resolved.ComparisonPath);
        }

        FramebufferSnapshot expected;
        try
        {
            expected = FramebufferSnapshot.FromFile(resolved.ComparisonPath);
        }
        catch (Exception ex)
        {
            throw Failure(name, $"Golden image '{name}' could not be decoded at '{resolved.ComparisonPath}'.", actualCopy, null, null, "Expected golden could not be decoded: " + ex, ex);
        }

        byte[] expectedPixels = (byte[])expected.RawRgba.Clone();
        expected = new FramebufferSnapshot(expectedPixels, expected.Width, expected.Height);
        if (actual.Width != expected.Width || actual.Height != expected.Height)
        {
            string detail = $"Image dimensions differ: actual is {actual.Width}×{actual.Height}, expected is {expected.Width}×{expected.Height}.";
            if (update)
            {
                try { GoldenImageStore.SaveAtomically(actualCopy, resolved.UpdatePath); return; }
                catch (Exception ex)
                {
                    throw Failure(name, $"Golden comparison failed for '{name}': {detail} Auto-update failed.", actualCopy, expected, null, detail + " Auto-update failed: " + ex, ex);
                }
            }
            throw Failure(name, $"Golden image comparison failed for '{name}'. {detail}", actualCopy, expected, null, detail);
        }

        GoldenImageComparison comparison;
        byte[] heatmap;
        try
        {
            comparison = GoldenImageComparator.Compare(actualPixels, expectedPixels, actual.Width, actual.Height, options);
            if (comparison.IsMatch) return;
            heatmap = GoldenImageComparator.Heatmap(actualPixels, expectedPixels, actual.Width, actual.Height, options);
        }
        catch (ArgumentException) { throw; }
        catch (Exception ex)
        {
            throw Failure(name, $"Golden comparison for '{name}' could not be completed.", actualCopy, expected, null, "Golden comparison could not be completed: " + ex, ex);
        }

        string report = $"Metric={comparison.Metric}; failed pixels={comparison.FailedPixels}/{comparison.ComparedPixels} ({comparison.FailedFraction:P3}); " +
            $"mean error={comparison.MeanError:F5}; max error={comparison.MaxError:F5}; " +
            $"local tolerance={comparison.LocalTolerance:F5}; global tolerance={comparison.GlobalTolerance:F5}.";
        if (update)
        {
            try { GoldenImageStore.SaveAtomically(actualCopy, resolved.UpdatePath); return; }
            catch (Exception ex)
            {
                throw Failure(name, $"Golden image comparison failed for '{name}'. {report} Auto-update failed at '{resolved.UpdatePath}'.", actualCopy, expected, heatmap, report + " Auto-update failed: " + ex, ex);
            }
        }

        throw Failure(name, $"Golden image comparison failed for '{name}'. {report}", actualCopy, expected, heatmap, report);
    }

    private static PharosAssertException Failure(string name, string message, FramebufferSnapshot actual, FramebufferSnapshot? expected, byte[]? heatmap, string detail, Exception? inner = null)
    {
        PharosAssertException exception = inner == null ? new PharosAssertException(message) : new PharosAssertException(message, inner);
        return GoldenImageFailureArtifacts.Attach(exception, new GoldenImageFailureCapture(
            name, new FramebufferSnapshot((byte[])actual.RawRgba.Clone(), actual.Width, actual.Height),
            expected is null ? null : new FramebufferSnapshot((byte[])expected.RawRgba.Clone(), expected.Width, expected.Height),
            heatmap is null ? null : (byte[])heatmap.Clone(), detail));
    }
}
