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
    /// session's old and new date, so nothing can be moved into or out of a locked week. The one change
    /// that may reach a session there is a merge of two classes (<see cref="MergeClass"/>), and its undo.
    /// </summary>
    public static void CheckLocks(Timetable t, Batch batch, DateOnly today)
    {
        foreach (var op in batch.Ops) CheckLock(op, batch, today);
    }

    /// <summary>
    /// Refuses one operation that changes a locked week, read with the batch it belongs to: undo checks
    /// each operation it puts back on its own, and a merge's re-pointing is only allowed beside the
    /// class it merges away (or, undone, beside the class it brings back).
    /// </summary>
    public static void CheckLock(Op op, Batch context, DateOnly today)
    {
        if (op.Collection == Collections.Sessions.Name)
        {
            if (IsMergeRepoint(op, context)) return;
            foreach (var node in new[] { op.Before, op.After })
            {
                if (node is null) continue;
                var s = System.Text.Json.JsonSerializer.Deserialize<Session>(node, ModelJson.Options)!;
                if (Week.HasEnded(s.Date, today))
                    throw new EditRefused($"The week of {Week.ShortDate(s.WeekMonday)} has ended, so its timetable is kept as it ran. Its registers can still be filed.");
                // The Scratch Week's classes are a list of their own; a real class dated into its week
                // would be counted by everything that reads the real timetable, in a week nobody sees.
                if (node == op.After && Week.IsScratch(s.Date))
                    throw new EditRefused("A class cannot be moved into the Scratch Week. Copy to Scratch Week puts a copy there and leaves the class where it is.");
            }
        }
        else if (op.Collection == Collections.Scratch.Name && op.After is not null)
        {
            var s = System.Text.Json.JsonSerializer.Deserialize<Session>(op.After!, ModelJson.Options)!;
            if (!Week.IsScratch(s.Date))
                throw new EditRefused("A class in the Scratch Week stays in it. Place onto a week… puts the plan onto a real week.");
        }
    }

    /// <summary>
    /// A session whose only change is the class it counts as, from a class the same batch deletes or to
    /// one the same batch puts back: two entries for one activity becoming one, which corrects the
    /// catalogue rather than what ran. Who, where, when and the title cannot change this way.
    /// </summary>
    private static bool IsMergeRepoint(Op op, Batch context)
    {
        if (op.Before is not System.Text.Json.Nodes.JsonObject before || op.After is not System.Text.Json.Nodes.JsonObject after) return false;
        var was = System.Text.Json.JsonSerializer.Deserialize<Session>(before, ModelJson.Options)!.ClassId;
        var now = System.Text.Json.JsonSerializer.Deserialize<Session>(after, ModelJson.Options)!.ClassId;
        if (was == now) return false;
        var b = before.DeepClone().AsObject();
        var a = after.DeepClone().AsObject();
        b.Remove("classId");
        a.Remove("classId");
        if (!System.Text.Json.Nodes.JsonNode.DeepEquals(b, a)) return false;
        var classes = context.Ops.Where(o => o.Collection == Collections.Classes.Name).ToList();
        return (was is { } w && classes.Any(o => o.Id == w && o.IsRemove))
            || (now is { } n && classes.Any(o => o.Id == n && o.IsAdd));
    }

    // ---- Sessions ---------------------------------------------------------------------------------

    /// <summary>Adds or replaces a session, normalising its people and its roster exceptions.</summary>
    public static Batch PutSession(Timetable t, Session s, string label = "Saved a class")
    {
        if (s.End <= s.Start) throw new EditRefused("A class must end after it starts.");
        return new Draft(t).Put(Collections.Sessions, Normalised(s)).ToBatch(label);
    }

    private static Session Normalised(Session s) => TimeWindows.Normalise(s) with
    {
        CoverIds = [.. s.CoverIds.Distinct()],
        AddedStudentIds = [.. s.AddedStudentIds.Distinct()],
        LeftOffStudentIds = [.. s.LeftOffStudentIds.Distinct()],
        Audience = s.Audience with { GroupIds = [.. s.Audience.GroupIds.Distinct()] },
    };

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
        return d.ToBatch(label ?? (n == 1 ? "Deleted a class" : $"Deleted {n} classes"));
    }

    /// <summary>
    /// A drag or a stretch: writes the date and the two times and nothing else, keeping each person's
    /// part in step. Times snap to the quarter hour and stay inside the day.
    /// </summary>
    public static Batch Move(Timetable t, Guid sessionId, DateOnly date, TimeOnly start, TimeOnly end)
    {
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That class has gone.");
        start = Snap(start);
        end = Snap(end);
        if (end <= start) end = start.AddMinutes(SnapMinutes);
        var delta = Week.Span(s.Start, start);
        bool stretched = end - start != s.Length;
        // A move carries each person's part with it; a stretch leaves the parts where they are in the day.
        var moved = (stretched ? s : TimeWindows.Shift(s, delta)) with
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
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That class has gone.");
        if (Week.MondayOf(date) != s.WeekMonday) throw new EditRefused("A reschedule stays inside its week. To move it to another week, copy it there.");
        var moved = TimeWindows.Shift(s, Week.Span(s.Start, start)) with
        {
            Date = date,
            Start = start,
            End = end,
            MovedFrom = s.MovedFrom ?? new MovedFrom(s.Date, s.Start, s.End),
            Notes = string.IsNullOrWhiteSpace(note) ? s.Notes : note,
        };
        return PutSession(t, moved, $"Rescheduled {s.DisplayTitle}");
    }

    /// <summary>
    /// A class put at a time found for it (Find a time), in the space given, each person's part moved with
    /// it. Saying it has moved remembers where it was first, as a reschedule does, so the plan and the
    /// screens show it; that stays inside its own week. Back where it first was, it no longer says so.
    /// </summary>
    public static Batch MoveTo(Timetable t, Guid sessionId, DateOnly date, TimeOnly start, Guid? roomId, bool sayMoved)
    {
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That class has gone.");
        var moved = Moved(s, date, start, roomId, sayMoved);
        return PutSession(t, moved, $"Moved {s.DisplayTitle} to {Week.ShortDate(date)}, {Week.Hm(moved.Start)}");
    }

    /// <summary>A class as <see cref="MoveTo"/> would leave it: each person's part moved with it, and where it was first when it says it has moved.</summary>
    public static Session Moved(Session s, DateOnly date, TimeOnly start, Guid? roomId, bool sayMoved)
    {
        if (sayMoved && Week.MondayOf(date) != s.WeekMonday) throw new EditRefused("A class says it has moved only inside its own week. To move it to another week, copy it there.");
        var moved = Openings.Placed(s, date, Snap(start), roomId);
        var from = sayMoved ? s.MovedFrom ?? new MovedFrom(s.Date, s.Start, s.End) : s.MovedFrom;
        return moved with { MovedFrom = from is { } f && f.Date == moved.Date && f.Start == moved.Start && f.End == moved.End ? null : from };
    }

    /// <summary>
    /// One of a class's ways out of a clash (<see cref="WaysOut"/>), taken. The ways are worked out again from the
    /// timetable as it is now, so one that somebody else's change has closed since it was offered is refused rather
    /// than taken into a new clash. Once its week has begun it says it has moved, as Find a time does.
    /// </summary>
    public static Batch TakeWayOut(Timetable t, Guid sessionId, DateOnly date, TimeOnly start, Guid? roomId, DateOnly today, TimeOnly now, EventViewer viewer)
    {
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That class has gone.");
        var way = WaysOut.For(t, sessionId, today, now, viewer).FirstOrDefault(w => w.Moved.Date == date && w.Moved.Start == start && w.Moved.RoomId == roomId)
            ?? throw new EditRefused("That is no longer a way out: something there has changed since. The ways out it has now are under the class.");
        return MoveTo(t, sessionId, date, start, roomId, sayMoved: Week.MondayOf(s.Date) <= today) with { Label = "Moved " + MovedWhere(t, s, way) };
    }

    /// <summary>
    /// Room made for a class (<see cref="Rules.MakeRoom"/>): the other class that was in its way moved to its way out,
    /// and the class put where it is going, as one step that one Undo takes back. <paramref name="placed"/> is the
    /// class as it is to be saved. Worked out again from the timetable as it is now, with the class there: when
    /// moving the other class there no longer takes it out of every clash, the step is refused rather than taken
    /// into a new one. Once the other class's week has begun, it says it has moved.
    /// </summary>
    public static Batch MakeRoom(Timetable t, Session placed, Guid otherId, DateOnly date, TimeOnly start, Guid? roomId,
        DateOnly today, TimeOnly now, EventViewer viewer)
    {
        if (placed.End <= placed.Start) throw new EditRefused("A class must end after it starts.");
        var other = t.Sessions[otherId] ?? throw new EditRefused("The class that was in the way has gone, so there is nothing to move. Look for a time again.");
        placed = Normalised(placed);
        var way = Rules.MakeRoom.Still(t, placed, otherId, date, start, roomId, today, now, viewer)
            ?? throw new EditRefused($"Moving {other.DisplayTitle} there no longer makes room: something has changed since. Look for a time again.");
        var moved = Moved(other, date, start, roomId, sayMoved: Week.MondayOf(other.Date) <= today);
        return new Draft(t).Put(Collections.Sessions, moved).Put(Collections.Sessions, placed)
            .ToBatch($"Made room for {placed.DisplayTitle}, {Week.ShortDate(placed.Date)} {Week.Hm(placed.Start)}, by moving {MovedWhere(t, other, way)}");
    }

    /// <summary>Where a way out takes a class, in a label: "Juggling to Studio B", or "Juggling to 11:00", with its space when that changes too.</summary>
    private static string MovedWhere(Timetable t, Session s, WayOut way)
    {
        string where = t.Rooms.Find(way.Moved.RoomId)?.Name ?? "no space";
        return way.SameTime ? $"{s.DisplayTitle} to {where}"
            : $"{s.DisplayTitle} to {Week.Hm(way.Moved.Start)}" + (way.Moved.RoomId != s.RoomId ? $" in {where}" : "");
    }

    public static Batch SetCancelled(Timetable t, Guid sessionId, bool cancelled, string? note)
    {
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That class has gone.");
        return PutSession(t, s with { Cancelled = cancelled, CancelNote = cancelled ? note : null },
            cancelled ? $"Cancelled {s.DisplayTitle}" : $"Put {s.DisplayTitle} back on");
    }

    public static Batch SetCover(Timetable t, Guid sessionId, IEnumerable<Guid> coverIds, string? note)
    {
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That class has gone.");
        var ids = coverIds.Distinct().ToImmutableList();
        return PutSession(t, s with { CoverIds = ids, CoverNote = ids.IsEmpty ? null : note },
            ids.IsEmpty ? $"Took the cover off {s.DisplayTitle}"
                        : $"{string.Join(" and ", ids.Select(id => t.Facilitators[id]?.Name ?? "somebody"))} covering {s.DisplayTitle}");
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

    /// <summary>
    /// A copy of a session on another date: nothing week-specific travels (cancelled, cover, moved,
    /// register), and nothing former (<see cref="Former.Strip"/>). Every copy goes through here.
    /// </summary>
    public static Session CopyOf(Timetable t, Session s, DateOnly date, Guid id) => Former.Strip(t, s) with
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
        foreach (var to in toWeeks.Select(Week.MondayOf).Distinct().Where(m => m != from && !Week.IsScratch(m)).OrderBy(m => m))
        {
            if (Week.HasEnded(to, today)) continue;
            // A week that already holds a register is never replaced: that would delete what somebody
            // stood in a room and recorded.
            if (HoldsRegisters(d.Current, to)) continue;
            ReplaceWeek(d, to, source, from);
            weeks++;
        }
        return d.ToBatch(weeks == 1 ? "Copied a week" : $"Copied a week onto {weeks} weeks");
    }

    /// <summary>
    /// Empties a real week and fills it with copies of <paramref name="source"/>, each on the same day
    /// of the week it had. The one implementation behind Copy this week to… and placing the Scratch Week.
    /// </summary>
    internal static void ReplaceWeek(Draft d, DateOnly to, IEnumerable<Session> source, DateOnly sourceMonday)
    {
        foreach (var old in d.Current.SessionsInWeek(to).ToList())
            d.Remove(Collections.Sessions, old.Id).Remove(Collections.Registers, old.Id);
        foreach (var s in source)
        {
            var date = to.AddDays(Math.Clamp(s.Date.DayNumber - sourceMonday.DayNumber, 0, 6));
            // The id is stable, so copying the same week twice gives the same ids; but a copy made last
            // time and moved to another week since still holds it, and putting over it would drag that
            // class back here and empty the day it was moved to. That copy gets a new id instead.
            var id = CopyId(s.Id, to);
            if (d.Current.Sessions.Contains(id)) id = Guid.NewGuid();
            d.Put(Collections.Sessions, CopyOf(d.Current, s, date, id));
        }
    }

    /// <summary>Whether any session of the week has a register filed: such a week is never replaced by a copy.</summary>
    public static bool HoldsRegisters(Timetable t, DateOnly anyDateInWeek) =>
        t.SessionsInWeek(anyDateInWeek).Any(s => t.Registers.Contains(s.Id));

    /// <summary>
    /// Records or changes an absence. For a student it also carries late news to registers already
    /// filed (somebody phones in at eleven): every register the absence covers moves that student from
    /// Absent (no word) to Excused. Present is never touched — somebody in the room saw them — and only
    /// a student on the list the register was taken against. Removing the absence later moves nothing
    /// back. This is the one change a filed register takes without being re-filed, and it only ever
    /// turns "we do not know why" into "we know why".
    /// </summary>
    public static Batch PutAbsence(Timetable t, Absence a)
    {
        var d = new Draft(t);
        bool isNew = !t.Absences.Contains(a.Id);
        int excused = PutAbsence(d, a);
        var label = isNew ? "Recorded an absence" : "Changed an absence";
        return d.ToBatch(excused == 0 ? label : $"{label}, and excused it on {excused} register{(excused == 1 ? "" : "s")} already taken");
    }

    /// <summary>An absence into a change being built, with whatever it excuses: the one way one is recorded. How many registers it excused.</summary>
    private static int PutAbsence(Draft d, Absence a)
    {
        var t = d.Current;
        d.Put(Collections.Absences, a);
        int excused = 0;
        if (a.Person == AbsentPerson.Student)
            foreach (var s in t.Sessions.Where(s => s.Date >= a.From && s.Date <= a.To && a.Covers(s.Date, s.Start, s.End)))
                if (t.Registers[s.Id] is { } r && r.RosterIds.Contains(a.PersonId) && r.MarkFor(a.PersonId) == Mark.Absent)
                {
                    d.Put(Collections.Registers, r with { ExcusedIds = r.ExcusedIds.Add(a.PersonId) });
                    excused++;
                }
        return excused;
    }

    // ---- What facilitators ask (Asking) -------------------------------------------------------------

    /// <summary>
    /// A facilitator saying they cannot come, for themselves only and from today on. It changes nothing on
    /// the timetable: an administrator's yes records the absence (<see cref="AnswerRequest"/>).
    /// </summary>
    public static Batch AskToBeAway(Timetable t, DateOnly from, DateOnly to, TimeOnly? start, TimeOnly? end,
        AskingReader reader, DateOnly today, DateTimeOffset at)
    {
        if (reader.FacilitatorId is not { } me || t.Facilitators[me] is not { } f || f.IsFormer())
            throw new EditRefused("Your account is not linked to a facilitator on this timetable, so there is nobody to be away.");
        if (from < today) throw new EditRefused("That day has gone. Say from today on.");
        if (to < from) throw new EditRefused("It has to end on or after the day it starts.");
        if ((start is null) != (end is null)) throw new EditRefused("Give both times, or neither for whole days.");
        if (start is { } a && end is { } b && b <= a) throw new EditRefused("The time has to finish after it starts.");
        var r = new TimeOffRequest { FacilitatorId = me, From = from, To = to, StartTime = start, EndTime = end, AskedOn = today, AskedAt = at };
        return new Draft(t).Put(Collections.Requests, r).ToBatch($"Asked to be away from {Week.ShortDate(from)}");
    }

    /// <summary>Whoever asked takes it back, while nobody has answered it.</summary>
    public static Batch TakeBackRequest(Timetable t, Guid id, AskingReader reader)
    {
        var r = t.Requests[id] ?? throw new EditRefused("That has been taken back already.");
        if (reader.FacilitatorId != r.FacilitatorId) throw new EditRefused("Only whoever asked can take it back.");
        if (r.Answer is not null) throw new EditRefused("An administrator has answered it already, so ask them to change it.");
        return new Draft(t).Remove(Collections.Requests, id).ToBatch("Took back asking to be away");
    }

    /// <summary>
    /// An administrator's answer. Yes records the absence it asks for, as Absences would (planned when asked
    /// before its first day), and the classes it takes them off then need cover like any other; no changes
    /// nothing on the timetable.
    /// </summary>
    public static Batch AnswerRequest(Timetable t, Guid id, Answer answer, AskingReader reader, DateTimeOffset at)
    {
        if (!reader.Answers) throw new EditRefused("Only an administrator can answer that.");
        var r = t.Requests[id] ?? throw new EditRefused("That has been taken back.");
        if (r.Answer is not null) throw new EditRefused("That has been answered already.");
        if (t.Facilitators[r.FacilitatorId] is not { } f || f.IsFormer()) throw new EditRefused("Whoever asked is no longer on this timetable.");
        var d = new Draft(t);
        if (answer == Answer.No)
            return d.Put(Collections.Requests, r with { Answer = Answer.No, AnsweredAt = at }).ToBatch("Said no to being away");
        var away = r.AsAbsence(Guid.NewGuid());
        PutAbsence(d, away);
        d.Put(Collections.Requests, r with { Answer = Answer.Yes, AnsweredAt = at, AbsenceId = away.Id });
        return d.ToBatch("Recorded an absence asked for");
    }

    /// <summary>
    /// A facilitator offering to cover a class that needs it: not one of theirs, and not one already passed.
    /// Whether they are free then is for the administrator to weigh: being busy is advice, never a refusal.
    /// </summary>
    public static Batch OfferCover(Timetable t, Guid sessionId, AskingReader reader, DateOnly today, DateTimeOffset at)
    {
        if (reader.FacilitatorId is not { } me || t.Facilitators[me] is not { } f || f.IsFormer())
            throw new EditRefused("Your account is not linked to a facilitator on this timetable, so there is nobody to offer.");
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That class has gone.");
        if (s.Date < today) throw new EditRefused("That class has been.");
        if (s.FacilitatorIds.Contains(me) || s.CoverIds.Contains(me)) throw new EditRefused("That class is yours already.");
        if (!Occurrences.Resolve(t, s).NeedsCover) throw new EditRefused("That class has cover now.");
        if (t.Offers.Any(o => o.SessionId == sessionId && o.FacilitatorId == me && o.Answer is null))
            throw new EditRefused("You have offered to cover it already.");
        var offer = new CoverOffer { SessionId = sessionId, FacilitatorId = me, OfferedAt = at };
        return new Draft(t).Put(Collections.Offers, offer).ToBatch($"Offered to cover {s.DisplayTitle}");
    }

    /// <summary>Whoever offered takes it back, while nobody has answered it.</summary>
    public static Batch TakeBackOffer(Timetable t, Guid id, AskingReader reader)
    {
        var o = t.Offers[id] ?? throw new EditRefused("That offer has been taken back already.");
        if (reader.FacilitatorId != o.FacilitatorId) throw new EditRefused("Only whoever offered can take it back.");
        if (o.Answer is not null) throw new EditRefused("An administrator has answered it already, so ask them to change it.");
        return new Draft(t).Remove(Collections.Offers, id).ToBatch("Took back an offer to cover");
    }

    /// <summary>
    /// An administrator's answer. Yes makes them the class's cover, as the Cover form would, while it still
    /// needs cover; no changes nothing on the timetable.
    /// </summary>
    public static Batch AnswerOffer(Timetable t, Guid id, Answer answer, AskingReader reader, DateOnly today, DateTimeOffset at)
    {
        if (!reader.Answers) throw new EditRefused("Only an administrator can answer that.");
        var o = t.Offers[id] ?? throw new EditRefused("That offer has been taken back.");
        if (o.Answer is not null) throw new EditRefused("That has been answered already.");
        var s = t.Sessions[o.SessionId] ?? throw new EditRefused("That class has gone.");
        var d = new Draft(t).Put(Collections.Offers, o with { Answer = answer, AnsweredAt = at });
        if (answer == Answer.No) return d.ToBatch("Said no thanks to an offer to cover");
        if (Asking.StateOf(t, o, today) != OfferState.Waiting) throw new EditRefused("That class does not need cover now.");
        if (t.Facilitators[o.FacilitatorId] is not { } f || f.IsFormer()) throw new EditRefused("Whoever offered is no longer on this timetable.");
        d.Put(Collections.Sessions, s with { CoverIds = [o.FacilitatorId], CoverNote = null });
        return d.ToBatch($"Said yes to an offer to cover {s.DisplayTitle}");
    }

    /// <summary>Copy day APPENDS to the destination day.</summary>
    public static Batch CopyDay(Timetable t, DateOnly from, DateOnly to)
    {
        var d = new Draft(t);
        foreach (var s in t.Sessions.Where(s => s.Date == from).OrderBy(s => s.Start))
            d.Put(Collections.Sessions, CopyOf(t, s, to, Guid.NewGuid()));
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
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That class has gone.");
        if (!Registers.MayChange(reader, s))
            throw new EditRefused("Only somebody in the room for this class, or an administrator, can change its register.");
        if (s.Audience.IsEveryone) throw new EditRefused("A class for everyone has nobody on it, so it has no register.");
        var r = Registers.File(t, s, present, excused, filedBy, at);
        return new Draft(t).Put(Collections.Registers, r).ToBatch($"Took the register for {s.DisplayTitle}");
    }

    // ---- The catalogue ----------------------------------------------------------------------------

    /// <summary>
    /// Every class a change to the catalogue reaches: the real ones in weeks not ended (a week that has
    /// ended keeps who taught it, where and what it was called), and every one in the Scratch Week, so a
    /// plan never holds somebody or something that has gone, and never places them back onto a real week.
    /// </summary>
    private static IEnumerable<(Collection<Session> In, Session S)> Reachable(Timetable t, DateOnly today) =>
        t.Sessions.Where(s => !Week.HasEnded(s.Date, today)).Select(s => (Collections.Sessions, s))
            .Concat(t.Scratch.Select(s => (Collections.Scratch, s)));

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
            foreach (var (into, s) in Reachable(t, today).Where(x => x.S.ClassId == c.Id && x.S.Title.Trim() == old.Name.Trim()))
                d.Put(into, s with { Title = c.Name });
        return d.ToBatch(old is null ? $"Added the class {c.Name}" : $"Changed the class {c.Name}");
    }

    /// <summary>
    /// Every class from today on, and every one in the Scratch Week: what deleting a class, a space or a
    /// facilitator takes it off. The days before today keep it, this week's included, because they ran.
    /// </summary>
    private static IEnumerable<(Collection<Session> In, Session S)> FromToday(Timetable t, DateOnly today) =>
        t.Sessions.Where(s => s.Date >= today).Select(s => (Collections.Sessions, s))
            .Concat(t.Scratch.Select(s => (Collections.Scratch, s)));

    /// <summary>
    /// Deleting a class takes it off every class from today on, which keep the name they showed. One that
    /// ran before today is kept as former for those days, still on its time rules so their credit stands;
    /// one that never ran is gone, and off every time rule.
    /// </summary>
    public static Batch RemoveClass(Timetable t, Guid id, DateOnly today)
    {
        var c = t.Classes[id] ?? throw new EditRefused("That class has gone.");
        var d = new Draft(t);
        foreach (var (into, s) in FromToday(t, today).Where(x => x.S.ClassId == id))
            d.Put(into, s with { ClassId = null, Title = string.IsNullOrWhiteSpace(s.Title) ? c.Name : s.Title });
        if (Former.Of(t, c, today).Before > 0)
        {
            if (!c.IsFormer()) d.Put(Collections.Classes, c with { FormerSince = today });
            return d.ToBatch($"Deleted the class {c.Name}, kept for the days it ran");
        }
        foreach (var r in t.TimeRules.Where(r => r.ClassIds.Contains(id)))
            d.Put(Collections.TimeRules, r with { ClassIds = r.ClassIds.Remove(id) });
        d.Remove(Collections.Classes, id);
        return d.ToBatch($"Deleted the class {c.Name}");
    }

    /// <summary>
    /// Deleting a space is a stated orphaning: its classes from today on keep running, with no space. One
    /// used before today is kept as former, so those days still say where they were, and so does an event
    /// that was wholly over by today.
    /// </summary>
    public static Batch RemoveRoom(Timetable t, Guid id, DateOnly today)
    {
        var r = t.Rooms[id] ?? throw new EditRefused("That space has gone.");
        var d = new Draft(t);
        foreach (var (into, s) in FromToday(t, today).Where(x => x.S.RoomId == id))
            d.Put(into, s with { RoomId = null });
        bool keep = Former.Of(t, r, today).Before > 0;
        foreach (var e in t.Events.Where(e => e.RoomId == id && !(keep && EventSchedule.LastDay(e) < today)))
            d.Put(Collections.Events, e with { RoomId = null });
        if (keep)
        {
            if (!r.IsFormer()) d.Put(Collections.Rooms, r with { FormerSince = today });
            return d.ToBatch($"Deleted the space {r.Name}, kept for the days it was used");
        }
        d.Remove(Collections.Rooms, id);
        return d.ToBatch($"Deleted the space {r.Name}");
    }

    /// <summary>
    /// Taking a piece of equipment off the school's list takes it off every space that has it and every class that
    /// needs it in the same step, so nothing is left naming what the school no longer lists, and one Undo puts
    /// all of it back.
    /// </summary>
    public static Batch RemoveEquipment(Timetable t, Guid id)
    {
        var e = t.Equipment[id] ?? throw new EditRefused("That equipment has gone.");
        var d = new Draft(t);
        foreach (var r in t.Rooms.Where(r => r.Has.Contains(id)))
            d.Put(Collections.Rooms, r with { Has = r.Has.RemoveAll(x => x == id) });
        foreach (var c in t.Classes.Where(c => c.Needs.Contains(id)))
            d.Put(Collections.Classes, c with { Needs = c.Needs.RemoveAll(x => x == id) });
        d.Remove(Collections.Equipment, id);
        return d.ToBatch($"Deleted the equipment {e.Name}");
    }

    /// <summary>
    /// Removing a facilitator takes them off every class from today on and every event not over by
    /// today. One who taught before today is kept as former, with every absence that began before today,
    /// so those days keep who taught, who was away and so who earned the hours; one who never taught is
    /// gone, absences and all. The label never names them: erasure cannot reach a label's words.
    /// </summary>
    public static Batch RemoveFacilitator(Timetable t, Guid id, DateOnly today)
    {
        var f = t.Facilitators[id] ?? throw new EditRefused("That facilitator has gone.");
        var d = new Draft(t);
        foreach (var (into, s) in FromToday(t, today).Where(x => x.S.FacilitatorIds.Contains(id) || x.S.CoverIds.Contains(id)))
            d.Put(into, s with
            {
                Facilitators = [.. s.Facilitators.Where(a => a.FacilitatorId != id)],
                CoverIds = s.CoverIds.Remove(id),
            });
        bool keep = Former.Of(t, f, today).Before > 0;
        foreach (var e in t.Events.Where(e => e.FacilitatorIds.Contains(id) && !(keep && EventSchedule.LastDay(e) < today)))
            d.Put(Collections.Events, e with { FacilitatorIds = e.FacilitatorIds.Remove(id) });
        foreach (var a in t.Absences.Where(a => a.Is(AbsentPerson.Facilitator, id) && !(keep && a.From < today)))
            d.Remove(Collections.Absences, a.Id);
        // What they asked and offered is theirs and is nobody's business once they have gone: it goes, all of it.
        foreach (var r in t.Requests.Where(r => r.FacilitatorId == id)) d.Remove(Collections.Requests, r.Id);
        foreach (var o in t.Offers.Where(o => o.FacilitatorId == id)) d.Remove(Collections.Offers, o.Id);
        if (keep)
        {
            if (!f.IsFormer()) d.Put(Collections.Facilitators, f with { FormerSince = today });
            return d.ToBatch("Removed a facilitator, kept for the days they taught");
        }
        d.Remove(Collections.Facilitators, id);
        return d.ToBatch("Removed a facilitator");
    }

    /// <summary>
    /// Brings a former class, space or facilitator back into use: offered again from now on. Nothing it was
    /// taken off comes back with it; that is what Undo is for.
    /// </summary>
    public static Batch BringBack(Timetable t, IRetirable x) => x switch
    {
        Class c => new Draft(t).Put(Collections.Classes, c with { FormerSince = null }).ToBatch($"Brought back the class {c.Name}"),
        Room r => new Draft(t).Put(Collections.Rooms, r with { FormerSince = null }).ToBatch($"Brought back the space {r.Name}"),
        Facilitator f => new Draft(t).Put(Collections.Facilitators, f with { FormerSince = null }).ToBatch("Brought back a facilitator"),
        _ => throw new ArgumentException("Only a class, a space or a facilitator can be former.", nameof(x)),
    };

    /// <summary>
    /// Two classes that are one activity become one: every class on the timetable that counts as
    /// <paramref name="mergedId"/> counts as <paramref name="survivorId"/> from then on, the weeks that have
    /// ended included, so no total over the year is split between two names for the same thing. It is the
    /// one change besides a register that reaches into an ended week, and there it changes only which
    /// class a session counts as: who, where, when and its title stay as they ran. In a week not ended, a
    /// class still called by the merged name takes the survivor's; a title of its own stays. The merged
    /// class leaves every time rule and is gone. The survivor's area, workload and time rules decide what
    /// those hours count towards, before today as well; <see cref="MergeDifferences"/> says so first.
    /// </summary>
    public static Batch MergeClass(Timetable t, Guid mergedId, Guid survivorId, DateOnly today)
    {
        if (mergedId == survivorId) throw new EditRefused("Choose another class to keep.");
        var merged = t.Classes[mergedId] ?? throw new EditRefused("That class has gone.");
        var survivor = t.Classes[survivorId] ?? throw new EditRefused("The class to keep has gone.");
        if (survivor.IsFormer()) throw new EditRefused($"{survivor.Name} is a former class. Bring it back first, or keep another.");
        var d = new Draft(t);
        bool Named(Session s) => s.Title.Trim() == merged.Name.Trim();
        foreach (var s in t.Sessions.Where(s => s.ClassId == mergedId))
            d.Put(Collections.Sessions, s with { ClassId = survivorId, Title = !Week.HasEnded(s.Date, today) && Named(s) ? survivor.Name : s.Title });
        foreach (var s in t.Scratch.Where(s => s.ClassId == mergedId))
            d.Put(Collections.Scratch, s with { ClassId = survivorId, Title = Named(s) ? survivor.Name : s.Title });
        foreach (var r in t.TimeRules.Where(r => r.ClassIds.Contains(mergedId)))
            d.Put(Collections.TimeRules, r with { ClassIds = r.ClassIds.Remove(mergedId) });
        d.Remove(Collections.Classes, mergedId);
        return d.ToBatch($"Merged {merged.Name} into {survivor.Name}");
    }

    /// <summary>
    /// What a merge changes beyond the name, in words for the person before they press: how many classes
    /// will count as the survivor, and where the two disagree on area, workload or a time rule, which
    /// the survivor's settings decide from then on, the days already run included.
    /// </summary>
    public static IReadOnlyList<string> MergeDifferences(Timetable t, Guid mergedId, Guid survivorId)
    {
        var merged = t.Classes[mergedId];
        var survivor = t.Classes[survivorId];
        if (merged is null || survivor is null || mergedId == survivorId) return [];
        int n = t.Sessions.Count(s => s.ClassId == mergedId) + t.Scratch.Count(s => s.ClassId == mergedId);
        var said = new List<string> { $"{(n == 1 ? "1 class" : $"{n} classes")} will count as {survivor.Name}, the days already run included." };
        var (wasArea, nowArea) = (t.Areas.Find(merged.AreaId), t.Areas.Find(survivor.AreaId));
        if (wasArea?.Id != nowArea?.Id)
            said.Add((wasArea, nowArea) switch
            {
                (null, { } a) => $"Their hours start counting towards {a.Name}.",
                ({ } a, null) => $"Their hours stop counting towards {a.Name}.",
                _ => $"Their hours count towards {nowArea!.Name}, not {wasArea!.Name}.",
            });
        // Workload words only when counting really changes: an open session and a not-counted class
        // are both outside a student's hours, so merging one into the other moves no total.
        bool wasCounted = merged.Workload == Workload.Counted, nowCounted = survivor.Workload == Workload.Counted;
        if (wasCounted != nowCounted)
            said.Add(nowCounted ? "They start counting towards a student's hours." : "They stop counting towards a student's hours.");
        foreach (var r in t.TimeRules.OrderBy(r => r.DisplayName, NameOrder.Comparer))
        {
            bool had = r.ClassIds.Contains(mergedId), has = r.ClassIds.Contains(survivorId);
            if (had && !has) said.Add($"{r.DisplayName} is no longer credited for them.");
            else if (!had && has) said.Add($"{r.DisplayName} is credited for them too.");
        }
        return said;
    }

    /// <summary>
    /// Removing a student takes them off every session's exceptions and every event, and removes their
    /// absences (audit finding C3: a live delete sweeps what a reload would). Filed registers keep the
    /// id, so a class's counts still add up; erasure is a separate step that rewrites history.
    /// </summary>
    public static Batch RemoveStudent(Timetable t, Guid id, DateOnly today)
    {
        var d = new Draft(t);
        // Weeks that have ended do not change: a dangling id there names nobody and counts nobody.
        foreach (var (into, s) in Reachable(t, today).Where(x => x.S.AddedStudentIds.Contains(id) || x.S.LeftOffStudentIds.Contains(id)))
            d.Put(into, s with { AddedStudentIds = s.AddedStudentIds.Remove(id), LeftOffStudentIds = s.LeftOffStudentIds.Remove(id) });
        foreach (var e in t.Events.Where(e => e.StudentIds.Contains(id)))
            d.Put(Collections.Events, e with { StudentIds = e.StudentIds.Remove(id) });
        foreach (var a in t.Absences.Where(a => a.Is(AbsentPerson.Student, id)))
            d.Remove(Collections.Absences, a.Id);
        d.Remove(Collections.Students, id);
        return d.ToBatch("Removed a student");
    }

    /// <summary>
    /// Removing a group takes it off sessions ahead (a session that was for that group alone is left for
    /// no group, never turned into a whole-building one), off closures, and leaves its students without
    /// a group.
    /// </summary>
    public static Batch RemoveGroup(Timetable t, Guid id, DateOnly today)
    {
        var d = new Draft(t);
        foreach (var (into, s) in Reachable(t, today).Where(x => x.S.Audience.GroupIds.Contains(id)))
        {
            var left = s.Audience.GroupIds.Remove(id);
            var audience = left.IsEmpty
                ? s.Audience with { GroupIds = [], Kind = AudienceKind.Group, ProgrammeId = null }
                : s.Audience with { GroupIds = left, Kind = left.Count == 1 && s.Audience.Kind == AudienceKind.Groups ? AudienceKind.Group : s.Audience.Kind };
            d.Put(into, s with { Audience = audience });
        }
        foreach (var c in t.Closures.Where(c => c.GroupIds.Contains(id)))
            d.Put(Collections.Closures, c with { GroupIds = c.GroupIds.Remove(id) });
        foreach (var st in t.Students.Where(st => st.GroupId == id))
            d.Put(Collections.Students, st with { GroupId = null });
        d.Remove(Collections.Groups, id);
        return d.ToBatch("Deleted a group");
    }

    public static Batch RemoveProgramme(Timetable t, Guid id, DateOnly today)
    {
        var d = new Draft(t);
        foreach (var g in t.Groups.Where(g => g.ProgrammeId == id))
            d.Put(Collections.Groups, g with { ProgrammeId = null });
        foreach (var (into, s) in Reachable(t, today).Where(x => x.S.Audience.ProgrammeId == id))
            d.Put(into, s with { Audience = s.Audience with { ProgrammeId = null, Kind = s.Audience.GroupIds.Count == 1 ? AudienceKind.Group : AudienceKind.Groups } });
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
