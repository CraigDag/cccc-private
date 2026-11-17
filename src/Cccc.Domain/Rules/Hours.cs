using System.Collections.Immutable;
using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>Time a rule credited, under the rule's own name.</summary>
public sealed record RuleTime(Guid RuleId, string Name, TimeSpan Time);

/// <summary>A facilitator's week: teaching, plus what each time rule added.</summary>
public sealed record HoursSplit(TimeSpan Teaching, ImmutableList<RuleTime> Rules)
{
    public static HoursSplit None { get; } = new(TimeSpan.Zero, []);
    public TimeSpan Added => Rules.Aggregate(TimeSpan.Zero, (a, r) => a + r.Time);
    public TimeSpan Total => Teaching + Added;
}

/// <summary>One credit a time rule gave one person for one occurrence.</summary>
public sealed record Credit(TimeRule Rule, Occurrence Occurrence, Guid FacilitatorId, TimeSpan Time);

/// <summary>What a line of a facilitator's hours is.</summary>
public enum HoursLineKind
{
    /// <summary>A class of their own they were in the room for.</summary>
    Teaching,
    /// <summary>A class they were in the room for as cover.</summary>
    Covering,
    /// <summary>Time a time rule credited them.</summary>
    Rule,
}

/// <summary>
/// One piece of time in a facilitator's hours (<see cref="Hours.Lines"/>): a class they were in the room
/// for, from <see cref="From"/> to <see cref="Until"/> (their own part of it), or a time rule's credit,
/// whose times are those of the class it was earned on.
/// </summary>
public sealed record HoursLine(Occurrence Occurrence, HoursLineKind Kind, TimeOnly From, TimeOnly Until, TimeSpan Time)
{
    public DateOnly Date => Occurrence.Date;

    /// <summary>The rule that credited the time, on a <see cref="HoursLineKind.Rule"/> line.</summary>
    public TimeRule? Rule { get; init; }

    /// <summary>The assigned people away whom this line's teaching stood in for, on a covering line.</summary>
    public ImmutableList<Guid> Covering { get; init; } = [];

    /// <summary>On a preparation line, the teaching the percentage was taken of.</summary>
    public TimeSpan Of { get; init; }
}

/// <summary>
/// Hours. Facilitator hours go to whoever is actually in the room; a session awaiting cover counts for
/// no facilitator. Student load counts Counted classes only, and DOES count a session awaiting cover
/// (Craig, 2026-08-22: welfare and curriculum, not contract — do not make it match).
/// </summary>
public static class Hours
{
    public static Workload WorkloadOf(Timetable t, Session s) =>
        t.Classes.Find(s.ClassId)?.Workload ?? Workload.Counted;

    /// <summary>A facilitator's teaching: the sum of their own windows in the occurrences they are in the room for.</summary>
    public static TimeSpan Teaching(IEnumerable<Occurrence> week, Guid facilitatorId) =>
        week.Where(o => o.Happens).Aggregate(TimeSpan.Zero, (a, o) => a + TimeWindows.Length(o, facilitatorId));

    public static IReadOnlyList<Occurrence> SessionsFor(IEnumerable<Occurrence> week, Guid facilitatorId) =>
        week.Where(o => o.Happens && o.InTheRoom.Contains(facilitatorId)).ToList();

    /// <summary>Every credit the project's time rules give over these occurrences (schema 18).</summary>
    public static List<Credit> Credits(Timetable t, IReadOnlyList<Occurrence> occurrences)
    {
        var credits = new List<Credit>();
        if (t.TimeRules.Count == 0) return credits;
        var happening = occurrences.Where(o => o.Happens).ToList();
        foreach (var rule in t.TimeRules.Where(r => r.Kind == TimeRuleKind.Preparation && r.Percent > 0).OrderBy(r => r.Id))
        {
            var classes = rule.ClassIds.ToHashSet();
            foreach (var o in happening)
            {
                if (o.Session.ClassId is not { } cid || !classes.Contains(cid)) continue;
                foreach (var fid in o.InTheRoom)
                {
                    var earned = Prep(TimeWindows.Length(o, fid), rule.Percent);
                    if (earned > TimeSpan.Zero) credits.Add(new Credit(rule, o, fid, earned));
                }
            }
        }
        var breaks = t.TimeRules.Where(r => r.Kind == TimeRuleKind.Break && r.Minutes > 0).OrderBy(r => r.Id).ToList();
        if (breaks.Count > 0) credits.AddRange(BreakCredits(t, happening, breaks));
        return credits;
    }

