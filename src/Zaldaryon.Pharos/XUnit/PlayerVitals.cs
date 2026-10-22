using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// A test player's survival state: health, hunger and the nutrients it feeds, oxygen, body
/// temperature and temporal stability. Read it with <see cref="Read"/> or the pass-through
/// properties, change it with
/// <see cref="Set"/>, and wait for it with <see cref="WaitForAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every read and write runs on the server's game thread, so a test can call it from any thread.
/// The values come from the game's own survival behaviours rather than from the watched attribute
/// trees, so the paths do not have to be guessed and what the game derives from them, such as max
/// health after a modifier, is the game's own.
/// </para>
/// <para>
/// Satiety and health regeneration accumulate over game time, so an exact value after a while
/// needs the virtual clock; see <see cref="EmbeddedServerHost.UseVirtualClock"/>.
/// <see cref="ConsumeSaturation"/> and <see cref="ReceiveSaturation"/> reach the same changes
/// directly, for a test that does not want to wait.
/// </para>
/// <para>
/// Hunger is skipped in creative and spectator mode: the game neither drains satiety nor
/// regenerates health from it. Changing or spending hunger for such a player throws rather than
/// quietly doing nothing.
/// </para>
/// </remarks>
public sealed class PlayerVitals
{
    private readonly System.Func<EntityPlayer?> _entity;
    private readonly System.Func<EmbeddedServerHost?> _host;

    internal PlayerVitals(ServerTestPlayer player, Func<EmbeddedServerHost?> host, Func<EntityPlayer?> entity)
    {
        Player = player;
        _host = host;
        _entity = entity;
    }

    /// <summary>The player the vitals belong to.</summary>
    public ServerTestPlayer Player { get; }

    /// <summary>
    /// Whether the player has the survival behaviours at all. A world without the survival mod
    /// gives the player none of them, and every value reads as null.
    /// </summary>
    public bool IsAvailable => Read().HasValues;

    /// <summary>Health points, from 0 up.</summary>
    public float? Health => Read().Health;

    /// <summary>The most health points the player can have right now.</summary>
    public float? MaxHealth => Read().MaxHealth;

    /// <summary>Max health before the modifiers and the stats that raise it.</summary>
    public float? BaseMaxHealth => Read().BaseMaxHealth;

    /// <summary>What each modifier adds to max health, keyed by the name its owner gave it.</summary>
    public IReadOnlyDictionary<string, float> MaxHealthModifiers => Read().MaxHealthModifiers;

    /// <summary>The health the running effects will leave the player with, or null when none is expected to heal.</summary>
    public float? FutureHealth => Read().FutureHealth;

    /// <summary>Health points per second the running effects take or give.</summary>
    public float HealthChangeRate => Read().HealthChangeRate;

    /// <summary>The damage-over-time effects running now, in the order the behaviour holds them.</summary>
    public IReadOnlyList<DamageOverTime> DamageOverTime => Read().DamageOverTime;

    /// <summary>Satiety, from 0 up. What stops the player starving.</summary>
    public float? Saturation => Read().Saturation;

    /// <summary>The most satiety the player can hold.</summary>
    public float? MaxSaturation => Read().MaxSaturation;

    /// <summary>The five nutrient levels.</summary>
    public Nutrition? Nutrition => Read().Nutrition;

    /// <summary>Oxygen in milliseconds of breathing left.</summary>
    public float? Oxygen => Read().Oxygen;

    /// <summary>The most oxygen the player can hold, in milliseconds.</summary>
    public float? MaxOxygen => Read().MaxOxygen;

    /// <summary>Whether the player has air to breathe.</summary>
    public bool? HasAir => Read().HasAir;

    /// <summary>Body temperature, from 31 up. Below 35 the player starts freezing.</summary>
    public float? BodyTemperature => Read().BodyTemperature;

    /// <summary>The temperature the player's body settles at.</summary>
    public float? NormalBodyTemperature => Read().NormalBodyTemperature;

    /// <summary>How wet the player is, from 0 up. Above 0.1 the player starts to cool.</summary>
    public float? Wetness => Read().Wetness;

    /// <summary>
    /// The temporal stability the player carries, from zero to one. Null without the survival
    /// mod's temporal stability behaviour.
    /// </summary>
    public double? TemporalStability => Read().TemporalStability;

    /// <summary>How fast the player's temporal stability changes now.</summary>
    public double? TemporalStabilityVelocity => Read().TemporalStabilityVelocity;

    /// <summary>Whether the player is alive.</summary>
    public bool IsAlive => Read().IsAlive;

    /// <summary>The player's game mode. Creative and spectators skip hunger.</summary>
    public EnumGameMode? GameMode => Read().GameMode;

    /// <summary>
    /// Takes a reading of every value at once. Costs one walk of the entity's behaviours, so take
    /// one reading and keep it rather than reading again per assertion.
    /// </summary>
    public VitalsReading Read() => OnGameThread(() => VitalsBehaviors.Read(_entity()));

