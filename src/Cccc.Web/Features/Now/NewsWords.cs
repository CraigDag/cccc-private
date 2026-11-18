using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;
using DomainWeek = Cccc.Domain.Week;

namespace Cccc.Web.Features.Now;

/// <summary>
/// One line of "New for you": a class of theirs that changed, or several of theirs added (or deleted) in one week,
/// which is what building a week ahead (or clearing one) does, said as one line with one Got it.
/// </summary>
public sealed record NewsLine(IReadOnlyList<ClassChange> Changes, NewsKind Kind)
{
    public ClassChange First => Changes[0];

    /// <summary>The class the line is about, as it is now, or as it was when it has gone.</summary>
    public Session Shown => (First.Now ?? First.Was)!;

    public IEnumerable<string> By => Changes.SelectMany(c => c.By).Distinct(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset At => Changes.Max(c => c.At);
}

public enum NewsKind { One, Added, Deleted }

/// <summary>
/// What a facilitator reads in "New for you": what is different about a class of theirs, in their own words, beside
/// the class as it is now (or as it was, if it has gone), so it says what it was: "moved from", "was", "was in".
/// Whether it is theirs now, or no longer, is said first, as it is what changes where they have to be.
/// </summary>
public static class NewsWords
{
    /// <summary>The lines for <paramref name="news"/>, soonest first: a week's added classes are one line, and so are its deleted ones.</summary>
    public static IReadOnlyList<NewsLine> Lines(IReadOnlyList<ClassChange> news)
    {
        static (NewsKind, DateOnly)? Key(ClassChange c) =>
            c.Added ? (NewsKind.Added, DomainWeek.MondayOf(c.Now!.Date)) : c.Deleted ? (NewsKind.Deleted, DomainWeek.MondayOf(c.Was!.Date)) : null;
        var several = news.GroupBy(Key).Where(g => g.Key is not null && g.Count() > 1).ToDictionary(g => g.Key!.Value, g => g.ToList());
        var lines = new List<NewsLine>();
        foreach (var c in news)
        {
            if (Key(c) is { } k && several.TryGetValue(k, out var group))
            {
                if (ReferenceEquals(group[0], c)) lines.Add(new NewsLine(group, k.Item1));
            }
            else lines.Add(new NewsLine([c], NewsKind.One));
        }
        return lines;
    }

    /// <summary>What is new, the most telling first: "You cover it, for Esme Halloran", "Moved from Tue 24 Nov · was in Studio".</summary>
    public static string What(Timetable t, NewsLine line, Guid me) => line.Kind switch
    {
        NewsKind.Added => $"{line.Changes.Count} new classes",
        NewsKind.Deleted => $"{line.Changes.Count} classes deleted",
        _ => ForYou(t, line.First, me),
    };

    /// <summary>The class's name, or the names of a week's several, the first three in the order they come.</summary>
    public static string Title(Timetable t, NewsLine line)
    {
        if (line.Kind == NewsKind.One) return Looks.Title(t, line.Shown);
        var names = line.Changes.Select(c => Looks.Title(t, (c.Now ?? c.Was)!)).Distinct().ToList();
        return names.Count <= 3 ? Data.EquipmentWords.And(names) : string.Join(", ", names.Take(3)) + $" and {names.Count - 3} more";
    }

    /// <summary>When it is (or was): its day, time and space, or the days a week's several are on.</summary>
    public static string When(Timetable t, NewsLine line)
    {
        if (line.Kind != NewsKind.One)
        {
            var days = line.Changes.Select(c => (c.Now ?? c.Was)!.Date).ToList();
            DateOnly first = days.Min(), last = days.Max();
            return first == last ? DomainWeek.ShortDate(first) : DomainWeek.ShortDate(first) + " to " + DomainWeek.ShortDate(last);
        }
        var s = line.Shown;
        return Looks.WhenWithDay(s) + (Looks.Room(t, s) is { Length: > 0 } r ? " · " + r : "");
    }

    /// <summary>Whether the line is about classes no longer in their days: deleted, cancelled, or not theirs now.</summary>
    public static bool Off(NewsLine line, Guid me) => line.First.Now is not { } now || now.Cancelled || !Theirs(now, me);

