using CCCCScheduler.Helpers;
using CCCCScheduler.Models;

namespace CCCCScheduler.ViewModels;

/// <summary>
/// <b>The Scratch Week's operations</b> — copy a class in, empty it, and keep what is in it pointing
/// at things the project still has. Craig, 2026-11-17: <i>"a week you can move things to, in order to
/// plan without affecting other weeks"</i>, with four safeguards he confirmed: copy, don't move;
/// nothing in it counts; administrators and editors only; one step to bring it back.
///
/// <para><b>Copying a whole week in and placing it back are not here</b>: they are
/// <see cref="ScheduleOps.CopyWeek"/> with <see cref="Building.ScratchMonday"/> at one end, so they
/// replace exactly as Copy week to… does and carry nothing that happened (registers, one-off changes),
/// because <see cref="ScheduleOps.Clone"/> drops both.</para>
///
/// <para><b>Why nothing in it counts</b> is <see cref="Building.ScratchSessions"/>: it is a list of
/// its own that no counting reader is handed.</para>
/// </summary>
public static class ScratchWeek
{
    /// <summary>
    /// <b>Copy one class into the Scratch Week</b>, beside whatever is there already. The original is
    /// not touched — the copy has its own id, so moving or deleting it in the scratch changes nothing
    /// on the real week. Answers the copy.
    /// </summary>
    public static ScheduleEntry CopyIn(Building building, ScheduleEntry source)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(source);
        var copy = ScheduleOps.Clone(source, newWeek: Building.ScratchMonday);
        building.ScratchSessions.Add(copy);
        return copy;
    }

    /// <summary>Empties the Scratch Week, and answers how many classes went.</summary>
    public static int Empty(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        int n = building.ScratchSessions.Count;
        building.ScratchSessions.Clear();
        return n;
    }

    /// <summary>
    /// <b>Keeps the Scratch Week honest</b>: every session in it stamped with the scratch Monday and
    /// nothing else, nothing of the scratch left in the real list, and no reference to a room, group,
    /// programme, person or class the project no longer has.
    ///
    /// <para><b>Why one sweep rather than a line in every delete.</b> The deletes (a room, a
    /// facilitator, a student, a class, a group) each sweep <see cref="Building.AllSessions"/>, which is
    /// the real timetable and deliberately not the scratch. A plan can carry a dead reference for a
    /// while without harm because nothing counts it; what must not happen is for that reference to be
    /// placed back onto a real week. So this runs where the scratch meets the real timetable — before a
    /// place-back — and on every load, the load path being this project's other half of every
    /// repair.</para>
    /// </summary>
    public static void Tidy(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);
        building.ScratchSessions ??= new List<ScheduleEntry>();
        building.ScratchSessions.RemoveAll(s => s is null);

        // Anything of the scratch's that found its way into the real list goes back where it belongs.
        var strays = building.Sessions.Where(s => s is not null && Building.IsScratch(s)).ToList();
        foreach (var stray in strays)
        {
            building.Sessions.Remove(stray);
            building.ScratchSessions.Add(stray);
        }

        var rooms = building.Rooms.Select(r => r.Id).ToHashSet();
        var groups = building.Groups.Select(g => g.Id).ToHashSet();
        var programmes = building.Strands.Select(s => s.Id).ToHashSet();
        var facilitators = building.Facilitators.Select(f => f.Id).ToHashSet();
        var students = building.Students.Select(s => s.Id).ToHashSet();
        var classes = building.Classes.Select(c => c.Id).ToHashSet();

        building.ScratchSessions.RemoveAll(s => s.End <= s.Start);
        foreach (var s in building.ScratchSessions)
        {
            s.WeekMonday = Building.ScratchMonday;
            // A plan has no history: no registers and no one-off changes, ever.
            (s.Attendance ??= new()).Clear();
            (s.Exceptions ??= new()).Clear();

            if (s.RoomId is Guid rid && !rooms.Contains(rid)) s.RoomId = null;
            if (s.GroupId is Guid gid && !groups.Contains(gid)) s.GroupId = null;
            (s.GroupIds ??= new()).RemoveAll(g => !groups.Contains(g));
            if (s.ProgrammeId is Guid pid && !programmes.Contains(pid)) s.ProgrammeId = null;
            (s.FacilitatorIds ??= new()).RemoveAll(f => !facilitators.Contains(f));
            s.FacilitatorParts ??= new();
            FacilitatorParts.Normalise(s);
            (s.StudentIds ??= new()).RemoveAll(id => !students.Contains(id));
            (s.ExcludedStudentIds ??= new()).RemoveAll(id => !students.Contains(id));
            if (s.ClassId is Guid cid && !classes.Contains(cid)) s.ClassId = null;
        }
    }
}
