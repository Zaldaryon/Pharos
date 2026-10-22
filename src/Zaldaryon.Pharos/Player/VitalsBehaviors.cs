using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// Reads and writes the survival behaviours a player entity carries, by the names the game gives
/// them. Every read and write happens on the side's own thread, where the entity lives.
/// </summary>
/// <remarks>
/// The behaviours are the essentials mod's <c>EntityBehaviorHealth</c>, <c>EntityBehaviorHunger</c>
/// and <c>EntityBehaviorBreathe</c>, and the survival mod's <c>EntityBehaviorBodyTemperature</c>.
/// Pharos does not build against those mods, so it finds each behaviour through
/// <see cref="Entity.GetBehavior(string)"/> and reaches its members by name. A member the game
/// renamed is reported by name rather than silently read as nothing.
/// </remarks>
internal static class VitalsBehaviors
{
    public const string Health = "health";
    public const string Hunger = "hunger";
    public const string Breathe = "breathe";
    public const string BodyTemperature = "bodytemperature";
    public const string TemporalStability = "temporalstabilityaffected";

    /// <summary>
    /// Takes a reading of <paramref name="entity"/>. Values whose behaviour the entity does not
    /// have come back null, which is what a client's health and hunger look like: the survival
    /// player keeps them on the server only.
    /// </summary>
    public static VitalsReading Read(EntityPlayer? entity)
    {
        if (entity is null) return VitalsReading.Empty;

        EntityBehavior? health = entity.GetBehavior(Health);
        EntityBehavior? hunger = entity.GetBehavior(Hunger);
        EntityBehavior? breathe = entity.GetBehavior(Breathe);
        EntityBehavior? temperature = entity.GetBehavior(BodyTemperature);
        EntityBehavior? stability = entity.GetBehavior(TemporalStability);

        return new VitalsReading(
            Health: Float(health, "Health"),
            MaxHealth: Float(health, "MaxHealth"),
            BaseMaxHealth: Float(health, "BaseMaxHealth"),
            MaxHealthModifiers: Dictionary(health, "maxHealthModifiers"),
            FutureHealth: NullableFloat(health, "FutureHealth"),
            HealthChangeRate: Float(health, "HealthChangeRate") ?? 0,
            DamageOverTime: DoTEffects(health),
            Saturation: Float(hunger, "Saturation"),
            MaxSaturation: Float(hunger, "MaxSaturation"),
            Nutrition: ReadNutrition(hunger),
            Oxygen: Float(breathe, "Oxygen"),
            MaxOxygen: Float(breathe, "MaxOxygen"),
            HasAir: NullableBool(breathe, "HasAir"),
            BodyTemperature: Float(temperature, "CurBodyTemperature"),
            NormalBodyTemperature: Float(temperature, "NormalBodyTemperature"),
            Wetness: Float(temperature, "Wetness"),
            IsAlive: entity.Alive,
            GameMode: GameMode(entity))
        {
            TemporalStability = Double(stability, "OwnStability"),
            TemporalStabilityVelocity = Double(stability, "TempStabChangeVelocity"),
        };
    }

    /// <summary>The values <see cref="Set"/> can change, each null when the caller did not name it.</summary>
    internal readonly record struct Changes(
        float? Health, float? MaxHealth, float? Saturation, float? MaxSaturation,
        Nutrition? Nutrition, float? Oxygen, bool? HasAir, float? BodyTemperature, float? Wetness);

