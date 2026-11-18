using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// What an absence takes somebody off, said before it is recorded, by the absence form, by a facilitator
/// asking and by a line typed into Find, so all three say the same. For a facilitator: each class on its days
/// that they are in the room for, teaching or covering, whose part of it the absence's times reach. For a
/// student: each class they are on whose time it reaches. Soonest first; a class that is cancelled or closed
/// takes nobody off anything.
/// </summary>
public static class Away
{
    /// <summary>
    /// The classes it takes them off, read as the timetable would be without it, so an absence being changed
    /// still lists the classes it already takes them off.
    /// </summary>
    public static IReadOnlyList<Occurrence> TakesOff(Timetable t, Absence a)
    {
        var without = t.Absences.Contains(a.Id) ? t with { Absences = t.Absences.Remove(a.Id) } : t;
        return Occurrences.Between(without, a.From, a.To).Where(o => o.Happens && Reaches(without, a, o)).ToList();
    }

    /// <summary>
    /// Each class it takes them off, as it is and as recording it would leave it: for a facilitator needing
    /// cover, still taught by somebody else for all of it, or (a class they cover) running on paper with its
    /// cover away. Resolved by <see cref="Occurrences.Resolve"/> against the timetable with the absence in it,
    /// so it says exactly what the plan would say afterwards.
    /// </summary>
    public static IReadOnlyList<(Occurrence Now, Occurrence After)> WhatItLeaves(Timetable t, Absence a)
    {
        var after = t with { Absences = t.Absences.Put(a) };
        return TakesOff(t, a).Select(o => (o, Occurrences.Resolve(after, o.Session))).ToList();
    }

    private static bool Reaches(Timetable t, Absence a, Occurrence o)
    {
        if (a.Person == AbsentPerson.Student)
            return Roster.Includes(t, o.Session, a.PersonId) && a.Covers(o.Date, o.Start, o.End);
        if (!o.InTheRoom.Contains(a.PersonId)) return false;
        var (from, until) = TimeWindows.InRoom(o, a.PersonId);
        return until > from && a.Covers(o.Date, from, until);
    }
}
