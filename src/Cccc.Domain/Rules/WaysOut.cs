using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// One way out of the clashes a class is in: the class as it would be (<see cref="Moved"/>), at the same time in
/// another space or at another time the same day, with what is worth knowing about it there.
/// </summary>
public sealed record WayOut(Session Moved, bool SameTime, IReadOnlyList<OpeningNote> Notes);

/// <summary>
/// The ways out of a clash for one class, where a clash is a space, a person, a group or students in two places at
/// once, or somebody outside their hours (<see cref="Clash.IsRing"/>). First the same time in another space, when
/// its space is all that clashes, so nobody's day moves; then another time the same day in its own space, nearest
/// the time it has; and only when there is neither, another time in a space like it, since moving both is the
/// bigger change. A space is like it when its class has been in it before (the most used first) or it is of the
/// same kind. Every way out is put to the clash check itself, so taking one never trades this clash for another;
/// what the check only advises about, a limit or a long gap, is said beside it. Suggestions, never changes:
/// <see cref="Editing.Edits.TakeWayOut"/> makes one, and Undo takes it back. A class that has started, is
/// cancelled, or is in a week that has ended has none. Moving the OTHER class is that class's own way out,
/// offered when it is chosen; another day is Find a time's.
/// </summary>
public static class WaysOut
{
    /// <summary>The most offered: a choice to make at a glance, not a list to read.</summary>
    public const int Most = 3;

    /// <summary>Whether a class is one to look for ways out for: on a real week, and not started yet.</summary>
    public static bool StillAhead(Session s, DateOnly today, TimeOnly now) =>
        !Week.IsScratch(s.Date) && (s.Date > today || (s.Date == today && s.Start > now));

    public static IReadOnlyList<WayOut> For(Timetable t, Guid sessionId, DateOnly today, TimeOnly now, EventViewer viewer)
    {
        if (t.Sessions[sessionId] is not { } s || !StillAhead(s, today, now)) return [];
        var day = Occurrences.Between(t, s.Date, s.Date);
        var clashes = InClash(t, s, day);
        if (clashes.Count == 0) return [];
        var like = SpacesLike(t, s);
        var ways = new List<WayOut>();

        // Only a clash in its space can end in another space (in use, or without what the class needs); the
        // clash check would refuse any other the same.
        if (s.RoomId is { } own && clashes.All(c => c.Kind is ClashKind.RoomDoubleBooked or ClashKind.SpaceLacksEquipment))
        {
            var used = Spaces.InUse(day, s.Start, s.End, s.Id);
            var booked = Spaces.Booked(t, s.Date, s.Start, s.End);
            foreach (var r in like.Where(r => r != own && !used.ContainsKey(r) && !booked.ContainsKey(r)))
            {
                var moved = s with { RoomId = r };
                if (InClash(Put(t, moved), moved, day).Count > 0) continue;
                ways.Add(new WayOut(moved, true, [SpaceNote(t, s, r)]));
                // Leave room for another time: a different space is not always the better answer.
                if (ways.Count == Most - 1) break;
            }
        }

        AnotherTime(t, s, s.RoomId is { } r0 ? [r0] : [], day, today, now, viewer, ways);
        if (ways.Count == 0 && like.Count > 1) AnotherTime(t, s, like, day, today, now, viewer, ways);
        return ways;
    }

    /// <summary>
    /// The clashes a class is in that ring its block. Each is two classes at once, or one outside somebody's
    /// hours, so its own day is all there is to look at, and only the classes it overlaps.
    /// </summary>
    public static IReadOnlyList<Clash> InClash(Timetable t, Session s) => InClash(t, s, Occurrences.Between(t, s.Date, s.Date));

    private static IReadOnlyList<Clash> InClash(Timetable t, Session s, IReadOnlyList<Occurrence> day)
    {
        var near = day.Where(o => o.Session.Id != s.Id && o.Date == s.Date && o.Start < s.End && s.Start < o.End)
            .Append(Occurrences.Resolve(t, s)).ToList();
        return Clashes.Find(t, near).Where(c => c.IsRing && c.SessionIds.Contains(s.Id)).ToList();
    }

