namespace Cccc.Web.Infrastructure;

/// <summary>
/// The one clock. "Today" is Copenhagen's date, never the server's, so a week ends at a Copenhagen
/// midnight. Development may pin the date (<c>Clock:Today</c>) so a preview is about a known week.
/// </summary>
public sealed class SchoolClock
{
    private readonly TimeZoneInfo _zone;
    private readonly DateOnly? _pinned;

    public SchoolClock(IConfiguration config, IHostEnvironment env, ILogger<SchoolClock> log)
    {
        try { _zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen"); }
        catch (TimeZoneNotFoundException)
        {
            log.LogWarning("The Europe/Copenhagen time zone is not installed; using UTC. Install tzdata.");
            _zone = TimeZoneInfo.Utc;
        }
        if (env.IsDevelopment() && DateOnly.TryParse(config["Clock:Today"], out var d)) _pinned = d;
    }

    public TimeZoneInfo Zone => _zone;

    public DateTimeOffset Now => DateTimeOffset.UtcNow;

    public DateTimeOffset Local(DateTimeOffset t) => TimeZoneInfo.ConvertTime(t, _zone);

    public DateOnly Today => _pinned ?? DateOnly.FromDateTime(Local(Now).DateTime);

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
