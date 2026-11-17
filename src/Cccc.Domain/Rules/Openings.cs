using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>What to find a time for: a class as it would be, the spaces it may go in, and where to look.</summary>
public sealed record OpeningAsk
{
    /// <summary>
    /// The class as it would be: who teaches it (somebody in part of it keeps their part as it moves), who it
    /// is for, which class it is and how long it lasts. Its date, time and space are what is being found. A
    /// class the timetable already has is looked for as itself, so it never stands in its own way.
    /// </summary>
    public required Session Class { get; init; }

    /// <summary>The spaces it may go in, the one to offer first first. Empty when it needs no space.</summary>
    public IReadOnlyList<Guid> Spaces { get; init; } = [];

    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }

    /// <summary>The part of each day to look in: a time found starts no earlier and ends no later.</summary>
    public TimeOnly DayStart { get; init; } = new(8, 0);
    public TimeOnly DayEnd { get; init; } = new(18, 0);
}

/// <summary>A reason for a time (<see cref="Good"/>) or against it, in words.</summary>
public sealed record OpeningNote(string Text, bool Good);

/// <summary>
/// A time it fits: starting at <see cref="Start"/>, or at any quarter hour up to <see cref="Latest"/>, in the
/// first of <see cref="Spaces"/>, all of them free at Start. The notes are about starting at Start, there.
/// </summary>
public sealed record Opening(DateOnly Date, TimeOnly Start, TimeOnly Latest, TimeSpan Length, IReadOnlyList<Guid> Spaces, IReadOnlyList<OpeningNote> Notes)
{
    public TimeOnly End => Start.Add(Length);
    public TimeOnly LatestEnd => Latest.Add(Length);
    public Guid? Space => Spaces.Count > 0 ? Spaces[0] : null;
}

/// <summary>Days, From to To, and why: not looked in at all, or looked in and nothing fits.</summary>
public sealed record DayNote(DateOnly From, DateOnly To, string Why);

/// <summary>
/// The times found, in the order they come; how many more there were past <see cref="Openings.Most"/>; the
/// days not looked in (closed, or a week with nothing in it yet); and the days looked in where nothing fits.
/// </summary>
public sealed record OpeningsFound(IReadOnlyList<Opening> Openings, int More, IReadOnlyList<DayNote> NotSearched, IReadOnlyList<DayNote> NoTime);

/// <summary>
/// Finding a time: when everybody a class needs is free, and a space for it. WHO is in what SPACE and WHEN,
/// asked the other way round. A time is offered only when every facilitator is in no other class and not
/// away for their part of it, no group it is for has a class then (an open session excepted, which nobody
/// is required at), none of its students is on another class, nobody it names is at an event named for
/// them, and one of its spaces has no class and no event in it. Everything else the clash check would say
/// (a limit, a long gap, outside somebody's hours) is a note beside the time, never a reason to leave it
/// out: bounds advise, never refuse.
/// </summary>
public static class Openings
{
    public const int StepMinutes = 15;

    /// <summary>The most times listed: a list to choose from, not a spreadsheet.</summary>
    public const int Most = 30;

    /// <summary>The furthest it looks: half a year of days.</summary>
    public const int MostDays = 183;

    public const string NothingYet = "Nothing in this week yet, so nothing there can be checked: a week ahead is blank until it is filled";

    /// <summary>The class at a date and time, in a space: every part of it moved by as much as the class.</summary>
    public static Session Placed(Session c, DateOnly date, TimeOnly start, Guid? space) =>
        TimeWindows.Shift(c, Week.Span(c.Start, start)) with { Date = date, Start = start, End = start.Add(c.Length), RoomId = space };

