using System.Diagnostics;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Reads and sets an embedded server's calendar: the time of day, the date, the season, and
/// whether time passes.
/// </summary>
/// <remarks>
/// <para>
/// The game's clock follows the wall clock, not ticks: while players are connected it moves 30
/// game seconds for every real second by default, and with no player connected it stands still.
/// A test that needs exact times calls <see cref="Freeze"/> and then sets them.
/// </para>
/// <para>
/// Every change lands on a whole second and is sent to connected clients at once, as
/// <c>/time</c> does. Clients count seconds in whole numbers, so a frozen calendar reads the same
/// on the server and on a client that has caught up; see
/// <see cref="ClientServerLoopbackSession.WaitForCalendarSyncAsync"/>. Everything runs on the
/// server's game thread.
/// </para>
/// </remarks>
public sealed class CalendarDriver
{
    private readonly EmbeddedServerHost _host;

    // The multiplier the calendar had before Freeze set it to 0.
    private float? _frozenMultiplier;

    internal CalendarDriver(EmbeddedServerHost host)
    {
        _host = host;
    }

    /// <summary>Hours since the world's calendar began.</summary>
    public double TotalHours => Read(c => c.TotalHours);

    /// <summary>Days since the world's calendar began.</summary>
    public double TotalDays => Read(c => c.TotalDays);

    /// <summary>The time of day, in hours from 0 up to <see cref="HoursPerDay"/>.</summary>
    public float HourOfDay => Read(c => c.HourOfDay);

    /// <summary>The year, counted from 0.</summary>
    public int Year => Read(c => c.Year);

    /// <summary>
    /// The month, from 1 to 12, as <see cref="SetDate"/> counts it. At midnight on a month's first
    /// day the game's own <c>Month</c> still shows the month before.
    /// </summary>
    public int Month => Read(c => c.DayOfYear / c.DaysPerMonth + 1);

    /// <summary>The day of the month, from 1 to <see cref="DaysPerMonth"/>.</summary>
    public int DayOfMonth => Read(c => c.DayOfMonth);

    /// <summary>How many days a month has in this world.</summary>
    public int DaysPerMonth => Read(c => c.DaysPerMonth);

    /// <summary>How many hours a day has in this world.</summary>
    public float HoursPerDay => Read(c => c.HoursPerDay);

    /// <summary>The season at the world's default spawn point.</summary>
    public EnumSeason Season => Read(c => c.GetSeason(_host.Server.DefaultSpawnPosition.AsBlockPos));

    /// <summary>The season at <paramref name="pos"/>, which depends on its latitude.</summary>
    public EnumSeason GetSeason(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return Read(c => c.GetSeason(pos));
    }

    /// <summary>
    /// Whether time stands still: after <see cref="Freeze"/>, or after <c>/time stop</c> or
    /// anything else that brought the speed of time to 0.
    /// </summary>
    public bool IsFrozen => Read(c => c.SpeedOfTime * c.CalendarSpeedMul == 0);

    /// <summary>
    /// Whether time is passing: the calendar runs, which the game allows only while players are
    /// connected, and it is not frozen.
    /// </summary>
    public bool IsRunning => Read(c => c.IsRunning && c.SpeedOfTime * c.CalendarSpeedMul != 0);

    /// <summary>
    /// Moves forward to the next time the clock shows <paramref name="hourOfDay"/>, as
    /// <c>/time set</c> does. A time within the current second counts as now.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The hour is outside the day.</exception>
    public void SetTime(float hourOfDay) => Change(calendar =>
    {
        if (!(hourOfDay >= 0) || hourOfDay >= calendar.HoursPerDay)
        {
            throw new ArgumentOutOfRangeException(nameof(hourOfDay), hourOfDay, $"The hour of day must be from 0 up to {calendar.HoursPerDay}.");
        }

        long now = (long)Internals.Timespan(calendar).TotalSeconds;
        Set(calendar, CalendarMath.NextTimeOfDay(now, hourOfDay, calendar.HoursPerDay));
    });

