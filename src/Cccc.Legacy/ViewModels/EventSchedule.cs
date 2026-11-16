using CCCCScheduler.Models;

namespace CCCCScheduler.ViewModels;

/// <summary>
/// <b>One occurrence of an event</b>: the day it starts, the day it ends (the same day unless it runs
/// over several), and which number it is in its series, counting from 1.
/// </summary>
public sealed record EventOccurrence(SchoolEvent Event, DateOnly Date, DateOnly LastDay, int Number)
{
    public bool IsMultiDay => LastDay > Date;

    /// <summary>True when the occurrence belongs in the strip above the day columns rather than in them.</summary>
    public bool IsAllDayStrip => Event.AllDay || IsMultiDay;

    public bool Covers(DateOnly day) => day >= Date && day <= LastDay;
}

/// <summary>
/// <b>When an event happens</b>: the one place a repeat rule is turned into dates. The screen, the
/// form's summary line and the "this and following" split all ask here, so they cannot come to two
/// opinions about which Thursday is the fourth.
///
/// <para><b>Decisions this makes, each the one Google Calendar makes</b>, because that is what the
/// administrator is used to:</para>
/// <list type="bullet">
/// <item><b>Monthly on the 31st skips a month without one</b>, rather than moving to the 30th. A
/// deadline that silently moves a day is worse than one that says it is not this month.</item>
/// <item><b>Monthly on the fifth Friday skips a month without one</b>, for the same reason.</item>
/// <item><b><see cref="EventRepeat.Count"/> counts every date the rule produces</b>, including ones
/// later deleted as <i>this one</i> and ones a school closure leaves out, so deleting one date never
/// makes the series grow a new one at the far end.</item>
/// </list>
///
/// <para><b>It can never run for ever.</b> A rule is cut off at <see cref="MaxOccurrences"/> dates and
/// at <see cref="HorizonYears"/> years after it starts, whatever its end says, because a hand-edited
/// file can say anything and a loop that never ends takes the server with it.</para>
/// </summary>
public static class EventSchedule
{
    public const int MaxOccurrences = 1000;
    public const int HorizonYears = 5;

    /// <summary>
    /// <b>Every date the rule produces, in order</b>, before <see cref="SchoolEvent.Skipped"/> and
    /// closures take anything out — the numbering <see cref="EventRepeat.Count"/> counts against. A
    /// one-off produces its one date.
    /// </summary>
    public static IEnumerable<DateOnly> RuleDates(SchoolEvent e)
    {
        if (e.Repeat is not { } rule)
        {
            yield return e.Date;
            yield break;
        }

        int every = Math.Max(1, rule.Every);
        int limit = Math.Min(rule.Count is > 0 and var c ? c : MaxOccurrences, MaxOccurrences);
        var horizon = e.Date.AddYears(HorizonYears);
        var until = rule.Until is { } u && u < horizon ? u : horizon;
        int produced = 0;

        foreach (var date in Candidates(e.Date, rule, every, horizon))
        {
            if (date > until || produced >= limit) yield break;
            produced++;
            yield return date;
        }
    }

    private static IEnumerable<DateOnly> Candidates(DateOnly first, EventRepeat rule, int every, DateOnly horizon)
    {
        switch (rule.Kind)
        {
            case RepeatKind.Daily:
                for (var d = first; d <= horizon; d = d.AddDays(every))
                    yield return d;
                break;

            case RepeatKind.Weekly:
            {
                var days = (rule.Days.Count == 0 ? new List<DayOfWeek> { first.DayOfWeek } : rule.Days)
                    .Distinct().OrderBy(MondayFirst).ToList();
                var monday = Helpers.WeekLabel.MondayOf(first);
                for (var week = monday; week <= horizon; week = week.AddDays(7 * every))
                    foreach (var day in days)
                    {
                        var d = week.AddDays(MondayFirst(day));
                        if (d >= first) yield return d;
                    }
                break;
            }

            case RepeatKind.MonthlyOnDate:
                for (int m = 0; ; m += every)
                {
                    var month = new DateOnly(first.Year, first.Month, 1).AddMonths(m);
                    if (month > horizon) yield break;
                    if (first.Day <= DateTime.DaysInMonth(month.Year, month.Month))
                        yield return new DateOnly(month.Year, month.Month, first.Day);
                }

            case RepeatKind.MonthlyOnWeekday:
            {
                int nth = (first.Day - 1) / 7 + 1;
                for (int m = 0; ; m += every)
                {
                    var month = new DateOnly(first.Year, first.Month, 1).AddMonths(m);
                    if (month > horizon) yield break;
                    if (NthWeekday(month, first.DayOfWeek, nth) is { } d) yield return d;
                }
            }

            default:
                // A kind this build has no name for is a one-off, never a guess at what it meant.
                yield return first;
                break;
        }
    }