    public static OpeningsFound Find(Timetable t, OpeningAsk ask, DateOnly today, TimeOnly now, EventViewer viewer)
    {
        var c = ask.Class;
        int length = (int)c.Length.TotalMinutes;
        var to = ask.To > ask.From.AddDays(MostDays) ? ask.From.AddDays(MostDays) : ask.To;
        if (length <= 0 || to < ask.From) return new([], 0, [], []);

        Guid? self = t.Sessions.Contains(c.Id) ? c.Id : null;
        // A whole-building class needs every group free, and is closed only when the building is.
        var groups = (c.Audience.IsEveryone ? t.Groups.Select(g => g.Id) : c.Audience.Cohorts.Where(t.Groups.Contains)).ToHashSet();
        var named = c.Audience.IsEveryone ? [] : groups.OrderBy(g => t.Groups[g]!.Name, NameOrder.Comparer).ToList();
        var students = Roster.For(t, c).ToHashSet();
        var people = c.FacilitatorIds.Where(t.Facilitators.Contains).ToList();
        var spaces = ask.Spaces.Distinct().Where(t.Rooms.Contains).ToList();
        bool needsSpace = ask.Spaces.Count > 0;
        int dayStart = Up(Minutes(ask.DayStart)), dayEnd = Minutes(ask.DayEnd);

        var found = new List<Opening>();
        var notSearched = new List<DayNote>();
        var noTime = new List<DayNote>();
        int more = 0;

        for (var day = ask.From < today ? today : ask.From; day <= to; day = day.AddDays(1))
        {
            if (Week.IsScratch(day)) continue;
            if ((Closed(t, day, named) ?? (t.SessionsInWeek(day).Any() ? null : NothingYet)) is { } why)
            {
                Note(notSearched, day, why);
                continue;
            }
            // A weekend is looked in only when the school uses it: when the timetable has a class that day.
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday && !t.Sessions.Any(s => s.Date == day)) continue;

            var occurrences = Occurrences.Between(t, day, day);
            var events = EventSchedule.On(t, day);
            var seen = events.Where(e => viewer.Sees(e.Event)).ToList();
            var rosters = new Dictionary<Guid, IReadOnlyList<Guid>>();
            IReadOnlyList<Guid> RosterOf(Session s) => rosters.TryGetValue(s.Id, out var r) ? r : rosters[s.Id] = Roster.For(t, s);

            // The spaces free for the whole of the class starting at minute m, empty when it needs none;
            // null when its people or groups are not free then, or no space is.
            IReadOnlyList<Guid>? Fits(Session template, int m, bool withSpace)
            {
                var start = At(m);
                var end = At(m + (int)template.Length.TotalMinutes);
                var placed = Placed(template, day, start, null);
                foreach (var id in people)
                {
                    var (a, b) = TimeWindows.Assigned(placed, id);
                    if (b <= a) continue;
                    if (Occurrences.IsAway(t, id, day, a, b)) return null;
                    if (Teaching.At(occurrences, a, b, self).ContainsKey(id)) return null;
                    if (seen.Any(e => e.Event.FacilitatorIds.Contains(id) && e.Overlaps(a, b))) return null;
                }
                foreach (var o in occurrences)
                {
                    if (o.Session.Id == self || !o.Happens || o.Start >= end || start >= o.End) continue;
                    if (Hours.WorkloadOf(t, o.Session) == Workload.DropIn) continue;
                    if (groups.Any(o.Session.Audience.IsFor)) return null;
                    if (students.Count > 0 && RosterOf(o.Session).Any(students.Contains)) return null;
                }
                if (students.Count > 0 && seen.Any(e => e.Event.StudentIds.Any(students.Contains) && e.Overlaps(start, end))) return null;
                if (!withSpace || !needsSpace) return [];
                var used = Spaces.InUse(occurrences, start, end, self);
                var booked = Spaces.Booked(t, events, start, end);
                var free = spaces.Where(r => !used.ContainsKey(r) && !booked.ContainsKey(r)).ToList();
                return free.Count > 0 ? free : null;
            }

            int first = day == today ? Math.Max(dayStart, Up(Minutes(now) + 1)) : dayStart;
            int runStart = -1, runLast = -1;
            IReadOnlyList<Guid> runSpaces = [];
            bool any = false;
            void Close()
            {
                if (runStart < 0) return;
                any = true;
                if (found.Count < Most)
                    found.Add(new Opening(day, At(runStart), At(runLast), TimeSpan.FromMinutes(length), runSpaces,
                        NotesFor(t, Placed(c, day, At(runStart), runSpaces.Count > 0 ? runSpaces[0] : null), self, people, named, groups.Count > 0, occurrences, seen)));
                else more++;
                runStart = -1;
            }
            for (int m = first; m + length <= dayEnd; m += StepMinutes)
            {
                // Where it already is is not another time.
                bool itself = self is not null && day == c.Date && m == Minutes(c.Start);
                var free = itself ? null : Fits(c, m, true);
                if (free is null) { Close(); continue; }
                if (runStart < 0) { runStart = m; runSpaces = free; }
                runLast = m;
            }
            Close();
            if (!any && first + length <= dayEnd) Note(noTime, day, WhyNoTime(t, c, day, people, first, dayEnd, Fits));
        }
        return new(found, more, notSearched, noTime);
    }

    /// <summary>
    /// What the time would mean, said beside it: whatever the clash check would warn about once it is
    /// there; an event its people are expected at; and how it sits in each person's and group's day.
    /// </summary>
    private static List<OpeningNote> NotesFor(Timetable t, Session placed, Guid? self, IReadOnlyList<Guid> people,
        IReadOnlyList<Guid> groups, bool forStudents, IReadOnlyList<Occurrence> day, IReadOnlyList<EventOnDay> seen)
    {
        var against = Clashes.CausedBy(t, TimeWindows.Normalise(placed)).Select(x => new OpeningNote(x.Description, false)).ToList();
        foreach (var e in seen.Where(e => e.Overlaps(placed.Start, placed.End)))
        {
            var who = e.Event.Audience;
            bool theirs = who == EventAudience.Everyone
                ? !e.AllDay
                : (people.Count > 0 && (who.HasFlag(EventAudience.Facilitators) || who.HasFlag(EventAudience.Staff)))
                  || (forStudents && who.HasFlag(EventAudience.Students));
            if (theirs) against.Add(new(Title(e.Event) + (e.AllDay ? " that day" : $" then, {Week.Hm(e.From)}–{Week.Hm(e.Until)}"), false));
        }
        var good = new List<OpeningNote>();
        foreach (var id in people)
        {
            var (a, b) = TimeWindows.Assigned(placed, id);
            if (b <= a) continue;
            var name = t.Facilitators[id]!.Name;
            var theirs = day.Where(o => o.Happens && o.Session.Id != self && o.InTheRoom.Contains(id))
                .Select(o => (O: o, W: TimeWindows.InRoom(o, id))).ToList();
            if (theirs.Count == 0) { against.Add(new($"{name} has nothing else that day", false)); continue; }
            if (theirs.FirstOrDefault(x => x.W.End == a).O is { } before) good.Add(new($"{name} comes straight from {before.Session.DisplayTitle}", true));
            if (theirs.FirstOrDefault(x => x.W.Start == b).O is { } after) good.Add(new($"{name} goes straight on to {after.Session.DisplayTitle}", true));
        }
        foreach (var g in groups)
        {
            var name = t.Groups[g]!.Name;
            var theirs = day.Where(o => o.Happens && o.Session.Id != self && o.Session.Audience.IsFor(g) && Hours.WorkloadOf(t, o.Session) == Workload.Counted).ToList();
            if (theirs.Count == 0) { against.Add(new($"{name} has nothing else that day", false)); continue; }
            if (theirs.FirstOrDefault(o => o.End == placed.Start) is { } before) good.Add(new($"{name} comes straight from {before.Session.DisplayTitle}", true));
            if (theirs.FirstOrDefault(o => o.Start == placed.End) is { } after) good.Add(new($"{name} goes straight on to {after.Session.DisplayTitle}", true));
        }
        return [.. against.DistinctBy(n => n.Text), .. good.DistinctBy(n => n.Text)];
    }

    /// <summary>
    /// Why a day looked in has no time: somebody away the whole of it, the longest everybody is free
    /// together when that is too short, or no space free when they are.
    /// </summary>
    private static string WhyNoTime(Timetable t, Session c, DateOnly day, IReadOnlyList<Guid> people, int first, int dayEnd,
        Func<Session, int, bool, IReadOnlyList<Guid>?> fits)
    {
        var quarters = Enumerable.Range(0, (dayEnd - first) / StepMinutes).Select(i => first + i * StepMinutes).ToList();
        var away = people.Where(id => quarters.All(m => Occurrences.IsAway(t, id, day, At(m), At(m + StepMinutes))))
            .Select(id => t.Facilitators[id]!.Name).OrderBy(n => n, NameOrder.Comparer).ToList();
        if (away.Count > 0) return string.Join(" and ", away) + (away.Count == 1 ? " is" : " are") + " away";
        // A quarter hour of the class with everybody in all of it: who is free together, and for how long.
        var quarter = c with { End = c.Start.AddMinutes(StepMinutes), Facilitators = [.. c.Facilitators.Select(a => new Assignment(a.FacilitatorId))] };
        int longest = 0, run = 0;
        foreach (var m in quarters)
        {
            run = fits(quarter, m, false) is null ? 0 : run + StepMinutes;
            longest = Math.Max(longest, run);
        }
        if (longest == 0) return "Never all free at once";
        if (longest < (int)c.Length.TotalMinutes) return $"Free together for {Week.Duration(TimeSpan.FromMinutes(longest))} at most";
        return "No space is free when everybody is";
    }

    /// <summary>The closure that leaves a day out: the whole building, or any group the class is for.</summary>
    private static string? Closed(Timetable t, DateOnly day, IReadOnlyList<Guid> groups)
    {
        if (Occurrences.ClosureFor(t, day, null) is { } whole) return (whole.Name.Trim() is { Length: > 0 } n ? n : "Closure") + ": closed";
        foreach (var c in t.Closures.Where(c => c.Covers(day) && c.Scope == ClosureScope.Groups).OrderBy(c => c.From).ThenBy(c => c.Id))
        {
            var shut = groups.Where(c.GroupIds.Contains).Select(g => t.Groups[g]!.Name).ToList();
            if (shut.Count > 0) return (c.Name.Trim() is { Length: > 0 } n ? n : "Closure") + ": closed for " + string.Join(" and ", shut);
        }
        return null;
    }

    /// <summary>Days with the same reason run together into one note, over a weekend too.</summary>
    private static void Note(List<DayNote> notes, DateOnly day, string why)
    {
        if (notes.Count > 0 && notes[^1] is var last && last.Why == why && day.DayNumber - last.To.DayNumber <= 3)
            notes[^1] = last with { To = day };
        else notes.Add(new DayNote(day, day, why));
    }

    private static string Title(SchoolEvent e) => e.Title.Trim() is { Length: > 0 } title ? title : "An event";

    private static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    /// <summary>Up to the next quarter hour, or the same one when it is on one.</summary>
    private static int Up(int m) => (m + StepMinutes - 1) / StepMinutes * StepMinutes;

    private static TimeOnly At(int m) => m >= 24 * 60 ? new TimeOnly(23, 59) : new TimeOnly(m / 60, m % 60);
}
