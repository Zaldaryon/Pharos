using System.Text;

namespace Zaldaryon.Pharos.Tests.Support;

/// <summary>
/// Random player names for test players. Names are always the longest the server accepts, which the tests observed
/// as 16 characters: a 16-character name joins, and a 17-character name is refused.
/// </summary>
public static class PlayerNames
{
    /// <summary>The longest name the server accepts for a player.</summary>
    public const int MaxLength = 16;

    private const string Letters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly Random Random = new();
    private static readonly object Lock = new();

    /// <summary>A random name of exactly <see cref="MaxLength"/> letters.</summary>
    public static string Next()
    {
        lock (Lock)
        {
            var name = new StringBuilder(MaxLength);
            for (int i = 0; i < MaxLength; i++) name.Append(Letters[Random.Next(Letters.Length)]);
            return name.ToString();
        }
    }
}
