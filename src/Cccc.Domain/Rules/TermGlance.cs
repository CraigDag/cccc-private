using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// One week of a semester at a glance: how much happens in it, whether it is built, closed or blank, what is
/// different from the week before, what needs attention, and how much each space, group and person has.
/// Every figure is the same rule the week itself is drawn and counted with, asked of each week in turn.
/// </summary>
public sealed record TermWeek
{
    public required DateOnly Monday { get; init; }

    /// <summary>Classes written into the week, whatever became of them.</summary>
    public required int Written { get; init; }

    /// <summary>Classes that happen: neither cancelled nor closed. What the day strip counts.</summary>
    public required int Happening { get; init; }

    public required int Cancelled { get; init; }

    /// <summary>A week nobody has filled: nothing in it can be checked, and nothing in it counts.</summary>
    public bool Blank => Written == 0;

    /// <summary>The school days (Monday to Friday) the whole building is closed, and the closure for each.</summary>
    public required IReadOnlyList<(DateOnly Day, Closure By)> ClosedDays { get; init; }

    /// <summary>The closure shutting the building every school day of the week, when one does.</summary>
    public Closure? ClosedAllWeek => ClosedDays.Count == 5 ? ClosedDays[0].By : null;

    /// <summary>The groups closed every school day of the week, by the building or by a closure naming them.</summary>
    public required IReadOnlySet<Guid> GroupsClosedAllWeek { get; init; }

    /// <summary>The week it is compared with (<see cref="WeekChanges.Previous(Timetable, DateOnly)"/>), and what is different.</summary>
    public required DateOnly? ComparedWith { get; init; }
    public required IReadOnlyList<WeekChange> Changes { get; init; }

    /// <summary>The clashes drawn as a ring, as the week's issues count them.</summary>
    public required int Clashes { get; init; }
    public required int CoverNeeded { get; init; }
    /// <summary>Registers missing that this reader may file, as the week bar counts them.</summary>
    public required int RegistersMissing { get; init; }
    public int Issues => Clashes + CoverNeeded + RegistersMissing;

    /// <summary>Events this reader may see on any day of the week.</summary>
    public required int Events { get; init; }

    /// <summary>Hours each space is in use by classes that happen.</summary>
    public required IReadOnlyDictionary<Guid, TimeSpan> SpaceHours { get; init; }

    /// <summary>
    /// Hours of counted classes each group's timetable has: classes named for it, never a whole-building one
    /// (which is on nobody's roster), so it is what each of its students has unless one is left off or added.
    /// </summary>
    public required IReadOnlyDictionary<Guid, TimeSpan> GroupHours { get; init; }

    /// <summary>How many of each group's students have fewer counted hours than the week's target, when it has one.</summary>
    public required IReadOnlyDictionary<Guid, int> GroupStudentsShort { get; init; }

    /// <summary>Each facilitator's hours, teaching and time rules, exactly as their own week counts them.</summary>
    public required IReadOnlyDictionary<Guid, HoursSplit> PersonHours { get; init; }

    /// <summary>How many classes each facilitator is in the room for.</summary>
    public required IReadOnlyDictionary<Guid, int> PersonClasses { get; init; }

    public bool HasEnded(DateOnly today) => Week.HasEnded(Monday, today);
    public bool IsThisWeek(DateOnly today) => Week.MondayOf(today) == Monday;
}

public static class TermGlance
{
    /// <summary>The most weeks laid side by side: a semester, not a year.</summary>
    public const int MostWeeks = 30;

    /// <summary>
    /// The weeks to lay out for a day: its semester, or the nearest one when it is in none; with no
    /// semesters at all, the thirteen weeks from four before its own.
    /// </summary>
    public static IReadOnlyList<DateOnly> WeeksFor(Timetable t, DateOnly day)
    {
        if (t.SemesterNear(day) is { WeekCount: > 0 } s)
        {
            var all = s.Mondays().ToList();
            if (all.Count <= MostWeeks) return all;
            // A very long semester: the stretch of it around the day.
            int at = Math.Clamp(all.IndexOf(Week.MondayOf(day)) is var i and >= 0 ? i - 4 : 0, 0, all.Count - MostWeeks);
            return all.GetRange(at, MostWeeks);
        }
        var m = Week.MondayOf(day);
        return Enumerable.Range(-4, 13).Select(i => m.AddDays(7 * i)).ToList();
    }

