using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// Next week as whoever plans the timetable needs it before it starts: the week at a glance (<see cref="TermWeek"/>),
/// with the clashes and the classes needing cover that it counts, in the order the plan lists them.
/// </summary>
public sealed record WeekAheadView(TermWeek Week, IReadOnlyList<Clash> Clashes, IReadOnlyList<Occurrence> Cover)
{
    /// <summary>
    /// Nothing in it to put right: built, nothing ringed and nobody to cover, which is what the plan's issues are. Days
    /// closed and what differs from the week before are worth knowing all the same. A group short of the week's hours is
    /// not part of it: the school's target is one for every group, so a group taught fewer hours on purpose would be said
    /// every week, and a line said every week stops being read (Reports ▸ Student hours has it).
    /// </summary>
    public bool Ready => !Week.Blank && Clashes.Count == 0 && Cover.Count == 0;
}

/// <summary>
/// The week ahead, from Thursday on: next week, counted as the term at a glance counts every week
/// (<see cref="TermGlance"/>), said while there is still time to put it right, since a week ahead is blank until
/// somebody fills it. Not a week with no school in it: one the building is closed every school day, or one outside
/// every semester with nothing written in it. Advice, never a refusal.
/// </summary>
public static class WeekAhead
{
    /// <summary>The first day next week is looked at from: Thursday, and Friday and the weekend after it.</summary>
    public const DayOfWeek From = DayOfWeek.Thursday;

    /// <summary>Next week's Monday, from Thursday on; none earlier in the week.</summary>
    public static DateOnly? Monday(DateOnly today) =>
        Week.DayIndex(today.DayOfWeek) >= Week.DayIndex(From) ? Week.MondayOf(today).AddDays(7) : null;

    public static WeekAheadView? For(Timetable t, DateOnly today, EventViewer viewer, RegisterReader reader)
    {
        if (Monday(today) is not { } next) return null;
        var week = TermGlance.For(t, [next], today, viewer, reader)[0];
        if (week.ClosedAllWeek is not null) return null;
        // A semester is whole weeks; with none at all, every week is one of school, as the week's hours target counts it
        // (Hours.WeekTarget).
        bool school = t.Semesters.Count == 0 || t.SemesterFor(next) is not null;
        if (week.Blank && !school) return null;
        return new WeekAheadView(week, Clashes.ForWeek(t, next).Where(c => c.IsRing).ToList(), Cover.StillNeeded(t, next, today));
    }
}