    /// <summary>
    /// Applies <paramref name="changes"/> through the behaviours, so what the game derives from
    /// them updates: max health goes through the behaviour's own recompute, and the game mode's
    /// skipping of hunger is refused rather than ignored.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The player has no behaviour for a value that was named, or the player is creative or a
    /// spectator and hunger was named, which the game would ignore.
    /// </exception>
    public static void Set(EntityPlayer entity, Changes changes)
    {
        ArgumentNullException.ThrowIfNull(entity);

        EntityBehavior? health = Require(entity, Health, changes.Health is not null || changes.MaxHealth is not null);
        EntityBehavior? hunger = Require(entity, Hunger, changes.Saturation is not null || changes.MaxSaturation is not null || changes.Nutrition is not null);
        EntityBehavior? breathe = Require(entity, Breathe, changes.Oxygen is not null || changes.HasAir is not null);
        EntityBehavior? temperature = Require(entity, BodyTemperature, changes.BodyTemperature is not null || changes.Wetness is not null);

        bool hungerNamed = changes.Saturation is not null || changes.MaxSaturation is not null || changes.Nutrition is not null;
        if (hunger is not null && hungerNamed) RequireHunger(entity);

        if (hunger is not null)
        {
            if (changes.MaxSaturation is { } maxSaturation) Member.Set(hunger, "MaxSaturation", maxSaturation);
            if (changes.Nutrition is { } nutrition) WriteNutrition(hunger, nutrition, changes.MaxSaturation);
            if (changes.Saturation is { } saturation) Member.Set(hunger, "Saturation", saturation);
        }

        if (health is not null)
        {
            if (changes.MaxHealth is { } maxHealth)
            {
                // MaxHealth is what the behaviour derives. Setting the base and recomputing is how
                // the game itself raises the ceiling, and it leaves the modifiers and the stats
                // alone.
                Member.Set(health, "BaseMaxHealth", maxHealth);
                Member.Call(health, "MarkDirty");
            }

            if (changes.Health is { } value) Member.Set(health, "Health", Math.Clamp(value, 0, Float(health, "MaxHealth") ?? value));
        }

        if (breathe is not null)
        {
            if (changes.Oxygen is { } oxygen) Member.Set(breathe, "Oxygen", oxygen);
            if (changes.HasAir is { } hasAir) Member.Set(breathe, "HasAir", hasAir);
        }

        if (temperature is not null)
        {
            if (changes.BodyTemperature is { } degrees) Member.Set(temperature, "CurBodyTemperature", degrees);
            if (changes.Wetness is { } wetness) Member.Set(temperature, "Wetness", wetness);
        }
    }

