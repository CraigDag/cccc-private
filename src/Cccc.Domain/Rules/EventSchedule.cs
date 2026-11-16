using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

public sealed record EventOccurrence(SchoolEvent Event, DateOnly Date, DateOnly LastDay, int Number)
{
    public bool IsMultiDay => LastDay > Date;
    public bool IsAllDayStrip => Event.AllDay || IsMultiDay;
    public bool Covers(DateOnly day) => day >= Date && day <= LastDay;
}

/// <summary>Expands an event's repeat into dates. A repeat always ends: at most 1000 times, or five years.</summary>
public static class EventSchedule
{
    public const int MaxOccurrences = 1000;
    public const int HorizonYears = 5;

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

    private static IEnumerable<DateOnly> Candidates(DateOnly first, Repeat rule, int every, DateOnly horizon)
    {
        switch (rule.Kind)
        {
            case RepeatKind.Daily:
                for (var d = first; d <= horizon; d = d.AddDays(every)) yield return d;
                break;
            case RepeatKind.Weekly:
            {
                var days = (rule.Days.IsEmpty ? [first.DayOfWeek] : rule.Days.ToList()).Distinct().OrderBy(Week.DayIndex).ToList();
                for (var week = Week.MondayOf(first); week <= horizon; week = week.AddDays(7 * every))
                    foreach (var day in days)
                    {
                        var d = week.AddDays(Week.DayIndex(day));
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
                yield return first;
                break;
        }
    }

    public static DateOnly? NthWeekday(DateOnly month, DayOfWeek day, int nth)
    {
        var first = new DateOnly(month.Year, month.Month, 1);
        var d = first.AddDays((Week.DayIndex(day) - Week.DayIndex(first.DayOfWeek) + 7) % 7 + 7 * (nth - 1));
        return d.Month == first.Month ? d : null;
    }

    public static IEnumerable<EventOccurrence> Occurrences(Timetable t, SchoolEvent e)
    {
        var skipped = e.Skipped.ToHashSet();
        var closures = e.SkipClosures ? t.Closures.Where(c => c.Scope == ClosureScope.WholeBuilding).ToList() : [];
        int span = e.SpanDays, number = 0;
        foreach (var date in RuleDates(e))
        {
            number++;
            if (skipped.Contains(date) || closures.Any(c => c.Covers(date))) continue;
            yield return new EventOccurrence(e, date, date.AddDays(span - 1), number);
        }
    }

    /// <summary>The occurrences touching these dates that this viewer may see.</summary>
    public static IReadOnlyList<EventOccurrence> Between(Timetable t, DateOnly from, DateOnly to, EventViewer viewer)
    {
        var found = new List<EventOccurrence>();
        foreach (var e in t.Events.Where(viewer.Sees))
            foreach (var o in Occurrences(t, e))
            {
                if (o.Date > to) break;
                if (o.LastDay >= from) found.Add(o);
            }
        return found.OrderBy(o => o.Date).ThenBy(o => o.IsAllDayStrip ? 0 : 1)
            .ThenBy(o => o.Event.AllDay ? TimeOnly.MinValue : o.Event.Start)
            .ThenBy(o => o.Event.Title, NameOrder.Comparer).ToList();
    }
}
