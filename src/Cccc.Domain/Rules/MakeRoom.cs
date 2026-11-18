using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// Room made for a class: the class where it would go (<see cref="Placed"/>) with what is worth knowing about it
/// there, and the one other class that was in its way, moved to one of that class's ways out (<see cref="Other"/>).
/// </summary>
public sealed record RoomMade(Session Placed, IReadOnlyList<OpeningNote> Notes, WayOut Other);

/// <summary>
/// Making room, for when Find a time finds no time at all: the times when only ONE other class is in a class's way
/// (its people, its groups, its students, or the space, all kept by that one class), each with that class moved to
/// one of its own ways out (<see cref="WaysOut"/>), so that afterwards neither is in any clash. One other class and
/// never two: one move is a change to look over, a chain of them is a reshuffle nobody asked for. Never round
/// somebody away or an event, which are not the timetable's to move, and never a class that has started. The
/// soonest first, as Find a time lists, each other class offered once: at the first time it alone is in the way,
/// or, when it has no way out then, at the last. Those two are enough. A way out that moves it later only needs the
/// class to start as early as it can, and one that moves it earlier only needs the class to start as late as it
/// can; anything else about where it goes is the same at every time in between.
/// Suggestions, never changes: <see cref="Editing.Edits.MakeRoom"/> makes one, both moves in one step that one Undo
/// takes back.
/// </summary>
public static class MakeRoom
{
    /// <summary>The most offered: a choice to make at a glance, as with ways out.</summary>
    public const int Most = 3;

    /// <summary>The most other classes looked at for a way out: each is a search of its own day, or two.</summary>
    public const int MostTried = 12;

    public static IReadOnlyList<RoomMade> For(Timetable t, OpeningAsk ask, DateOnly today, TimeOnly now, EventViewer viewer)
    {
        var c = ask.Class;
        int length = (int)c.Length.TotalMinutes;
        var to = ask.To > ask.From.AddDays(Openings.MostDays) ? ask.From.AddDays(Openings.MostDays) : ask.To;
        if (length <= 0 || to < ask.From) return [];
        Guid? self = t.Sessions.Contains(c.Id) ? c.Id : null;
        var named = Openings.Named(t, c);
        var spaces = ask.Spaces.Distinct().Where(t.Rooms.Contains).ToList();
        bool needsSpace = ask.Spaces.Count > 0;
        int dayStart = Openings.Up(Openings.Minutes(ask.DayStart)), dayEnd = Openings.Minutes(ask.DayEnd);

        var made = new List<RoomMade>();
        var tried = new HashSet<Guid>();
        bool Done() => made.Count >= Most || tried.Count >= MostTried;
        for (var day = ask.From < today ? today : ask.From; day <= to && !Done(); day = day.AddDays(1))
        {
            if (!Openings.LooksIn(t, day, named)) continue;
            var occurrences = Occurrences.Between(t, day, day);
            var events = EventSchedule.On(t, day);
            // Who the class needs that is elsewhere that day, as Find a time asks it; its spaces are asked below.
            var busy = Busy.For(t, c, day, viewer, occurrences, events).Where(b => b.Kind != BusyKind.Space).ToList();
            int first = day == today ? Math.Max(dayStart, Openings.Up(Openings.Minutes(now) + 1)) : dayStart;
            // Each other class alone in the way that day, in the order first met: the first time it is, and the last.
            var alone = new List<(Guid Other, TimeOnly First, Guid? FirstSpace, TimeOnly Last, Guid? LastSpace)>();
            void Alone(Guid other, TimeOnly start, Guid? space)
            {
                int i = alone.FindIndex(a => a.Other == other);
                if (i < 0) alone.Add((other, start, space, start, space));
                else alone[i] = alone[i] with { Last = start, LastSpace = space };
            }
            for (int m = first; m + length <= dayEnd; m += Openings.StepMinutes)
            {
                // Where it already is is not another time.
                if (self is not null && day == c.Date && m == Openings.Minutes(c.Start)) continue;
                var start = Openings.At(m);
                var end = Openings.At(m + length);
                if (!OneInTheWay(busy, Openings.Placed(c, day, start, null), start, end, out var only)) continue;
                if (!needsSpace)
                {
                    if (only is { } x) Alone(x, start, c.RoomId);
                    continue;
                }
                var used = Spaces.Uses(occurrences, self).Where(u => u.O.Start < end && start < u.O.End).ToList();
                var booked = Spaces.Booked(t, events, start, end);
                foreach (var r in spaces)
                {
                    if (booked.ContainsKey(r)) continue;
                    // The one other class: the one keeping its people, groups or students, the one in the space, or both.
                    var inTheWay = used.Where(u => u.Room == r).Select(u => u.O.Session.Id).ToHashSet();
                    if (only is { } y) inTheWay.Add(y);
                    if (inTheWay.Count == 1) Alone(inTheWay.Single(), start, r);
                }
            }

            var found = new List<RoomMade>();
            foreach (var a in alone)
            {
                // What is found already that day is the soonest, once there is enough of it no later than this one.
                if (made.Count + found.Count(f => f.Placed.Start <= a.First) >= Most || tried.Count >= MostTried) break;
                if (!tried.Add(a.Other)) continue;
                if ((Try(a.Other, a.First, a.FirstSpace) ?? (a.Last != a.First || a.LastSpace != a.FirstSpace ? Try(a.Other, a.Last, a.LastSpace) : null)) is { } room)
                    found.Add(room);
            }
            made.AddRange(found.OrderBy(f => f.Placed.Start).Take(Most - made.Count));

            RoomMade? Try(Guid otherId, TimeOnly start, Guid? space)
            {
                var placed = Openings.Placed(c, day, start, space);
                var with = Put(t, placed);
                // A class that has started has no ways out, so it is looked at and goes no further.
                foreach (var w in WaysOut.For(with, otherId, today, now, viewer))
                {
                    // Its way out leaves the other class in no clash; the class has to be in none either once it has gone.
                    var after = Put(with, w.Moved);
                    if (WaysOut.InClash(after, placed).Count == 0) return new RoomMade(placed, Openings.NotesAt(after, placed, viewer), w);
                }
                return null;
            }
        }
        return made;
    }

