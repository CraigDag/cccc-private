using System.Collections.Immutable;
using System.Security.Cryptography;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;

namespace Cccc.Domain.Editing;

/// <summary>A change the rules refuse, in words for the person who asked.</summary>
public sealed class EditRefused(string message) : Exception(message);

/// <summary>
/// The commands a person can give, each compiled to one batch. Every one starts from the timetable
/// as it is now and is handed today, so nothing here reads a clock.
/// </summary>
public static class Edits
{
    public const int SnapMinutes = 15;

    /// <summary>
    /// Refuses a batch that changes a session in a week that has wholly ended. Registers are the
    /// exception: they may still be filed for an ended week. Locking is checked against BOTH the
    /// session's old and new date, so nothing can be moved into or out of a locked week.
    /// </summary>
    public static void CheckLocks(Timetable t, Batch batch, DateOnly today)
    {
        foreach (var op in batch.Ops.Where(o => o.Collection == Collections.Sessions.Name))
        {
            foreach (var node in new[] { op.Before, op.After })
            {
                if (node is null) continue;
                var s = System.Text.Json.JsonSerializer.Deserialize<Session>(node, ModelJson.Options)!;
                if (Week.HasEnded(s.Date, today))
                    throw new EditRefused($"The week of {Week.ShortDate(s.WeekMonday)} has ended, so its timetable is kept as it ran. Its registers can still be filed.");
            }
        }
    }

    // ---- Sessions ---------------------------------------------------------------------------------

    /// <summary>Adds or replaces a session, normalising its people and its roster exceptions.</summary>
    public static Batch PutSession(Timetable t, Session s, string label = "Saved a session")
    {
        if (s.End <= s.Start) throw new EditRefused("A session must end after it starts.");
        s = Windows.Normalise(s) with
        {
            CoverIds = [.. s.CoverIds.Distinct()],
            AddedStudentIds = [.. s.AddedStudentIds.Distinct()],
            LeftOffStudentIds = [.. s.LeftOffStudentIds.Distinct()],
            Audience = s.Audience with { GroupIds = [.. s.Audience.GroupIds.Distinct()] },
        };
        return new Draft(t).Put(Collections.Sessions, s).ToBatch(label);
    }

    /// <summary>Removes sessions and their registers.</summary>
    public static Batch RemoveSessions(Timetable t, IEnumerable<Guid> ids, string? label = null)
    {
        var d = new Draft(t);
        int n = 0;
        foreach (var id in ids.Distinct())
        {
            if (!t.Sessions.Contains(id)) continue;
            d.Remove(Collections.Sessions, id).Remove(Collections.Registers, id);
            n++;
        }
        return d.ToBatch(label ?? (n == 1 ? "Deleted a session" : $"Deleted {n} sessions"));
    }

    /// <summary>
    /// A drag or a stretch: writes the date and the two times and nothing else, keeping each person's
    /// part in step. Times snap to the quarter hour and stay inside the day.
    /// </summary>
    public static Batch Move(Timetable t, Guid sessionId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That session has gone.");
        start = Snap(start);
        end = Snap(end);
        if (end <= start) end = start.AddMinutes(SnapMinutes);
        var delta = start - s.Start;
        bool stretched = end - start != s.Length;
        // A move carries each person's part with it; a stretch leaves the parts where they are in the day.
        var moved = (stretched ? s : Windows.Shift(s, delta)) with
        {
            Date = date,
            Start = start,
            End = end,
        };
        return PutSession(t, moved, stretched ? $"Changed the times of {s.DisplayTitle}" : $"Moved {s.DisplayTitle}");
    }

    /// <summary>Reschedules one occurrence, remembering where it was for the reader.</summary>
    public static Batch Reschedule(Timetable t, Guid sessionId, DateOnly date, TimeOnly start, TimeOnly end, string? note)
    {
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That session has gone.");
        if (Week.MondayOf(date) != s.WeekMonday) throw new EditRefused("A reschedule stays inside its week. To move it to another week, copy it there.");
        var moved = Windows.Shift(s, start - s.Start) with
        {
            Date = date,
            Start = start,
            End = end,
            MovedFrom = s.MovedFrom ?? new MovedFrom(s.Date, s.Start, s.End),
            Notes = string.IsNullOrWhiteSpace(note) ? s.Notes : note,
        };
        return PutSession(t, moved, $"Rescheduled {s.DisplayTitle}");
    }

