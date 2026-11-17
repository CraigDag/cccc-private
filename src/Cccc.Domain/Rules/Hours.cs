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
/// One class on a student's week (<see cref="Hours.StudentLines"/>), and why it does not count towards
/// their load when it does not.
/// </summary>
public sealed record StudentLine(Occurrence Occurrence, string? NotCounted)
{
    public bool Counted => NotCounted is null;
    public TimeSpan Time => Occurrence.Session.Length;
}

/// <summary>
/// A student's week as Reports, the plan and the lines drawer measure it (<see cref="Hours.ForStudent"/>):
/// every class of theirs, and the school's weekly target when there is one to measure it against.
/// </summary>
public sealed record StudentWeek(DateOnly Monday, IReadOnlyList<StudentLine> Lines, TimeSpan? Target)
{
    public TimeSpan Counted => Lines.Where(l => l.Counted).Aggregate(TimeSpan.Zero, (a, l) => a + l.Time);

    public int NotCounted => Lines.Count(l => !l.Counted);

    /// <summary>
    /// Measured against the target: there is one (<see cref="Hours.StudentTarget"/>), and something of
    /// theirs counted. A week in which nothing counted (the school closed, every class cancelled, nothing
    /// on) is not called short: it is not a timetable falling short, and a report that turns every
    /// holiday week amber is one nobody reads. Its lines say what happened instead.
    /// </summary>
    public bool Measured => Target is not null && Counted > TimeSpan.Zero;

    public bool Short => Measured && Counted < Target!.Value;

    public TimeSpan ShortBy => Short ? Target!.Value - Counted : TimeSpan.Zero;
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

    /// <summary>
    /// Every facilitator's hours over the given weeks (any date in each), the school's own before guests, each
    /// in name order. Each week is counted as it runs, exactly as <see cref="ByFacilitator"/> counts it.
    /// </summary>
    public static IReadOnlyList<FacilitatorWeeks> OverWeeks(Timetable t, IEnumerable<DateOnly> weeks)
    {
        var each = weeks.Select(Week.MondayOf).Distinct().Order()
            .Select(m => (Monday: m, Happening: Occurrences.HappeningInWeek(t, m)))
            .Select(w => (w.Monday, Ran: w.Happening.Count > 0, By: ByFacilitator(t, w.Happening)))
            .ToList();
        return t.Facilitators.OrderBy(f => f.IsGuest).ThenBy(f => f.Name, NameOrder.Comparer)
            .Select(f => new FacilitatorWeeks(f, each.Select(w => new WeekHours(w.Monday, w.By.GetValueOrDefault(f.Id) ?? HoursSplit.None, w.Ran)).ToList()))
            .ToList();
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
    /// The sum of <see cref="StudentLines"/> that count, so a total and its lines cannot disagree.
    /// </summary>
    public static TimeSpan StudentLoad(Timetable t, IEnumerable<Occurrence> week, Guid studentId) =>
        StudentLines(t, week, studentId).Where(l => l.Counted).Aggregate(TimeSpan.Zero, (a, l) => a + l.Time);

    /// <summary>
    /// Every class a student is on in these occurrences (closed and cancelled ones too, when given), in
    /// day and time order, each saying why it does not count when it does not: the closure, a
    /// cancellation, or the class's workload in the words the class form offers. A class still waiting
    /// for cover counts (Craig, 2026-08-22: a student is still timetabled to be in that room). A
    /// whole-building class is on nobody's roster, so it is on no student's lines.
    /// </summary>
    public static IReadOnlyList<StudentLine> StudentLines(Timetable t, IEnumerable<Occurrence> occurrences, Guid studentId) =>
        [.. occurrences.Where(o => Roster.Includes(t, o.Session, studentId))
                       .OrderBy(o => o.Date).ThenBy(o => o.Start).ThenBy(o => o.Session.Id)
                       .Select(o => new StudentLine(o, o.State switch
                       {
                           OccurrenceState.Closed => o.ClosedBy is { Name: var n } && !string.IsNullOrWhiteSpace(n) ? "Closed: " + n.Trim() : "Closed",
                           OccurrenceState.Cancelled => "Cancelled",
                           _ => WhyUncounted(t, o.Session),
                       }))];

    /// <summary>
    /// The weekly target a student's week is measured against, or null when there is nothing to measure:
    /// the school set none (People &amp; places ▸ Settings); the week is outside every semester, as the
    /// current app's week tally reads it, so that the summer is not short of a target nobody meant for it
    /// (a school with no semesters at all is never outside one); or the student is in a visiting
    /// programme, whose week is not the school's own curriculum.
    /// </summary>
    public static TimeSpan? StudentTarget(Timetable t, DateOnly anyDateInWeek, Guid studentId)
    {
        if (t.Settings.WeeklyScheduledTarget is not { } target) return null;
        if (t.Semesters.Count > 0 && t.SemesterFor(anyDateInWeek) is null) return null;
        if (t.Students[studentId]?.GroupId is { } gid && t.Groups[gid] is { } g && t.Programmes.Find(g.ProgrammeId) is { IsGuest: true }) return null;
        return target;
    }

    /// <summary>
    /// A student's week, from that week's occurrences with the closed and cancelled ones in them
    /// (<see cref="Occurrences.ForWeek"/>), so that what did not count is there to say why.
    /// </summary>
    public static StudentWeek ForStudent(Timetable t, DateOnly anyDateInWeek, IEnumerable<Occurrence> week, Guid studentId) =>
        new(Week.MondayOf(anyDateInWeek), StudentLines(t, week, studentId), StudentTarget(t, anyDateInWeek, studentId));

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
