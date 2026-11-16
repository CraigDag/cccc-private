using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// Who is on a class — the one answer (schema 17, Craig 2026-09-24): the students of every group it is
/// for, plus anybody put on by hand, less anybody left off. An Everyone session has nobody on it.
/// </summary>
public static class Roster
{
    public static IReadOnlyList<Guid> For(Timetable t, Session s)
    {
        if (s.Audience.IsEveryone) return [];
        var cohorts = s.Audience.Cohorts.ToHashSet();
        var left = s.LeftOffStudentIds.ToHashSet();
        var seen = new HashSet<Guid>();
        var roster = new List<Guid>();
        foreach (var st in t.Students.OrderBy(x => x.Name, NameOrder.Comparer).ThenBy(x => x.Id))
            if (st.GroupId is { } g && cohorts.Contains(g) && !left.Contains(st.Id) && seen.Add(st.Id))
                roster.Add(st.Id);
        foreach (var id in s.AddedStudentIds)
            if (!left.Contains(id) && t.Students.Contains(id) && seen.Add(id))
                roster.Add(id);
        return roster;
    }

    public static bool Includes(Timetable t, Session s, Guid studentId)
    {
        if (s.Audience.IsEveryone) return false;
        if (s.LeftOffStudentIds.Contains(studentId)) return false;
        if (s.AddedStudentIds.Contains(studentId)) return true;
        return t.Students[studentId]?.GroupId is { } g && s.Audience.Cohorts.Contains(g);
    }

    /// <summary>The roster a register lists: the one it was filed against, or today's.</summary>
    public static IReadOnlyList<Guid> ForRegister(Timetable t, Session s) =>
        t.Registers[s.Id] is { } r ? r.RosterIds : For(t, s);

    /// <summary>The members of the session's groups, before the hand-made exceptions.</summary>
    public static HashSet<Guid> GroupMembers(Timetable t, Session s)
    {
        if (s.Audience.IsEveryone) return [];
        var cohorts = s.Audience.Cohorts.ToHashSet();
        return t.Students.Where(x => x.GroupId is { } g && cohorts.Contains(g)).Select(x => x.Id).ToHashSet();
    }

    /// <summary>Given who should be on it, the exceptions to store: added and left off.</summary>
    public static Session WithRoster(Timetable t, Session s, IEnumerable<Guid> onIt)
    {
        var want = onIt.Distinct().ToList();
        var members = GroupMembers(t, s);
        return s with
        {
            AddedStudentIds = [.. want.Where(id => !members.Contains(id))],
            LeftOffStudentIds = [.. members.Where(id => !want.Contains(id)).OrderBy(id => id)],
        };
    }
}
