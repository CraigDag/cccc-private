using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>One person who could take a class needing cover, with what the ranking knew about them.</summary>
public sealed record CoverCandidate(Facilitator Person, Occurrence? TeachingThen, HoursSplit Week)
{
    public bool IsFree => TeachingThen is null;
    public bool IsOverLimit => Person.MaxWeeklyHours is { } max && Week.Total > max;
    public string StateText => TeachingThen is { } o ? $"Teaching {o.Session.DisplayTitle} then" : "Free";
}

/// <summary>
/// Assign cover OFFERS everybody who is not away and ranks rather than filters (1.56.0): non-guests
/// first, free before busy, lightest week first, then by name. Busy or over a limit is advice.
/// </summary>
public static class Cover
{
    public static IReadOnlyList<Occurrence> NeededInWeek(Timetable t, DateOnly anyDateInWeek) =>
        Occurrences.ForWeek(t, anyDateInWeek).Where(o => o.NeedsCover).ToList();

    /// <summary>Cover still needed from today onwards — a week that has passed needs no cover.</summary>
    public static IReadOnlyList<Occurrence> StillNeeded(Timetable t, DateOnly anyDateInWeek, DateOnly today) =>
        NeededInWeek(t, anyDateInWeek).Where(o => o.Date >= today).ToList();

    public static IReadOnlyList<CoverCandidate> Ranked(Timetable t, Occurrence gap)
    {
        var splits = Hours.ByFacilitator(t, Occurrences.HappeningInWeek(t, gap.Date));
        var teaching = Teaching.At(t, gap.Date, gap.Start, gap.End, except: gap.Session.Id);
        var assigned = gap.Session.FacilitatorIds.ToHashSet();
        return t.Facilitators.Current()
            .Where(f => !Occurrences.IsAway(t, f.Id, gap.Date, gap.Start, gap.End))
            .Where(f => !assigned.Contains(f.Id) || !gap.AwayIds.Contains(f.Id))
            .Select(f => new CoverCandidate(f, teaching.GetValueOrDefault(f.Id), splits.GetValueOrDefault(f.Id) ?? HoursSplit.None))
            .OrderBy(c => c.Person.IsGuest ? 1 : 0)
            .ThenBy(c => c.IsFree ? 0 : 1)
            .ThenBy(c => c.Week.Total)
            .ThenBy(c => c.Person.Name, NameOrder.Comparer)
            .ToList();
    }
}