    /// <summary>Preparation earned: a percentage of the length, rounded down to the minute, never divided.</summary>
    public static TimeSpan Prep(TimeSpan length, int percent) =>
        TimeSpan.FromMinutes(Math.Floor(length.TotalMinutes * percent / 100.0));

    private static IEnumerable<Credit> BreakCredits(Timetable t, List<Occurrence> happening, List<TimeRule> breaks)
    {
        bool Teaches(Occurrence o) => WorkloadOf(t, o.Session) == Workload.Counted || t.Classes.Find(o.Session.ClassId) is null;
        var paid = new HashSet<(Guid, DateOnly, Guid)>();
        foreach (var day in happening.GroupBy(o => o.Date))
        {
            var today = day.ToList();
            foreach (var brk in today)
            {
                if (brk.NeedsCover || brk.Session.ClassId is not { } breakClass) continue;
                var matching = breaks.Where(r => r.ClassIds.Contains(breakClass)
                                                 && (r.Days.IsEmpty || r.Days.Contains(day.Key.DayOfWeek))).ToList();
                if (matching.Count == 0) continue;
                var people = today.Where(o => !ReferenceEquals(o, brk)).SelectMany(o => o.InTheRoom).Distinct();
                foreach (var fid in people)
                {
                    var theirs = today.Where(o => o.InTheRoom.Contains(fid))
                        .Select(o => (Occ: o, W: TimeWindows.InRoom(o, fid))).ToList();
                    if (theirs.Any(x => x.W.Start < brk.End && x.W.End > brk.Start)) continue;
                    var teaching = theirs.Where(x => Teaches(x.Occ)).ToList();
                    if (teaching.Count == 0) continue;
                    bool before = teaching.Any(x => x.W.End <= brk.Start);
                    bool after = teaching.Any(x => x.W.Start >= brk.End);
                    TimeRule? best = null;
                    foreach (var rule in matching)
                    {
                        bool met = rule.Condition != TimeRuleCondition.TeachesBeforeAndAfter || (before && after);
                        if (met && (best is null || rule.Minutes > best.Minutes)) best = rule;
                    }
                    // Once per person, per day, per rule (audit finding C1).
                    if (best is not null && paid.Add((fid, day.Key, best.Id)))
                        yield return new Credit(best, brk, fid, TimeSpan.FromMinutes(best.Minutes));
                }
            }
        }
    }

