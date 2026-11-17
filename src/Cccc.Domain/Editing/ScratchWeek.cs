using Cccc.Domain.Model;

namespace Cccc.Domain.Editing;

/// <summary>
/// <b>The Scratch Week</b>: one week with no dates, shared by everybody who edits, to try a timetable
/// out in without touching any real week. Craig asked for it in the current app on 2026-11-17 (1.67.0),
/// with four safeguards, all kept here: copy in, never move; nothing in it counts; editors and
/// administrators only; one step to put it onto a real week.
///
/// <para><b>Its classes are a collection of their own</b>, <see cref="Timetable.Scratch"/>, and never part
/// of <see cref="Timetable.Sessions"/>. Every rule that counts (hours, time rules, a student's load,
/// registers, cover, clashes, Now, the printed sheets, the reports) reads the real sessions, so a plan
/// cannot be counted by any of them unless somebody writes a line to make it.</para>
///
/// <para><b>Its classes are dated in <see cref="Week.ScratchMonday"/></b>, a week no semester reaches, so
/// the plan, the class form, a drag and Copy a day work on it exactly as they do on a real week, through
/// <see cref="View"/> and <see cref="Edit"/>. <see cref="Edits.CheckLocks"/>, which every change passes,
/// keeps the two apart: nothing real is dated into that week, and nothing of the plan leaves it except
/// through <see cref="PlaceOnto"/>.</para>
/// </summary>
public static class ScratchWeek
{
    public static DateOnly Monday => Week.ScratchMonday;

    /// <summary>
    /// The timetable as the Scratch Week is drawn and edited: its classes in place of the real ones, and
    /// nothing that belongs to a real date (registers, absences, closures, events), so nothing in it is
    /// closed, away, owed cover or owed a register. Its spaces, groups, people and classes are the
    /// timetable's own.
    /// </summary>
    public static Timetable View(Timetable t) => t with
    {
        Sessions = t.Scratch,
        Scratch = EntitySet<Session>.Empty,
        Registers = EntitySet<Register>.Empty,
        Absences = EntitySet<Absence>.Empty,
        Closures = EntitySet<Closure>.Empty,
        Events = EntitySet<SchoolEvent>.Empty,
    };

    /// <summary>
    /// An ordinary edit of classes (<see cref="Edits.PutSession"/>, <see cref="Edits.Move"/>, a delete,
    /// Copy a day) made on <see cref="View"/> and written back to the Scratch Week. It may change classes
    /// and nothing else.
    /// </summary>
    public static Batch Edit(Timetable t, Func<Timetable, Batch> edit)
    {
        var b = edit(View(t));
        var ops = new List<Op>();
        foreach (var op in b.Ops)
        {
            if (op.Collection != Collections.Sessions.Name)
                throw new EditRefused("The Scratch Week holds classes and nothing else.");
            ops.Add(op with { Collection = Collections.Scratch.Name });
        }
        return new Batch(b.Label + " in the Scratch Week", [.. ops]);
    }

    /// <summary>
    /// Copies one class into the Scratch Week, on the same day of the week, beside whatever is there.
    /// The class itself is not touched, and the copy has an id of its own, so moving or deleting it in
    /// the plan changes nothing on the real week. Nothing that happened travels (cancelled, cover, moved).
    /// </summary>
    public static Batch CopyIn(Timetable t, Guid sessionId)
    {
        var s = t.Sessions[sessionId] ?? throw new EditRefused("That class has gone.");
        var copy = Edits.CopyOf(s, Week.DateOf(Monday, s.Date.DayOfWeek), Guid.NewGuid());
        return new Draft(t).Put(Collections.Scratch, copy).ToBatch($"Copied {s.DisplayTitle} to the Scratch Week");
    }

    /// <summary>Copies a whole real week into the Scratch Week, replacing whatever it held.</summary>
    public static Batch CopyWeekIn(Timetable t, DateOnly anyDateInWeek)
    {
        var from = Week.MondayOf(anyDateInWeek);
        if (Week.IsScratch(from)) throw new EditRefused("That is the Scratch Week already.");
        var d = new Draft(t);
        foreach (var old in t.Scratch.ToList()) d.Remove(Collections.Scratch, old.Id);
        foreach (var s in t.SessionsInWeek(from))
            d.Put(Collections.Scratch, Edits.CopyOf(s, Week.DateOf(Monday, s.Date.DayOfWeek), Edits.CopyId(s.Id, Monday)));
        return d.ToBatch($"Copied week {Week.IsoNumber(from)} to the Scratch Week");
    }

    /// <summary>
    /// Why the plan cannot be placed onto that week, or null when it can: the week has ended, or it
    /// already holds a register — the same two rules as Copy this week to….
    /// </summary>
    public static string? WhyNot(Timetable t, DateOnly anyDateInWeek, DateOnly today)
    {
        var to = Week.MondayOf(anyDateInWeek);
        return Week.IsScratch(to) ? "Choose a real week to place the plan onto."
            : Week.HasEnded(to, today) ? $"Week {Week.IsoNumber(to)} has ended, so its timetable is kept as it ran."
            : Edits.HoldsRegisters(t, to) ? $"Week {Week.IsoNumber(to)} already has registers taken, and a week with a register is never replaced: that would delete what somebody stood in a room and recorded."
            : null;
    }

    /// <summary>
    /// Puts the plan onto a real week, REPLACING what that week had: from then on those classes count
    /// like any other. The Scratch Week keeps its plan, so it can be placed again, onto the same week or
    /// another. Refused when <see cref="WhyNot"/> says so.
    /// </summary>
    public static Batch PlaceOnto(Timetable t, DateOnly anyDateInWeek, DateOnly today)
    {
        var to = Week.MondayOf(anyDateInWeek);
        if (WhyNot(t, to, today) is { } why) throw new EditRefused(why);
        var d = new Draft(t);
        Edits.ReplaceWeek(d, to, t.Scratch.OrderBy(s => s.Date).ThenBy(s => s.Start).ThenBy(s => s.Id), Monday);
        return d.ToBatch($"Placed the Scratch Week onto week {Week.IsoNumber(to)}");
    }

    /// <summary>Empties the Scratch Week.</summary>
    public static Batch Empty(Timetable t)
    {
        var d = new Draft(t);
        foreach (var s in t.Scratch.ToList()) d.Remove(Collections.Scratch, s.Id);
        return d.ToBatch("Emptied the Scratch Week");
    }
}
