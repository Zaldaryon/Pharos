namespace Zaldaryon.Pharos.Network;

/// <summary>
/// A timeline of degradation profiles. Each step takes effect at its time after the script starts, measured on the link's clock,
/// so a test can plan an outage and its recovery without counting frames.
/// </summary>
public sealed class NetworkScript
{
    private readonly IReadOnlyList<(int AtMs, DegradedNetworkProfile Profile)> _steps;

    private NetworkScript(IReadOnlyList<(int AtMs, DegradedNetworkProfile Profile)> steps)
    {
        _steps = steps;
    }

    /// <summary>A script that starts with <paramref name="profile"/> at once.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="profile"/> is out of range.</exception>
    public static NetworkScript Begin(DegradedNetworkProfile profile) => new([(0, Valid(profile))]);

    /// <summary>
    /// The script with one more step: <paramref name="profile"/> takes effect <paramref name="atMs"/> milliseconds after the script starts.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="atMs"/> is negative.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="profile"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="profile"/> is out of range.</exception>
    public NetworkScript Then(int atMs, DegradedNetworkProfile profile)
    {
        if (atMs < 0) throw new ArgumentOutOfRangeException(nameof(atMs), atMs, "A step cannot take effect before the script starts.");
        return new NetworkScript([.. _steps, (atMs, Valid(profile))]);
    }

    /// <summary>The steps in the order they take effect.</summary>
    internal IReadOnlyList<(int AtMs, DegradedNetworkProfile Profile)> Steps => [.. _steps.OrderBy(s => s.AtMs)];

    private static DegradedNetworkProfile Valid(DegradedNetworkProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsValid) throw new ArgumentException("Profile parameters are out of valid range.", nameof(profile));
        return profile;
    }
}
