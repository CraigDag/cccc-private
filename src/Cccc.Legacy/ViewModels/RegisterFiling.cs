using System.Security.Cryptography;
using CCCCScheduler.Helpers;
using CCCCScheduler.Models;

namespace CCCCScheduler.ViewModels;

/// <summary>
/// <b>Who may change a register, how one is written, and how the same register is found again in
/// another copy of the same project.</b>
///
/// <para><b>Craig's rule, 2026-09-24, with his addition of 2026-09-29.</b> Every facilitator may SEE
/// every register; only a facilitator scheduled on the class may CHANGE its register; and the
/// administrators, "the power users of the app", may change every one. <see cref="MayChange"/> is
/// that rule and nothing else asks it a second way.</para>
///
/// <para><b>"Scheduled on the class" is whoever is in the room that week</b> — the facilitators
/// assigned to it, plus anybody a cover arrangement put there for that week. The register design of
/// 2026-09-15 recommended it: a substitute who stood in front of the class is the person who knows
/// who came. It is the opposite of <see cref="RegisterGaps"/>, which counts an outstanding register
/// against the ASSIGNED roster, and deliberately so: who may write it and whose gap it is are two
/// different questions.</para>
///
/// <para><b>Why a register needs finding again at all.</b> A coach's register is written straight
/// into the file on the server (<c>ServerProjectStore.FileRegister</c> in the web host), not into
/// their tab's copy, and the tab that opened the project at nine may not agree with the file at ten
/// about which object draws a week: freezing a week that has ended replaces the pattern's session
/// with a copy of its own. <see cref="Locate"/> answers that, and <see cref="FrozenCopyId"/> is what
/// makes it answerable — every copy that freezing makes has the id its pattern and its week imply,
/// so two copies of one project that froze the same week separately agree about it.</para>
/// </summary>
public static class RegisterFiling
{
    /// <summary>
    /// <b>The id the frozen copy of a pattern session takes for one week</b>: the same answer for the
    /// same pair wherever it is asked, so a week frozen in two tabs' copies of one project is frozen
    /// into the same ids in both.
    ///
    /// <para>Until 2026-09-29 each copy took a fresh random id, which was harmless while only one
    /// tab could ever write: the copies nobody saved were thrown away. Once a register can be
    /// written into the file from a tab that is not the one editing, the two have to be able to
    /// name the same session.</para>
    /// </summary>
    public static Guid FrozenCopyId(Guid patternId, DateOnly anyDateInWeek)
    {
        var monday = WeekLabel.MondayOf(anyDateInWeek);
        Span<byte> input = stackalloc byte[16 + 4];
        patternId.TryWriteBytes(input);
        BitConverter.TryWriteBytes(input[16..], monday.DayNumber);

        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);

        // An RFC 4122 name-based shape (version 5, variant 1), so the value reads as what it is to
        // anybody looking at the file, and cannot be mistaken for one of Guid.NewGuid's.
        var bytes = hash[..16].ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    /// <summary>
    /// <b>The session that draws <paramref name="week"/> in this building and is the same session as
    /// <paramref name="sessionId"/> in another copy of the project</b>, or null when there is none.
    ///
    /// <para>Three cases, and only these three, because they are the only ways two copies of one file
    /// can come to disagree without anybody editing the timetable: the id is the same in both; this
    /// copy has frozen the week and the other has not (the id is the pattern's, and this copy holds
    /// its <see cref="FrozenCopyId"/>); or the other copy froze it and this one has not (the id is a
    /// frozen copy's, and this copy still draws the week from the pattern it came from). Anything else
    /// — the class deleted, the week rebuilt — is null, and a register is never guessed onto
    /// something else.</para>
    /// </summary>
    public static ScheduleEntry? Locate(Building building, Guid sessionId, DateOnly week)
    {
        ArgumentNullException.ThrowIfNull(building);
        var monday = WeekLabel.MondayOf(week);
        var drawn = building.SessionsInWeek(monday).ToList();

        if (drawn.FirstOrDefault(s => s.Id == sessionId) is { } same) return same;

        var frozen = FrozenCopyId(sessionId, monday);
        if (drawn.FirstOrDefault(s => s.Id == frozen) is { } copy) return copy;

        return drawn.FirstOrDefault(s => s.WeekMonday is null && FrozenCopyId(s.Id, monday) == sessionId);
    }

