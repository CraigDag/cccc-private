using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>Too few facilitators for a class's students: how many there are, one to every how many, how many that wants.</summary>
public sealed record Shortfall(int Students, int Each, int Wanted, int InTheRoom);

/// <summary>
/// Enough facilitators for a class's students, where its class says how many students one facilitator may have
/// (<see cref="Class.StudentsEach"/>, one to every six on the trampoline): the students on it (<see cref="Roster"/>)
/// less those recorded away then, against the fewest facilitators in the room at once, each for their own part of it.
/// A class short of them is a ring (<see cref="ClashKind.ShortStaffed"/>), unless somebody on it is away and nobody
/// covers yet, when it needs cover like a class with nobody left (<see cref="Occurrences.Resolve"/>). The rebuild's
/// own; advice, never a refusal.
/// </summary>
public static class Staffing
{
    /// <summary>How many students one facilitator may have in it; none when its class does not say.</summary>
    public static int? Each(Timetable t, Session s) => t.Classes.Find(s.ClassId)?.StudentsEach is > 0 and var n ? n : null;

    /// <summary>The students expected in it: those on it, less those recorded away for any of it.</summary>
    public static int Students(Timetable t, Session s) =>
        Roster.For(t, s).Count(id => !t.Absences.Any(a => a.Is(AbsentPerson.Student, id) && a.Covers(s.Date, s.Start, s.End)));

    /// <summary>The facilitators so many students want, one to every <paramref name="each"/>.</summary>
    public static int Wanted(int students, int each) => (students + each - 1) / each;

    /// <summary>The fewest of these people in the room at once over the whole class, each for their own part of it.</summary>
    public static int Fewest(Session s, IReadOnlyCollection<Guid> away, IEnumerable<Guid> room)
    {
        var windows = room.Select(id => TimeWindows.InRoom(s, away, id)).Where(w => w.End > w.Start).ToList();
        if (s.End <= s.Start) return windows.Count;
        // Only where somebody comes or goes can the count change, so it is counted from each of those.
        return windows.SelectMany(w => new[] { w.Start, w.End }).Append(s.Start).Where(p => p >= s.Start && p < s.End).Distinct()
            .Min(p => windows.Count(w => w.Start <= p && p < w.End));
    }

    /// <summary>How the class falls short as it runs; none with enough, with no number for its class, or with nobody on it.</summary>
    public static Shortfall? Short(Timetable t, Occurrence o) => o.Happens ? Short(t, o.Session, o.AwayIds, o.InTheRoom) : null;

    /// <summary>How the class falls short with these people in the room, those away being away. Nobody on it wants nobody.</summary>
    public static Shortfall? Short(Timetable t, Session s, IReadOnlyCollection<Guid> away, IEnumerable<Guid> room)
    {
        if (Each(t, s) is not { } each) return null;
        int students = Students(t, s), wanted = Wanted(students, each), fewest = Fewest(s, away, room);
        return fewest < wanted ? new Shortfall(students, each, wanted, fewest) : null;
    }

    /// <summary>
    /// Why a class needing cover needs it when somebody is still in the room for all of it: too few for its students.
    /// None when the absence leaves a stretch with nobody there, which is reason enough.
    /// </summary>
    public static Shortfall? LeftShort(Timetable t, Occurrence o) =>
        o.NeedsCover && !TimeWindows.LeavesAGap(o.Session, o.AwayIds) ? Short(t, o) : null;

    /// <summary>"14 students and 2 facilitators: one to every 6 wants 3", as the clash check says it.</summary>
    public static string Say(Shortfall f) => $"{Students(f.Students)} and {Facilitators(f.InTheRoom)}: one to every {f.Each} wants {f.Wanted}";

    /// <summary>"1 facilitator for 14 students, one to every 6": what an absence nobody covers yet leaves.</summary>
    public static string Left(Shortfall f) => $"{Facilitators(f.InTheRoom)} for {Students(f.Students)}, one to every {f.Each}";

    private static string Students(int n) => n == 1 ? "1 student" : $"{n} students";

    private static string Facilitators(int n) => n switch { 0 => "no facilitator", 1 => "1 facilitator", _ => $"{n} facilitators" };
}
