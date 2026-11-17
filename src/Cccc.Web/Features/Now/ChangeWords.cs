using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;
using DomainWeek = Cccc.Domain.Week;

namespace Cccc.Web.Features.Now;

/// <summary>
/// What a person reads about a class that changed lately. It sits beside the class as it is now (or as
/// it was, if it has gone), so it says only what is different, the most telling first, on one line.
/// </summary>
public static class ChangeWords
{
    public static string What(Timetable t, ClassChange c, DateOnly day)
    {
        if (c.Now is not { } now) return "Deleted";
        if (c.Was is not { } was) return "New";
        var w = new List<string>();
        if (c.CancelledChanged) w.Add(now.Cancelled ? "cancelled" : "back on");
        if (c.DayChanged) w.Add(now.Date == day ? "moved here from " + DomainWeek.ShortDate(was.Date) : "moved to " + DomainWeek.ShortDate(now.Date));
        if (c.TimesChanged) w.Add("was " + Looks.When(was));
        if (c.SpaceChanged) w.Add(now.RoomId is null ? "no space now, was in " + Space(t, was) : was.RoomId is null ? "a space given" : "was in " + Space(t, was));
        if (c.TeachersChanged) w.Add(Teachers(t, now.FacilitatorIds.Except(was.FacilitatorIds), was.FacilitatorIds.Except(now.FacilitatorIds)));
        else if (c.PartsChanged) w.Add("a different part for " + Looks.Names(t, now.Facilitators.Where(a => !was.Facilitators.Contains(a)).Select(a => a.FacilitatorId)));
        if (c.CoverChanged) w.Add(now.CoverIds.Count == 0 ? "cover taken off" : Looks.Names(t, now.CoverIds) + " covering");
        if (c.AudienceChanged) w.Add("now for " + Looks.Audience(t, now) + ", was " + Looks.Audience(t, was));
        if (c.TitleChanged) w.Add("was called " + was.DisplayTitle);
        var line = string.Join(" · ", w);
        return line.Length == 0 ? "Changed" : char.ToUpperInvariant(line[0]) + line[1..];
    }

    /// <summary>
    /// What a person reads about a class that is different from the week before, beside the class as it is
    /// this week (or as it was, if it is not on this week): only what is different, the most telling first.
    /// </summary>
    public static string FromTheWeekBefore(Timetable t, WeekChange c)
    {
        if (c.Now is not { } now) return "Not on this week";
        if (c.Was is not { } was) return "New this week";
        var w = new List<string>();
        if (c.DayChanged) w.Add("moved from " + was.Date.DayOfWeek);
        if (c.TimesChanged) w.Add("was " + Looks.When(was));
        if (c.SpaceChanged) w.Add(now.RoomId is null ? "no space now, was in " + Space(t, was) : was.RoomId is null ? "a space given" : "was in " + Space(t, was));
        if (c.TeachersChanged) w.Add(Teachers(t, now.FacilitatorIds.Except(was.FacilitatorIds), was.FacilitatorIds.Except(now.FacilitatorIds)));
        else if (c.PartsChanged) w.Add("a different part for " + Looks.Names(t, now.Facilitators.Where(a => !was.Facilitators.Contains(a)).Select(a => a.FacilitatorId)));
        if (c.AudienceChanged) w.Add("now for " + Looks.Audience(t, now) + ", was " + Looks.Audience(t, was));
        if (c.TitleChanged) w.Add("was called " + was.DisplayTitle);
        var line = string.Join(" · ", w);
        return line.Length == 0 ? "Changed" : char.ToUpperInvariant(line[0]) + line[1..];
    }

    /// <summary>
    /// The changed classes a facilitator had today when the window opened and does not have today now:
    /// moved to another day, deleted, or given to somebody else. Their own day lists these too.
    /// </summary>
    public static IReadOnlyList<ClassChange> NoLongerTheirs(IEnumerable<ClassChange> changed, Guid facilitatorId, DateOnly today) =>
        changed.Where(c => c.Was is { } w && w.Date == today && Theirs(w, facilitatorId)
                           && !(c.Now is { } n && n.Date == today && Theirs(n, facilitatorId))).ToList();

    /// <summary>What a facilitator reads about a class of theirs they no longer have today.</summary>
    public static string NoLongerTheirsWhat(Timetable t, ClassChange c, DateOnly today) =>
        (c.Now is { Date: var d } && d == today ? "Not yours now · " : "") + What(t, c, today);

    private static bool Theirs(Session s, Guid id) => s.FacilitatorIds.Contains(id) || s.CoverIds.Contains(id);

    private static string Space(Timetable t, Session s) => Looks.Room(t, s) is { Length: > 0 } r ? r : "no space";

    // "Dag Øverlie added", "Ada Brightwater taken off", "Dag Øverlie instead of Ada Brightwater".
    private static string Teachers(Timetable t, IEnumerable<Guid> added, IEnumerable<Guid> removed)
    {
        string on = Looks.Names(t, added), off = Looks.Names(t, removed);
        return off.Length == 0 ? on + " added" : on.Length == 0 ? off + " taken off" : on + " instead of " + off;
    }
}
