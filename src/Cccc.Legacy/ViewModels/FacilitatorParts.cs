using CCCCScheduler.Helpers;
using CCCCScheduler.Models;

namespace CCCCScheduler.ViewModels;

/// <summary>
/// <b>When each facilitator is in a session</b> — schema 23, Craig's ask of 2026-11-07. Circus
/// Disciplines runs 13:00–15:00; somebody teaching elsewhere until 14:00 is in it from 14:00, so
/// their clash check, their hours, their preparation time and their availability all read
/// 14:00–15:00 and the session stays one block with one register.
///
/// <para><b>The one place a part is read.</b> Everything that asks "is this person in the room, and
/// when" asks here: <see cref="ScheduleConflicts"/>, <see cref="WeeklyLoad"/>, <see cref="TimeRules"/>,
/// <see cref="ScheduleResolver"/>'s cover rule. A second reading of
/// <see cref="ScheduleEntry.FacilitatorParts"/> is how the hours and the clash check would come to
/// disagree about the same afternoon.</para>
///
/// <para><b>Somebody with no part is in the whole session</b>, so on every session without parts —
/// every session written before schema 23 — each answer here is exactly what the session's own times
/// already said.</para>
/// </summary>
public static class FacilitatorParts
{
    /// <summary>
    /// The part <paramref name="facilitatorId"/> has on <paramref name="entry"/>, or null when they are
    /// in the whole of it — including when they are not on it at all, since a part for somebody not
    /// on <see cref="ScheduleEntry.FacilitatorIds"/> means nothing.
    /// </summary>
    public static FacilitatorPart? PartOf(ScheduleEntry entry, Guid facilitatorId)
        => entry.FacilitatorIds.Contains(facilitatorId)
            ? entry.FacilitatorParts.FirstOrDefault(p => p.FacilitatorId == facilitatorId)
            : null;

    /// <summary>
    /// <b>When <paramref name="facilitatorId"/> is in <paramref name="entry"/>, on an occurrence that
    /// runs <paramref name="start"/>–<paramref name="end"/>.</b> A part moves with an occurrence that
    /// has been moved (a one-off change to 15:00 takes "from 14:00" to "from 16:00") and is clamped to
    /// the occurrence's own hours, so nobody is ever counted outside the session they are on.
    /// </summary>
    public static (TimeOnly Start, TimeOnly End) Window(
        ScheduleEntry entry, Guid facilitatorId, TimeOnly start, TimeOnly end)
    {
        if (PartOf(entry, facilitatorId) is not { } part) return (start, end);

        var offset = start.ToTimeSpan() - entry.Start.ToTimeSpan();
        var from = Clamp((part.From ?? entry.Start).ToTimeSpan() + offset, start, end);
        var until = Clamp((part.Until ?? entry.End).ToTimeSpan() + offset, start, end);
        return until > from ? (from, until) : (from, from);
    }

