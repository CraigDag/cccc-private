using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

public enum WeekChangeKind { Added, Gone, Changed }

/// <summary>
/// One difference between a week and the week it is compared with: a class that is new, one that has gone,
/// or the same class on another day, at other times, in another space, taught by other people (or for other
/// parts of it), for other groups, or under another name. <see cref="Was"/> is in the earlier week,
/// <see cref="Now"/> in the later one.
/// </summary>
public sealed record WeekChange(Session? Was, Session? Now)
{
    public WeekChangeKind Kind => Was is null ? WeekChangeKind.Added : Now is null ? WeekChangeKind.Gone : WeekChangeKind.Changed;

    /// <summary>The class as it is in the later week, or as it was when it has gone.</summary>
    public Session Either => Now ?? Was!;

    private bool Both => Was is not null && Now is not null;
    public bool DayChanged => Both && Was!.Date.DayOfWeek != Now!.Date.DayOfWeek;
    public bool TimesChanged => Both && (Was!.Start != Now!.Start || Was.End != Now.End);
    public bool SpaceChanged => Both && Was!.RoomId != Now!.RoomId;
    public bool TeachersChanged => Both && !Recent.SameSet(Was!.FacilitatorIds, Now!.FacilitatorIds);
    /// <summary>The same people teach, but somebody's part of the class is a different stretch of it.</summary>
    public bool PartsChanged => Both && !TeachersChanged && !Recent.SameSet(Was!.Facilitators, Now!.Facilitators);
    public bool AudienceChanged => Both && !Was!.Audience.Equivalent(Now!.Audience);
    public bool TitleChanged => Both && Was!.DisplayTitle != Now!.DisplayTitle;
}

/// <summary>
/// What is different about a week from the one before it. Every week has its own timetable, usually copied
/// from the week before, so nothing links a class to its copy: the same class is found again by what it
/// is (its class, or its name when it has none), on the same day for the same groups first, then on the same
/// day, then for the same groups on any day, each time the nearest in time. What is left over has gone, or
/// is new. A week is compared as it is written: a class cancelled, closed or covered that week is the week
/// happening, not its timetable changing, and so are registers, notes and a class's roster exceptions.
/// </summary>
public static class WeekChanges
{
    /// <summary>How far back a week looks for the one it is compared with: half a year.</summary>
    public const int LookBackWeeks = 26;

    /// <summary>The weeks that have any class in them, by their Mondays.</summary>
    public static IReadOnlySet<DateOnly> BuiltWeeks(Timetable t) => t.Sessions.Select(s => s.WeekMonday).ToHashSet();

    /// <summary>
    /// The week this one is compared with: the latest one before it with any class in it, within half a
    /// year, so a week after a blank or closed one is compared with the last week the school had.
    /// </summary>
    public static DateOnly? Previous(Timetable t, DateOnly anyDateInWeek) => Previous(BuiltWeeks(t), anyDateInWeek);

    public static DateOnly? Previous(IReadOnlySet<DateOnly> built, DateOnly anyDateInWeek)
    {
        var monday = Week.MondayOf(anyDateInWeek);
        for (int i = 1; i <= LookBackWeeks; i++)
            if (built.Contains(monday.AddDays(-7 * i))) return monday.AddDays(-7 * i);
        return null;
    }

    /// <summary>Every difference from the earlier week to the later one, in the order of the later week's days.</summary>
    public static IReadOnlyList<WeekChange> Between(Timetable t, DateOnly earlier, DateOnly later)
    {
        var was = t.SessionsInWeek(earlier).OrderBy(s => s.Date).ThenBy(s => s.Start).ThenBy(s => s.Id).ToList();
        var now = t.SessionsInWeek(later).OrderBy(s => s.Date).ThenBy(s => s.Start).ThenBy(s => s.Id).ToList();
        var changes = new List<WeekChange>();
        foreach (var key in was.Select(Key).Concat(now.Select(Key)).Distinct())
        {
            var a = was.Where(s => Key(s) == key).ToList();
            var b = now.Where(s => Key(s) == key).ToList();
            // The same class, unchanged, first: a week copied from the one before is no change at all.
            Pair(a, b, Same, null);
            Pair(a, b, (x, y) => x.Date.DayOfWeek == y.Date.DayOfWeek && x.Audience.Equivalent(y.Audience), changes);
            Pair(a, b, (x, y) => x.Date.DayOfWeek == y.Date.DayOfWeek, changes);
            Pair(a, b, (x, y) => x.Audience.Equivalent(y.Audience), changes);
            changes.AddRange(a.Select(s => new WeekChange(s, null)));
            changes.AddRange(b.Select(s => new WeekChange(null, s)));
        }
        return changes
            .OrderBy(c => Week.DayIndex(c.Either.Date.DayOfWeek))
            .ThenBy(c => c.Either.Start)
            .ThenBy(c => c.Either.DisplayTitle, NameOrder.Comparer)
            .ThenBy(c => c.Kind)
            .ThenBy(c => c.Either.Id)
            .ToList();
    }

    /// <summary>What a class is, across weeks: its class, or its name when it has none.</summary>
    private static string Key(Session s) => s.ClassId is { } id ? "class:" + id : "title:" + s.DisplayTitle;

    /// <summary>The same in everything a person in the building would notice about its place in the week.</summary>
    private static bool Same(Session x, Session y) =>
        x.Date.DayOfWeek == y.Date.DayOfWeek && x.Start == y.Start && x.End == y.End && x.RoomId == y.RoomId
        && x.DisplayTitle == y.DisplayTitle && x.Audience.Equivalent(y.Audience) && Recent.SameSet(x.Facilitators, y.Facilitators);

    /// <summary>
    /// Pairs what is left of the two weeks that the test allows, each earlier class with the nearest later
    /// one in time, and takes both out; each pair is a change, when changes are wanted. The first pass takes
    /// every pair that is the same, so no pair found after it is.
    /// </summary>
    private static void Pair(List<Session> a, List<Session> b, Func<Session, Session, bool> test, List<WeekChange>? changes)
    {
        foreach (var x in a.ToList())
        {
            var y = b.Where(y => test(x, y))
                .OrderBy(y => Math.Abs((y.Start.ToTimeSpan() - x.Start.ToTimeSpan()).TotalMinutes))
                .ThenBy(y => Math.Abs(Week.DayIndex(y.Date.DayOfWeek) - Week.DayIndex(x.Date.DayOfWeek)))
                .FirstOrDefault();
            if (y is null) continue;
            a.Remove(x);
            b.Remove(y);
            changes?.Add(new WeekChange(x, y));
        }
    }
}