    /// <summary>What is new about one class of theirs, with "you" for them.</summary>
    public static string ForYou(Timetable t, ClassChange c, Guid me)
    {
        if (c.Now is not { } now) return "Deleted";
        if (c.Was is not { } was) return now.CoverIds.Contains(me) ? "New, and you cover it" : "New";
        bool taught = was.FacilitatorIds.Contains(me), teaches = now.FacilitatorIds.Contains(me);
        bool covered = was.CoverIds.Contains(me), covers = now.CoverIds.Contains(me);
        var w = new List<string>();
        bool whoSaid = false;
        if (Theirs(was, me) && !Theirs(now, me))
        {
            // Not theirs now: who has it instead, the covering if it was cover they had.
            w.Add("not yours now: " + (covered
                ? now.CoverIds.Count == 0 ? "cover taken off" : Verb(t, now.CoverIds, "covers", "cover")
                : !now.FacilitatorIds.Any() ? "nobody teaches it" : Verb(t, now.FacilitatorIds, "teaches", "teach")));
            whoSaid = true;
        }
        else if (teaches && !taught)
        {
            var instead = was.FacilitatorIds.Except(now.FacilitatorIds).ToList();
            var with = now.FacilitatorIds.Where(id => id != me).ToList();
            w.Add("you teach it now" + (instead.Count > 0 ? " instead of " + Looks.Names(t, instead) : "")
                  + (with.Count > 0 ? ", with " + Looks.Names(t, with) : ""));
            whoSaid = true;
        }
        else if (covers && !covered && !teaches)
        {
            w.Add("you cover it" + (Occurrences.Resolve(t, now).AwayIds is { Count: > 0 } away ? ", for " + Looks.Names(t, away) : ""));
            whoSaid = true;
        }
        if (c.CancelledChanged) w.Add(now.Cancelled ? "cancelled" : "back on");
        if (c.DayChanged) w.Add("moved from " + DomainWeek.ShortDate(was.Date));
        if (c.TimesChanged) w.Add("was " + Looks.When(was));
        if (c.SpaceChanged) w.Add(now.RoomId is null ? "no space now, was in " + Space(t, was) : was.RoomId is null ? "a space given" : "was in " + Space(t, was));
        if (!whoSaid)
        {
            if (c.TeachersChanged) w.Add(Teachers(t, now.FacilitatorIds.Except(was.FacilitatorIds), was.FacilitatorIds.Except(now.FacilitatorIds)));
            else if (c.PartsChanged)
            {
                var others = now.Facilitators.Where(a => !was.Facilitators.Contains(a) && a.FacilitatorId != me).Select(a => a.FacilitatorId).ToList();
                if (!now.Facilitators.Where(a => a.FacilitatorId == me).SequenceEqual(was.Facilitators.Where(a => a.FacilitatorId == me)))
                    w.Add("your part is now " + (TimeWindows.Describe(now, me) ?? "all of it"));
                if (others.Count > 0) w.Add("a different part for " + Looks.Names(t, others));
            }
            if (c.CoverChanged)
                w.Add(now.CoverIds.Count == 0 ? "cover taken off"
                    : Looks.Names(t, now.CoverIds) + (teaches && Occurrences.Resolve(t, now).AwayIds.Contains(me) ? " covering for you" : " covering"));
        }
        if (c.AudienceChanged) w.Add("now for " + Looks.Audience(t, now) + ", was " + Looks.Audience(t, was));
        if (c.TitleChanged) w.Add("was called " + was.DisplayTitle);
        var line = string.Join(" · ", w);
        return line.Length == 0 ? "Changed" : char.ToUpperInvariant(line[0]) + line[1..];
    }

    private static bool Theirs(Session s, Guid me) => s.FacilitatorIds.Contains(me) || s.CoverIds.Contains(me);

    private static string Verb(Timetable t, IEnumerable<Guid> ids, string one, string many) =>
        Looks.Names(t, ids) + " " + (ids.Count() == 1 ? one : many) + " it";

    private static string Space(Timetable t, Session s) => Looks.Room(t, s) is { Length: > 0 } r ? r : "no space";

    private static string Teachers(Timetable t, IEnumerable<Guid> added, IEnumerable<Guid> removed)
    {
        string on = Looks.Names(t, added), off = Looks.Names(t, removed);
        return off.Length == 0 ? on + " added" : on.Length == 0 ? off + " taken off" : on + " instead of " + off;
    }
}
