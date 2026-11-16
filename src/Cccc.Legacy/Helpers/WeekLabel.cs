using System.Globalization;

namespace CCCCScheduler.Helpers;

/// <summary>
/// Formats the ISO week that contains a given date, e.g. "Week 26 (22nd–28th June)".
/// Weeks run Monday–Sunday; the range is cross-month aware ("30th June–6th July").
/// </summary>
public static class WeekLabel
{
    public static DateOnly MondayOf(DateOnly d)
    {
        int offset = ((int)d.DayOfWeek + 6) % 7; // Monday = 0 … Sunday = 6
        return d.AddDays(-offset);
    }

    public static int IsoWeek(DateOnly d) => ISOWeek.GetWeekOfYear(d.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// <b>The seven days as a column head calls them, Monday first</b> — the index every row in this
    /// app already uses (<c>SessionRow.DayOrder</c>).
    ///
    /// <para><b>Here rather than private to the calendar</b>, because there are two surfaces drawing
    /// day names now: the week grid's column heads and the narrow day strip Design's 6c puts above a
    /// day at a time. A second array would be a second thing to keep in step, and a day strip reading
    /// <i>Tues</i> over a column reading <i>Tue</i> is the kind of drift nothing goes red for.</para>
    ///
    /// <para>Written out rather than formatted. <c>"ddd"</c> is culture-sensitive, and the one place
    /// this app lets a culture decide anything is <c>NameOrder</c>'s Danish collation for names a
    /// person reads — a column head is not one of those, and the invariant abbreviations are not
    /// guaranteed to stay three letters (invariant <c>MMM</c> already yields <i>Sept</i>, which cost
    /// a layout here once).</para>
    /// </summary>
    public static IReadOnlyList<string> ShortDayNames { get; } =
        new[] { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };

    /// <summary>
    /// <b>Which week, on its own</b> — "Week 38". The half of <see cref="Format"/> that answers the
    /// question a heading is asking, split out on 2026-09-16 so the week screen can draw the number
    /// as its title and the dates beside it in quieter ink, which is the designer's turn 1.
    ///
    /// <para><b><see cref="Format"/> is still assembled from these two and is unchanged to the
    /// character</b>, which is the point of splitting rather than adding: everything that already
    /// prints a week — the forms, the week box's label, the specs that compare against it — keeps
    /// reading one formatter, and the screen composes the same string out of its own two spans.</para>
    /// </summary>
    public static string Number(DateOnly anyDateInWeek) => $"Week {IsoWeek(MondayOf(anyDateInWeek))}";

    /// <summary>
    /// <b>Which dates, on their own</b> — "21st–27th September", or "28th September–4th October"
    /// across a month. The other half of <see cref="Format"/>; see <see cref="Number"/>.
    /// </summary>
    public static string Range(DateOnly anyDateInWeek)
    {
        var mon = MondayOf(anyDateInWeek);
        var sun = mon.AddDays(6);

        return mon.Month == sun.Month
            ? $"{Ordinal(mon.Day)}–{Ordinal(sun.Day)} {Month(sun)}"
            : $"{Ordinal(mon.Day)} {Month(mon)}–{Ordinal(sun.Day)} {Month(sun)}";
    }

    public static string Format(DateOnly anyDateInWeek)
        => $"{Number(anyDateInWeek)} ({Range(anyDateInWeek)})";

    /// <summary>
    /// <b>The same week as a DOCUMENT says it</b> — "Week 37, 7–13 September 2026" — cross-month
    /// ("Week 40, 28 September–4 October 2026") and cross-year ("Week 1, 29 December 2025–4 January
    /// 2026") aware.
    ///
    /// <para><b>A second formatter rather than a change to <see cref="Format"/>.</b> That one is
    /// what the toolbar's week box and every form say, and it is deliberately short because it sits
    /// beside a control the reader is already looking at. A sheet of paper has neither: Craig's
    /// call, 2026-09-10 — the school will keep a folder of these, and a week number repeats every
    /// year, so a printed week carries its YEAR. The ordinals go with it; "7th–13th" is how the app
    /// talks and "7–13 September 2026" is how a document does.</para>
    ///
    /// <para>The year is printed ONCE when the week sits inside one, and twice only for the week
    /// that straddles New Year — where naming one of the two would be worse than naming neither.
    /// The month is printed twice only when the week straddles two.</para>
    /// </summary>
    public static string ForDocuments(DateOnly anyDateInWeek)
    {
        var mon = MondayOf(anyDateInWeek);
        var sun = mon.AddDays(6);

        string range = mon.Year != sun.Year
            ? $"{mon.Day} {Month(mon)} {mon.Year}–{sun.Day} {Month(sun)} {sun.Year}"
            : mon.Month != sun.Month
                ? $"{mon.Day} {Month(mon)}–{sun.Day} {Month(sun)} {sun.Year}"
                : $"{mon.Day}–{sun.Day} {Month(sun)} {sun.Year}";

        return $"Week {IsoWeek(mon)}, {range}";
    }

    /// <summary>
    /// <b>The week as a FILE NAME leads with it</b> — "Week 37 2026". Craig's call, 2026-09-10:
    /// <i>"Use 'CCCC Scheduler', week first"</i>, with the year because a folder of these spans more
    /// than one and a week number does not.
    ///
    /// <para><b>The year is the ISO week-numbering year, never the calendar year of the Monday.</b>
    /// Week 1 can begin in December and week 52 or 53 can end in January, so pairing "Week 1" with
    /// 2025 because its Monday fell there would file it a year away from the six days of it that
    /// are 2026. It is the year that makes the week number mean something, so it is the year that
    /// goes beside it.</para>
    /// </summary>
    public static string ForFileName(DateOnly anyDateInWeek)
    {
        var mon = MondayOf(anyDateInWeek);
        return $"Week {IsoWeek(mon)} {ISOWeek.GetYear(mon.ToDateTime(TimeOnly.MinValue))}";
    }

    private static string Month(DateOnly d) => d.ToString("MMMM", CultureInfo.InvariantCulture);

    public static string Ordinal(int day)
    {
        string suffix = (day % 100) is >= 11 and <= 13
            ? "th"
            : (day % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return day + suffix;
    }
}
