using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// How two classes are swapped: each to the other's day and time in its own space (<see cref="Times"/>), each to the
/// other's day, time and space (<see cref="Places"/>), or, while they are on at once, each to the other's space
/// (<see cref="Spaces"/>).
/// </summary>
public enum SwapKind { Times, Places, Spaces }

/// <summary>
/// One way of swapping two classes: both as they would be (<see cref="A"/> and <see cref="B"/>); everything the two
/// would then clash with or need, in the clash check's words (<see cref="After"/>), and of that what was against them
/// already (<see cref="Still"/>); what the swap brings to the week, the two or anybody else (<see cref="Brings"/>):
/// a facilitator left a long gap between two other classes is the swap's too; and what it puts right, as the clash
/// check says it now (<see cref="PutRight"/>).
/// </summary>
public sealed record Swap(SwapKind Kind, Session A, Session B, IReadOnlyList<Clash> After, IReadOnlyList<Clash> Brings,
    IReadOnlyList<Clash> PutRight, IReadOnlyList<Clash> Still);

/// <summary>The ways two classes can be swapped, and, in a sentence each, why another way cannot be or why none can.</summary>
public sealed record SwapsFound(IReadOnlyList<Swap> Ways, IReadOnlyList<string> Notes);

/// <summary>
/// Swapping two classes of one week (the rebuild's own). Each keeps its own length and people, each person's part
/// moving with it, and each way says, in the clash check's words, what it would bring the week and what it would put
/// right: a clash, cover needed, too much hard training for a group or a facilitator's long gap, about the two or
/// about the classes they leave behind; so the choice is made seeing it. Suggestions, never changes:
/// <see cref="Editing.Edits.Swap"/> takes one, as one change that one Undo takes back, while it is still the swap
/// offered (<see cref="AsOffered"/>). Two classes of different weeks, or one that has started or is cancelled, cannot
/// be swapped, and their times cannot be when either would run past midnight or on a day closed for it.
/// </summary>
public static class Swaps
{
    public static SwapsFound For(Timetable t, Guid aId, Guid bId, DateOnly today, TimeOnly now)
    {
        if (t.Sessions[aId] is not { } a || t.Sessions[bId] is not { } b) return None("One of those classes has gone.");
        if (a.Id == b.Id) return None("That is the class being swapped. Choose another class to swap it with.");
        if (Week.MondayOf(a.Date) != Week.MondayOf(b.Date)) return None("Only two classes of the same week can be swapped.");
        foreach (var s in new[] { a, b })
        {
            if (s.Cancelled) return None($"{s.DisplayTitle} is cancelled, so it is not swapped with anything.");
            if (!WaysOut.StillAhead(s, today, now))
                return None(s.Date < today || s.End <= now ? $"{s.DisplayTitle} is over, so it stays where it was." : $"{s.DisplayTitle} has started, so it stays where it is.");
        }

        var before = WeekAgainst(t, a.Date);
        var ways = new List<Swap>();
        var notes = new List<string>();
        bool times = a.Date != b.Date || a.Start != b.Start, spaces = a.RoomId != b.RoomId;
        if (times)
        {
            var x = Openings.Placed(a, b.Date, b.Start, a.RoomId);
            var y = Openings.Placed(b, a.Date, a.Start, b.RoomId);
            if ((CannotGo(t, x) ?? CannotGo(t, y)) is { } why) notes.Add("Their times cannot be swapped: " + why);
            else
            {
                ways.Add(Make(t, SwapKind.Times, x, y, before));
                if (spaces) ways.Add(Make(t, SwapKind.Places, x with { RoomId = b.RoomId }, y with { RoomId = a.RoomId }, before));
            }
        }
        // Each to the other's space at its own time is a swap only while both are on: otherwise it is two changes of space.
        if (spaces && a.Date == b.Date && a.Start < b.End && b.Start < a.End)
            ways.Add(Make(t, SwapKind.Spaces, a with { RoomId = b.RoomId }, b with { RoomId = a.RoomId }, before));
        if (ways.Count == 0 && notes.Count == 0) notes.Add("They are at the same time in the same space, so swapping them would change nothing.");
        return new SwapsFound(ways, notes);
    }

