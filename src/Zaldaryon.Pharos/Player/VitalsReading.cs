using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// The five nutrient levels a player carries, as the game's hunger behaviour stores them. Each is
/// from 0 up to the player's max saturation.
/// </summary>
/// <param name="Fruit">The fruit level.</param>
/// <param name="Vegetable">The vegetable level.</param>
/// <param name="Protein">The protein level.</param>
/// <param name="Grain">The grain level.</param>
/// <param name="Dairy">The dairy level.</param>
public sealed record Nutrition(float Fruit, float Vegetable, float Protein, float Grain, float Dairy)
{
    /// <summary>Five nutrient levels of nothing.</summary>
    public static Nutrition Zero { get; } = new(0, 0, 0, 0, 0);

    /// <summary>
    /// Whether every level is within <paramref name="tolerance"/> of <paramref name="other"/>'s.
    /// The levels are floats the game moves in fractions of, so a test comparing them needs a
    /// tolerance.
    /// </summary>
    public bool Matches(Nutrition other, float tolerance) =>
        Within(Fruit, other.Fruit, tolerance) && Within(Vegetable, other.Vegetable, tolerance) &&
        Within(Protein, other.Protein, tolerance) && Within(Grain, other.Grain, tolerance) &&
        Within(Dairy, other.Dairy, tolerance);

    private static bool Within(float a, float b, float tolerance) => Math.Abs(a - b) <= tolerance;

    /// <inheritdoc />
    public override string ToString() =>
        $"fruit {Fruit:0.#}, vegetable {Vegetable:0.#}, protein {Protein:0.#}, grain {Grain:0.#}, dairy {Dairy:0.#}";
}

/// <summary>
/// One damage-over-time effect running on a player, as a copy of the game's own entry. See
/// <see cref="VitalsReading.DamageOverTime"/>.
/// </summary>
/// <param name="Source">Where the damage comes from, such as poison or fire.</param>
/// <param name="Type">How it damages.</param>
/// <param name="Tier">The damage tier, which decides armour against it.</param>
/// <param name="Damage">The damage one tick takes off.</param>
/// <param name="TickDuration">How long one tick lasts.</param>
/// <param name="TicksLeft">How many ticks are left.</param>
/// <param name="EffectType">The effect's own kind, so a mod can stop the ones it applied.</param>
public sealed record DamageOverTime(
    EnumDamageSource Source, EnumDamageType Type, int Tier, float Damage,
    TimeSpan TickDuration, int TicksLeft, int EffectType);

/// <summary>
/// Which values of a <see cref="VitalsReading"/> a comparison looks at.
/// </summary>
/// <remarks>
/// The game does not send every value to the client. Health and wetness are watched attributes the
/// server pushes; oxygen, air and body temperature the survival mod works out on each side for
/// itself, so a headless client holds its own and never catches up with the server's. Comparing
/// those by default would fail on every session, so <see cref="SyncedByDefault"/> leaves them out
/// and a test that sets them asks for them by name.
/// </remarks>
[Flags]
public enum VitalsValues
{
    /// <summary>Compare nothing.</summary>
    None = 0,

    /// <summary>Compare health points.</summary>
    Health = 1 << 0,

    /// <summary>Compare the most health points the player can hold.</summary>
    MaxHealth = 1 << 1,

    /// <summary>Compare satiety.</summary>
    Saturation = 1 << 2,

    /// <summary>Compare the most satiety the player can hold.</summary>
    MaxSaturation = 1 << 3,

    /// <summary>Compare oxygen.</summary>
    Oxygen = 1 << 4,

    /// <summary>Compare the most oxygen the player can hold.</summary>
    MaxOxygen = 1 << 5,

    /// <summary>Compare whether the player has air to breathe.</summary>
    Air = 1 << 6,

    /// <summary>Compare body temperature.</summary>
    BodyTemperature = 1 << 7,

    /// <summary>Compare the temperature the player's body settles at.</summary>
    NormalBodyTemperature = 1 << 8,

    /// <summary>Compare how wet the player is.</summary>
    Wetness = 1 << 9,

    /// <summary>Compare the five nutrient levels.</summary>
    Nutrition = 1 << 10,

    /// <summary>What the game itself keeps the two sides agreeing on.</summary>
    SyncedByDefault = Health | MaxHealth | Wetness,

    /// <summary>Every value, whether the game syncs it or not.</summary>
    All = Health | MaxHealth | Saturation | MaxSaturation | Oxygen | MaxOxygen | Air | BodyTemperature | NormalBodyTemperature | Wetness | Nutrition,
}

