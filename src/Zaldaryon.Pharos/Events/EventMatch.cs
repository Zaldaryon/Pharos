namespace Zaldaryon.Pharos.Events;

/// <summary>
/// What one expected record must be, for <see cref="Assertions.PharosAssert.EventOrder(GameEventLog, EventMatch, EventMatch[])"/>:
/// a record type, with or without a condition. A <see cref="Type"/> converts to one.
/// </summary>
public sealed class EventMatch
{
    private readonly Func<GameEventRecord, bool> _test;

    private EventMatch(string description, Func<GameEventRecord, bool> test)
    {
        Description = description;
        _test = test;
    }

    /// <summary>How the match is named in failure messages.</summary>
    public string Description { get; }

    /// <summary>
    /// A record of type <typeparamref name="T"/>, or of a type derived from it, that matches
    /// <paramref name="predicate"/> when one is given.
    /// </summary>
    /// <param name="predicate">The condition, or null for any record of the type.</param>
    /// <param name="description">How to name it in failure messages; by default the type's name.</param>
    public static EventMatch Of<T>(Func<T, bool>? predicate = null, string? description = null) where T : GameEventRecord =>
        new(description ?? (predicate == null ? typeof(T).Name : $"{typeof(T).Name} (with a condition)"),
            r => r is T t && (predicate?.Invoke(t) ?? true));

    /// <summary>A record of type <paramref name="type"/>, or of a type derived from it.</summary>
    /// <exception cref="ArgumentException"><paramref name="type"/> is not a <see cref="GameEventRecord"/> type.</exception>
    public static EventMatch Of(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!typeof(GameEventRecord).IsAssignableFrom(type))
        {
            throw new ArgumentException($"{type.Name} is not a {nameof(GameEventRecord)} type.", nameof(type));
        }

        return new(type.Name, type.IsInstanceOfType);
    }

    /// <summary>Any record that matches <paramref name="predicate"/>.</summary>
    /// <param name="predicate">The condition.</param>
    /// <param name="description">How to name it in failure messages.</param>
    public static EventMatch Where(Func<GameEventRecord, bool> predicate, string description)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new(description, predicate);
    }

    /// <summary>The match for a record type: <see cref="Of(Type)"/>.</summary>
    public static implicit operator EventMatch(Type type) => Of(type);

    /// <summary>Whether <paramref name="record"/> matches.</summary>
    public bool Matches(GameEventRecord record) => _test(record);

    /// <inheritdoc/>
    public override string ToString() => Description;
}
