using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>A class somebody or something is in, and the stretch of it that is theirs.</summary>
public sealed record Stint(Occurrence O, TimeOnly From, TimeOnly Until);

/// <summary>What is happening for one person, group or space at a moment, what comes next that day, and whether they are recorded away.</summary>
public sealed record Whereabouts(Stint? Now, Stint? Next, bool Away);

/// <summary>
/// WHO is in what SPACE and WHEN, asked for one of them at one moment: the question the app exists to
/// answer, so it has one implementation. Each kind says what is theirs the way the rest of the app does:
/// a student by the class's roster, a facilitator by being in the room (cover included, away not, a part
/// only for their part), a group by the class being on for it (a whole-building class is), a space by
/// the class being in it. Only classes that happen count. A class that ends as the moment starts is over.
/// </summary>
public static class Where
{
    public static Whereabouts Student(Timetable t, Guid id, DateOnly date, TimeOnly at) =>
        Pick(Today(t, date).Where(o => Roster.Includes(t, o.Session, id)).Select(Whole), at,
             t.Absences.Any(a => a.Is(AbsentPerson.Student, id) && a.Covers(date, at, Later(at))));

    public static Whereabouts Facilitator(Timetable t, Guid id, DateOnly date, TimeOnly at) =>
        Pick(Today(t, date).Where(o => o.InTheRoom.Contains(id)).Select(o => { var (a, b) = TimeWindows.InRoom(o, id); return new Stint(o, a, b); }), at,
             Occurrences.IsAway(t, id, date, at, Later(at)));

    public static Whereabouts Group(Timetable t, Guid id, DateOnly date, TimeOnly at) =>
        Pick(Today(t, date).Where(o => o.Session.Audience.IsFor(id)).Select(Whole), at, false);

    public static Whereabouts Space(Timetable t, Guid id, DateOnly date, TimeOnly at) =>
        Pick(Today(t, date).Where(o => o.Session.RoomId == id).Select(Whole), at, false);

    private static IEnumerable<Occurrence> Today(Timetable t, DateOnly date) => Occurrences.Between(t, date, date).Where(o => o.Happens);

    private static Stint Whole(Occurrence o) => new(o, o.Start, o.End);

    // The minute starting at the moment, so an absence ending exactly then does not count as now.
    private static TimeOnly Later(TimeOnly at) => at >= new TimeOnly(23, 59) ? TimeOnly.MaxValue : at.AddMinutes(1);

    private static Whereabouts Pick(IEnumerable<Stint> theirs, TimeOnly at, bool away)
    {
        var ordered = theirs.Where(s => s.Until > s.From).OrderBy(s => s.From).ThenBy(s => s.Until).ToList();
        return new(ordered.FirstOrDefault(s => s.From <= at && at < s.Until), ordered.FirstOrDefault(s => s.From > at), away);
    }
}
