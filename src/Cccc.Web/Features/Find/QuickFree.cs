using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Features.Find;

/// <summary>
/// A question typed into Find about who or what is free, or where somebody is, the way it is asked: "free fri
/// 13-15", "who's free tomorrow morning", "rooms free now", "free rigging thu 14", "is studio b free thu 14",
/// "bo fri 10", "where is alba". A line is such a question when it says free (available, empty, vacant) or where,
/// or names somebody or something with a day, a time or now; a class named makes it a class to add instead, and a
/// line saying somebody is away is that. The answer is <see cref="Free"/>'s, so it agrees with Find a time and the
/// plan's shading. It only looks: Enter opens that day on the plan, and nothing here is logged.
/// </summary>
public static class QuickFree
{
    /// <summary>How much of the day a question is about: a moment, a stretch of it, or all of it.</summary>
    public enum Span { Moment, Stretch, Day }

    /// <summary>What a question naming nobody lists: spaces and facilitators, one of the two, or the groups.</summary>
    public enum Only { Both, Spaces, People, Groups }

    /// <summary>Somebody or something a question names: a facilitator, a student, a space or a group.</summary>
    public sealed record Asked(string Kind, Guid Id, string Name);

    /// <summary>
    /// What a line was read as: the day; the stretch of it asked about, a moment being its first minute; whether
    /// that moment is now; who or what it names, if anybody; what to list when it names nobody, and what a space
    /// listed must have; and the words not understood.
    /// </summary>
    public sealed record Reading(DateOnly Date, TimeOnly Start, TimeOnly End, Span Span, bool Now, IReadOnlyList<Asked> Things, Only Only,
        IReadOnlyList<Guid> Has, IReadOnlyList<string> NotUnderstood)
    {
        /// <summary>How the stretch was said when it was not two times: "morning", "from 14:00", "until 11:00".</summary>
        public string? Part { get; init; }
    }

    private static readonly string[] FreeWords = ["free", "available", "empty", "vacant", "unused", "spare"];
    private static readonly string[] WhereWords = ["where", "where's", "wheres", "whereabouts"];
    private static readonly string[] SpaceWords = ["space", "spaces", "room", "rooms", "place", "places"];
    private static readonly string[] PeopleWords =
        ["who", "who's", "whos", "people", "facilitator", "facilitators", "coach", "coaches", "teacher", "teachers", "staff",
         "anyone", "anybody", "someone", "somebody"];
    private static readonly string[] GroupWords = ["group", "groups"];
    private static readonly string[] Filler =
        ["is", "are", "am", "be", "what", "what's", "whats", "which", "any", "anything", "something", "there", "then", "right", "all", "whole", "day", "or", "of"];

    /// <summary>The last moment a stretch said as "afternoon" or "from 14" runs to, as for an absence.</summary>
    private static readonly TimeOnly EndOfDay = new(23, 45);

