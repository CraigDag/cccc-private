using System.Collections.Immutable;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// What is new to a facilitator about their own classes, told before their days come: "New for you" on Now, and on
/// the Cover page whether the person covering has seen it. A class is theirs when they teach or cover it, as it was
/// or as it is now, on a day from today to <see cref="LooksAhead"/> days after, and not over yet; so a class of
/// theirs moved away, given to somebody else or deleted is news to them, and so is one given to them. What changed
/// is read from the last <see cref="LooksBack"/> days of the timetable's own history, as <see cref="Recent"/> reads
/// it: only what somebody in the building would notice, and a change put back is no change.
/// <para>
/// A change is news to them until they say they have seen it (Got it), which is remembered as the last change to
/// that class they saw: a class changed again after that is news again, told from as they saw it. A change they
/// made themselves counts as seen, since they saw the class as they left it. What they have seen is kept outside
/// the timetable, as ids and places in the history only.
/// </para>
/// </summary>
public static class News
{
    /// <summary>How many days of changes are read.</summary>
    public const int LooksBack = 14;

    /// <summary>How many days after today a class can be on and be news: today and the next thirteen, two weeks.</summary>
    public const int LooksAhead = 13;

    /// <summary>The day the changes are read from, at its start.</summary>
    public static DateOnly ReadFrom(DateOnly today) => today.AddDays(-LooksBack);

    /// <summary>
    /// What is new to <paramref name="facilitatorId"/> in <paramref name="changes"/> at <paramref name="now"/> on
    /// <paramref name="today"/>, soonest first by the day and time the class is on now (or was on, when it has gone).
    /// <paramref name="theirLogins"/> are the sign-ins linked to them, whose changes are theirs; <paramref name="seen"/>
    /// is the last change to each class they have seen, by class.
    /// </summary>
    public static IReadOnlyList<ClassChange> For(Timetable t, IReadOnlyList<Change> changes, Guid facilitatorId, DateOnly today, TimeOnly now,
        IReadOnlyCollection<string> theirLogins, IReadOnlyDictionary<Guid, long> seen)
    {
        var to = today.AddDays(LooksAhead);
        bool Theirs(Session? s) => s is not null && IsTheirs(s, facilitatorId)
                                   && (s.Date > today && s.Date <= to || s.Date == today && s.End > now);
        return Recent.Gather(t, changes, UpTo(changes, theirLogins, seen))
            .Where(x => Theirs(x.Was) || Theirs(x.Now))
            .OrderBy(x => (x.Now ?? x.Was)!.Date).ThenBy(x => (x.Now ?? x.Was)!.Start).ThenBy(x => x.Id)
            .ToList();
    }

    /// <summary>Whether the class is theirs: they teach it or cover it.</summary>
    public static bool IsTheirs(Session s, Guid facilitatorId) => s.FacilitatorIds.Contains(facilitatorId) || s.CoverIds.Contains(facilitatorId);

    /// <summary>
    /// Whether <paramref name="facilitatorId"/> has seen the last change to each class of theirs that changed lately:
    /// false while it is news to them, true once they have said so or made it themselves. A class nothing changed
    /// lately is not in it: there is nothing to have seen.
    /// </summary>
    public static IReadOnlyDictionary<Guid, bool> Seen(Timetable t, IReadOnlyList<Change> changes, Guid facilitatorId, DateOnly today, TimeOnly now,
        IReadOnlyCollection<string> theirLogins, IReadOnlyDictionary<Guid, long> seen)
    {
        var answer = For(t, changes, facilitatorId, today, now, [], ImmutableDictionary<Guid, long>.Empty).ToDictionary(x => x.Id, _ => true);
        // Read after what they saw, a class can be news when everything read was no change at all: moved, seen, moved back.
        foreach (var x in For(t, changes, facilitatorId, today, now, theirLogins, seen)) answer[x.Id] = false;
        return answer;
    }

    /// <summary>
    /// What a facilitator has seen once they say so of <paramref name="items"/>: each class up to its last change.
    /// <paramref name="oldest"/> is the first change still read; anything seen before it decides nothing any more
    /// and is dropped, so what is kept stays small.
    /// </summary>
    public static ImmutableDictionary<Guid, long> GotIt(IReadOnlyDictionary<Guid, long> seen, IEnumerable<ClassChange> items, long oldest)
    {
        var kept = seen.Where(kv => kv.Value >= oldest).ToImmutableDictionary().ToBuilder();
        foreach (var x in items) kept[x.Id] = kept.TryGetValue(x.Id, out var was) ? Math.Max(was, x.Sequence) : x.Sequence;
        return kept.ToImmutable();
    }

    /// <summary>The last change to each class that is not news to them: the one they saw, or one they made, whichever is later.</summary>
    private static Dictionary<Guid, long> UpTo(IEnumerable<Change> changes, IReadOnlyCollection<string> theirLogins, IReadOnlyDictionary<Guid, long> seen)
    {
        var upTo = new Dictionary<Guid, long>(seen);
        foreach (var c in changes.Where(c => theirLogins.Contains(c.By, StringComparer.OrdinalIgnoreCase)))
            foreach (var op in c.Batch.Ops.Where(o => o.Collection == Collections.Sessions.Name))
                upTo[op.Id] = upTo.TryGetValue(op.Id, out var was) ? Math.Max(was, c.Sequence) : c.Sequence;
        return upTo;
    }
}

/// <summary>One facilitator's news about their next day, <see cref="Day"/>: what the evening email says. No student in it.</summary>
public sealed record EveningNote(Facilitator Facilitator, DateOnly Day, IReadOnlyList<ClassChange> Changes);

/// <summary>
/// Who is emailed the evening before, and about what: each facilitator's news (<see cref="News.For"/>) about their
/// classes on the next day, a class of theirs that is on it now or was on it, that they have not seen. So somebody
/// who never opens the app still hears, the night before, that their morning is not as they left it. Never a guest
/// (nobody's account), a former facilitator or somebody with no address.
/// </summary>
public static class EveningNotes
{
    public static IReadOnlyList<EveningNote> For(Timetable t, IReadOnlyList<Change> changes, DateOnly today, TimeOnly now,
        Func<Guid, IReadOnlyCollection<string>> loginsOf, Func<Guid, IReadOnlyDictionary<Guid, long>> seenBy)
    {
        var tomorrow = today.AddDays(1);
        var notes = new List<EveningNote>();
        foreach (var f in t.Facilitators.Current().Where(f => !f.IsGuest && !string.IsNullOrWhiteSpace(f.Email)))
        {
            bool OnTomorrow(Session? s) => s is not null && s.Date == tomorrow && News.IsTheirs(s, f.Id);
            var about = News.For(t, changes, f.Id, today, now, loginsOf(f.Id), seenBy(f.Id))
                .Where(x => OnTomorrow(x.Was) || OnTomorrow(x.Now)).ToList();
            if (about.Count > 0) notes.Add(new EveningNote(f, tomorrow, about));
        }
        return notes.OrderBy(n => n.Facilitator.Name, NameOrder.Comparer).ToList();
    }
}