    public static IReadOnlyList<TermWeek> For(Timetable t, IEnumerable<DateOnly> weeks, DateOnly today, EventViewer viewer, RegisterReader reader)
    {
        var built = WeekChanges.BuiltWeeks(t);
        var groupStudents = t.Students.Where(s => s.GroupId is not null).GroupBy(s => s.GroupId!.Value).ToDictionary(g => g.Key, g => g.Select(s => s.Id).ToList());
        var list = new List<TermWeek>();
        foreach (var monday in weeks.Select(Week.MondayOf).Distinct().Order())
        {
            var all = Occurrences.ForWeek(t, monday);
            var happening = all.Where(o => o.Happens).ToList();
            var previous = WeekChanges.Previous(built, monday);

            var spaces = new Dictionary<Guid, TimeSpan>();
            var groups = t.Groups.ToDictionary(g => g.Id, _ => TimeSpan.Zero);
            var classes = new Dictionary<Guid, int>();
            foreach (var o in happening)
            {
                if (o.Session.RoomId is { } r) spaces[r] = spaces.GetValueOrDefault(r) + o.Session.Length;
                if (Hours.IsRequired(t, o.Session))
                    foreach (var g in o.Session.Audience.Cohorts.Where(groups.ContainsKey))
                        groups[g] += o.Session.Length;
                foreach (var id in o.InTheRoom) classes[id] = classes.GetValueOrDefault(id) + 1;
            }

            // Each student measured as Reports ▸ Student hours measures them; a blank week measures nobody.
            var shortBy = new Dictionary<Guid, int>();
            if (all.Count > 0)
                foreach (var (g, students) in groupStudents)
                    if (Hours.GroupTarget(t, monday, g) is { } goal && students.Count(id => Hours.StudentLoad(t, all, id) < goal) is > 0 and var n)
                        shortBy[g] = n;

            list.Add(new TermWeek
            {
                Monday = monday,
                Written = all.Count,
                Happening = happening.Count,
                Cancelled = all.Count(o => o.State == OccurrenceState.Cancelled),
                ClosedDays = Week.Days(monday).Take(5)
                    .Select(d => (Day: d, By: Occurrences.ClosureFor(t, d, null)))
                    .Where(x => x.By is not null).Select(x => (x.Day, x.By!)).ToList(),
                GroupsClosedAllWeek = ClosedAllWeek(t, monday),
                ComparedWith = previous,
                Changes = previous is { } p && built.Contains(monday) ? WeekChanges.Between(t, p, monday) : [],
                Clashes = Rules.Clashes.ForWeek(t, monday).Count(c => c.IsRing),
                CoverNeeded = Cover.StillNeeded(t, monday, today).Count,
                RegistersMissing = Registers.FileableBy(Registers.MissingInWeek(t, monday, today), reader).Count,
                Events = EventSchedule.Between(t, monday, monday.AddDays(6), viewer).Count,
                SpaceHours = spaces,
                GroupHours = groups,
                GroupStudentsShort = shortBy,
                PersonHours = Hours.ByFacilitator(t, happening),
                PersonClasses = classes,
            });
        }
        return list;
    }

    /// <summary>The groups closed on every school day of the week: the building, or a closure naming them.</summary>
    private static HashSet<Guid> ClosedAllWeek(Timetable t, DateOnly monday)
    {
        HashSet<Guid>? closed = null;
        foreach (var d in Week.Days(monday).Take(5))
        {
            var today = t.Closures.Any(c => c.Covers(d) && c.Scope == ClosureScope.WholeBuilding)
                ? t.Groups.Select(g => g.Id).ToHashSet()
                : t.Closures.Where(c => c.Covers(d) && c.Scope == ClosureScope.Groups).SelectMany(c => c.GroupIds).ToHashSet();
            if (closed is null) closed = today; else closed.IntersectWith(today);
            if (closed.Count == 0) break;
        }
        return closed ?? [];
    }
}
