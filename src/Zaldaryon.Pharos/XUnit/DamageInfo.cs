using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// What a damage to a player came from: how it damages, what caused it, and how hard. A test builds
/// one and hands it to <see cref="ServerTestPlayer.DamageAsync"/> or
/// <see cref="ServerTestPlayer.KillAsync"/>.
/// </summary>
/// <remarks>
/// The game reads all of it: the damage type decides which armour resists it, the source entity is
/// what <c>DamageSource.GetCauseEntity</c> returns and what an audit line names, the tier decides
/// how much armour gets through, and the knockback is what the hit pushes the player by. The damage
/// itself runs through <c>Entity.ReceiveDamage</c>, so every behaviour and every mod's hook sees it.
/// </remarks>
/// <param name="Type">How it damages, such as <see cref="EnumDamageType.PiercingAttack"/>.</param>
/// <param name="Kind">What kind of thing caused it, such as <see cref="EnumDamageSource.Entity"/>.</param>
/// <param name="Source">The entity that caused it: a wolf, another player, a thrown stone.</param>
/// <param name="Cause">The entity behind a projectile, when the source is the projectile itself.</param>
/// <param name="Tier">The weapon tier, which decides how much armour gets through.</param>
/// <param name="Knockback">How hard the hit pushes the player.</param>
public sealed record DamageInfo(
    EnumDamageType Type = EnumDamageType.BluntAttack,
    EnumDamageSource Kind = EnumDamageSource.Internal,
    Entity? Source = null,
    Entity? Cause = null,
    int Tier = 0,
    float Knockback = 1f)
{
    /// <summary>A plain hit, as a fall or an explosion does.</summary>
    public static DamageInfo Blunt { get; } = new();

    /// <summary>A hit from a wolf or another animal.</summary>
    public static DamageInfo FromEntity(Entity source, EnumDamageType type = EnumDamageType.BluntAttack) =>
        new(type, EnumDamageSource.Entity, source);

    /// <summary>A hit from another player, which is what the game's damage audit line names.</summary>
    public static DamageInfo FromPlayer(EntityPlayer attacker, EnumDamageType type = EnumDamageType.BluntAttack) =>
        new(type, EnumDamageSource.Player, attacker);

    /// <summary>
    /// Damage spread over <paramref name="duration"/>, the way a poison or a bleeding wound is.
    /// The health behaviour turns this into a damage-over-time effect rather than taking it at once.
    /// </summary>
    public DamageInfo OverTime(TimeSpan duration, int ticks, EnumDamageOverTimeEffectType effect = EnumDamageOverTimeEffectType.Unknown) =>
        this with
        {
            Duration = duration,
            TicksPerDuration = Math.Max(2, ticks),
            Effect = effect,
        };

    /// <summary>How long the whole effect takes, when it is spread out.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>How many portions the damage is split into, when it is spread out.</summary>
    public int TicksPerDuration { get; init; } = 1;

    /// <summary>The effect's own kind, so a test can tell a poison from a bleeding wound.</summary>
    public EnumDamageOverTimeEffectType Effect { get; init; } = EnumDamageOverTimeEffectType.Unknown;

    /// <summary>The game's own damage source, as the entity's behaviours expect it.</summary>
    public DamageSource ToDamageSource(Entity? target = null)
    {
        DamageSource source = new()
        {
            Source = Kind,
            Type = Type,
            SourceEntity = Source,
            CauseEntity = Cause,
            DamageTier = Tier,
            KnockbackStrength = Knockback,
            Duration = Duration,
            TicksPerDuration = TicksPerDuration,
            DamageOverTimeTypeEnum = Effect,
        };

        if (Source is not null) source.SourcePos = Source.Pos.XYZ.Clone();
        else if (target is not null) source.SourcePos = target.Pos.XYZ.Clone();

        return source;
    }
}