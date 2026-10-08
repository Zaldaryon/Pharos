using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Calendar arithmetic, before anything boots.</summary>
public class CalendarMathTests
{
    private const long Day = 24 * 3600;

    [Theory]
    [InlineData(10 * 3600, 22f, 22 * 3600)]
    [InlineData(23 * 3600, 6f, Day + 6 * 3600)]
    [InlineData(22 * 3600, 22f, 22 * 3600)]
    [InlineData(5 * Day + 3600, 0.5f, 6 * Day + 1800)]
    public void NextTimeOfDay_IsTheNextWholeSecondTheClockShowsIt(long now, float hour, long expected)
    {
        Assert.Equal(expected, CalendarMath.NextTimeOfDay(now, hour, 24));
    }

    [Theory]
    [InlineData(23.9f)]
    [InlineData(13.3f)]
    [InlineData(24.5f)]
    public void NextTimeOfDay_LandsOnTheHour_WithDaysOfAnyLength(float hoursPerDay)
    {
        foreach (long now in new long[] { 0, 1234567, 98765432, 400 * Day + 17 })
        {
            for (float hour = 0; hour < hoursPerDay; hour += 1.5f)
            {
                long at = CalendarMath.NextTimeOfDay(now, hour, hoursPerDay);

                // How the game reads the hour off the clock.
                double shown = at / 3600.0 % hoursPerDay;
                Assert.InRange(at, now, now + (long)(hoursPerDay * 3600) + 1);
                Assert.InRange(shown - hour, 0, 1 / 3600.0 + 1e-9);
            }
        }
    }

    [Fact]
    public void DateToSeconds_KeepsTheTimeOfDay_WithDaysOfAnyLength()
    {
        const float hoursPerDay = 23.9f;
        long now = 50_000_000;
        long at = CalendarMath.DateToSeconds(now, 3, 4, 5, hoursPerDay, 9, 12);

        Assert.Equal(now / 3600.0 % hoursPerDay, at / 3600.0 % hoursPerDay, 3);
        Assert.Equal(3 * 108 + 3 * 9 + 4, (int)(at / 3600.0 / hoursPerDay));
    }

    [Fact]
    public void DateToSeconds_KeepsTheTimeOfDay()
    {
        long now = 3 * Day + 7 * 3600 + 15;

        Assert.Equal(7 * 3600 + 15, CalendarMath.DateToSeconds(now, 0, 1, 1, 24, 9, 12));
        Assert.Equal((1L * 108 + 2 * 9 + 4) * Day + 7 * 3600 + 15, CalendarMath.DateToSeconds(now, 1, 3, 5, 24, 9, 12));
    }
}

/// <summary>The calendar and the weather of a live server, set and read back.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat", Isolation = WorldIsolation.Restart)]
public class LiveCalendarWeatherTests : ServerScenarioBase
{
    private CalendarDriver Calendar => Host!.Calendar;

    private WeatherDriver Weather => Host!.Weather;

