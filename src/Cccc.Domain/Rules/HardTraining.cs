using System.Collections.Immutable;
using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// Hard classes a group has with less than the school's rest between them, run together. <see cref="Hard"/>
/// is the time in those classes, not the span: the short breaks between them are not training, but they
/// are not a rest either.
/// </summary>
public sealed record HardStretch(DateOnly Date, TimeOnly Start, TimeOnly End, TimeSpan Hard, ImmutableList<Guid> SessionIds);

/// <summary>
/// One group's hard training on one day: its stretches, the day's total, and where it goes past the
/// school's limits (<see cref="TooLong"/>, the stretches longer than a stretch may be; <see cref="TooMuch"/>,
/// the day past what a day may hold).
/// </summary>
public sealed record HardDay(Guid GroupId, DateOnly Date, IReadOnlyList<HardStretch> Stretches, TimeSpan Total,
    IReadOnlyList<HardStretch> TooLong, bool TooMuch)
{
    public bool Over => TooLong.Count > 0 || TooMuch;
}

/// <summary>
/// How hard a group's day is on the body, the rebuild's own. A class is hard training for a group when its
/// class is marked <see cref="Effort.Hard"/>, it is on for that group (a whole-building class is on for every
/// group), it counts (an open session or a break is nobody's required training) and it happens (cancelled
/// or closed is not; waiting for cover still is, because the students are still timetabled to be there).
/// The school sets the limits; with none set it is something to look at, never a warning, and never a
/// refusal either way.
/// </summary>
public static class HardTraining
{
    /// <summary>The rest that ends a stretch when the school has not said.</summary>
    public static readonly TimeSpan DefaultRest = TimeSpan.FromMinutes(30);

    public static Effort EffortOf(Timetable t, Session s) => s.ClassId is { } id && t.Classes[id] is { } c ? c.Effort : Effort.NotSet;

    public static TimeSpan Rest(Timetable t) => t.Settings.RestBetweenHard ?? DefaultRest;

    public static bool HasLimits(Timetable t) => t.Settings.MostHardInARow is not null || t.Settings.MostHardInADay is not null;

    /// <summary>How hard this occurrence is for somebody timetabled to be at it: not said for one that does not happen or does not count.</summary>
    public static Effort Of(Timetable t, Occurrence o) =>
        o.Happens && Hours.WorkloadOf(t, o.Session) == Workload.Counted ? EffortOf(t, o.Session) : Effort.NotSet;

    /// <summary>Whether this occurrence is hard training somebody is timetabled to be at.</summary>
    public static bool Counts(Timetable t, Occurrence o) => Of(t, o) == Effort.Hard;

    /// <summary>A group's hard training on <paramref name="date"/>, from any occurrences that include that day's.</summary>
    public static HardDay For(Timetable t, IEnumerable<Occurrence> occurrences, Guid groupId, DateOnly date)
    {
        var rest = Rest(t);
        var hard = occurrences.Where(o => o.Date == date && o.Session.Audience.IsFor(groupId) && Counts(t, o))
            .OrderBy(o => o.Start).ThenBy(o => o.End).ThenBy(o => o.Session.Id).ToList();
        var stretches = new List<HardStretch>();
        for (int i = 0; i < hard.Count;)
        {
            var start = hard[i].Start;
            var end = hard[i].End;
            var ids = new List<Guid>();
            var time = TimeSpan.Zero;
            var counted = start;     // how far along the day the time in classes has been added, so an overlap counts once
            int j = i;
            // Back to back, overlapping, or less than the rest apart: still the same stretch.
            for (; j < hard.Count && (j == i || Week.Span(end, hard[j].Start) < rest || hard[j].Start <= end); j++)
            {
                var o = hard[j];
                if (!ids.Contains(o.Session.Id)) ids.Add(o.Session.Id);
                var from = o.Start > counted ? o.Start : counted;
                if (o.End > from) time += Week.Span(from, o.End);
                if (o.End > counted) counted = o.End;
                if (o.End > end) end = o.End;
            }
            stretches.Add(new HardStretch(date, start, end, time, [.. ids]));
            i = j;
        }
        var total = stretches.Aggregate(TimeSpan.Zero, (a, s) => a + s.Hard);
        var tooLong = t.Settings.MostHardInARow is { } most ? stretches.Where(s => s.Hard > most).ToList() : [];
        bool tooMuch = t.Settings.MostHardInADay is { } inADay && total > inADay;
        return new HardDay(groupId, date, stretches, total, tooLong, tooMuch);
    }

    /// <summary>
    /// The warnings for these occurrences' days, group by group, in the order a person reads them: a stretch
    /// too long ("BA 1: 2h 45m of hard training in a row, Mon 09:00–12:00 (at most 2h without a 30m rest)"),
    /// then a day too full. Nothing at all when the school has set no limit.
    /// </summary>
    public static IEnumerable<Clash> Warnings(Timetable t, IReadOnlyList<Occurrence> occurrences)
    {
        if (!HasLimits(t)) yield break;
        var hard = occurrences.Where(o => Counts(t, o)).ToList();     // asked once, not once a group and a day
        var days = hard.Select(o => o.Date).Distinct().Order().ToList();
        foreach (var g in t.Groups.OrderBy(g => g.Name, NameOrder.Comparer))
            foreach (var date in days)
            {
                var day = For(t, hard, g.Id, date);
                foreach (var s in day.TooLong)
                    yield return new Clash(ClashKind.GroupHardInARow,
                        $"{g.Name}: {Week.Duration(s.Hard)} of hard training in a row, {Week.ShortDay(date.DayOfWeek)} {Week.Hm(s.Start)}–{Week.Hm(s.End)} " +
                        $"(at most {Week.Duration(t.Settings.MostHardInARow!.Value)} without a {Week.Duration(Rest(t))} rest) — {Titles(hard, s.SessionIds)}",
                        s.SessionIds);
                if (day.TooMuch)
                    yield return new Clash(ClashKind.GroupHardInADay,
                        $"{g.Name}: {Week.Duration(day.Total)} of hard training on {Week.ShortDay(date.DayOfWeek)} (at most {Week.Duration(t.Settings.MostHardInADay!.Value)} in a day)",
                        [.. day.Stretches.SelectMany(s => s.SessionIds)]);
            }
    }

    private static string Titles(IReadOnlyList<Occurrence> occurrences, IEnumerable<Guid> ids) =>
        string.Join(", ", ids.Select(id => occurrences.FirstOrDefault(o => o.Session.Id == id)?.Session.DisplayTitle ?? "a class").Select(n => $"\"{n}\""));
}

public static class EffortWords
{
    public static string For(Effort e) => e switch
    {
        Effort.Light => "Light",
        Effort.Moderate => "Moderate",
        Effort.Hard => "Hard",
        _ => "Not said",
    };
}
