using Vintagestory.API.Common;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.XUnit;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Player;

/// <summary>A survival player's health, hunger, oxygen and body temperature, on a live server.</summary>
[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "surviveandbuild", Isolation = Isolation.Restart)]
public class LivePlayerVitalsTests : ThreadPoolWarningScenarioBase
{
    [ServerScenario]
    public async Task AJoinedPlayer_CarriesTheSurvivalBehaviors()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        PlayerVitals vitals = player.Vitals;

        Assert.True(vitals.IsAvailable);

        VitalsReading reading = vitals.Read();
        Assert.Equal(15, reading.MaxHealth);
        Assert.Equal(15, reading.BaseMaxHealth);
        Assert.Equal(reading.MaxHealth, reading.Health);
        Assert.Equal(1500, reading.Saturation);
        Assert.Equal(1500, reading.MaxSaturation);
        Assert.NotNull(reading.Nutrition);
        Assert.Equal(40000, reading.MaxOxygen);
        Assert.NotNull(reading.BodyTemperature);
        Assert.True(reading.IsAlive);
        Assert.Equal(EnumGameMode.Survival, reading.GameMode);
        Assert.Empty(reading.DamageOverTime);
    }

    [ServerScenario]
    public async Task Set_AppliesHealthAndSaturation()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        PlayerVitals vitals = player.Vitals;

        vitals.Set(health: 5, saturation: 300, oxygen: 1000, wetness: 0.4f);

        Assert.Equal(5, vitals.Health);
        Assert.Equal(300, vitals.Saturation);
        Assert.Equal(1000, vitals.Oxygen);
        Assert.Equal(0.4f, vitals.Wetness);

        // Health never goes past what the player can hold, or below nothing.
        vitals.Set(health: 900);
        Assert.Equal(vitals.MaxHealth, vitals.Health);

        vitals.Set(health: -3);
        Assert.Equal(0, vitals.Health);
    }

    [ServerScenario]
    public async Task MaxHealth_GetsItsDerivedValueFromTheBehaviour()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        PlayerVitals vitals = player.Vitals;

        vitals.Set(maxHealth: 20);
        Assert.Equal(20, vitals.MaxHealth);
        Assert.Equal(20, vitals.BaseMaxHealth);

        // A modifier is what a trait or an armour piece adds; the behaviour recomputes the
        // ceiling from the base and the modifier.
        vitals.SetMaxHealthModifier("pharos-test", 5);
        Assert.Equal(25, vitals.MaxHealth);
        Assert.Equal(5, vitals.MaxHealthModifiers["pharos-test"]);

        vitals.RemoveMaxHealthModifier("pharos-test");
        Assert.Equal(20, vitals.MaxHealth);
    }

    [ServerScenario]
    public async Task ReceiveSaturation_FeedsTheNutrientLevelTheGameNames()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        PlayerVitals vitals = player.Vitals;

        vitals.Set(saturation: 0);
        vitals.ReceiveSaturation(250, EnumFoodCategory.Protein, nutritionGainMultiplier: 2f);

        // The game adds a fifth of the saturation to the level, doubled by the multiplier.
        Assert.Equal(250, vitals.Saturation);
        Assert.Equal(200, vitals.Nutrition!.Protein);
        Assert.Equal(0, vitals.Nutrition.Vegetable);

        // Satiety never passes the ceiling, and neither does a nutrient level. A full player is not fed
        // the level at all: the game only adds to it while satiety is below the ceiling.
        vitals.Set(saturation: 0, maxSaturation: 100);
        vitals.ReceiveSaturation(5000, EnumFoodCategory.Grain);
        Assert.Equal(100, vitals.Saturation);
        Assert.Equal(100, vitals.Nutrition!.Grain);

        vitals.ReceiveSaturation(5000, EnumFoodCategory.Dairy);
        Assert.Equal(100, vitals.Saturation);
        Assert.Equal(0, vitals.Nutrition.Dairy);
    }

    [ServerScenario]
    public async Task ConsumeSaturation_SpendsSatietyTheWayTheGameDoes()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        PlayerVitals vitals = player.Vitals;

        vitals.Set(saturation: 1000);

        // The amount goes through the game's own hunger speed and the player's hungerrate stat.
        vitals.ConsumeSaturation(300);
        Assert.Equal(700, vitals.Saturation);

        // It cannot go below nothing, however much is spent.
        vitals.ConsumeSaturation(100_000);
        Assert.Equal(0, vitals.Saturation);
    }

    [ServerScenario]
    public async Task Hunger_IsRefusedForACreativePlayer()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        Host!.RunOnGameThread(() => player.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative);
        Assert.Equal(EnumGameMode.Creative, player.Vitals.GameMode);

        PlayerVitals vitals = player.Vitals;
        InvalidOperationException timeout = Assert.Throws<InvalidOperationException>(() => vitals.ConsumeSaturation(10));
        Assert.Contains("skips hunger", timeout.Message);

        Assert.Throws<InvalidOperationException>(() => vitals.ReceiveSaturation(10));
        Assert.Throws<InvalidOperationException>(() => vitals.Set(saturation: 10));
    }

    [ServerScenario]
    public async Task WaitForAsync_WatchesTheServerUntilTheValueComes()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        PlayerVitals vitals = player.Vitals;

        // Without a clock the game's time follows the wall clock, so the wait cannot promise an exact
        // value. The virtual clock makes each tick worth the same, which is what regeneration and
        // the satiety drain accumulate over.
        using VirtualClock clock = Host!.UseVirtualClock();

        vitals.Set(health: 2, saturation: 1500);
        Assert.Equal(2, vitals.Health);

        // Health regenerates from the satiety that is left, once a game second has passed.
        VitalsReading healed = await vitals.WaitForAsync(v => v.Health > 2, maxTicks: 600);
        Assert.True(healed.Health > 2);
        Assert.True(clock.Elapsed >= TimeSpan.Zero);
    }
}
