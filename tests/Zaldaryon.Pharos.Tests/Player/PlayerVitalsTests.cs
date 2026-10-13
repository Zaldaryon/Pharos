using Vintagestory.API.Common;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Player;

/// <summary>Comparisons between vitals readings, and a player that has not joined.</summary>
public class PlayerVitalsTests
{
    private static VitalsReading Reading(
        float? health = 10, float? saturation = 800, float? oxygen = 20000, float? temperature = 37,
        Nutrition? nutrition = null, bool? air = true, bool alive = true) =>
        new(health, health is null ? null : 15, health is null ? null : 15, new Dictionary<string, float>(),
            null, 0, [], saturation, 1500, nutrition,
            oxygen, 40000, air, temperature, 37, 0, alive, EnumGameMode.Survival);

    [Fact]
    public void Matches_AcceptsReadingsThatAgreeWithinTheTolerance()
    {
        Assert.True(Reading().Matches(Reading(health: 10.4f), 0.5f, VitalsValues.All, out string why, out IReadOnlyList<string> compared));
        Assert.Equal("", why);
        Assert.Contains("health", compared);
        Assert.Contains("saturation", compared);
    }

    [Fact]
    public void Matches_SaysWhichValueDiffers()
    {
        Assert.False(Reading().Matches(Reading(saturation: 799f), 0.5f, VitalsValues.All, out string why, out _));
        Assert.Equal("saturation 800 against 799", why);

        Assert.False(Reading().Matches(Reading(oxygen: 10000f), 0.5f, VitalsValues.All, out string airless, out _));
        Assert.Contains("oxygen", airless);
    }

    [Fact]
    public void Matches_LooksAtOnlyTheValuesBothSidesCarry()
    {
        // The client's player entity keeps no hunger, so a sync comparison sees nulls where the
        // server has values and must skip them instead of calling them a mismatch.
        VitalsReading server = Reading();
        VitalsReading client = Reading(health: null, saturation: null);

        Assert.True(server.Matches(client, 0.5f, VitalsValues.All, out string why, out IReadOnlyList<string> compared));
        Assert.Equal("", why);
        Assert.DoesNotContain("health", compared);
        Assert.DoesNotContain("saturation", compared);
        Assert.Contains("oxygen", compared);
    }

    [Fact]
    public void Matches_LooksAtOnlyTheValuesItWasAskedFor()
    {
        VitalsReading server = Reading();
        VitalsReading client = Reading(saturation: 0);

        Assert.False(server.Matches(client, 0.5f, VitalsValues.All, out _, out _));
        Assert.True(server.Matches(client, 0.5f, VitalsValues.Health, out _, out IReadOnlyList<string> compared));
        Assert.DoesNotContain("saturation", compared);
        Assert.Contains("health", compared);
    }

    [Fact]
    public void Matches_CatchesNutritionAirAndBeingAlive()
    {
        VitalsReading reading = Reading(nutrition: new Nutrition(1, 2, 3, 4, 5));

        Assert.False(reading.Matches(Reading(nutrition: new Nutrition(1, 2, 3, 4, 90)), 0.5f, VitalsValues.All, out string nutrition, out _));
        Assert.Contains("protein", nutrition);

        Assert.False(reading.Matches(Reading(air: false), 0.5f, VitalsValues.All, out string air, out _));
        Assert.Equal("air True against False", air);

        Assert.False(reading.Matches(Reading(alive: false), 0.5f, VitalsValues.All, out string alive, out _));
        Assert.Equal("alive True against False", alive);
    }

    [Fact]
    public void Matches_NeedsAReadingToCompareWith()
    {
        Assert.Throws<ArgumentNullException>(() => Reading().Matches(null!, 0.5f, VitalsValues.All, out _, out _));
    }

    [Fact]
    public void Nutrition_MatchesWithinTheTolerance()
    {
        Nutrition nutrition = new(10, 20, 30, 40, 50);

        Assert.True(nutrition.Matches(new Nutrition(10.2f, 20, 30, 40, 50), 0.5f));
        Assert.False(nutrition.Matches(new Nutrition(10.2f, 20, 30, 40, 50), 0.1f));
        Assert.Contains("fruit", nutrition.ToString());
    }

    [Fact]
    public void APlayerThatHasNotJoined_HasNoValuesAndRefusesAChange()
    {
        using ServerTestPlayer player = new("not-joined");
        PlayerVitals vitals = player.Vitals;

        Assert.False(vitals.IsAvailable);
        Assert.Same(player, vitals.Player);
        Assert.False(vitals.Read().HasValues);
        Assert.Equal(VitalsReading.Empty, vitals.Read());

        Assert.Throws<InvalidOperationException>(() => vitals.Set(health: 5));
        Assert.Throws<InvalidOperationException>(() => vitals.SetMaxHealthModifier("test", 1));
        Assert.Throws<InvalidOperationException>(() => vitals.ConsumeSaturation(10));
        Assert.Throws<InvalidOperationException>(() => vitals.ReceiveSaturation(10));
    }

    [Fact]
    public async Task WaitForAsync_WithoutATickOnlyLooks()
    {
        using ServerTestPlayer player = new("not-joined");
        PlayerVitals vitals = player.Vitals;

        VitalsReading read = await vitals.WaitForAsync(_ => true, maxTicks: 0);
        Assert.Same(VitalsReading.Empty, read);

        TimeoutException timeout = await Assert.ThrowsAsync<TimeoutException>(() => vitals.WaitForAsync(_ => false, maxTicks: 0));
        Assert.Contains("No vitals matched", timeout.Message);
    }

    [Fact]
    public async Task WaitForAsync_RejectsABadArgument()
    {
        using ServerTestPlayer player = new("not-joined");

        await Assert.ThrowsAsync<ArgumentNullException>(() => player.Vitals.WaitForAsync(null!));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => player.Vitals.WaitForAsync(_ => true, maxTicks: -1));
    }
}