    /// <summary>
    /// Moves to <paramref name="day"/> of <paramref name="month"/> in <paramref name="year"/>,
    /// keeping the time of day. The date can be earlier than now.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The date does not exist in this world's calendar.</exception>
    public void SetDate(int year, int month, int day) => Change(calendar =>
    {
        ArgumentOutOfRangeException.ThrowIfNegative(year);
        if (month < 1 || month > calendar.MonthsPerYear) throw new ArgumentOutOfRangeException(nameof(month), month, $"The month must be from 1 to {calendar.MonthsPerYear}.");
        if (day < 1 || day > calendar.DaysPerMonth) throw new ArgumentOutOfRangeException(nameof(day), day, $"The day must be from 1 to {calendar.DaysPerMonth}.");

        long now = (long)Internals.Timespan(calendar).TotalSeconds;
        Set(calendar, CalendarMath.DateToSeconds(now, year, month, day, calendar.HoursPerDay, calendar.DaysPerMonth, calendar.MonthsPerYear));
    });

    /// <summary>
    /// Moves time forward by <paramref name="gameTime"/> at once, as <c>/time add</c> does. Game
    /// systems that work from the time passed, such as crops and snow, catch up on their next
    /// update.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="gameTime"/> is negative.</exception>
    public void Advance(TimeSpan gameTime)
    {
        if (gameTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(gameTime), gameTime, "Time only moves forward.");
        Change(calendar => Set(calendar, (long)Math.Round((Internals.Timespan(calendar) + gameTime).TotalSeconds)));
    }

    /// <summary>
    /// Stops time on the server and on its clients, at the current whole second, as
    /// <c>/time stop</c> does in effect: hunger and health regeneration stop with it. Weather keeps
    /// changing; see <see cref="WeatherDriver.StopChanges"/>.
    /// </summary>
    public void Freeze() => Change(calendar =>
    {
        Set(calendar, (long)Internals.Timespan(calendar).TotalSeconds);
        if (calendar.CalendarSpeedMul != 0)
        {
            _frozenMultiplier = calendar.CalendarSpeedMul;
            calendar.CalendarSpeedMul = 0;
        }
    });

    /// <summary>Lets time pass again after <see cref="Freeze"/>.</summary>
    public void Unfreeze() => Change(calendar =>
    {
        if (_frozenMultiplier is { } multiplier && calendar.CalendarSpeedMul == 0)
        {
            calendar.CalendarSpeedMul = multiplier;
        }

        _frozenMultiplier = null;
    });

    private T Read<T>(System.Func<GameCalendar, T> read) => _host.RunOnGameThread(() => read(Calendar));

    private GameCalendar Calendar => (GameCalendar)_host.Server.Calendar;

    // Banks the time the wall clock has run since the game last looked, so the change starts from
    // now, then sends the result to every client.
    private void Change(Action<GameCalendar> change) => _host.RunOnGameThread(() =>
    {
        GameCalendar calendar = Calendar;
        calendar.Tick();
        change(calendar);
        calendar.Update();
        Broadcast();
    });

    private static void Set(GameCalendar calendar, long totalSeconds) =>
        Internals.Timespan(calendar) = TimeSpan.FromSeconds(totalSeconds);

    /// <summary>Sends the calendar to every connected client. Runs on the game thread.</summary>
    internal void Broadcast()
    {
        GameCalendar calendar = Calendar;
        _host.Server.BroadcastPacket(calendar.ToPacket());
    }

    /// <summary>The calendar as it is now, for a world snapshot. Runs on the game thread.</summary>
    internal Saved Capture()
    {
        GameCalendar calendar = Calendar;
        calendar.Tick();
        return new Saved(
            Internals.Timespan(calendar), Internals.TotalSecondsStart(calendar), new Dictionary<string, float>(calendar.TimeSpeedModifiers),
            calendar.CalendarSpeedMul, calendar.HoursPerDay, calendar.DaysPerMonth, _frozenMultiplier);
    }