    /// <summary>The word for what is swapped: "times", "places" or "spaces".</summary>
    public static string Name(SwapKind kind) => kind switch
    {
        SwapKind.Times => "times",
        SwapKind.Places => "places",
        _ => "spaces",
    };

    /// <summary>
    /// Whether a swap worked out again (<paramref name="now"/>) is still the one <paramref name="offered"/>: both classes
    /// to the same places at the same lengths, and nothing brought that was not said. What it puts right, or leaves as
    /// it was, may have changed since without anybody walking into anything.
    /// </summary>
    public static bool AsOffered(Swap now, Swap offered) =>
        At(now.A) == At(offered.A) && At(now.B) == At(offered.B) && now.Brings.All(c => Among(c, offered.Brings));

    private static (Guid, DateOnly, TimeOnly, TimeOnly, Guid?) At(Session s) => (s.Id, s.Date, s.Start, s.End, s.RoomId);

    /// <summary>Where a swap takes the two, in a line: "Juggling to Thu 5 Mar 09:00, Strength to Wed 4 Mar 10:45".</summary>
    public static string Says(Timetable t, Swap w)
    {
        string When(Session s) => w.A.Date == w.B.Date ? Week.Hm(s.Start) : $"{Week.ShortDate(s.Date)} {Week.Hm(s.Start)}";
        string In(Session s, string before) => t.Rooms.Find(s.RoomId)?.Name is { } name ? before + name : "without a space";
        string To(Session s) => w.Kind switch
        {
            SwapKind.Times => "to " + When(s),
            SwapKind.Places => $"to {When(s)} " + In(s, "in "),
            _ => In(s, "to "),
        };
        return $"{w.A.DisplayTitle} {To(w.A)}, {w.B.DisplayTitle} {To(w.B)}";
    }

    private static SwapsFound None(string why) => new([], [why]);

    /// <summary>Why a class cannot be where a swap of times would put it: past midnight, or on a day closed for it.</summary>
    private static string? CannotGo(Timetable t, Session moved)
    {
        if (moved.End <= moved.Start) return $"{moved.DisplayTitle} would run past midnight.";
        if (Occurrences.ClosureFor(t, moved.Date, moved) is { } c)
            return $"{moved.DisplayTitle} would not run on {Week.ShortDate(moved.Date)} ({(c.Name.Trim() is { Length: > 0 } n ? n : "a closure")}).";
        return null;
    }

    private static Swap Make(Timetable t, SwapKind kind, Session x, Session y, IReadOnlyList<Clash> before)
    {
        // Only the two move, so whatever the week has then that it had not is the swap's, whoever it names.
        var week = WeekAgainst(t with { Sessions = t.Sessions.Put(x).Put(y) }, x.Date);
        var after = week.Where(c => c.SessionIds.Contains(x.Id) || c.SessionIds.Contains(y.Id)).ToList();
        return new Swap(kind, x, y, after, [.. week.Where(c => !Among(c, before))], [.. before.Where(c => !Among(c, week))],
            [.. after.Where(c => Among(c, before))]);
    }

    /// <summary>
    /// Whether the clash is one of these: the same kind between the same classes, wherever in the day it has moved
    /// to, or the same kind in the same words, as a group's day of hard training is whichever of its classes make it.
    /// </summary>
    private static bool Among(Clash c, IReadOnlyList<Clash> those) =>
        those.Any(o => o.Kind == c.Kind && (o.Description == c.Description || o.SessionIds.ToHashSet().SetEquals(c.SessionIds)));

    /// <summary>
    /// What the week has against its classes: rings, cover needed, a group's hard training and a facilitator's long
    /// gap. Not a facilitator's weekly hours, which a swap inside the week leaves as they were and which name only the
    /// first class of their week.
    /// </summary>
    private static List<Clash> WeekAgainst(Timetable t, DateOnly date) =>
        [.. Clashes.ForWeek(t, date).Where(c => c.Kind != ClashKind.FacilitatorOverHours)];
}