    /// <summary>
    /// Adds <paramref name="value"/> to the player's max health under <paramref name="key"/>, the
    /// way a trait, an armour piece or a mod does, and lets the behaviour recompute.
    /// </summary>
    public static void SetMaxHealthModifier(EntityPlayer entity, string key, float value)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        EntityBehavior health = Require(entity, Health, true)!;
        Member.Call(health, "SetMaxHealthModifiers", key, value);
    }

    /// <summary>Removes the max health modifier under <paramref name="key"/>.</summary>
    public static void RemoveMaxHealthModifier(EntityPlayer entity, string key)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        EntityBehavior health = Require(entity, Health, true)!;
        Member.SetMaxHealthModifierRemoved(health, key);
    }

    /// <summary>
    /// Spends satiety the way the game spends it when the player attacks or regenerates health, so
    /// a test does not have to reach the ten-second drain. The amount is scaled by the game's own
    /// hunger speed and the player's <c>hungerrate</c> stat, as any other drain is, and cannot take
    /// satiety below zero.
    /// </summary>
    public static void ConsumeSaturation(EntityPlayer entity, float amount)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Member.Call(RequireHunger(entity), "ConsumeSaturation", amount);
    }

    /// <summary>
    /// Feeds the player <paramref name="saturation"/> of <paramref name="category"/> the way an item
    /// does, so the nutrient levels and the health boost that follows them are the game's own.
    /// </summary>
    public static void ReceiveSaturation(
        EntityPlayer entity, float saturation, EnumFoodCategory category = EnumFoodCategory.Unknown,
        float saturationLossDelay = 10, float nutritionGainMultiplier = 1f)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Member.Call(RequireHunger(entity), "OnEntityReceiveSaturation", saturation, category, saturationLossDelay, nutritionGainMultiplier);
    }

    private static EntityBehavior RequireHunger(EntityPlayer entity)
    {
        if (GameMode(entity) is EnumGameMode.Creative or EnumGameMode.Spectator)
        {
            throw new InvalidOperationException(
                $"'{entity.GetName()}' is playing in {GameMode(entity)}, where the game skips hunger: satiety does not drain and health does not regenerate from it. " +
                "Put the player in survival first.");
        }

        return Require(entity, Hunger, true)!;
    }

    private static EntityBehavior? Require(EntityPlayer entity, string behavior, bool needed)
    {
        EntityBehavior? found = entity.GetBehavior(behavior);
        if (found is null && needed) throw new InvalidOperationException($"The player has no '{behavior}' behaviour: this world does not give players survival state.");
        return found;
    }

    private static EnumGameMode? GameMode(EntityPlayer entity) => entity.Player?.WorldData?.CurrentGameMode;

    private static Nutrition? ReadNutrition(EntityBehavior? hunger) => hunger is null ? null : new Nutrition(
        Float(hunger, "FruitLevel") ?? 0,
        Float(hunger, "VegetableLevel") ?? 0,
        Float(hunger, "ProteinLevel") ?? 0,
        Float(hunger, "GrainLevel") ?? 0,
        Float(hunger, "DairyLevel") ?? 0);

    private static void WriteNutrition(EntityBehavior hunger, Nutrition nutrition, float? maxSaturation)
    {
        float cap = maxSaturation ?? Float(hunger, "MaxSaturation") ?? float.MaxValue;
        Member.Set(hunger, "FruitLevel", Math.Min(nutrition.Fruit, cap));
        Member.Set(hunger, "VegetableLevel", Math.Min(nutrition.Vegetable, cap));
        Member.Set(hunger, "ProteinLevel", Math.Min(nutrition.Protein, cap));
        Member.Set(hunger, "GrainLevel", Math.Min(nutrition.Grain, cap));
        Member.Set(hunger, "DairyLevel", Math.Min(nutrition.Dairy, cap));
    }

    private static IReadOnlyList<DamageOverTime> DoTEffects(EntityBehavior? health)
    {
        if (Member.TryGet(health, "ActiveDoTEffects") is not IEnumerable list) return [];

        List<DamageOverTime> effects = [];
        foreach (object entry in list)
        {
            effects.Add(new DamageOverTime(
                (EnumDamageSource)Member.Get(entry, "DamageSource")!,
                (EnumDamageType)Member.Get(entry, "DamageType")!,
                (int)Member.Get(entry, "DamageTier")!,
                Member.Get(entry, "Damage") is { } damage ? Convert.ToSingle(damage) : 0,
                (TimeSpan)Member.Get(entry, "TickDuration")!,
                (int)Member.Get(entry, "TicksLeft")!,
                (int)Member.Get(entry, "EffectType")!));
        }

        return effects;
    }

    private static float? Float(object? target, string name) => Member.TryGet(target, name) is { } value ? Convert.ToSingle(value) : null;

    private static float? NullableFloat(object? target, string name) => Float(target, name);

    private static double? Double(object? target, string name) => Member.TryGet(target, name) is { } value ? Convert.ToDouble(value) : null;

    private static bool? NullableBool(object? target, string name) => Member.TryGet(target, name) is { } value ? Convert.ToBoolean(value) : null;

    private static IReadOnlyDictionary<string, float> Dictionary(object? target, string name)
    {
        if (Member.TryGet(target, name) is not IDictionary dictionary) return new Dictionary<string, float>();

        Dictionary<string, float> values = new(dictionary.Count);
        foreach (DictionaryEntry entry in dictionary) values[(string)entry.Key] = Convert.ToSingle(entry.Value);
        return values;
    }

    /// <summary>
    /// The behaviours' members, found by name. A member the game renamed is reported by name
    /// rather than quietly read as nothing, so a change to the game shows up as a test failure
    /// with a message that says what moved.
    /// </summary>
    private static class Member
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static object? TryGet(object? target, string name)
        {
            if (target is null) return null;

            Type type = target.GetType();
            return type.GetProperty(name, Flags) is { } property ? property.GetValue(target)
                : type.GetField(name, Flags) is { } field ? field.GetValue(target)
                : throw Missing(type, name);
        }

        public static object Get(object target, string name) =>
            TryGet(target, name) ?? throw Missing(target.GetType(), name);

        public static void Set(object target, string name, object? value)
        {
            Type type = target.GetType();
            if (type.GetProperty(name, Flags) is { CanWrite: true } property) property.SetValue(target, value);
            else if (type.GetField(name, Flags) is { } field) field.SetValue(target, value);
            else throw Missing(type, name);
        }

        public static object? Call(object target, string name, params object?[] args)
        {
            Type type = target.GetType();
            MethodInfo method = type.GetMethods(Flags)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length)
                ?? throw Missing(type, name);

            try
            {
                return method.Invoke(target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        /// <summary>
        /// Drops a max health modifier. The behaviour keeps its own dictionary private and only
        /// adds, so the key is written as zero, which is how the behaviour itself treats a removal.
        /// </summary>
        public static void SetMaxHealthModifierRemoved(object health, string key)
        {
            Type type = health.GetType();
            FieldInfo? modifiers = type.GetField("maxHealthModifiers", Flags);
            if (modifiers?.GetValue(health) is not IDictionary dictionary)
            {
                throw Missing(type, "maxHealthModifiers");
            }

            dictionary[key] = 0f;
            Call(health, "MarkDirty");
        }

        private static MissingMemberException Missing(Type type, string name) =>
            new($"Pharos cannot find {type.Name}.{name} in the game's survival behaviour: the game changed.");
    }
}