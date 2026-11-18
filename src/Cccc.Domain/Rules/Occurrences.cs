using System.Collections.Immutable;
using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

public enum OccurrenceState
{
    Runs,
    NeedsCover,
    Cancelled,
    Closed,
}

/// <summary>
/// A session as it runs: its state, and who is actually in the room. Every reader of "what happens this
/// week" — the calendar, hours, clashes, registers, cover, the printed sheets — reads these.
/// </summary>
public sealed record Occurrence(
    Session Session,
    OccurrenceState State,
    Closure? ClosedBy,
    ImmutableList<Guid> AwayIds,
    ImmutableList<Guid> InTheRoom)
{
    public DateOnly Date => Session.Date;
    public DayOfWeek Day => Session.Date.DayOfWeek;
    public TimeOnly Start => Session.Start;
    public TimeOnly End => Session.End;

    /// <summary>Runs or needs cover: it happens, even if somebody is still missing.</summary>
    public bool Happens => State is OccurrenceState.Runs or OccurrenceState.NeedsCover;

    public bool NeedsCover => State == OccurrenceState.NeedsCover;

    public bool Covered => State == OccurrenceState.Runs && !Session.CoverIds.IsEmpty;
}

/// <summary>
/// Resolves sessions into occurrences. Precedence: closure, then cancelled, then covered, then absence
/// (the old <c>ScheduleResolver.Resolve</c>).
/// </summary>
public static class Occurrences
{
    /// <summary>Every session of the week, including closed and cancelled ones, in day/time/room order.</summary>
    public static IReadOnlyList<Occurrence> ForWeek(Timetable t, DateOnly anyDateInWeek) =>
        Order(t, t.SessionsInWeek(anyDateInWeek).Select(s => Resolve(t, s)));

    /// <summary>Only the occurrences that happen (run or need cover) — what counts.</summary>
    public static IReadOnlyList<Occurrence> HappeningInWeek(Timetable t, DateOnly anyDateInWeek) =>
        ForWeek(t, anyDateInWeek).Where(o => o.Happens).ToList();

    public static IReadOnlyList<Occurrence> Between(Timetable t, DateOnly from, DateOnly to) =>
        Order(t, t.Sessions.Where(s => s.Date >= from && s.Date <= to).Select(s => Resolve(t, s)));

    private static IReadOnlyList<Occurrence> Order(Timetable t, IEnumerable<Occurrence> items) =>
        items.OrderBy(o => o.Date)
             .ThenBy(o => o.Start)
             .ThenBy(o => t.Rooms.Find(o.Session.RoomId)?.Name ?? "", NameOrder.Comparer)
             .ThenBy(o => o.Session.DisplayTitle, NameOrder.Comparer)
             .ThenBy(o => o.Session.Id)
             .ToList();

    public static Occurrence Resolve(Timetable t, Session s)
    {
        if (ClosureFor(t, s.Date, s) is { } closure)
            return new Occurrence(s, OccurrenceState.Closed, closure, [], []);
        if (s.Cancelled)
            return new Occurrence(s, OccurrenceState.Cancelled, null, [], []);

        var assigned = s.FacilitatorIds.ToList();
        var away = assigned.Where(id =>
        {
            var (from, until) = TimeWindows.Assigned(s, id);
            return until > from && IsAway(t, id, s.Date, from, until);
        }).ToImmutableList();

        var present = assigned.Where(id => !away.Contains(id));
        if (!s.CoverIds.IsEmpty)
            return new Occurrence(s, OccurrenceState.Runs, null, away,
                present.Concat(s.CoverIds).Distinct().Where(id => TimeWindows.Length(s, away, id) > TimeSpan.Zero).ToImmutableList());

        var room = present.Where(id => TimeWindows.Length(s, away, id) > TimeSpan.Zero).ToImmutableList();
        // Somebody away with nobody covering yet needs cover when it leaves a stretch with nobody in the room, or,
        // where its class says how many students one facilitator may have, too few for its students (Staffing).
        if (TimeWindows.LeavesAGap(s, away) || (!away.IsEmpty && Staffing.Short(t, s, away, room) is not null))
            return new Occurrence(s, OccurrenceState.NeedsCover, null, away, room);

        return new Occurrence(s, OccurrenceState.Runs, null, away, room);
    }

    public static bool IsAway(Timetable t, Guid facilitatorId, DateOnly date, TimeOnly from, TimeOnly until) =>
        t.Absences.Any(a => a.Is(AbsentPerson.Facilitator, facilitatorId) && a.Covers(date, from, until));

    /// <summary>
    /// The closure that shuts this session on this date. A group closure applies when every group the
    /// session is for is among its groups; an Everyone session only by a whole-building closure. With no
    /// session, only whole-building closures count.
    /// </summary>
    public static Closure? ClosureFor(Timetable t, DateOnly date, Session? s) =>
        t.Closures.OrderBy(c => c.From).ThenBy(c => c.Id).FirstOrDefault(c => c.Covers(date) && AppliesTo(c, s));

    private static bool AppliesTo(Closure c, Session? s)
    {
        if (c.Scope == ClosureScope.WholeBuilding) return true;
        if (s is null || s.Audience.IsEveryone) return false;
        var cohorts = s.Audience.Cohorts.ToList();
        return cohorts.Count > 0 && cohorts.All(c.GroupIds.Contains);
    }

    public static int WholeBuildingClosedDays(Timetable t, DateOnly monday) =>
        Week.Days(monday).Count(d => ClosureFor(t, d, null) is not null);

    /// <summary>Weeks of a semester not closed for all seven days.</summary>
    public static IEnumerable<DateOnly> TeachingWeeks(Timetable t, Semester s) =>
        s.Mondays().Where(m => WholeBuildingClosedDays(t, m) < 7);
}