/// <summary>
/// A player's survival state, read at one moment: health, hunger and the nutrients it feeds,
/// oxygen, and body temperature. Both sides of a session read it, so a test can compare them.
/// </summary>
/// <remarks>
/// <para>
/// The values are the ones the game's own behaviours hold, and a behaviour the entity does not
/// have leaves its values null. The survival player keeps hunger on the server only, so a
/// client's reading has no satiety or nutrient levels where the server's has.
/// </para>
/// <para>
/// Reading it walks the entity's behaviours, so take one reading and keep it rather than reading
/// again per assertion. Runs on the side's own thread.
/// </para>
/// </remarks>
/// <param name="Health">Health points, from 0 up. Null without the health behaviour.</param>
/// <param name="MaxHealth">The most health points the player can have right now.</param>
/// <param name="BaseMaxHealth">Max health before the modifiers and the stats that raise it.</param>
/// <param name="MaxHealthModifiers">What each modifier adds to max health, keyed by the name its owner gave it.</param>
/// <param name="FutureHealth">The health the running effects will leave the player with, or null when none is expected to heal.</param>
/// <param name="HealthChangeRate">Health points per second the running effects take or give.</param>
/// <param name="DamageOverTime">The damage-over-time effects running now, in the order the behaviour holds them.</param>
/// <param name="Saturation">Satiety, from 0 up. What stops the player starving.</param>
/// <param name="MaxSaturation">The most satiety the player can hold.</param>
/// <param name="Nutrition">The five nutrient levels. Null without the hunger behaviour.</param>
/// <param name="Oxygen">Oxygen in milliseconds of breathing left.</param>
/// <param name="MaxOxygen">The most oxygen the player can hold, in milliseconds.</param>
/// <param name="HasAir">Whether the player has air to breathe.</param>
/// <param name="BodyTemperature">Body temperature, from 31 up. Below 35 the player starts freezing.</param>
/// <param name="NormalBodyTemperature">The temperature the player's body settles at.</param>
/// <param name="Wetness">How wet the player is, from 0 up. Above 0.1 the player starts to cool.</param>
/// <param name="IsAlive">Whether the player is alive.</param>
/// <param name="GameMode">The player's game mode. Creative and spectators skip hunger.</param>
public sealed record VitalsReading(
    float? Health, float? MaxHealth, float? BaseMaxHealth, IReadOnlyDictionary<string, float> MaxHealthModifiers,
    float? FutureHealth, float HealthChangeRate, IReadOnlyList<DamageOverTime> DamageOverTime,
    float? Saturation, float? MaxSaturation, Nutrition? Nutrition,
    float? Oxygen, float? MaxOxygen, bool? HasAir,
    float? BodyTemperature, float? NormalBodyTemperature, float? Wetness,
    bool IsAlive, EnumGameMode? GameMode)
{
    /// <summary>A reading of a player with none of the behaviours.</summary>
    public static VitalsReading Empty { get; } = new(
        null, null, null, new Dictionary<string, float>(), null, 0, [],
        null, null, null,
        null, null, null,
        null, null, null,
        true, null);

    /// <summary>Whether the reading holds any value at all.</summary>
    public bool HasValues =>
        Health is not null || Saturation is not null || Oxygen is not null || BodyTemperature is not null;

    /// <summary>
    /// Whether the two readings agree on the values of <paramref name="values"/> both sides carry,
    /// to within <paramref name="tolerance"/> for the floats.
    /// <paramref name="compared"/> lists the values this checked, so a sync assertion can report
    /// what it did not look at. A value one side does not carry is skipped rather than called a
    /// mismatch.
    /// </summary>
    /// <param name="other">The reading to compare with.</param>
    /// <param name="tolerance">How far a float may differ.</param>
    /// <param name="values">Which values to look at. See <see cref="VitalsValues"/>.</param>
    /// <param name="why">What differs, or "" when they agree.</param>
    /// <param name="compared">The values the comparison looked at.</param>
    public bool Matches(VitalsReading other, float tolerance, VitalsValues values, out string why, out IReadOnlyList<string> compared)
    {
        ArgumentNullException.ThrowIfNull(other);
        List<string> checked_ = [];

        (VitalsValues Value, string Name, float? Mine, float? Theirs)[] floats =
        [
            (VitalsValues.Health, "health", Health, other.Health),
            (VitalsValues.MaxHealth, "max health", MaxHealth, other.MaxHealth),
            (VitalsValues.Saturation, "saturation", Saturation, other.Saturation),
            (VitalsValues.MaxSaturation, "max saturation", MaxSaturation, other.MaxSaturation),
            (VitalsValues.Oxygen, "oxygen", Oxygen, other.Oxygen),
            (VitalsValues.MaxOxygen, "max oxygen", MaxOxygen, other.MaxOxygen),
            (VitalsValues.BodyTemperature, "body temperature", BodyTemperature, other.BodyTemperature),
            (VitalsValues.NormalBodyTemperature, "normal body temperature", NormalBodyTemperature, other.NormalBodyTemperature),
            (VitalsValues.Wetness, "wetness", Wetness, other.Wetness),
        ];

        foreach ((VitalsValues value, string name, float? mine, float? theirs) in floats)
        {
            if ((values & value) == 0 || mine is not { } a || theirs is not { } b) continue;
            checked_.Add(name);
            if (Math.Abs(a - b) > tolerance)
            {
                why = $"{name} {a:0.###} against {b:0.###}";
                compared = checked_;
                return false;
            }
        }

        if ((values & VitalsValues.Nutrition) != 0 && Nutrition is { } nutrition && other.Nutrition is { } otherNutrition)
        {
            checked_.Add("nutrition");
            if (!nutrition.Matches(otherNutrition, tolerance))
            {
                why = $"nutrition ({nutrition}) against ({otherNutrition})";
                compared = checked_;
                return false;
            }
        }

        if ((values & VitalsValues.Air) != 0 && HasAir is { } air && other.HasAir is { } otherAir)
        {
            checked_.Add("air");
            if (air != otherAir)
            {
                why = $"air {air} against {otherAir}";
                compared = checked_;
                return false;
            }
        }

        if (IsAlive != other.IsAlive)
        {
            why = $"alive {IsAlive} against {other.IsAlive}";
            compared = checked_;
            return false;
        }

        checked_.Add("alive");
        why = "";
        compared = checked_;
        return true;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{Health:0.###}/{MaxHealth:0.###} health, {Saturation:0.#}/{MaxSaturation:0.###} saturation, " +
        $"{(Nutrition is { } n ? n.ToString() : "no nutrition")}, oxygen {Oxygen:0.#}, " +
        $"temperature {BodyTemperature:0.##}, wetness {Wetness:0.###}";
}