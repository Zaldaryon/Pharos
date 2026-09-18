namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Pure-math helper to determine chiseled block LOD level from camera distance.
/// LOD thresholds: LOD0 &lt; 32, LOD1 &lt; 64, LOD2 &lt; 128, LOD3 &gt;= 128.
/// These are Pharos testing defaults and may differ from actual VS values.
/// </summary>
public static class ChiseledBlockLodInspector
{
    /// <summary>Distance threshold for LOD0 to LOD1 transition.</summary>
    public const float Lod0Threshold = 32f;

    /// <summary>Distance threshold for LOD1 to LOD2 transition.</summary>
    public const float Lod1Threshold = 64f;

    /// <summary>Distance threshold for LOD2 to LOD3 transition.</summary>
    public const float Lod2Threshold = 128f;

    /// <summary>
    /// Returns the LOD level (0-3) for a given camera-to-block distance.
    /// </summary>
    /// <param name="distance">Euclidean distance from camera to block center.</param>
    /// <returns>LOD level: 0 (highest detail) to 3 (lowest detail).</returns>
    public static int GetLodLevel(float distance)
    {
        if (distance < 0f) distance = 0f; // Clamp negative

        return distance switch
        {
            < Lod0Threshold => 0,
            < Lod1Threshold => 1,
            < Lod2Threshold => 2,
            _ => 3,
        };
    }

    /// <summary>
    /// Returns the minimum distance at which a given LOD level becomes active.
    /// </summary>
    /// <param name="lodLevel">LOD level (0-3).</param>
    /// <returns>Minimum distance for the specified LOD level.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when lodLevel is not 0-3.</exception>
    public static float GetThresholdForLevel(int lodLevel) => lodLevel switch
    {
        0 => 0f,
        1 => Lod0Threshold,
        2 => Lod1Threshold,
        3 => Lod2Threshold,
        _ => throw new ArgumentOutOfRangeException(nameof(lodLevel), "LOD level must be 0-3"),
    };

    /// <summary>
    /// Returns true if distance is within the hysteresis band for any LOD transition.
    /// Useful for testing edge-case distances.
    /// </summary>
    /// <param name="distance">Distance to check.</param>
    /// <param name="hysteresis">Hysteresis band width (default: 1f).</param>
    /// <returns>True if near a threshold boundary.</returns>
    public static bool IsNearThreshold(float distance, float hysteresis = 1f)
    {
        return Math.Abs(distance - Lod0Threshold) < hysteresis ||
               Math.Abs(distance - Lod1Threshold) < hysteresis ||
               Math.Abs(distance - Lod2Threshold) < hysteresis;
    }
}