    /// <summary>Puts the calendar back as <paramref name="saved"/> holds it. Runs on the game thread.</summary>
    internal void Restore(Saved saved)
    {
        GameCalendar calendar = Calendar;

        // Bank first: the restored time starts from now, not from the last tick.
        calendar.Tick();
        calendar.HoursPerDay = saved.HoursPerDay;
        calendar.DaysPerMonth = saved.DaysPerMonth;

        // The game shares this dictionary with the save game: refill it rather than replace it.
        Dictionary<string, float> modifiers = calendar.TimeSpeedModifiers;
        modifiers.Clear();
        foreach ((string name, float speed) in saved.Modifiers) modifiers[name] = speed;
        calendar.CalendarSpeedMul = saved.SpeedMultiplier;

        Internals.Timespan(calendar) = saved.Time;
        Internals.TotalSecondsStart(calendar) = saved.TotalSecondsStart;
        _frozenMultiplier = saved.FrozenMultiplier;
        calendar.Update();
        Broadcast();
    }

    /// <summary>A calendar as a snapshot holds it.</summary>
    internal sealed record Saved(
        TimeSpan Time, long TotalSecondsStart, IReadOnlyDictionary<string, float> Modifiers,
        float SpeedMultiplier, float HoursPerDay, int DaysPerMonth, float? FrozenMultiplier);

    /// <summary>The game's fields, read and written where they are.</summary>
    internal static class Internals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "timespan")]
        public static extern ref TimeSpan Timespan(GameCalendar calendar);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "totalSecondsStart")]
        public static extern ref long TotalSecondsStart(GameCalendar calendar);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "watchIngameTime")]
        public static extern ref Stopwatch Watch(GameCalendar calendar);

        /// <summary>
        /// The calendar's time in seconds including what its wall clock has run since its last
        /// tick, without changing it: where it would be if it ticked now.
        /// </summary>
        public static double ProjectedSeconds(GameCalendar calendar)
        {
            Stopwatch watch = Watch(calendar);
            double pending = watch.IsRunning ? watch.Elapsed.TotalSeconds * calendar.SpeedOfTime * calendar.CalendarSpeedMul : 0;
            return Timespan(calendar).TotalSeconds + pending;
        }
    }
}

/// <summary>Calendar arithmetic on whole seconds.</summary>
internal static class CalendarMath
{
    /// <summary>
    /// The first whole second at or after <paramref name="nowSeconds"/> at which the clock shows
    /// <paramref name="hourOfDay"/>.
    /// </summary>
    public static long NextTimeOfDay(long nowSeconds, float hourOfDay, float hoursPerDay)
    {
        // The game divides by the day's length as a float, not in whole seconds.
        double day = hoursPerDay * 3600.0;
        double dayStart = Math.Floor(nowSeconds / day) * day;
        long target = (long)Math.Ceiling(dayStart + hourOfDay * 3600.0 - 1e-6);
        return target < nowSeconds ? (long)Math.Ceiling(dayStart + day + hourOfDay * 3600.0 - 1e-6) : target;
    }

    /// <summary>
    /// The second at which <paramref name="day"/> of <paramref name="month"/> in
    /// <paramref name="year"/> shows the same time of day as <paramref name="nowSeconds"/>.
    /// </summary>
    public static long DateToSeconds(long nowSeconds, int year, int month, int day, float hoursPerDay, int daysPerMonth, int monthsPerYear)
    {
        double dayLength = hoursPerDay * 3600.0;
        double timeOfDay = nowSeconds - Math.Floor(nowSeconds / dayLength) * dayLength;
        long days = (long)year * daysPerMonth * monthsPerYear + (long)(month - 1) * daysPerMonth + (day - 1);
        return (long)Math.Round(days * dayLength + timeOfDay);
    }
}
