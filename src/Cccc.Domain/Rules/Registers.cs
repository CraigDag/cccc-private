using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

public enum RegisterState
{
    /// <summary>Shut by a closure.</summary>
    Closed,
    /// <summary>Cancelled, or nobody on it: no register.</summary>
    NotRunning,
    /// <summary>Today or later.</summary>
    ToCome,
    Taken,
    Missing,
}

/// <summary>Who is reading: what lets them change a register.</summary>
public sealed record RegisterReader(bool MayTakeEvery, Guid? FacilitatorId)
{
    public static RegisterReader Administrator { get; } = new(true, null);
    public static RegisterReader Nobody { get; } = new(false, null);
}

public static class Registers
{
    /// <summary>
    /// The one rule (Craig 2026-09-24 and 29): everybody reads every register; a facilitator changes one
    /// for a session they were in the room for (assigned or covering), through their linked account;
    /// administrators change all. Editors are narrowed like anybody else.
    /// </summary>
    public static bool MayChange(RegisterReader reader, Session s) =>
        reader.MayTakeEvery
        || (reader.FacilitatorId is { } me && (s.FacilitatorIds.Contains(me) || s.CoverIds.Contains(me)));

    /// <summary>
    /// A register is MISSING when three things are true: the session ran, somebody is on its roster, and
    /// its date is before today. One with nobody ticked is not missing — it says nobody came.
    /// </summary>
    public static RegisterState StateOf(Timetable t, Occurrence o, DateOnly today)
    {
        if (o.State == OccurrenceState.Closed) return RegisterState.Closed;
        if (o.State == OccurrenceState.Cancelled) return RegisterState.NotRunning;
        if (t.Registers.Contains(o.Session.Id)) return RegisterState.Taken;
        if (Roster.For(t, o.Session).Count == 0) return RegisterState.NotRunning;
        return o.Date >= today ? RegisterState.ToCome : RegisterState.Missing;
    }

    public static IReadOnlyList<Occurrence> MissingBetween(Timetable t, DateOnly from, DateOnly to, DateOnly today) =>
        Occurrences.Between(t, from, to).Where(o => StateOf(t, o, today) == RegisterState.Missing).ToList();

    public static IReadOnlyList<Occurrence> MissingInWeek(Timetable t, DateOnly anyDateInWeek, DateOnly today) =>
        Occurrences.ForWeek(t, anyDateInWeek).Where(o => StateOf(t, o, today) == RegisterState.Missing).ToList();

    /// <summary>The week bar's "N registers missing": only what this reader may file.</summary>
    public static IReadOnlyList<Occurrence> FileableBy(IEnumerable<Occurrence> missing, RegisterReader reader) =>
        missing.Where(o => MayChange(reader, o.Session)).ToList();

    /// <summary>Registers outstanding on a person count off the ASSIGNED roster, so covered weeks still count as theirs.</summary>
    public static int OutstandingFor(IEnumerable<Occurrence> missing, Guid facilitatorId) =>
        missing.Count(o => o.Session.FacilitatorIds.Contains(facilitatorId));

    /// <summary>
    /// Files a register: keeps the roster it was taken against (an earlier filing's, or today's), never
    /// lets present and excused overlap, and stamps who and when.
    /// </summary>
    public static Register File(Timetable t, Session s, IEnumerable<Guid> present, IEnumerable<Guid> excused,
        string? filedBy, DateTimeOffset filedAt)
    {
        var roster = Roster.ForRegister(t, s);
        var on = roster.ToHashSet();
        return new Register
        {
            Id = s.Id,
            RosterIds = [.. roster],
            PresentIds = [.. present.Where(on.Contains)],
            ExcusedIds = [.. excused.Where(on.Contains)],
            FiledBy = string.IsNullOrWhiteSpace(filedBy) ? null : filedBy,
            FiledAt = filedAt,
        }.Normalised();
    }

    /// <summary>Known absences pre-fill the excused list when a register is opened; the register then stores its own answer.</summary>
    public static IReadOnlyList<Guid> KnownAway(Timetable t, Session s) =>
        Roster.ForRegister(t, s)
            .Where(id => t.Absences.Any(a => a.Is(AbsentPerson.Student, id) && a.Covers(s.Date, s.Start, s.End)))
            .ToList();
}