    /// <summary>
    /// The line read as a question about who or what is free, or null when it is not one. A time with no day is on
    /// the day the plan shows (<paramref name="shown"/>), as for a class; a day with no time is all of it; neither is
    /// <paramref name="now"/>, today.
    /// </summary>
    public static Reading? Read(Timetable t, string line, DateOnly today, DateOnly shown, TimeOnly now)
    {
        var words = TypedLine.Words(line);
        if (words.Count == 0) return null;
        // Nobody is free or busy in the Scratch Week: a day named while it is on screen is this week's.
        if (Cccc.Domain.Week.IsScratch(shown)) shown = today;
        var unclear = new List<string>();
        var ambiguous = new List<TypedLine.Named>();

        // 1. Names, whole and longest first: "Studio B", "BA 1", "Bo Lindqvist", then "bo".
        var names = TypedLine.Names(t, students: true, equipment: true);
        var found = TypedLine.Whole(words, names, unclear, ambiguous);
        // A class or a programme named is something to add, or a question about many: not this one.
        if (found.Concat(ambiguous).Any(n => n.Kind is "class" or "programme")) return null;

        // 2. A stretch of the day in one word or three: "13-15", "13 to 15".
        (TimeOnly From, TimeOnly Until)? stretch = null;
        string? part = null;
        for (int i = 0; i < words.Count && stretch is null; i++)
        {
            var w = words[i];
            if (w.Used) continue;
            var next = TypedLine.Next(words, i);
            if (TypedLine.RangeIn(w.Raw) is { } r) { stretch = r; w.Used = true; }
            else if (i + 2 < words.Count && next is not null && !words[i + 2].Used && TypedLine.IsDash(next)
                     && TypedLine.Range(w.Raw, words[i + 2].Raw) is { } r3)
            { stretch = r3; w.Used = next.Used = words[i + 2].Used = true; }
        }

        // 3. A day, a time, now, or a part of the day.
        DateOnly? date = null;
        TimeOnly? at = null;
        bool nowSaid = false;
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (w.Used) continue;
            var next = TypedLine.Next(words, i);
            bool timeFree = stretch is null && at is null;
            if (date is null && TypedLine.DateAt(words, i, today, shown) is { } d) date = d;
            else if (w.Is("now")) { nowSaid = true; w.Used = true; }
            else if (timeFree && w.Is("noon", "midday")) { at = new TimeOnly(12, 0); w.Used = true; }
            else if (timeFree && w.Is("morning")) { stretch = (TimeOnly.MinValue, new TimeOnly(12, 0)); part = "morning"; w.Used = true; }
            else if (timeFree && w.Is("afternoon")) { stretch = (new TimeOnly(12, 0), EndOfDay); part = "afternoon"; w.Used = true; }
            else if (timeFree && w.Is("evening")) { stretch = (new TimeOnly(17, 0), EndOfDay); part = "evening"; w.Used = true; }
            else if (timeFree && w.Is("from", "after") && next is not null && TypedLine.Clock(next.Raw, bare: true) is { } from && from < EndOfDay)
            { stretch = (from, EndOfDay); part = "from " + Hm(from); w.Used = next.Used = true; }
            else if (timeFree && w.Is("until", "till", "til", "before") && next is not null && TypedLine.Clock(next.Raw, bare: true) is { } by && by > TimeOnly.MinValue)
            { stretch = (TimeOnly.MinValue, by); part = "until " + Hm(by); w.Used = next.Used = true; }
            else if (timeFree && TypedLine.Clock(w.Raw, bare: false) is { } clock) { at = clock; w.Used = true; }
        }
        // A bare number is the hour when nothing else said one: "free fri 13", "free at 1" ("at" says nothing more).
        if (stretch is null && at is null && words.FirstOrDefault(w => !w.Used && TypedLine.IsHour(w.Raw)) is { } bare
            && TypedLine.Clock(bare.Raw, bare: true) is { } hour)
        { at = hour; bare.Used = true; }

        // 4. What is asked: whether free, or where, and of what.
        bool free = false, where = false, spaces = false, people = false, groups = false;
        foreach (var w in words.Where(w => !w.Used))
        {
            if (w.Is(FreeWords)) free = true;
            else if (w.Is(WhereWords)) where = true;
            else if (w.Is(SpaceWords)) spaces = true;
            else if (w.Is(PeopleWords)) people = true;
            else if (w.Is(GroupWords)) groups = true;
            else if (!w.Is(Filler)) continue;
            w.Used = true;
        }

        // 5. The start of one name: "lindq", "rig"; or of two, and then both are answered: "stud".
        var askable = names.Where(n => n.Kind is "facilitator" or "student" or "room" or "group" or "equipment").ToList();
        found.AddRange(TypedLine.Starts(words, askable, unclear, ambiguous));

        var things = found.Concat(ambiguous.OrderBy(n => n.Name, NameOrder.Comparer)).Where(n => n.Kind is "facilitator" or "student" or "room" or "group")
            .DistinctBy(n => n.Id).Select(n => new Asked(n.Kind, n.Id, n.Name)).ToList();
        var has = found.Concat(ambiguous).Where(n => n.Kind == "equipment").Select(n => n.Id).Distinct().ToList();
        bool timeSaid = date is not null || stretch is not null || at is not null || nowSaid;
        // A question only when it says so, or names somebody or something with a day, a time or now.
        if (things.Count == 0 ? !free : !free && !where && !timeSaid) return null;