    /// <summary>
    /// Sets the values named, and only those, through the behaviours. What the game derives from
    /// them updates: <paramref name="maxHealth"/> goes through the behaviour's own recompute and
    /// leaves the modifiers and the stats alone, and <paramref name="health"/> is clamped to what
    /// the player can hold.
    /// </summary>
    /// <param name="health">Health points.</param>
    /// <param name="maxHealth">The most health points the player can have.</param>
    /// <param name="saturation">Satiety.</param>
    /// <param name="maxSaturation">The most satiety the player can hold.</param>
    /// <param name="nutrition">The five nutrient levels, each capped at the max saturation.</param>
    /// <param name="oxygen">Oxygen in milliseconds.</param>
    /// <param name="hasAir">Whether the player has air to breathe.</param>
    /// <param name="bodyTemperature">Body temperature.</param>
    /// <param name="wetness">How wet the player is.</param>
    /// <exception cref="InvalidOperationException">
    /// The player has no behaviour for a value that was named, or hunger was named for a creative
    /// or spectator player, which the game skips.
    /// </exception>
    /// <exception cref="InvalidOperationException">The player is not joined.</exception>
    public void Set(
        float? health = null, float? maxHealth = null, float? saturation = null, float? maxSaturation = null,
        Nutrition? nutrition = null, float? oxygen = null, bool? hasAir = null,
        float? bodyTemperature = null, float? wetness = null) =>
        OnGameThread(() =>
        {
            EntityPlayer entity = Entity();
            VitalsBehaviors.Set(entity, new VitalsBehaviors.Changes(
                health, maxHealth, saturation, maxSaturation, nutrition, oxygen, hasAir, bodyTemperature, wetness));
        });

    /// <summary>
    /// Adds <paramref name="value"/> to the player's max health under <paramref name="key"/>, the
    /// way a trait, an armour piece or a mod does. The key is what a later
    /// <see cref="RemoveMaxHealthModifier"/> takes.
    /// </summary>
    /// <exception cref="InvalidOperationException">The player is not joined.</exception>
    public void SetMaxHealthModifier(string key, float value) => OnGameThread(() =>
        VitalsBehaviors.SetMaxHealthModifier(Entity(), key, value));

    /// <summary>Takes the modifier under <paramref name="key"/> off the player's max health.</summary>
    /// <exception cref="InvalidOperationException">The player is not joined.</exception>
    public void RemoveMaxHealthModifier(string key) => OnGameThread(() =>
        VitalsBehaviors.RemoveMaxHealthModifier(Entity(), key));

    /// <summary>
    /// Spends <paramref name="amount"/> of satiety the way the game spends it when the player
    /// attacks or regenerates health, instead of waiting for the ten-second drain.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The player is not joined, has no hunger behaviour, or is creative or a spectator.
    /// </exception>
    public void ConsumeSaturation(float amount) => OnGameThread(() => VitalsBehaviors.ConsumeSaturation(Entity(), amount));

    /// <summary>
    /// Feeds the player <paramref name="saturation"/> of <paramref name="category"/> the way an
    /// item does, so the nutrient levels and the health boost that follows them are the game's own.
    /// </summary>
    /// <param name="saturation">Satiety to add, capped at the player's max saturation.</param>
    /// <param name="category">Which nutrient level it feeds.</param>
    /// <param name="saturationLossDelay">Game seconds before that level starts falling, as food sets it.</param>
    /// <param name="nutritionGainMultiplier">How much of the nutrient level it adds.</param>
    /// <exception cref="InvalidOperationException">
    /// The player is not joined, has no hunger behaviour, or is creative or a spectator.
    /// </exception>
    public void ReceiveSaturation(
        float saturation, EnumFoodCategory category = EnumFoodCategory.Unknown,
        float saturationLossDelay = 10, float nutritionGainMultiplier = 1f) =>
        OnGameThread(() => VitalsBehaviors.ReceiveSaturation(Entity(), saturation, category, saturationLossDelay, nutritionGainMultiplier));

    /// <summary>
    /// Ticks until a reading matches <paramref name="predicate"/>, and returns that reading. The
    /// first reading is tested before any tick, so a value that already matches returns at once.
    /// </summary>
    /// <remarks>
    /// The server ticks by itself while it waits. Inside a client-server scenario the client is
    /// stepped by the session, so wait for the client's copy with
    /// <c>Session.StepUntilAsync</c> instead.
    /// </remarks>
    /// <param name="predicate">What the reading must match.</param>
    /// <param name="maxTicks">How many ticks to wait at most. 0 only looks.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="TimeoutException">No reading matched within <paramref name="maxTicks"/> ticks; the message shows what it saw.</exception>
    public async Task<VitalsReading> WaitForAsync(System.Func<VitalsReading, bool> predicate, int maxTicks = 600, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTicks);

        VitalsReading seen = Read();
        for (int tick = 0; ; tick++)
        {
            if (predicate(seen)) return seen;
            if (tick >= maxTicks) break;
            ct.ThrowIfCancellationRequested();
            await StepAsync(ct).ConfigureAwait(false);
            seen = Read();
        }

        throw new TimeoutException($"No vitals matched within {maxTicks} ticks. Last seen: {seen}.");
    }

    private Task StepAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        _host()?.Tick();
        return Task.CompletedTask;
    }

    private EntityPlayer Entity() =>
        _entity() ?? throw new InvalidOperationException($"'{Player.PlayerName ?? Player.PlayerUID}' has no entity: it has not joined.");

    private T OnGameThread<T>(Func<T> read) => _host() is { } host ? host.RunOnGameThread(read) : read();

    private void OnGameThread(Action action)
    {
        if (_host() is { } host) host.RunOnGameThread(action);
        else action();
    }
}