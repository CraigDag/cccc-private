using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>The classes one person being away leaves needing cover, together: what one substitute could take whole.</summary>
public sealed record AwayStretch(Guid AwayId, IReadOnlyList<Occurrence> Gaps);

/// <summary>
/// Somebody who could take an <see cref="AwayStretch"/>: the classes in it they are free for, those they would be
/// teaching something else at, the stretch's classes they have been in the room for before and how many times,
/// whether they offered to cover one of them, and their hours in the week of its first class.
/// </summary>
public sealed record StretchTaker(Facilitator Person, IReadOnlyList<Occurrence> Free, IReadOnlyList<Occurrence> Busy,
    IReadOnlyList<Guid> Taught, int Times, bool Offered, TimeSpan Week)
{
    /// <summary>How long the classes they are free for last together: what taking it would add to their week.</summary>
    public TimeSpan Taking => Free.Aggregate(TimeSpan.Zero, (sum, o) => sum + (o.End - o.Start));

    /// <summary>Their week and what they would take on come to more than their limit: said, never a reason to leave them out.</summary>
    public bool WouldBeOverLimit => Person.MaxWeeklyHours is { } max && Week + Taking > max;
}

/// <summary>
/// One substitute for everything one person being away leaves needing cover (the rebuild's own). Each class is
/// asked of <see cref="Cover.Ranked"/>, so somebody away then is never offered it and somebody teaching then is
/// busy for it, exactly as that class's own cover picks say; a class at the same time as one they would already be
/// taking from the stretch is busy for them too, so the count a person reads is what one press gives them. The
/// classes they are not free for still need cover, so nobody is put in two rooms at once. Ranked: the school's own
/// before guests, free for more of it first, then whoever offered to cover one of them (they said they can), then
/// whoever has been in the room for its classes most often (they know them), then the lightest week, then by name.
/// Being busy for some of it, or going over a limit, is said, never a reason to leave somebody out.
/// </summary>
public static class CoverAll
{
    /// <summary>
    /// Everybody away for more than one class needing cover among <paramref name="occurrences"/> that has not
    /// started by <paramref name="now"/> on <paramref name="today"/>, by name, each with those classes in order.
    /// One class alone is its own cover card's; one that has started is for recording who stood in, one at a time.
    /// </summary>
    public static IReadOnlyList<AwayStretch> Stretches(Timetable t, IReadOnlyList<Occurrence> occurrences, DateOnly today, TimeOnly now) =>
        occurrences.Where(o => o.NeedsCover && (o.Date > today || (o.Date == today && o.Start > now)))
            .SelectMany(o => o.AwayIds.Select(id => (Id: id, O: o)))
            .GroupBy(x => x.Id)
            .Where(g => g.Count() > 1)
            .Select(g => new AwayStretch(g.Key, g.Select(x => x.O).OrderBy(o => o.Date).ThenBy(o => o.Start).ThenBy(o => o.Session.Id).ToList()))
            .OrderBy(s => t.Facilitators[s.AwayId]?.Name ?? "", NameOrder.Comparer)
            .ToList();

    /// <summary>Who could take the stretch, free for at least one of its classes, best first; what they taught counts from before <paramref name="today"/>.</summary>
    public static IReadOnlyList<StretchTaker> Ranked(Timetable t, AwayStretch s, DateOnly today)
    {
        var people = new Dictionary<Guid, Taker>();
        foreach (var gap in s.Gaps)
            foreach (var c in Cover.Ranked(t, gap))
            {
                if (!people.TryGetValue(c.Person.Id, out var p)) people[c.Person.Id] = p = new Taker(c.Person, c.Week.Total);
                bool clashes = p.Free.Any(o => o.Date == gap.Date && o.Start < gap.End && gap.Start < o.End);
                (c.IsFree && !clashes ? p.Free : p.Busy).Add(gap);
                p.Offered |= c.Offered;
            }
        // Who has been in the room for the stretch's classes before, and how often: somebody who knows the class.
        var classes = s.Gaps.Select(o => o.Session.ClassId).OfType<Guid>().ToHashSet();
        var before = t.Sessions.Where(x => x.Date < today && x.ClassId is { } c && classes.Contains(c))
            .Select(x => Occurrences.Resolve(t, x))
            .SelectMany(o => o.InTheRoom.Select(id => (Id: id, Class: o.Session.ClassId!.Value)))
            .ToLookup(x => x.Id);
        return people.Values.Where(p => p.Free.Count > 0)
            .Select(p => new StretchTaker(p.Person, p.Free, p.Busy, [.. before[p.Person.Id].Select(x => x.Class).Distinct()],
                before[p.Person.Id].Count(), p.Offered, p.Week))
            .OrderBy(x => x.Person.IsGuest ? 1 : 0)
            .ThenByDescending(x => x.Free.Count)
            .ThenBy(x => x.Offered ? 0 : 1)
            .ThenByDescending(x => x.Times)
            .ThenBy(x => x.Week)
            .ThenBy(x => x.Person.Name, NameOrder.Comparer)
            .ToList();
    }

    private sealed class Taker(Facilitator person, TimeSpan week)
    {
        public Facilitator Person { get; } = person;
        public TimeSpan Week { get; } = week;
        public List<Occurrence> Free { get; } = [];
        public List<Occurrence> Busy { get; } = [];
        public bool Offered { get; set; }
    }
}