        var notUnderstood = TypedLine.Left(words).Select(i => words[i].Raw).ToList();
        // 6. When: a time is on the day the plan shows unless a day is said; a day alone is all of it; else now.
        var (span, start, end, day, isNow) = (stretch, at, date) switch
        {
            ({ } s, _, _) => (Span.Stretch, s.From, s.Until, date ?? shown, false),
            (null, { } a, _) => (Span.Moment, Free.Moment(a).Start, Free.Moment(a).End, date ?? shown, false),
            (null, null, { } d0) when !nowSaid => (Span.Day, TimeOnly.MinValue, TimeOnly.MaxValue, d0, false),
            _ => (Span.Moment, Free.Moment(now).Start, Free.Moment(now).End, today, true),
        };
        var only = (spaces || has.Count > 0, people, groups) switch
        {
            (true, false, false) => Only.Spaces,
            (false, true, false) => Only.People,
            (false, false, true) => Only.Groups,
            _ => Only.Both,
        };
        return new Reading(day, start, end, span, isNow, things, only, has, notUnderstood) { Part = part };
    }

    /// <summary>
    /// What Find shows for the question, first: one line for each thing it names, saying whether they are free
    /// then, or what they are in; or, naming nobody, a line saying how many are free and then each of them. At most
    /// <paramref name="max"/>. An event is named only to a reader who may see it (<paramref name="viewer"/>).
    /// </summary>
    public static IReadOnlyList<FindHit> Hits(Timetable t, Reading r, EventViewer viewer, int max)
    {
        if (max <= 0) return [];
        var when = When(r);
        var said = r.NotUnderstood.Count > 0 ? " · not understood: " + string.Join(", ", r.NotUnderstood) : "";
        var occurrences = Occurrences.Between(t, r.Date, r.Date);
        var events = EventSchedule.On(t, r.Date);
        IReadOnlyList<BusyStretch> DayOf(Whose whose, Guid id) => Free.Day(t, whose, id, r.Date, viewer, occurrences, events);

        if (r.Things.Count > 0)
            return r.Things.Take(max).Select(x =>
            {
                var answer = Free.Unknowable(t, r.Date, GroupsOf(t, x)) ?? Answer(t, r, x, DayOf(WhoseOf(x.Kind), x.Id));
                return new FindHit(x.Kind, x.Name, when + " · " + answer + said, x.Id, ColourOf(t, x), On: r.Date);
            }).ToList();

        var kind = r.Only switch { Only.People => "free-people", Only.Groups => "free-groups", _ => "free" };
        var label = "Free " + (r.Now ? "now" : when);
        if (Free.Unknowable(t, r.Date, []) is { } shut) return [new FindHit(kind, label, shut + said, null, null, On: r.Date)];

        var spaces = new List<FindHit>();
        var people = new List<(FindHit Hit, bool Outside)>();
        var groups = new List<FindHit>();
        if (r.Only is Only.Both or Only.Spaces)
            foreach (var room in t.Rooms.Current().Where(x => r.Has.All(x.Has.Contains)).OrderBy(x => x.Name, NameOrder.Comparer))
                if (Free.Over(DayOf(Whose.Space, room.Id), r.Start, r.End) is { IsFree: true } a)
                    spaces.Add(new FindHit("room", room.Name, Around(a), room.Id, room.Colour, On: r.Date));
        if (r.Only is Only.Both or Only.People)
            foreach (var f in t.Facilitators.Current().Where(f => !f.IsGuest))
                if (Free.Over(DayOf(Whose.Facilitator, f.Id), r.Start, r.End) is { IsFree: true } a)
                {
                    // Outside their hours is said, and comes last: bounds advise, never refuse.
                    bool outside = !InTheirHours(f, r);
                    people.Add((new FindHit("facilitator", f.Name, Around(a) + (outside ? " · outside their hours" : ""), f.Id, f.Colour, On: r.Date), outside));
                }
        if (r.Only is Only.Groups)
            // A visiting company's group, or one the day is closed for, is nobody to give the time to.
            foreach (var g in t.Groups.Where(g => t.Programmes.Find(g.ProgrammeId) is not { IsGuest: true } && Free.Unknowable(t, r.Date, [g.Id]) is null)
                         .OrderBy(g => g.Name, NameOrder.Comparer))
                if (Free.Over(DayOf(Whose.Group, g.Id), r.Start, r.End) is { IsFree: true } a)
                    groups.Add(new FindHit("group", g.Name, Around(a), g.Id, g.Colour, On: r.Date));

        var counts = new List<string>();
        if (r.Only is Only.Both or Only.Spaces) counts.Add(Count(spaces.Count, "space") + Having(t, r.Has));
        if (r.Only is Only.Both or Only.People) counts.Add(Count(people.Count, "facilitator"));
        if (r.Only is Only.Groups) counts.Add(Count(groups.Count, "group"));
        var summary = new FindHit(kind, label, string.Join(" and ", counts) + said, null, null, On: r.Date);
        var items = spaces.Concat(people.OrderBy(p => p.Outside).ThenBy(p => p.Hit.Label, NameOrder.Comparer).Select(p => p.Hit)).Concat(groups);
        return [summary, .. items.Take(max - 1)];
    }

    /// <summary>The day and time asked about, as a line says it: "Fri 20 Nov 13:00", "Fri 20 Nov 13:00–15:00", "Fri 20 Nov", or "Now".</summary>
    public static string When(Reading r) =>
        r.Now ? "Now"
        : r.Span switch
        {
            Span.Moment => $"{Cccc.Domain.Week.ShortDate(r.Date)} {Cccc.Domain.Week.Hm(r.Start)}",
            Span.Stretch => $"{Cccc.Domain.Week.ShortDate(r.Date)} {r.Part ?? Looks.When(r.Start, r.End)}",
            _ => Cccc.Domain.Week.ShortDate(r.Date),
        };

    /// <summary>One thing named, then: what they are in or free for at a moment, over a stretch, or across the day.</summary>
    private static string Answer(Timetable t, Reading r, Asked x, IReadOnlyList<BusyStretch> day)
    {
        if (r.Span == Span.Day)
        {
            // What is left after the last time an absence can run to is no time to give anybody.
            var stretches = Free.Stretches(day).Where(s => s.From is not { } f || f < EndOfDay).ToList();
            if (stretches.Count == 0)
                // Taken by one thing, say which (away comes before an event that day); by several, the first of them.
                return day.FirstOrDefault(b => b.From == TimeOnly.MinValue && ToTheEnd(b.Until)) is { } whole
                    ? What(t, x, whole) + " all day"
                    : $"not free: {What(t, x, day[0])} {Times(day[0])} and {day.Count - 1} more";
            var parts = stretches.Take(4).Select(s => s switch
            {
                (null, { } u) => "until " + Hm(u),
                ({ } f, null) => "from " + Hm(f),
                ({ } f, { } u) => Looks.When(f, u),
                _ => "all day",
            }).ToList();
            if (stretches.Count > 4) parts.Add($"{stretches.Count - 4} more");
            return "free " + (parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1]);
        }
        var a = Free.Over(day, r.Start, r.End);
        if (r.Span == Span.Moment)
        {
            if (!a.IsFree)
            {
                var b = a.InTheWay[0];
                return What(t, x, b) + (ToTheEnd(b.Until) ? (b.From == TimeOnly.MinValue ? " all day" : " for the rest of the day") : " until " + Hm(b.Until));
            }
            return a.Next is { } n ? $"free until {Hm(n.From)}, then {What(t, x, n)}" : a.Since is null ? "free all day" : "free for the rest of the day";
        }
        if (!a.IsFree)
        {
            var first = a.InTheWay[0];
            return $"not free: {What(t, x, first)} {Times(first)}" + (a.InTheWay.Count > 1 ? $" and {a.InTheWay.Count - 1} more" : "");
        }
        return Around(a);
    }

    /// <summary>When something in the way is, as a line says it: "all day", "from 13:00", "until 12:00", "10:45–12:00".</summary>
    private static string Times(BusyStretch b) => (b.From == TimeOnly.MinValue, ToTheEnd(b.Until)) switch
    {
        (true, true) => "all day",
        (false, true) => "from " + Hm(b.From),
        (true, false) => "until " + Hm(b.Until),
        _ => Looks.When(b.From, b.Until),
    };

    /// <summary>Whether something lasts the rest of the day: to midnight, or to the last time an absence can run to.</summary>
    private static bool ToTheEnd(TimeOnly until) => until >= EndOfDay;

    /// <summary>How long somebody or something free is free for, around the time asked: "free from 12:00 until 16:30".</summary>
    private static string Around(FreeAnswer a) => (a.Since, a.Until) switch
    {
        (null, null) => "free all day",
        (null, { } u) => "free until " + Hm(u),
        ({ } s, null) => "free from " + Hm(s),
        ({ } s, { } u) => $"free from {Hm(s)} until {Hm(u)}",
    };

    /// <summary>
    /// What keeps them busy, in the words the plan uses: a class (where it is, or for a space who it is for), an event
    /// the reader may see by its title, away, or for a space an event they may not see, booked.
    /// </summary>
    private static string What(Timetable t, Asked x, BusyStretch b)
    {
        if (b.Class is { } o)
        {
            var title = Looks.Title(t, o.Session);
            if (x.Kind == "room") return title + " for " + Looks.Audience(t, o.Session);
            if (x.Kind == "group" && b.Kind == BusyKind.Students) return $"{b.Students} of its students in {title}";
            bool covering = x.Kind == "facilitator" && o.Session.CoverIds.Contains(x.Id);
            return (covering ? "covering " : "") + title + (Looks.Room(t, o.Session) is { Length: > 0 } room ? " in " + room : "");
        }
        if (b.Event is { } e) return EventSchedule.Title(e);
        return b.Kind == BusyKind.Away ? "away" : "booked";
    }

    /// <summary>Whether a facilitator works then, by the hours they gave; anybody who gave none works any time.</summary>
    private static bool InTheirHours(Facilitator f, Reading r) =>
        f.Availability.IsEmpty
        || (r.Span == Span.Day
            ? f.Availability.Any(w => w.Day == r.Date.DayOfWeek)
            : f.Availability.Any(w => w.Day == r.Date.DayOfWeek && w.Start <= r.Start && w.End >= r.End));

    private static string Having(Timetable t, IReadOnlyList<Guid> has) =>
        has.Count == 0 ? "" : " with " + string.Join(" and ", has.Select(id => t.Equipment[id]?.Name ?? "it").Order(NameOrder.Comparer));

    private static string Count(int n, string one) => n == 0 ? "no " + one : Looks.Count(n, one);

    /// <summary>The groups a closure must name to close the day for them: a group's own, a student's group.</summary>
    private static IReadOnlyList<Guid> GroupsOf(Timetable t, Asked x) => x.Kind switch
    {
        "group" => [x.Id],
        "student" when t.Students[x.Id]?.GroupId is { } g => [g],
        _ => [],
    };

    private static Whose WhoseOf(string kind) => kind switch
    {
        "room" => Whose.Space,
        "facilitator" => Whose.Facilitator,
        "group" => Whose.Group,
        _ => Whose.Student,
    };

    private static string? ColourOf(Timetable t, Asked x) => x.Kind switch
    {
        "room" => t.Rooms[x.Id]?.Colour,
        "facilitator" => t.Facilitators[x.Id]?.Colour,
        "group" => t.Groups[x.Id]?.Colour,
        _ => null,
    };

    private static string Hm(TimeOnly t) => Cccc.Domain.Week.Hm(t);
}
