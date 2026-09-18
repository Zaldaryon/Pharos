using Zaldaryon.Pharos.XUnit;

namespace Atlas.Api;

/// <summary>
/// Adapter that wraps <see cref="IServerTestPlayer"/> to implement <see cref="ITestPlayer"/>.
/// </summary>
[Obsolete("Use IServerTestPlayer from Zaldaryon.Pharos.XUnit instead. This shim exists only for migration.")]
public sealed class TestPlayerAdapter : ITestPlayer
{
    private readonly IServerTestPlayer _inner;

    /// <summary>
    /// Initializes a new test player adapter wrapping a Pharos server test player.
    /// </summary>
    /// <param name="inner">The Pharos server test player to wrap.</param>
    public TestPlayerAdapter(IServerTestPlayer inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    /// <inheritdoc />
    public IServerTestPlayer Inner => _inner;

    /// <inheritdoc />
    public string PlayerUID => _inner.PlayerUID;

    /// <inheritdoc />
    public string RoleCode
    {
        get => _inner.RoleCode;
        set => _inner.RoleCode = value;
    }

    /// <inheritdoc />
    public void GrantPrivilege(params string[] privileges) => _inner.GrantPrivilege(privileges);

    /// <inheritdoc />
    public void RevokePrivilege(params string[] privileges) => _inner.RevokePrivilege(privileges);

    /// <inheritdoc />
    public Task TeleportTo(double x, double y, double z) => _inner.TeleportTo(x, y, z);

    /// <inheritdoc />
    public void GiveItem(string itemCode, int quantity = 1) => _inner.GiveItem(itemCode, quantity);

    /// <inheritdoc />
    public bool HasItem(string itemCode, int quantity = 1) => _inner.HasItem(itemCode, quantity);
}

/// <summary>
/// Extension methods for converting between Atlas and Pharos test player types.
/// </summary>
[Obsolete("Use IServerTestPlayer from Zaldaryon.Pharos.XUnit instead. This shim exists only for migration.")]
public static class TestPlayerExtensions
{
    /// <summary>
    /// Wraps a Pharos server test player as an Atlas test player.
    /// </summary>
    /// <param name="player">The Pharos server test player to wrap.</param>
    /// <returns>An Atlas-compatible test player adapter.</returns>
    public static ITestPlayer AsAtlasPlayer(this IServerTestPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new TestPlayerAdapter(player);
    }

    /// <summary>
    /// Unwraps an Atlas test player to its underlying Pharos server test player.
    /// </summary>
    /// <param name="player">The Atlas test player to unwrap.</param>
    /// <returns>The underlying Pharos server test player.</returns>
    /// <exception cref="InvalidCastException">The player is not a TestPlayerAdapter.</exception>
    public static IServerTestPlayer AsPharosPlayer(this ITestPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player is TestPlayerAdapter adapter)
        {
            return adapter.Inner;
        }

        throw new InvalidCastException(
            $"Cannot unwrap {player.GetType().Name} to IServerTestPlayer. " +
            "Only TestPlayerAdapter instances created from AsAtlasPlayer() can be unwrapped.");
    }
}