    /// <summary>
    /// The times Find a time finds that day in these spaces, nearest the time the class has first: from each
    /// stretch it finds, the time in it nearest that one that the clash check has nothing against, so each is a
    /// different gap in the day (a stretch never holds the time itself, which is not another).
    /// </summary>
    private static void AnotherTime(Timetable t, Session s, IReadOnlyList<Guid> spaces, IReadOnlyList<Occurrence> day,
        DateOnly today, TimeOnly now, EventViewer viewer, List<WayOut> ways)
    {
        if (ways.Count >= Most) return;
        // Only where it may go: a space without what the class needs is no way out, however free it is.
        if (spaces.Count > 0 && (spaces = [.. spaces.Where(r => Spaces.Suits(t, s.ClassId, r))]).Count == 0) return;
        int at = Minutes(s.Start);
        var ask = new OpeningAsk
        {
            Class = s, Spaces = spaces, From = s.Date, To = s.Date,
            // An early or a late class is looked for around its own time too.
            DayStart = s.Start < new TimeOnly(8, 0) ? s.Start : new TimeOnly(8, 0),
            DayEnd = s.End > new TimeOnly(18, 0) ? s.End : new TimeOnly(18, 0),
        };
        var picks = new List<(int Minute, Session Moved)>();
        foreach (var o in Openings.Find(t, ask, today, now, viewer).Openings.Where(o => o.Date == s.Date))
        {
            var times = Enumerable.Range(0, (Minutes(o.Latest) - Minutes(o.Start)) / Openings.StepMinutes + 1)
                .Select(i => Minutes(o.Start) + i * Openings.StepMinutes)
                .OrderBy(m => Math.Abs(m - at)).ThenBy(m => m);
            foreach (var m in times)
            {
                var start = new TimeOnly(m / 60, m % 60);
                if (!TryFree(t, s, spaces, start, day, out var space)) continue;
                var moved = Openings.Placed(s, s.Date, start, space);
                if (InClash(Put(t, moved), moved, day).Count > 0) continue;
                picks.Add((m, moved));
                break;
            }
        }
        if (picks.Count == 0) return;
        // What is against it where it is already is no reason against another time: a limit it is over wherever
        // it goes, an event all that day, somebody with nothing else that day.
        var already = Openings.NotesAt(t, s, viewer).Where(n => !n.Good).Select(n => n.Text).ToHashSet();
        foreach (var (_, moved) in picks.OrderBy(p => Math.Abs(p.Minute - at)).ThenBy(p => p.Minute))
        {
            if (ways.Count >= Most) return;
            ways.Add(new WayOut(moved, false, Openings.NotesAt(t, moved, viewer).Where(n => n.Good || !already.Contains(n.Text)).ToList()));
        }
    }

    /// <summary>
    /// Its own space, then each space its class has been in (the most used first), then the rest of the same kind,
    /// and for a class that needs equipment every space that has it: where it could go, in the order to offer them.
    /// A space with no kind is like no other. None when it has no space, so none is needed; a former space is
    /// offered to nothing ahead.
    /// </summary>
    private static IReadOnlyList<Guid> SpacesLike(Timetable t, Session s)
    {
        if (s.RoomId is not { } own || t.Rooms[own] is not { } room) return [];
        var used = Uses(t, s);
        bool needs = Spaces.Needed(t, s.ClassId).Count > 0;
        var like = t.Rooms.Current()
            .Where(r => r.Id != own && (used.ContainsKey(r.Id) || (room.Category is { Length: > 0 } kind && r.Category == kind)
                                        || (needs && Spaces.Suits(t, s.ClassId, r.Id))))
            .OrderByDescending(r => used.GetValueOrDefault(r.Id))
            .ThenBy(r => r.Name, NameOrder.Comparer)
            .Select(r => r.Id);
        return [own, .. like];
    }

    /// <summary>How often its class has been in each space, over the whole timetable.</summary>
    private static Dictionary<Guid, int> Uses(Timetable t, Session s) =>
        s.ClassId is { } c
            ? t.Sessions.Where(x => x.ClassId == c && x.Id != s.Id && x.RoomId is not null)
                .GroupBy(x => x.RoomId!.Value).ToDictionary(g => g.Key, g => g.Count())
            : [];

    private static OpeningNote SpaceNote(Timetable t, Session s, Guid r) =>
        Uses(t, s).ContainsKey(r)
            ? new($"{s.DisplayTitle} has been here before", true)
            : new($"{s.DisplayTitle} has not been here before", false);

    /// <summary>The first of its spaces free for the whole of it from <paramref name="start"/>; none needed when it has none.</summary>
    private static bool TryFree(Timetable t, Session s, IReadOnlyList<Guid> spaces, TimeOnly start, IReadOnlyList<Occurrence> day, out Guid? space)
    {
        space = null;
        if (spaces.Count == 0) return true;
        var end = start.Add(s.Length);
        var used = Spaces.InUse(day, start, end, s.Id);
        var booked = Spaces.Booked(t, s.Date, start, end);
        foreach (var r in spaces)
            if (!used.ContainsKey(r) && !booked.ContainsKey(r)) { space = r; return true; }
        return false;
    }

    private static Timetable Put(Timetable t, Session moved) => t with { Sessions = t.Sessions.Put(moved) };

    private static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;
}