    /// <summary>
    /// <b>Who is in the room for this session that week</b>: its assigned facilitators, and anybody a
    /// cover arrangement for that week put there. The absent person is still in the list — it is still
    /// their class, and they may be the one who later puts a register right.
    /// </summary>
    public static IReadOnlyList<Guid> InTheRoom(ScheduleEntry entry, DateOnly week)
    {
        ArgumentNullException.ThrowIfNull(entry);
        var people = new List<Guid>(entry.FacilitatorIds);
        if (ScheduleResolver.ExceptionFor(entry, week) is { Kind: ExceptionKind.Covered, CoverFacilitatorIds: { } cover })
            foreach (var id in cover)
                if (!people.Contains(id)) people.Add(id);
        return people;
    }

    /// <summary>
    /// <b>Whether this person may change this register.</b> <paramref name="everyRegister"/> is the
    /// administrators' answer — <c>WebAccess.MayTakeEveryRegister</c> in the web host — and
    /// <paramref name="facilitatorId"/> is who the signed-in account is in the timetable, or null for
    /// somebody the timetable has no row for, who may change none.
    /// </summary>
    public static bool MayChange(bool everyRegister, Guid? facilitatorId, ScheduleEntry entry, DateOnly week)
        => everyRegister
           || (facilitatorId is { } me && InTheRoom(entry, week).Contains(me));

    /// <summary>
    /// <b>Writes one register onto the session that draws its week</b>, replacing whatever that week
    /// held, and answers the record written. No undo step and no freezing: that is the caller's —
    /// <c>MainViewModel.SetAttendance</c> pushes one first, and a register written straight into the
    /// file has no history to push onto.
    ///
    /// <para><b>The list it is counted against is written down with it</b> (schema 17): a register
    /// being re-filed keeps the list it was first filed against, and a new one takes the class's list
    /// as it stands now.</para>
    /// </summary>
    public static AttendanceRecord File(
        Building building, ScheduleEntry entry, DateOnly week, List<Guid> presentIds,
        List<Guid>? excusedIds, string? filedBy, DateTimeOffset? filedAt)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(entry);
        var monday = WeekLabel.MondayOf(week);

        var previous = entry.Attendance.FirstOrDefault(a => WeekLabel.MondayOf(a.WeekMonday) == monday);
        var roster = SessionRoster.ForRegister(building, entry, previous).ToList();

        var record = new AttendanceRecord
        {
            WeekMonday = monday,
            RosterStudentIds = roster,
            PresentStudentIds = presentIds,
            ExcusedStudentIds = excusedIds ?? new List<Guid>(),
            FiledBy = string.IsNullOrWhiteSpace(filedBy) ? null : filedBy,
            FiledAt = filedAt ?? DateTimeOffset.UtcNow,
        };
        record.Normalise();
        Put(entry, record);
        return record;
    }

    /// <summary>
    /// <b>Puts a copy of a record onto a session, replacing that week's.</b> A copy, so two projects
    /// handed the same record never share a list somebody could later change under the other.
    /// </summary>
    public static void Put(ScheduleEntry entry, AttendanceRecord record)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(record);
        var monday = WeekLabel.MondayOf(record.WeekMonday);
        entry.Attendance.RemoveAll(a => WeekLabel.MondayOf(a.WeekMonday) == monday);
        entry.Attendance.Add(Copy(record));
    }

    /// <summary>A record nobody else holds a reference into.</summary>
    public static AttendanceRecord Copy(AttendanceRecord record) => new()
    {
        WeekMonday = WeekLabel.MondayOf(record.WeekMonday),
        RosterStudentIds = record.RosterStudentIds is { } roster ? new List<Guid>(roster) : null,
        PresentStudentIds = new List<Guid>(record.PresentStudentIds ?? new List<Guid>()),
        ExcusedStudentIds = new List<Guid>(record.ExcusedStudentIds ?? new List<Guid>()),
        FiledBy = record.FiledBy,
        FiledAt = record.FiledAt,
    };
}