    /// <summary>Monday is 0, Sunday 6: the order a school week is read in.</summary>
    public static int MondayFirst(DayOfWeek day) => ((int)day + 6) % 7;

    /// <summary>The <paramref name="nth"/> <paramref name="day"/> of the month starting <paramref name="month"/>, or null if it has none.</summary>
    public static DateOnly? NthWeekday(DateOnly month, DayOfWeek day, int nth)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var d = first.AddDays((MondayFirst(day) - MondayFirst(first.DayOfWeek) + 7) % 7 + 7 * (nth - 1));
        return d.Month == first.Month ? d : null;
    }

    /// <summary>
    /// <b>The occurrences of one event that actually happen</b>, in order: the rule's dates, less the
    /// skipped ones, less — when <see cref="SchoolEvent.SkipClosures"/> is ticked — the ones starting
    /// on a day the whole building is closed. Group closures never remove an event: an event is not on
    /// for a group.
    /// </summary>
    public static IEnumerable<EventOccurrence> Occurrences(SchoolEvent e, IEnumerable<Holiday> holidays)
    {
        var skipped = e.Skipped.ToHashSet();
        var closures = e.SkipClosures
            ? holidays.Where(h => h.Scope == HolidayScope.WholeBuilding).ToList()
            : new List<Holiday>();
        int span = e.SpanDays;
        int number = 0;

        foreach (var date in RuleDates(e))
        {
            number++;
            if (skipped.Contains(date)) continue;
            if (closures.Any(h => h.CoversDate(date))) continue;
            yield return new EventOccurrence(e, date, date.AddDays(span - 1), number);
        }
    }

    /// <summary>
    /// <b>Every occurrence of every event that touches the days <paramref name="from"/> to
    /// <paramref name="to"/></b>, inclusive — a trip that started last Friday is on this Monday's
    /// strip. Ordered by start date, then all-day first, then time, then title, so the screen and the
    /// phone list the same day in the same order.
    /// </summary>
    public static IReadOnlyList<EventOccurrence> Between(
        IEnumerable<SchoolEvent> events, DateOnly from, DateOnly to, IEnumerable<Holiday> holidays)
    {
        var closures = holidays.ToList();
        var found = new List<EventOccurrence>();
        foreach (var e in events)
            foreach (var o in Occurrences(e, closures))
            {
                if (o.Date > to) break;
                if (o.LastDay >= from) found.Add(o);
            }

        return found
            .OrderBy(o => o.Date)
            .ThenBy(o => o.IsAllDayStrip ? 0 : 1)
            .ThenBy(o => o.Event.AllDay ? TimeOnly.MinValue : o.Event.Start)
            .ThenBy(o => o.Event.Title, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>How many occurrences the series has in all — what the form's summary line counts.</summary>
    public static int CountOf(SchoolEvent e, IEnumerable<Holiday> holidays) => Occurrences(e, holidays).Count();

    /// <summary>The last date an occurrence starts on, or null when there is none at all.</summary>
    public static DateOnly? LastDate(SchoolEvent e, IEnumerable<Holiday> holidays)
        => Occurrences(e, holidays).Select(o => (DateOnly?)o.Date).LastOrDefault();
}
