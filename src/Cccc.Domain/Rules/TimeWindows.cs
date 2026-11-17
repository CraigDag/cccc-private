using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// The part of a session each person is in (schema 23). With no times, the whole session. The old
/// <c>FacilitatorParts</c>.
/// </summary>
public static class TimeWindows
{
    public static Assignment? AssignmentOf(Session s, Guid facilitatorId) =>
        s.Facilitators.FirstOrDefault(a => a.FacilitatorId == facilitatorId);

    /// <summary>An assigned person's own window, clamped to the session. Empty (start == end) when nothing is left.</summary>
    public static (TimeOnly Start, TimeOnly End) Assigned(Session s, Guid facilitatorId)
    {
        if (AssignmentOf(s, facilitatorId) is not { } a) return (s.Start, s.End);
        var from = Clamp(a.From ?? s.Start, s.Start, s.End);
        var until = Clamp(a.Until ?? s.End, s.Start, s.End);
        return until > from ? (from, until) : (from, from);
    }

    /// <summary>
    /// The window of somebody in the room. A person covering takes on the windows of the people they
    /// replaced (their union), widened by their own if they were assigned too (audit finding C2).
    /// </summary>
    public static (TimeOnly Start, TimeOnly End) InRoom(Session s, IReadOnlyCollection<Guid> away, Guid facilitatorId)
    {
        bool assigned = AssignmentOf(s, facilitatorId) is not null;
        var own = assigned ? Assigned(s, facilitatorId) : (s.Start, s.End);
        if (s.CoverIds.Contains(facilitatorId) && away.Count > 0)
        {
            var windows = away.Select(id => Assigned(s, id)).Where(w => w.End > w.Start).ToList();
            if (assigned && own.Item2 > own.Item1) windows.Add(own);
            if (windows.Count > 0) return (windows.Min(w => w.Start), windows.Max(w => w.End));
        }
        return own;
    }

    public static (TimeOnly Start, TimeOnly End) InRoom(Occurrence o, Guid facilitatorId) =>
        InRoom(o.Session, o.AwayIds, facilitatorId);

    public static TimeSpan Length(Session s, IReadOnlyCollection<Guid> away, Guid facilitatorId)
    {
        var (a, b) = InRoom(s, away, facilitatorId);
        return b > a ? b - a : TimeSpan.Zero;
    }

    public static TimeSpan Length(Occurrence o, Guid facilitatorId) =>
        o.InTheRoom.Contains(facilitatorId) ? Length(o.Session, o.AwayIds, facilitatorId) : TimeSpan.Zero;

    /// <summary>
    /// Whether the people away leave some stretch of the session with nobody present: each away
    /// person's window must be reached end to end by present people's windows.
    /// </summary>
    public static bool LeavesAGap(Session s, IReadOnlyCollection<Guid> away)
    {
        if (away.Count == 0) return false;
        var present = s.FacilitatorIds.Where(id => !away.Contains(id))
            .Select(id => Assigned(s, id)).Where(w => w.End > w.Start).ToList();
        foreach (var (from, until) in away.Select(id => Assigned(s, id)).Where(w => w.End > w.Start))
        {
            var reached = from;
            bool moved = true;
            while (reached < until && moved)
            {
                moved = false;
                foreach (var w in present)
                    if (w.Start <= reached && w.End > reached) { reached = w.End; moved = true; }
            }
            if (reached < until) return true;
        }
        return false;
    }

    public static bool Overlap(Occurrence a, Occurrence b, Guid facilitatorId)
    {
        if (a.Date != b.Date) return false;
        var (aS, aE) = InRoom(a, facilitatorId);
        var (bS, bE) = InRoom(b, facilitatorId);
        return aS < bE && bS < aE;
    }

    /// <summary>"from 14:00", "until 14:00", "13:30–14:30", or null for the whole session.</summary>
    public static string? Describe(Session s, Guid facilitatorId) => AssignmentOf(s, facilitatorId) switch
    {
        { From: { } f, Until: { } u } => $"{Week.Hm(f)}–{Week.Hm(u)}",
        { From: { } f } => $"from {Week.Hm(f)}",
        { Until: { } u } => $"until {Week.Hm(u)}",
        _ => null,
    };

    /// <summary>
    /// Drops parts for people not assigned, duplicates, whole-session parts and parts with nothing left.
    /// Called at every write door so the store never holds an ambiguous assignment.
    /// </summary>
    public static Session Normalise(Session s)
    {
        var kept = new List<Assignment>();
        foreach (var a in s.Facilitators)
        {
            if (kept.Any(k => k.FacilitatorId == a.FacilitatorId)) continue;
            var from = a.From is { } f && f > s.Start ? f : (TimeOnly?)null;
            var until = a.Until is { } u && u < s.End ? u : (TimeOnly?)null;
            if ((from ?? s.Start) >= (until ?? s.End)) { from = null; until = null; }
            kept.Add(new Assignment(a.FacilitatorId, from, until));
        }
        return s with { Facilitators = [.. kept] };
    }

    /// <summary>Moves every part by the same amount as the session moved.</summary>
    public static Session Shift(Session s, TimeSpan delta)
    {
        if (delta == TimeSpan.Zero) return s;
        return s with
        {
            Facilitators = [.. s.Facilitators.Select(a => a with
            {
                From = a.From is { } f ? ClampSpan(f.ToTimeSpan() + delta) : null,
                Until = a.Until is { } u ? ClampSpan(u.ToTimeSpan() + delta) : null,
            })],
        };
    }

    private static TimeOnly Clamp(TimeOnly v, TimeOnly lo, TimeOnly hi) => v < lo ? lo : v > hi ? hi : v;

    private static TimeOnly ClampSpan(TimeSpan v) =>
        v <= TimeSpan.Zero ? TimeOnly.MinValue : v >= TimeSpan.FromDays(1) ? TimeOnly.MaxValue : TimeOnly.FromTimeSpan(v);
}