    /// <summary>
    /// Every piece of time in a facilitator's hours over these occurrences, one line each, in day and time
    /// order, a credit straight after the class it was earned on. Built from the same two lists the totals
    /// are (<see cref="Teaching"/>'s windows and <see cref="Credits"/>), so the lines add up to
    /// <see cref="For"/>'s total: Craig, 2026-11-16, could not find an hour and a half of a coach's week
    /// on the timetable, and the answer was a class covered and a rule's credit, neither of which the
    /// week's own timetable shows under that person.
    /// </summary>
    public static IReadOnlyList<HoursLine> Lines(Timetable t, IReadOnlyList<Occurrence> occurrences, Guid facilitatorId)
    {
        var lines = new List<HoursLine>();
        foreach (var o in occurrences.Where(o => o.Happens))
        {
            var time = TimeWindows.Length(o, facilitatorId);
            if (time <= TimeSpan.Zero) continue;
            var (from, until) = TimeWindows.InRoom(o, facilitatorId);
            bool covering = o.Session.CoverIds.Contains(facilitatorId);
            lines.Add(new HoursLine(o, covering ? HoursLineKind.Covering : HoursLineKind.Teaching, from, until, time)
            {
                Covering = covering ? o.AwayIds : [],
            });
        }
        foreach (var c in Credits(t, occurrences).Where(c => c.FacilitatorId == facilitatorId))
            lines.Add(new HoursLine(c.Occurrence, HoursLineKind.Rule, c.Occurrence.Start, c.Occurrence.End, c.Time)
            {
                Rule = c.Rule,
                Of = c.Rule.Kind == TimeRuleKind.Preparation ? TimeWindows.Length(c.Occurrence, facilitatorId) : TimeSpan.Zero,
            });
        return [.. lines.OrderBy(l => l.Date).ThenBy(l => l.Occurrence.Start).ThenBy(l => l.Occurrence.Session.Id)
                        .ThenBy(l => l.Kind == HoursLineKind.Rule).ThenBy(l => l.Rule?.Id)];
    }

    public static HoursSplit For(Timetable t, IReadOnlyList<Occurrence> occurrences, Guid facilitatorId) =>
        new(Teaching(occurrences, facilitatorId), ByRule(t, Credits(t, occurrences).Where(c => c.FacilitatorId == facilitatorId)));

    public static Dictionary<Guid, HoursSplit> ByFacilitator(Timetable t, IReadOnlyList<Occurrence> occurrences)
    {
        var credits = Credits(t, occurrences).ToLookup(c => c.FacilitatorId);
        var ids = occurrences.Where(o => o.Happens).SelectMany(o => o.InTheRoom).Concat(credits.Select(g => g.Key)).Distinct();
        return ids.ToDictionary(id => id, id => new HoursSplit(Teaching(occurrences, id), ByRule(t, credits[id])));
    }

    private static ImmutableList<RuleTime> ByRule(Timetable t, IEnumerable<Credit> credits)
    {
        var sums = new Dictionary<Guid, TimeSpan>();
        foreach (var c in credits) sums[c.Rule.Id] = sums.GetValueOrDefault(c.Rule.Id) + c.Time;
        return [.. t.TimeRules.Where(r => sums.ContainsKey(r.Id))
                    .OrderBy(r => r.DisplayName, NameOrder.Comparer)
                    .Select(r => new RuleTime(r.Id, r.DisplayName, sums[r.Id]))];
    }

    /// <summary>
    /// A student's load: Counted classes they are on the roster of, plus sessions whose class no longer
    /// exists (rather than understate a week over a defect they cannot see). Needs-cover still counts.
    /// </summary>
    public static TimeSpan StudentLoad(Timetable t, IEnumerable<Occurrence> week, Guid studentId) =>
        week.Where(o => o.Happens && IsRequired(t, o.Session) && Roster.Includes(t, o.Session, studentId))
            .Aggregate(TimeSpan.Zero, (a, o) => a + o.Session.Length);

    public static bool IsRequired(Timetable t, Session s) => WorkloadOf(t, s) == Workload.Counted;

    /// <summary>Why a line is not in a student's total, in the words the class form offers.</summary>
    public static string? WhyUncounted(Timetable t, Session s) => WorkloadOf(t, s) switch
    {
        Workload.DropIn => WorkloadWords.DropIn,
        Workload.NotCounted => WorkloadWords.NotCounted,
        _ => null,
    };
}

/// <summary>The words for a class's workload, used by the class form and every line that explains one.</summary>
public static class WorkloadWords
{
    public const string Counted = "Counted";
    public const string DropIn = "Open session";
    public const string NotCounted = "Not counted";

    public static string For(Workload w) => w switch
    {
        Workload.DropIn => DropIn,
        Workload.NotCounted => NotCounted,
        _ => Counted,
    };
}
