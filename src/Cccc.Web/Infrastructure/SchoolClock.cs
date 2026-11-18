namespace Cccc.Web.Infrastructure;

/// <summary>
/// The one clock. "Today" is Copenhagen's date, never the server's, so a week ends at a Copenhagen
/// midnight. Development may move it to another day of this week (<c>Clock:Day</c>, a weekday) and set
/// the time of day (<c>Clock:Time</c>), from which the clock runs on: a preview of Now or a screen is
/// about the middle of a school day whenever it is looked at, and a walk written for the sample week's
/// Wednesday is about a Wednesday whatever day it is run on. Both move the instant itself, so today, the
/// time and every change's time stamp agree.
/// </summary>
public sealed class SchoolClock
{
    private readonly TimeZoneInfo _zone;
    private readonly TimeSpan _shift;

    public SchoolClock(IConfiguration config, IHostEnvironment env, ILogger<SchoolClock> log)
    {
        try { _zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"); }
        catch (TimeZoneNotFoundException)
        {
            log.LogWarning("The Europe/Copenhagen time zone is not installed; using UTC. Install tzdata.");
            _zone = TimeZoneInfo.Utc;
        }
        if (!env.IsDevelopment()) return;
        var wanted = Weekday(config["Clock:Day"]);
        bool timed = TimeOnly.TryParse(config["Clock:Time"], System.Globalization.CultureInfo.InvariantCulture, out var at);
        if (wanted is null && !timed) return;
        var utc = DateTimeOffset.UtcNow;
        var local = Local(utc).DateTime;
        var day = DateOnly.FromDateTime(local);
        if (wanted is { } d) day = Cccc.Domain.Week.MondayOf(day).AddDays(((int)d + 6) % 7);
        // Through the zone, so a day across a change to or from summer time still reads the time asked for.
        var target = day.ToDateTime(timed ? at : TimeOnly.FromDateTime(local));
        if (_zone.IsInvalidTime(target)) target = target.AddHours(1);
        _shift = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(target, _zone), TimeSpan.Zero) - utc;
    }

    /// <summary>"wed", "Wednesday" or "WED" as a weekday; anything else is no day asked for.</summary>
    public static DayOfWeek? Weekday(string? name) =>
        name is { Length: >= 2 } n
            ? Enum.GetValues<DayOfWeek>().Cast<DayOfWeek?>().FirstOrDefault(d => d!.Value.ToString().StartsWith(n.Trim(), StringComparison.OrdinalIgnoreCase))
            : null;

    public TimeZoneInfo Zone => _zone;

    public DateTimeOffset Now => DateTimeOffset.UtcNow + _shift;

    public DateTimeOffset Local(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, _zone);

    public DateOnly Today => DateOnly.FromDateTime(Local(Now).DateTime);

    /// <summary>The time of day in Copenhagen now: what has started and what is still ahead.</summary>
    public TimeOnly TimeOfDay => TimeOnly.FromDateTime(Local(Now).DateTime);

    /// <summary>The moment a day begins in Copenhagen: midnight there, whatever the offset that day.</summary>
    public DateTimeOffset StartOf(DateOnly day) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue), _zone), TimeSpan.Zero);

    /// <summary>A time a person reads: Copenhagen's, in words they use.</summary>
    public string Describe(DateTimeOffset t)
    {
        var local = Local(t);
        var day = DateOnly.FromDateTime(local.DateTime);
        var time = local.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
        if (day == Today) return "today " + time;
        if (day == Today.AddDays(-1)) return "yesterday " + time;
        if (day < Today && day > Today.AddDays(-7)) return day.DayOfWeek + " " + time;
        return local.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture) + " " + time;
    }
}
