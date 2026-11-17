using System.Globalization;

namespace Cccc.Domain;

/// <summary>Weeks run Monday to Sunday, numbered by ISO 8601.</summary>
public static class Week
{
    public static DateOnly MondayOf(DateOnly date) => date.AddDays(-DayIndex(date.DayOfWeek));

    /// <summary>Monday = 0 … Sunday = 6.</summary>
    public static int DayIndex(DayOfWeek day) => ((int)day + 6) % 7;

    public static DateOnly DateOf(DateOnly monday, DayOfWeek day) => MondayOf(monday).AddDays(DayIndex(day));

    public static IEnumerable<DateOnly> Days(DateOnly monday)
    {
        var m = MondayOf(monday);
        for (int i = 0; i < 7; i++) yield return m.AddDays(i);
    }

    public static int IsoNumber(DateOnly date) => ISOWeek.GetWeekOfYear(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// The week the Scratch Week's classes are dated in (<see cref="Editing.ScratchWeek"/>): Monday 6
    /// January 2200, past any semester this timetable will hold and far enough from
    /// <see cref="DateOnly.MaxValue"/> that stepping a week either side cannot overflow. It is in the
    /// future, so no week-has-ended rule ever applies to it. It is never shown: the words below say
    /// "Scratch Week" instead of its dates. The current app stamps its own Scratch Week with the same
    /// Monday (1.67.0), so one brought in from there lands on the same days.
    /// </summary>
    public static readonly DateOnly ScratchMonday = new(2200, 1, 6);

    /// <summary>Whether the date falls in the Scratch Week.</summary>
    public static bool IsScratch(DateOnly anyDateInWeek) => MondayOf(anyDateInWeek) == ScratchMonday;

    /// <summary>The week has wholly ended: its Sunday is before today. Such a week is locked.</summary>
    public static bool HasEnded(DateOnly anyDateInWeek, DateOnly today) => MondayOf(anyDateInWeek).AddDays(6) < today;

    /// <summary>"Week 37, 7–13 September 2026" — dates and year, because week numbers repeat.</summary>
    public static string ForDocuments(DateOnly anyDateInWeek) =>
        IsScratch(anyDateInWeek) ? "the Scratch Week" : $"Week {IsoNumber(anyDateInWeek)}, {Range(anyDateInWeek)}";

    /// <summary>"7–13 September 2026", the week's Monday to Sunday, for beside a heading that already says the week.</summary>
    public static string Range(DateOnly anyDateInWeek)
    {
        if (IsScratch(anyDateInWeek)) return "the Scratch Week";
        var m = MondayOf(anyDateInWeek);
        var s = m.AddDays(6);
        var inv = CultureInfo.InvariantCulture;
        return m.Month == s.Month
            ? $"{m.Day}–{s.Day} {s.ToString("MMMM yyyy", inv)}"
            : m.Year == s.Year
                ? $"{m.Day} {m.ToString("MMMM", inv)} – {s.Day} {s.ToString("MMMM yyyy", inv)}"
                : $"{m.Day} {m.ToString("MMMM yyyy", inv)} – {s.Day} {s.ToString("MMMM yyyy", inv)}";
    }

    public static string ShortDay(DayOfWeek day) => day.ToString()[..3];

    /// <summary>"Wed 18 Nov"; a day of the Scratch Week has no date, so it is its day alone ("Wed").</summary>
    public static string ShortDate(DateOnly date) => IsScratch(date)
        ? ShortDay(date.DayOfWeek)
        : $"{ShortDay(date.DayOfWeek)} {date.Day} {date.ToString("MMM", CultureInfo.InvariantCulture)}";

    public static string Hm(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// The time from one time of day to another: negative when it is earlier. Never <c>to - from</c>, because
    /// one TimeOnly taken from another goes round the clock: 09:00 − 13:00 is twenty hours, not minus four.
    /// </summary>
    public static TimeSpan Span(TimeOnly from, TimeOnly to) => to.ToTimeSpan() - from.ToTimeSpan();

    /// <summary>"3h", "1h 30m", "45m".</summary>
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) return "-" + Duration(-t);
        int h = (int)t.TotalHours, m = t.Minutes;
        return h == 0 ? $"{m}m" : m == 0 ? $"{h}h" : $"{h}h {m}m";
    }
}

/// <summary>
/// Every name a person reads is ordered in Danish: Æ Ø Å after Z, and Aa as Å. Never ambient: nothing
/// about CurrentCulture changes.
/// </summary>
public static class NameOrder
{
    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo("da-DK");
    public static StringComparer Comparer { get; } = StringComparer.Create(Culture, ignoreCase: true);
}
