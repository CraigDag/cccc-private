using CCCCScheduler.Models;

namespace CCCCScheduler.Helpers;

/// <summary>
/// <b>The twelve months a <c>.cccc</c> covers</b>, derived from the term the project already carries
/// rather than stored a second time. The owner's decisions, 2026-09-13, made when he asked for a file
/// to be an archive of a year rather than of a term:
///
/// <list type="number">
/// <item><i>"The file should be for one academic year minimum."</i> A file per term is off the
/// table.</item>
/// <item>It is the <b>building's</b> twelve months, not the teaching year: <i>"there is no date on
/// the calendar untouched in a year… the real academic year will end before summer but the building
/// calendar runs the full year."</i> That is why a summer intensive in July is inside the year rather
/// than an exception to it.</item>
/// <item><i>"We'd run all the way until Sept 6 next year as the building year range, set by the
/// student day 1."</i> <b>One date typed and the end derived</b> — from it, here.</item>
/// </list>
///
/// <para><b>Since schema 14 a project holds a LIST of terms, and the year opens with the first of
/// them</b> — <see cref="ProjectFile.FirstTerm"/>, the earliest-starting one. Nothing else here
/// changed: the year is still one date typed and an end derived, and the later terms are inside it
/// rather than beside it. A term that starts before the first one is not a second answer to when the
/// year opens; it simply becomes the first.</para>
///
/// <para><b>There is deliberately no second date field.</b> The year starts when the first term does,
/// which is his decision and also the thing that cannot drift: a start date stored beside a term
/// start is two answers to one question, and this app has been caught by that shape before. The term
/// is the <i>teaching</i> span inside the year and stays exactly what it was — see
/// <see cref="ScheduleResolver.TermWeeks"/>, which is unchanged and still what the reports, the
/// registers and the weekly targets read.</para>
///
/// <para><b>Derived by calendar date, then rounded back to a Sunday.</b> Not as a fixed 52 weeks:
/// 2027–28 runs 13 Sep 2027 to 12 Sep 2028, which is 366 days, so a week count loses a day every
/// fourth year and the files stop tiling. Rounding the end back to a Sunday is what keeps the year
/// whole weeks, so no week straddles two files and next year's opens on the Monday after this one
/// closes: 7 Sep 2026 gives 5 Sep 2027, 52 whole weeks, and 2027–28 opens on 6 Sep.</para>
/// </summary>
public static class AcademicYear
{
    /// <summary>The Monday the year opens on — the term's own first Monday, normalised.</summary>
    public static DateOnly FirstMonday(TermInfo term)
    {
        ArgumentNullException.ThrowIfNull(term);
        return WeekLabel.MondayOf(term.StartMonday);
    }

    /// <summary>
    /// The Sunday the year closes on: a calendar year after <see cref="FirstMonday"/>, then back to
    /// the nearest Sunday on or before that, so the span is whole weeks. Always at least one week
    /// after the start, which matters only for the degenerate dates a hand-edited file can hold.
    /// </summary>
    public static DateOnly LastSunday(TermInfo term)
    {
        var first = FirstMonday(term);
        var provisional = first.AddYears(1).AddDays(-1);
        // Back to the Sunday on or before: Sunday is 6 in Monday-first terms, so drop the remainder.
        var lastMonday = WeekLabel.MondayOf(provisional);
        var sunday = lastMonday.AddDays(6) <= provisional ? lastMonday.AddDays(6) : lastMonday.AddDays(-1);
        return sunday < first.AddDays(6) ? first.AddDays(6) : sunday;
    }

    /// <summary>Every Monday in the year, in order. 52 of them for a year that does not span a leap day.</summary>
    public static IEnumerable<DateOnly> Weeks(TermInfo term)
    {
        var last = LastSunday(term).AddDays(-6);
        for (var m = FirstMonday(term); m <= last; m = m.AddDays(7))
            yield return m;
    }

    /// <summary>Whether the week containing <paramref name="anyDateInWeek"/> is inside the year.</summary>
    public static bool Contains(TermInfo term, DateOnly anyDateInWeek)
    {
        var m = WeekLabel.MondayOf(anyDateInWeek);
        return m >= FirstMonday(term) && m <= LastSunday(term).AddDays(-6);
    }

    /// <summary>
    /// <b>A year of Mondays beginning at <paramref name="anyDateInWeek"/></b> — what a project with no
    /// term at all offers instead of <see cref="Weeks"/>. It is not the school's academic year and
    /// does not pretend to be; it is a year's worth of weeks from where you are standing, which is
    /// enough for the copy picker to have something to list. Set a term and the real year replaces it.
    /// </summary>
    public static IEnumerable<DateOnly> AYearFrom(DateOnly anyDateInWeek)
    {
        var first = WeekLabel.MondayOf(anyDateInWeek);
        for (int i = 0; i < 52; i++) yield return first.AddDays(7 * i);
    }

    /// <summary>
    /// The Monday this project's year opens on, or null when it has no term to open with.
    /// </summary>
    public static DateOnly? FirstMondayOf(ProjectFile project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.FirstTerm is { } t ? FirstMonday(t) : null;
    }

    /// <summary>
    /// The Sunday this project's year closes on, or null when it has no term to open with.
    /// </summary>
    public static DateOnly? LastSundayOf(ProjectFile project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.FirstTerm is { } t ? LastSunday(t) : null;
    }

    /// <summary>
    /// Whether the week containing <paramref name="anyDateInWeek"/> is inside this project's year.
    /// <b>True when the project has no term</b>: a file with no year has no outside, exactly as a
    /// project with no term has always had no week that is "outside the term".
    /// </summary>
    public static bool ContainsWeek(ProjectFile project, DateOnly anyDateInWeek)
    {
        ArgumentNullException.ThrowIfNull(project);
        return project.FirstTerm is not { } t || Contains(t, anyDateInWeek);
    }

    /// <summary>
    /// <b>The weeks a project can put a timetable on</b>: the academic year when it has a term, else a
    /// year from <paramref name="anyDateInWeek"/>. The one answer the copy picker and the blank-week
    /// notice both read, so the two cannot come to describe different years.
    /// </summary>
    public static IReadOnlyList<DateOnly> WeeksFor(ProjectFile project, DateOnly anyDateInWeek)
    {
        ArgumentNullException.ThrowIfNull(project);
        var weeks = (project.FirstTerm is { } term ? Weeks(term) : AYearFrom(anyDateInWeek)).ToList();

        // The week on screen is always offerable, even when it falls outside the year — the box can be
        // paged anywhere, and a picker that silently omitted the week you are looking at would read as
        // broken rather than as a bound. It advises; it never blocks, which is this project's rule.
        var here = WeekLabel.MondayOf(anyDateInWeek);
        if (!weeks.Contains(here))
        {
            weeks.Add(here);
            weeks.Sort();
        }
        return weeks;
    }
}
