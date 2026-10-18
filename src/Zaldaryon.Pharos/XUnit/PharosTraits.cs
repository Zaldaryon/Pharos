namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// The traits Pharos gives tests, for filtering a test run.
/// </summary>
public static class PharosTraits
{
    /// <summary>The trait name Pharos uses.</summary>
    public const string Category = "Category";

    /// <summary>Tests that boot a real client or server.</summary>
    public const string Live = "Live";

    /// <summary>Tests that measure throughput and take long to run.</summary>
    public const string Benchmark = "Benchmark";

    /// <summary>Tests that run game days of a real server, and run only in the nightly full lane.</summary>
    public const string Soak = "Soak";
}
