using CCCCScheduler.Models;

namespace CCCCScheduler.ViewModels;

/// <summary>
/// <b>Who is on a class — the one place it is worked out</b>, schema 17.
///
/// <para>Until schema 17 a session's roster was <see cref="ScheduleEntry.StudentIds"/>, ticked one
/// student at a time, and nothing connected it to the group the session is on for or the group each
/// student is in. Craig, 2026-09-24: <i>"if the class is for BA Y1, it adds the names of all BA Y1
/// students to be checked off"</i>, and from the most detailed list on the slot — a Y1 class lists
/// Y1, never the whole of BA. So the roster is now:</para>
/// <list type="bullet">
/// <item><b>every student whose group the session is on for</b> (<see cref="ScheduleEntry.CohortIds"/>
/// — one group, the named groups, or every group of a programme);</item>
/// <item><b>plus</b> anybody put on it by hand (<see cref="ScheduleEntry.StudentIds"/>);</item>
/// <item><b>less</b> anybody left off it by hand (<see cref="ScheduleEntry.ExcludedStudentIds"/>).</item>
/// </list>
///
/// <para><b>A whole-building session has nobody on it, hand-picked or not.</b> Craig's call on the
/// decision card, 2026-09-24: Open Training and Lunch have no register and never count as a missing
/// one. That is why this answers empty for <see cref="SessionScope.WholeBuilding"/> rather than every
/// student in the school, and why Enrolment refuses on one rather than offering a list nobody's
/// register reads.</para>
///
/// <para><b>Every reader of "who is on this class" asks here</b> — the register, the missing-register
/// count, the attendance report, the student filter, a student's hours, the student-clash check and
/// the per-student calendars. Two answers would let the register and the count of missing registers
/// describe different classes.</para>
/// </summary>
public static class SessionRoster
{
    /// <summary>The students on <paramref name="entry"/> as it stands, in the project's own order.</summary>
    public static IReadOnlyList<Guid> For(Building building, ScheduleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Scope == SessionScope.WholeBuilding) return Array.Empty<Guid>();

        var cohorts = entry.CohortIds.ToHashSet();
        var excluded = entry.ExcludedStudentIds.ToHashSet();

        // The group's students in the project's order, then the hand-added ones in the order they
        // were added. A hand-added id is kept even when it names nobody in the building: the load
        // scrubs those, and a roster that silently shrank here would hide a defect the scrub exists
        // to repair rather than show it.
        var roster = new List<Guid>();
        var seen = new HashSet<Guid>();
        foreach (var student in building.Students)
            if (student.GroupId is { } g && cohorts.Contains(g) && !excluded.Contains(student.Id)
                && seen.Add(student.Id))
                roster.Add(student.Id);
        foreach (var id in entry.StudentIds)
            if (!excluded.Contains(id) && seen.Add(id))
                roster.Add(id);
        return roster;
    }

    /// <summary>Whether <paramref name="studentId"/> is on <paramref name="entry"/>.</summary>
    public static bool Includes(Building building, ScheduleEntry entry, Guid studentId)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Scope == SessionScope.WholeBuilding) return false;
        if (entry.ExcludedStudentIds.Contains(studentId)) return false;
        if (entry.StudentIds.Contains(studentId)) return true;
        return building.Students.FirstOrDefault(s => s.Id == studentId)?.GroupId is { } g
               && entry.CohortIds.Contains(g);
    }

    /// <summary>
    /// <b>The list a register is counted against</b>: the one written down when it was filed, or the
    /// class's list as it stands when it has none. See <see cref="AttendanceRecord.RosterStudentIds"/>.
    /// </summary>
    public static IReadOnlyList<Guid> ForRegister(Building building, ScheduleEntry entry, AttendanceRecord? record)
        => record?.RosterStudentIds is { } filed ? filed : For(building, entry);

    /// <summary>
    /// <b>What Enrolment's ticks mean, stored.</b> <paramref name="ticked"/> is the whole list the
    /// person left ticked; what is kept is only how it differs from the session's groups — who was
    /// added from outside them and who was left out — so a student who joins the group later still
    /// lands on the class.
    /// </summary>
    public static (List<Guid> Added, List<Guid> Excluded) Differences(
        Building building, ScheduleEntry entry, IEnumerable<Guid> ticked)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(ticked);

        var order = ticked.ToList();
        var tick = order.ToHashSet();
        var members = GroupMembers(building, entry);
        // Added keeps the order it was given in, so a hand-kept list that predates schema 17 reads
        // back exactly as it was written; Excluded is in the project's order, having no other.
        var added = order.Where(id => !members.Contains(id)).Distinct().ToList();
        var excluded = building.Students.Select(s => s.Id).Where(id => members.Contains(id) && !tick.Contains(id)).ToList();
        return (added, excluded);
    }

    /// <summary>The students whose group <paramref name="entry"/> is on for, before any hand change.</summary>
    public static HashSet<Guid> GroupMembers(Building building, ScheduleEntry entry)
    {
        if (entry.Scope == SessionScope.WholeBuilding) return new HashSet<Guid>();
        var cohorts = entry.CohortIds.ToHashSet();
        return building.Students
            .Where(s => s.GroupId is { } g && cohorts.Contains(g))
            .Select(s => s.Id)
            .ToHashSet();
    }
}