    public static Batch SetCancelled(Timetable t, Guid sessionId, bool cancelled, string? note)
    {
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That session has gone.");
        return PutSession(t, s with { Cancelled = cancelled, CancelNote = cancelled ? note : null },
            cancelled ? $"Cancelled {s.DisplayTitle}" : $"Put {s.DisplayTitle} back on");
    }

    public static Batch SetCover(Timetable t, Guid sessionId, IEnumerable<Guid> coverIds, string? note)
    {
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That session has gone.");
        var ids = coverIds.Distinct().ToImmutableList();
        return PutSession(t, s with { CoverIds = ids, CoverNote = ids.IsEmpty ? null : note },
            ids.IsEmpty ? $"Took the cover off {s.DisplayTitle}" : $"Assigned cover for {s.DisplayTitle}");
    }

    public static TimeOnly Snap(TimeOnly t)
    {
        int m = t.Hour * 60 + t.Minute;
        m = (int)Math.Round(m / (double)SnapMinutes) * SnapMinutes;
        return m >= 24 * 60 ? new TimeOnly(23, 45) : new TimeOnly(m / 60, m % 60);
    }

    // ---- Weeks ------------------------------------------------------------------------------------

    /// <summary>
    /// A stable id for a copy of a session in another week, so copying the same week twice produces the
    /// same ids and an imported pattern lands on the ids its registers expect (the old <c>FrozenCopyId</c>).
    /// </summary>
    public static Guid CopyId(Guid sourceId, DateOnly anyDateInWeek)
    {
        var monday = Week.MondayOf(anyDateInWeek);
        Span<byte> input = stackalloc byte[20];
        sourceId.TryWriteBytes(input);
        BitConverter.TryWriteBytes(input[16..], monday.DayNumber);
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        var bytes = hash[..16].ToArray();
        bytes[7] = (byte)((bytes[7] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    /// <summary>A copy of a session on another date: nothing week-specific travels (cancelled, cover, moved, register).</summary>
    public static Session CopyOf(Session s, DateOnly date, Guid id) => s with
    {
        Id = id,
        Date = date,
        Cancelled = false,
        CancelNote = null,
        CoverIds = [],
        CoverNote = null,
        MovedFrom = null,
    };

    /// <summary>How many sessions a copy onto that week would replace — said to the person before they press.</summary>
    public static int SessionCountIn(Timetable t, DateOnly anyDateInWeek) => t.SessionsInWeek(anyDateInWeek).Count();

    /// <summary>
    /// Copy week REPLACES each destination week and never merges into it: appending would silently
    /// double every session. Copying a blank week empties the destination, legitimately.
    /// </summary>
    public static Batch CopyWeek(Timetable t, DateOnly fromAnyDate, IEnumerable<DateOnly> toWeeks, DateOnly today)
    {
        var from = Week.MondayOf(fromAnyDate);
        var source = t.SessionsInWeek(from).ToList();
        var d = new Draft(t);
        int weeks = 0;
        foreach (var to in toWeeks.Select(Week.MondayOf).Distinct().Where(m => m != from).OrderBy(m => m))
        {
            if (Week.HasEnded(to, today)) continue;
            // A week that already holds a register is never replaced: that would delete what somebody
            // stood in a room and recorded.
            if (HoldsRegisters(d.Current, to)) continue;
            foreach (var old in d.Current.SessionsInWeek(to).ToList())
                d.Remove(Collections.Sessions, old.Id).Remove(Collections.Registers, old.Id);
            foreach (var s in source)
            {
                var date = to.AddDays(s.Date.DayNumber - from.DayNumber);
                d.Put(Collections.Sessions, CopyOf(s, date, CopyId(s.Id, to)));
            }
            weeks++;
        }
        return d.ToBatch(weeks == 1 ? "Copied a week" : $"Copied a week onto {weeks} weeks");
    }

    /// <summary>Whether any session of the week has a register filed: such a week is never replaced by a copy.</summary>
    public static bool HoldsRegisters(Timetable t, DateOnly anyDateInWeek) =>
        t.SessionsInWeek(anyDateInWeek).Any(s => t.Registers.Contains(s.Id));

    /// <summary>Copy day APPENDS to the destination day.</summary>
    public static Batch CopyDay(Timetable t, DateOnly from, DateOnly to)
    {
        var d = new Draft(t);
        foreach (var s in t.Sessions.Where(s => s.Date == from).OrderBy(s => s.Start))
            d.Put(Collections.Sessions, CopyOf(s, to, Guid.NewGuid()));
        return d.ToBatch($"Copied {Week.ShortDate(from)} onto {Week.ShortDate(to)}");
    }

    /// <summary>
    /// Empties every week after TODAY's (not the week on screen), inside the year, on a press, never
    /// automatically. Weeks already run keep their registers.
    /// </summary>
    public static Batch ClearWeeksAfter(Timetable t, DateOnly today)
    {
        var after = Week.MondayOf(today).AddDays(7);
        var end = t.YearEndMonday?.AddDays(7) ?? DateOnly.MaxValue;
        var ids = t.Sessions.Where(s => s.Date >= after && s.Date < end).Select(s => s.Id).ToList();
        return RemoveSessions(t, ids, "Cleared the weeks after this one");
    }

    public static int CountClearedAfter(Timetable t, DateOnly today)
    {
        var after = Week.MondayOf(today).AddDays(7);
        var end = t.YearEndMonday?.AddDays(7) ?? DateOnly.MaxValue;
        return t.Sessions.Count(s => s.Date >= after && s.Date < end);
    }

    // ---- Registers --------------------------------------------------------------------------------

    public static Batch FileRegister(Timetable t, Guid sessionId, IEnumerable<Guid> present, IEnumerable<Guid> excused,
        RegisterReader reader, string filedBy, DateTimeOffset at)
    {
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That session has gone.");
        if (!Registers.MayChange(reader, s))
            throw new EditRefused("Only somebody in the room for this class, or an administrator, can change its register.");
        if (s.Audience.IsEveryone) throw new EditRefused("A session for everyone has nobody on it, so it has no register.");
        var r = Registers.File(t, s, present, excused, filedBy, at);
        return new Draft(t).Put(Collections.Registers, r).ToBatch($"Took the register for {s.DisplayTitle}");
    }

    // ---- The catalogue ----------------------------------------------------------------------------

    /// <summary>
    /// Renames a class and the sessions still carrying its old title, in weeks not locked. A session
    /// given a title of its own keeps it.
    /// </summary>
    public static Batch PutClass(Timetable t, Class c, DateOnly today)
    {
        var d = new Draft(t);
        var old = t.Classes[c.Id];
        d.Put(Collections.Classes, c);
        if (old is not null && old.Name != c.Name)
            foreach (var s in t.Sessions.Where(s => s.ClassId == c.Id && s.Title.Trim() == old.Name.Trim() && !Week.HasEnded(s.Date, today)))
                d.Put(Collections.Sessions, s with { Title = c.Name });
        return d.ToBatch(old is null ? $"Added the class {c.Name}" : $"Changed the class {c.Name}");
    }

    /// <summary>Deleting a class leaves its sessions with their titles and no class, and takes it off every time rule.</summary>
    public static Batch RemoveClass(Timetable t, Guid id, DateOnly today)
    {
        var d = new Draft(t);
        foreach (var s in t.Sessions.Where(s => s.ClassId == id && !Week.HasEnded(s.Date, today)))
            d.Put(Collections.Sessions, s with { ClassId = null });
        foreach (var r in t.TimeRules.Where(r => r.ClassIds.Contains(id)))
            d.Put(Collections.TimeRules, r with { ClassIds = r.ClassIds.Remove(id) });
        d.Remove(Collections.Classes, id);
        return d.ToBatch("Deleted a class");
    }

    /// <summary>Deleting a room is a stated orphaning: its sessions keep running, with no room.</summary>
    public static Batch RemoveRoom(Timetable t, Guid id, DateOnly today)
    {
        var d = new Draft(t);
        foreach (var s in t.Sessions.Where(s => s.RoomId == id && !Week.HasEnded(s.Date, today)))
            d.Put(Collections.Sessions, s with { RoomId = null });
        foreach (var e in t.Events.Where(e => e.RoomId == id))
            d.Put(Collections.Events, e with { RoomId = null });
        d.Remove(Collections.Rooms, id);
        return d.ToBatch("Deleted a room");
    }

    /// <summary>
    /// Removing a facilitator takes them off every session ahead (ended weeks keep who taught them),
    /// every event, and removes their absences.
    /// </summary>
    public static Batch RemoveFacilitator(Timetable t, Guid id, DateOnly today)
    {
        var d = new Draft(t);
        foreach (var s in t.Sessions.Where(s => (s.FacilitatorIds.Contains(id) || s.CoverIds.Contains(id)) && !Week.HasEnded(s.Date, today)))
            d.Put(Collections.Sessions, s with
            {
                Facilitators = [.. s.Facilitators.Where(a => a.FacilitatorId != id)],
                CoverIds = s.CoverIds.Remove(id),
            });
        foreach (var e in t.Events.Where(e => e.FacilitatorIds.Contains(id)))
            d.Put(Collections.Events, e with { FacilitatorIds = e.FacilitatorIds.Remove(id) });
        foreach (var a in t.Absences.Where(a => a.Is(AbsentPerson.Facilitator, id)))
            d.Remove(Collections.Absences, a.Id);
        d.Remove(Collections.Facilitators, id);
        return d.ToBatch("Removed a facilitator");
    }

    /// <summary>
    /// Removing a student takes them off every session's exceptions and every event, and removes their
    /// absences (audit finding C3: a live delete sweeps what a reload would). Filed registers keep the
    /// id, so a class's counts still add up; erasure is a separate step that rewrites history.
    /// </summary>
    public static Batch RemoveStudent(Timetable t, Guid id)
    {
        var d = new Draft(t);
        foreach (var s in t.Sessions.Where(s => s.AddedStudentIds.Contains(id) || s.LeftOffStudentIds.Contains(id)))
            d.Put(Collections.Sessions, s with { AddedStudentIds = s.AddedStudentIds.Remove(id), LeftOffStudentIds = s.LeftOffStudentIds.Remove(id) });
        foreach (var e in t.Events.Where(e => e.StudentIds.Contains(id)))
            d.Put(Collections.Events, e with { StudentIds = e.StudentIds.Remove(id) });
        foreach (var a in t.Absences.Where(a => a.Is(AbsentPerson.Student, id)))
            d.Remove(Collections.Absences, a.Id);
        d.Remove(Collections.Students, id);
        return d.ToBatch("Removed a student");
    }

    /// <summary>
    /// Removing a group takes it off sessions ahead (a session left with no group becomes for everyone
    /// only if it was for that one group alone — it is then a whole-building session, said in the label),
    /// off closures, and leaves its students without a group.
    /// </summary>
    public static Batch RemoveGroup(Timetable t, Guid id, DateOnly today)
    {
        var d = new Draft(t);
        foreach (var s in t.Sessions.Where(s => s.Audience.GroupIds.Contains(id) && !Week.HasEnded(s.Date, today)))
        {
            var left = s.Audience.GroupIds.Remove(id);
            var audience = left.IsEmpty
                ? Audience.Everyone
                : s.Audience with { GroupIds = left, Kind = left.Count == 1 && s.Audience.Kind == AudienceKind.Groups ? AudienceKind.Group : s.Audience.Kind };
            d.Put(Collections.Sessions, s with { Audience = audience });
        }
        foreach (var c in t.Closures.Where(c => c.GroupIds.Contains(id)))
            d.Put(Collections.Closures, c with { GroupIds = c.GroupIds.Remove(id) });
        foreach (var st in t.Students.Where(st => st.GroupId == id))
            d.Put(Collections.Students, st with { GroupId = null });
        d.Remove(Collections.Groups, id);
        return d.ToBatch("Deleted a group");
    }

    public static Batch RemoveProgramme(Timetable t, Guid id)
    {
        var d = new Draft(t);
        foreach (var g in t.Groups.Where(g => g.ProgrammeId == id))
            d.Put(Collections.Groups, g with { ProgrammeId = null });
        foreach (var s in t.Sessions.Where(s => s.Audience.ProgrammeId == id))
            d.Put(Collections.Sessions, s with { Audience = s.Audience with { ProgrammeId = null, Kind = s.Audience.GroupIds.Count == 1 ? AudienceKind.Group : AudienceKind.Groups } });
        d.Remove(Collections.Programmes, id);
        return d.ToBatch("Deleted a programme");
    }

    /// <summary>The groups of a programme as a session audience.</summary>
    public static Audience ForProgramme(Timetable t, Guid programmeId) => new()
    {
        Kind = AudienceKind.Programme,
        ProgrammeId = programmeId,
        GroupIds = [.. t.GroupsOf(programmeId).OrderBy(g => g.Name, NameOrder.Comparer).Select(g => g.Id)],
    };
}