    /// <summary>
    /// <b>One person's week drawn at their own times</b> — Craig's ask of 2026-11-08: Evelina is in
    /// Circus Disciplines 14:00–15:00 of its 13:00–15:00, and her own calendar has to say so. Each
    /// row <paramref name="facilitatorId"/> has a part on comes back as <see cref="SessionRow.PartFor"/>
    /// at their window; every other row comes back as itself. The week everybody else reads keeps
    /// the class's own times, so this is called only where the week is one person's.
    ///
    /// <para><b>The same copy comes back for the same row</b> while its times still agree, because the
    /// calendar asks afresh on every render and a new copy each time would be a block nobody can keep
    /// selected or focused.</para>
    /// </summary>
    public static IReadOnlyList<SessionRow> AsSeenBy(IEnumerable<SessionRow> rows, Guid facilitatorId)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (facilitatorId == Guid.Empty) return rows as IReadOnlyList<SessionRow> ?? rows.ToList();
        return rows.Select(r => AsSeenBy(r, facilitatorId)).ToList();
    }

    /// <inheritdoc cref="AsSeenBy(IEnumerable{SessionRow}, Guid)"/>
    public static SessionRow AsSeenBy(SessionRow row, Guid facilitatorId)
    {
        ArgumentNullException.ThrowIfNull(row);
        var original = row.Original;
        if (PartOf(original.Entry, facilitatorId) is null) return original;
        var (from, until) = Window(original.Entry, facilitatorId, original.StartTime, original.EndTime);
        if (until <= from || (from == original.StartTime && until == original.EndTime)) return original;

        var parts = Drawn.GetOrCreateValue(original);
        lock (parts)
        {
            if (parts.TryGetValue(facilitatorId, out var kept)
                && kept.StartTime == from && kept.EndTime == until)
                return kept;
            var part = original.PartFor(from, until);
            parts[facilitatorId] = part;
            return part;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<SessionRow, Dictionary<Guid, SessionRow>> Drawn = new();

    /// <summary>
    /// <b>When <paramref name="facilitatorId"/> is in this occurrence.</b> A substitute stands in for
    /// the hours of the person they replace: covering somebody who was in from 14:00 is from 14:00, and
    /// standing in for two people is from the earlier one's start to the later one's end. A substitute
    /// with nobody to replace — cover assigned while nobody is away — is in the whole session.
    ///
    /// <para><b>A co-teacher named as cover for their partner is in for both their hours</b>: their
    /// own, and the partner's. Somebody who stayed on while a third person covers keeps only their
    /// own. Until an outside audit on 2026-11-16 the co-teacher kept only their own either way, so the
    /// partner's hours went to nobody and the clash check missed them.</para>
    /// </summary>
    public static (TimeOnly Start, TimeOnly End) Window(ResolvedSession session, Guid facilitatorId)
    {
        var entry = session.Entry;
        bool onRoster = entry.FacilitatorIds.Contains(facilitatorId);
        var own = onRoster ? Window(entry, facilitatorId, session.Start, session.End) : (session.Start, session.End);

        bool covering = session.Covered
            && (!onRoster || session.CoverFacilitatorIds?.Contains(facilitatorId) == true);
        if (covering)
        {
            var replaced = entry.FacilitatorIds.Where(id => !session.FacilitatorIds.Contains(id)).ToList();
            if (replaced.Count > 0)
            {
                var windows = replaced.Select(id => Window(entry, id, session.Start, session.End)).ToList();
                if (onRoster && own.Item2 > own.Item1) windows.Add(own);
                return (windows.Min(w => w.Start), windows.Max(w => w.End));
            }
        }
        return own;
    }

    /// <summary>How long <paramref name="facilitatorId"/> is in this occurrence.</summary>
    public static TimeSpan Length(ResolvedSession session, Guid facilitatorId)
    {
        var (start, end) = Window(session, facilitatorId);
        var length = end.ToTimeSpan() - start.ToTimeSpan();
        return length > TimeSpan.Zero ? length : TimeSpan.Zero;
    }

    /// <summary>
    /// <b>Whether <paramref name="facilitatorId"/> is in the room for this occurrence at all.</b> On it,
    /// and not one of the people a needs-cover occurrence is waiting to replace. Before schema 23 a
    /// needs-cover occurrence had nobody in it by definition; with parts, the person in from 14:00 is
    /// still teaching while the hour before them waits for cover.
    /// </summary>
    public static bool IsIn(ResolvedSession session, Guid facilitatorId)
        => session.FacilitatorIds.Contains(facilitatorId)
           && !(session.NeedsCover && (session.AwayFacilitatorIds is not { } away || away.Contains(facilitatorId)))
           && Length(session, facilitatorId) > TimeSpan.Zero;

    /// <summary>Everybody in the room for this occurrence, once each. See <see cref="IsIn"/>.</summary>
    public static IEnumerable<Guid> InTheRoom(ResolvedSession session)
        => session.FacilitatorIds.Distinct().Where(id => IsIn(session, id));

    /// <summary>
    /// <b>Whether this person's time in two occurrences overlaps</b> — the facilitator clash, asked of
    /// their own hours rather than the two sessions' whole lengths.
    /// </summary>
    public static bool Overlap(ResolvedSession a, ResolvedSession b, Guid facilitatorId)
    {
        if (a.Day != b.Day) return false;
        var (aStart, aEnd) = Window(a, facilitatorId);
        var (bStart, bEnd) = Window(b, facilitatorId);
        return aStart < bEnd && bStart < aEnd;
    }

    /// <summary>
    /// <b>The needs-cover rule with parts in it:</b> an occurrence needs cover when there is a moment in
    /// it that somebody away was meant to be in and nobody who is there is in. With no parts that is
    /// exactly the old rule, "everybody on it is away"; with parts, the person in for 13:00–14:00 being
    /// away leaves that hour empty even though the one in from 14:00 is there.
    /// </summary>
    public static bool LeavesAGap(
        ScheduleEntry entry, TimeOnly start, TimeOnly end, IReadOnlyCollection<Guid> away)
    {
        if (away.Count == 0) return false;
        var present = entry.FacilitatorIds.Where(id => !away.Contains(id))
            .Select(id => Window(entry, id, start, end))
            .Where(w => w.End > w.Start)
            .ToList();

        foreach (var (from, until) in away.Select(id => Window(entry, id, start, end)).Where(w => w.End > w.Start))
        {
            // Walk the away person's hours forward, swallowing each present window that reaches the
            // point walked to; anything left over is a stretch nobody is in.
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

    /// <summary>
    /// <b>Moves every part by <paramref name="delta"/></b>, for a session dragged to a new time: the
    /// person in for the second hour is still in for the second hour.
    /// </summary>
    public static void Shift(ScheduleEntry entry, TimeSpan delta)
    {
        if (delta == TimeSpan.Zero) return;
        foreach (var part in entry.FacilitatorParts)
        {
            if (part.From is { } from) part.From = Clamp(from.ToTimeSpan() + delta, TimeOnly.MinValue, TimeOnly.MaxValue);
            if (part.Until is { } until) part.Until = Clamp(until.ToTimeSpan() + delta, TimeOnly.MinValue, TimeOnly.MaxValue);
        }
    }

    /// <summary>
    /// <b>Leaves only the parts that say something</b>: one per person (the first that says something),
    /// for somebody on the session,
    /// with a side that differs from the session's own hours and something left inside them. Run on
    /// load, on every save from the editor and after a drag, so a session never carries a part that
    /// cannot be read the way it looks.
    /// </summary>
    public static void Normalise(ScheduleEntry entry)
    {
        var kept = new List<FacilitatorPart>();
        foreach (var part in entry.FacilitatorParts)
        {
            if (part is null || !entry.FacilitatorIds.Contains(part.FacilitatorId)) continue;
            // The first part for a person THAT SAYS SOMETHING is theirs; one that says nothing
            // claims nobody.
            if (kept.Any(k => k.FacilitatorId == part.FacilitatorId)) continue;

            var from = part.From is { } f && f > entry.Start ? f : (TimeOnly?)null;
            var until = part.Until is { } u && u < entry.End ? u : (TimeOnly?)null;
            if (from is null && until is null) continue;                       // the whole session
            if ((from ?? entry.Start) >= (until ?? entry.End)) continue;       // nothing left inside it

            kept.Add(new FacilitatorPart { FacilitatorId = part.FacilitatorId, From = from, Until = until });
        }
        entry.FacilitatorParts = kept;
    }

    /// <summary>"from 14:00", "until 14:00" or "14:00–14:30", or null for the whole session.</summary>
    public static string? Describe(ScheduleEntry entry, Guid facilitatorId)
        => PartOf(entry, facilitatorId) switch
        {
            null => null,
            { From: { } f, Until: { } u } => $"{TimeSlots.Text(f)}–{TimeSlots.Text(u)}",
            { From: { } f } => $"from {TimeSlots.Text(f)}",
            { Until: { } u } => $"until {TimeSlots.Text(u)}",
            _ => null,
        };

    private static TimeOnly Clamp(TimeSpan value, TimeOnly low, TimeOnly high)
    {
        if (value <= low.ToTimeSpan()) return low;
        if (value >= high.ToTimeSpan()) return high;
        return TimeOnly.FromTimeSpan(value);
    }
}