    [ServerScenario]
    public void TimeAndDate_LandWhereTheyAreSet()
    {
        Calendar.Freeze();

        Calendar.SetTime(22);
        Assert.Equal(22f, Calendar.HourOfDay);
        double hours = Calendar.TotalHours;

        Calendar.SetTime(6);
        Assert.Equal(6f, Calendar.HourOfDay);
        Assert.Equal(hours + 8, Calendar.TotalHours, 6);

        Calendar.Advance(TimeSpan.FromHours(30));
        Assert.Equal(hours + 38, Calendar.TotalHours, 6);
        Assert.Equal(12f, Calendar.HourOfDay);

        Calendar.SetDate(2, 1, 4);
        EnumSeason winter = Calendar.Season;
        Calendar.SetDate(2, 7, 4);
        Assert.Equal((2, 7, 4), (Calendar.Year, Calendar.Month, Calendar.DayOfMonth));
        Assert.Equal(12f, Calendar.HourOfDay);
        Assert.NotEqual(winter, Calendar.Season);

        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.SetTime(24));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.SetDate(0, 13, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Calendar.Advance(TimeSpan.FromHours(-1)));
    }

    [ServerScenario]
    public async Task AFrozenCalendar_StandsStillWhilePlayersPlay()
    {
        await CreateTestPlayerAsync("Clockwatcher");
        Assert.True(Calendar.IsRunning);

        Calendar.Freeze();
        double frozen = Calendar.TotalHours;
        await TickForAsync(TimeSpan.FromMilliseconds(600));
        Assert.True(Calendar.IsFrozen);
        Assert.Equal(frozen, Calendar.TotalHours);

        Calendar.Unfreeze();
        await TickForAsync(TimeSpan.FromMilliseconds(600));
        Assert.False(Calendar.IsFrozen);
        Assert.True(Calendar.TotalHours > frozen, "Time did not pass again after unfreezing");
    }

    [ServerScenario]
    public void Weather_IsSetAndReadBackAtOnce()
    {
        Calendar.Freeze();
        Assert.True(Weather.IsAvailable);
        Assert.Contains("overcast", Weather.Patterns);
        Assert.Contains("still", Weather.Winds);

        BlockPos spawn = Host!.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos);
        Weather.SetPattern("overcast");
        Assert.Equal("overcast", Weather.PatternAt(spawn));
        Weather.SetPattern("clearsky", at: spawn);
        Assert.Equal("clearsky", Weather.PatternAt(spawn));

        Weather.SetWind("still");
        Assert.True(Weather.WindSpeedAt(spawn.ToVec3d()) < 0.05, $"Still wind reads {Weather.WindSpeedAt(spawn.ToVec3d())}");
        Weather.SetWind("storm");
        Assert.True(Weather.WindSpeedAt(spawn.ToVec3d()) > 0.5, $"A storm reads {Weather.WindSpeedAt(spawn.ToVec3d())}");

        Weather.SetPrecipitation(1);
        Assert.Equal(1f, Weather.PrecipitationOverride);
        Assert.Equal(1f, Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetClimateAt(spawn, EnumGetClimateMode.NowValues)!.Rainfall), 3);
        Weather.SetPrecipitation(null);
        Assert.Null(Weather.PrecipitationOverride);

        ArgumentException error = Assert.Throws<ArgumentException>(() => Weather.SetPattern("pharosfog"));
        Assert.Contains("overcast", error.Message);
    }

    [ServerScenario]
    public void StoppedWeather_DoesNotChangeOnItsOwn()
    {
        Calendar.Freeze();
        BlockPos spawn = Host!.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos);
        Weather.SetPattern("overcast");

        Weather.StopChanges();
        Assert.True(Weather.ChangesStopped);
        Assert.False(TickRegionPastItsPattern(spawn), "Stopped weather started a change");

        Weather.ResumeChanges();
        Assert.False(Weather.ChangesStopped);
        Assert.True(TickRegionPastItsPattern(spawn), "Weather did not change once allowed to");
    }

    [ServerScenario]
    public void ASnapshot_RestoresTheCalendarAndTheWeather()
    {
        BlockPos spawn = Host!.RunOnGameThread(() => Server!.DefaultSpawnPosition.AsBlockPos);
        Weather.SetPattern("clearsky");
        double hours = Calendar.TotalHours;
        WorldSnapshot snapshot = Host.TakeSnapshot();

        Calendar.Freeze();
        Calendar.SetDate(3, 2, 2);
        Weather.SetPattern("overcast");
        Weather.SetPrecipitation(1);
        Weather.StopChanges();

        Host.RestoreSnapshot(snapshot);

        Assert.Equal(hours, Calendar.TotalHours, 3);
        Assert.False(Calendar.IsFrozen);
        Assert.Equal("clearsky", Weather.PatternAt(spawn));
        Assert.Null(Weather.PrecipitationOverride);
        Assert.False(Weather.ChangesStopped);
    }

    // Makes the region's pattern due to end, then runs its weather once, as the game does every
    // 25 ms of wall time. Returns whether a change began.
    private bool TickRegionPastItsPattern(BlockPos pos) => Host!.RunOnGameThread(() =>
    {
        object system = Api!.ModLoader.GetModSystem("Vintagestory.GameContent.WeatherSystemServer")!;
        int size = Api.World.BlockAccessor.RegionSize;
        object region = Call(system, "getOrCreateWeatherSimForRegion", pos.X / size, pos.Z / size)!;
        object state = Get(Get(region, "NewWePattern")!, "State")!;
        Set(state, "ActiveUntilTotalHours", Api.World.Calendar.TotalHours - 1);
        Set(region, "Transitioning", false);
        Call(region, "TickEvery25ms", 0.025f);
        return (bool)Get(region, "Transitioning")!;
    });

    private async Task TickForAsync(TimeSpan wallTime)
    {
        DateTime until = DateTime.UtcNow + wallTime;
        while (DateTime.UtcNow < until)
        {
            Host!.Tick();
            await Task.Delay(20);
        }
    }

    private const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static object? Get(object target, string name) =>
        target.GetType().GetField(name, Any)?.GetValue(target) ?? target.GetType().GetProperty(name, Any)?.GetValue(target);

    private static void Set(object target, string name, object? value)
    {
        if (target.GetType().GetField(name, Any) is { } field) field.SetValue(target, value);
        else target.GetType().GetProperty(name, Any)!.SetValue(target, value);
    }

    private static object? Call(object target, string name, params object[] args) =>
        target.GetType().GetMethods(Any).First(m => m.Name == name && m.GetParameters().Length == args.Length && m.GetParameters()[0].ParameterType == args[0].GetType()).Invoke(target, args);
}