    /// <summary>
    /// Whether at most one other class keeps the class's people, groups or students from a time, and which
    /// (<paramref name="only"/>, none when nothing does). Not when two classes do, or when anything that is not a
    /// class does (somebody away, an event), since no move of a class can end that.
    /// </summary>
    private static bool OneInTheWay(IReadOnlyList<BusyStretch> busy, Session placed, TimeOnly start, TimeOnly end, out Guid? only)
    {
        only = null;
        foreach (var b in busy)
        {
            // One person's stretch keeps the class only from the part of it that person is in.
            var (from, until) = b.PersonId is { } id ? TimeWindows.Assigned(placed, id) : (start, end);
            if (until <= from || !b.Overlaps(from, until)) continue;
            if (b.Class is not { } o || (only is { } seen && seen != o.Session.Id)) return false;
            only = o.Session.Id;
        }
        return true;
    }

    /// <summary>
    /// The way out that makes room for a class placed where it is: the other class's move, while it is still one of
    /// that class's ways out with the class there. None once something has changed so that it no longer is, by
    /// somebody else meanwhile or in the class itself.
    /// </summary>
    public static WayOut? Still(Timetable t, Session placed, Guid otherId, DateOnly date, TimeOnly start, Guid? roomId,
        DateOnly today, TimeOnly now, EventViewer viewer) =>
        WaysOut.For(Put(t, placed), otherId, today, now, viewer).FirstOrDefault(w => w.Moved.Date == date && w.Moved.Start == start && w.Moved.RoomId == roomId);

    /// <summary>
    /// Whether the other class is in the way of one placed where it is: in a clash with it (<see cref="Clash.IsRing"/>).
    /// A group's day grown too hard beside it, a limit or a long gap names it too, and none of those is in the way.
    /// </summary>
    public static bool InTheWay(Timetable t, Session placed, Guid otherId) =>
        Clashes.CausedBy(t, placed).Any(c => c.IsRing && c.SessionIds.Contains(otherId));

    private static Timetable Put(Timetable t, Session s) => t with { Sessions = t.Sessions.Put(s) };
}