/// <summary>
/// The calendar and weather put back between the tests of a rolled-back class. Every test does the
/// same, so whichever runs later checks what the ones before it left.
/// </summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveCalendarRollbackTests : ServerScenarioBase
{
    private static double? s_baselineHours;

    [ServerScenario]
    public void First() => FindsTheBaselineAndChangesIt();

    [ServerScenario]
    public void Second() => FindsTheBaselineAndChangesIt();

    [ServerScenario]
    public void Third() => FindsTheBaselineAndChangesIt();

    private void FindsTheBaselineAndChangesIt()
    {
        // No player joins, so the clock stands still and the baseline is exact.
        double hours = Host!.Calendar.TotalHours;
        s_baselineHours ??= hours;
        Assert.Equal(s_baselineHours.Value, hours, 6);
        Assert.False(Host.Calendar.IsFrozen);
        Assert.Null(Host.Weather.PrecipitationOverride);
        Assert.False(Host.Weather.ChangesStopped);

        Host.Calendar.Freeze();
        Host.Calendar.Advance(TimeSpan.FromDays(40));
        Host.Weather.SetPrecipitation(-1);
        Host.Weather.StopChanges();
    }
}

/// <summary>The client's calendar following the server's.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveCalendarSyncTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 };

    [ClientServerScenario]
    public Task First() => FollowsTheServerAsync();

    [ClientServerScenario]
    public Task Second() => FollowsTheServerAsync();

    private async Task FollowsTheServerAsync()
    {
        // What the previous test left was rolled back, and the client followed.
        Assert.False(ServerHost!.Calendar.IsFrozen);
        await Session!.WaitForCalendarSyncAsync();

        ServerHost.Calendar.Freeze();
        ServerHost.Calendar.SetTime(hourOfDay: 22);
        await Session.WaitForCalendarSyncAsync();

        float client = Client!.RunOnClientThread(() => Client.Client.Calendar.HourOfDay);
        Assert.True(client >= 22, $"The client reads {client}");
        Assert.Equal(ServerHost.Calendar.HourOfDay, client);

        await Session.StepFramesAsync(30);
        Assert.Equal(client, Client.RunOnClientThread(() => Client.Client.Calendar.HourOfDay));

        ServerHost.Calendar.Advance(TimeSpan.FromHours(5));
        await Session.WaitForCalendarSyncAsync();
        Assert.Equal(3f, Client.RunOnClientThread(() => Client.Client.Calendar.HourOfDay));

        ServerHost.Weather.SetPrecipitation(1);
        BlockPos at = Client.RunOnClientThread(() => Client.Client.EntityPlayer.Pos.AsBlockPos);
        Assert.True(await Session.StepUntilAsync(
            () => Client.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetClimateAt(at, EnumGetClimateMode.NowValues)?.Rainfall >= 0.99f),
            maxFrames: 120), "The client did not take the precipitation override");
    }
}
