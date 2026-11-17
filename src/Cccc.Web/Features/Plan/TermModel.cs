using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Shell;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Features.Plan;

using Week = Cccc.Domain.Week;

/// <summary>One row of the term at a glance: a space, a group, a person or a student, and what the plan narrows to for it.</summary>
public sealed record TermRow(Guid Id, string Name, string? Colour, string Section, string Kind);

/// <summary>A row's figure for one week: what it says, whether it is short or over, and how full it is beside the rest.</summary>
public sealed record TermCell(string Text, string? Note, bool Warn, double Fill, string Title);

/// <summary>
/// The term at a glance for one render: the weeks of the semester the plan is on, side by side, each from
/// <see cref="TermGlance"/>, and the rows of the plan's lens with each week's figure for them.
/// </summary>
public sealed class TermModel
{
    public required IReadOnlyList<TermWeek> Weeks { get; init; }
    public required IReadOnlyList<TermRow> Rows { get; init; }
    public required Semester? Semester { get; init; }
    public required IReadOnlyDictionary<(Guid Row, DateOnly Monday), TermCell> Cells { get; init; }

    /// <summary>What a row's figure is, said once above the rows.</summary>
    public required string Measure { get; init; }

    /// <summary>Guest groups left out of the Groups rows until the plan is asked to show guests, as their classes are on a week.</summary>
    public int HiddenGuestGroups { get; init; }

    public bool Headed => Sections.Headed(Rows.Select(r => r.Section));

    public TermCell? Cell(TermRow r, TermWeek w) => Cells.GetValueOrDefault((r.Id, w.Monday));

    /// <summary>Each row's figure for all the weeks laid out, beside them: the term's hours, or its classes.</summary>
    public required IReadOnlyDictionary<Guid, TermCell> Totals { get; init; }

    public TermCell? Total(TermRow r) => Totals.GetValueOrDefault(r.Id);

    /// <summary>The weeks the term view would lay out for this day, without working anything out.</summary>
    public static IReadOnlyList<DateOnly> WeeksFor(Timetable t, DateOnly day) => TermGlance.WeeksFor(t, day);

    public static TermModel Build(Timetable t, PlanView v, Access access, DateOnly today)
    {
        var weeks = TermGlance.For(t, TermGlance.WeeksFor(t, v.Day), today, access.Events, access.Registers);
        var semester = t.SemesterNear(v.Day);
        var focus = v.Focus;
        var rows = new List<TermRow>();
        var cells = new Dictionary<(Guid, DateOnly), TermCell>();
        var totals = new Dictionary<Guid, TermCell>();
        int hiddenGuests = 0;
        string Weeks(int n) => Cccc.Web.Features.Week.Looks.Count(n, "week");
        string measure;
        string D(TimeSpan x) => Week.Duration(x);

        switch (v.Lens)
        {
            case Lens.Spaces:
            {
                measure = "Hours each space is in use by classes that happen.";
                var max = weeks.SelectMany(w => w.SpaceHours.Values).DefaultIfEmpty(TimeSpan.Zero).Max();
                foreach (var r in t.Rooms.OrderBy(r => r.Category is null).ThenBy(r => r.Category, NameOrder.Comparer).ThenBy(r => r.Name, NameOrder.Comparer))
                {
                    if (focus is { Kind: "room" } && focus.Id != r.Id) continue;
                    if (focus is not null and not { Kind: "room" }) continue;
                    // A former space is a row only where it was used in these weeks.
                    if (r.IsFormer() && focus?.Id != r.Id && !weeks.Any(w => w.SpaceHours.ContainsKey(r.Id))) continue;
                    rows.Add(new TermRow(r.Id, r.Name, r.Colour, Sections.Of(r), "room"));
                    foreach (var w in weeks)
                        if (w.SpaceHours.GetValueOrDefault(r.Id) is var h && h > TimeSpan.Zero)
                            cells[(r.Id, w.Monday)] = new TermCell(D(h), null, false, Fill(h, max), $"{r.Name}, week {Week.IsoNumber(w.Monday)}: {D(h)} in use");
                    var sum = weeks.Aggregate(TimeSpan.Zero, (a, w) => a + w.SpaceHours.GetValueOrDefault(r.Id));
                    if (sum > TimeSpan.Zero) totals[r.Id] = new TermCell(D(sum), null, false, 0, $"{r.Name}: {D(sum)} in use over these {Weeks(weeks.Count)}");
                }
                break;
            }
            case Lens.Groups:
            {
                measure = "Counted hours on each group's timetable; short of the weekly target in orange, and how many students are short.";
                var max = weeks.SelectMany(w => w.GroupHours.Values).DefaultIfEmpty(TimeSpan.Zero).Max();
                if (!v.ShowGuests && focus is null) hiddenGuests = t.Groups.Count(g => t.Programmes.Find(g.ProgrammeId) is { IsGuest: true });
                foreach (var g in t.Groups
                             .Where(g => focus is null || focus.Id == g.Id || g.ProgrammeId == focus.Id)
                             .Where(g => v.ShowGuests || focus is not null || t.Programmes.Find(g.ProgrammeId) is not { IsGuest: true })
                             .OrderBy(g => g.ProgrammeId is null).ThenBy(g => t.Programmes.Find(g.ProgrammeId)?.Name, NameOrder.Comparer).ThenBy(g => g.Name, NameOrder.Comparer))
                {
                    if (focus is { Kind: not ("group" or "programme") }) break;
                    rows.Add(new TermRow(g.Id, g.Name, g.Colour, Sections.Of(t, g), "group"));
                    foreach (var w in weeks)
                    {
                        if (w.Blank) continue;
                        if (w.GroupsClosedAllWeek.Contains(g.Id))
                        {
                            cells[(g.Id, w.Monday)] = new TermCell("Closed", null, false, 0, $"{g.Name}, week {Week.IsoNumber(w.Monday)}: closed all week");
                            continue;
                        }
                        var h = w.GroupHours.GetValueOrDefault(g.Id);
                        var target = Hours.GroupTarget(t, w.Monday, g.Id);
                        int shortOnes = w.GroupStudentsShort.GetValueOrDefault(g.Id);
                        bool under = target is { } goal && h < goal;
                        var note = shortOnes > 0 ? $"{shortOnes} short" : null;
                        var title = $"{g.Name}, week {Week.IsoNumber(w.Monday)}: {D(h)} counted"
                                    + (target is { } tg ? $", target {D(tg)}" : "")
                                    + (shortOnes > 0 ? $"; {Cccc.Web.Features.Week.Looks.Count(shortOnes, "student")} short of it" : "");
                        cells[(g.Id, w.Monday)] = new TermCell(D(h), note, under || shortOnes > 0, Fill(h, max), title);
                    }
                    var counted = weeks.Where(w => cells.ContainsKey((g.Id, w.Monday)) && !w.GroupsClosedAllWeek.Contains(g.Id)).ToList();
                    if (counted.Count > 0)
                    {
                        var sum = counted.Aggregate(TimeSpan.Zero, (a, w) => a + w.GroupHours.GetValueOrDefault(g.Id));
                        int shortWeeks = counted.Count(w => cells[(g.Id, w.Monday)].Warn);
                        totals[g.Id] = new TermCell(D(sum), shortWeeks > 0 ? $"{shortWeeks} short" : null, shortWeeks > 0, 0,
                            $"{g.Name}: {D(sum)} counted over {Weeks(counted.Count)}" + (shortWeeks > 0 ? $", {Weeks(shortWeeks)} short of the target" : ""));
                    }
                }
                break;
            }
            default:
            {
                if (focus is { Kind: "student" } st)
                {
                    bool hours = access.MayUseAdminTools;
                    measure = hours ? "Counted hours each week, against the weekly target." : "How many classes they are on each week.";
                    rows.Add(new TermRow(st.Id, st.Name, null, "Student", "student"));
                    var studentHours = TimeSpan.Zero;
                    int studentClasses = 0, studentShort = 0, studentWeeks = 0;
                    foreach (var w in weeks.Where(w => !w.Blank))
                    {
                        var week = Occurrences.ForWeek(t, w.Monday);
                        var sw = Hours.ForStudent(t, w.Monday, week, st.Id);
                        int on = week.Count(o => o.Happens && Roster.Includes(t, o.Session, st.Id));
                        cells[(st.Id, w.Monday)] = hours
                            ? new TermCell(D(sw.Counted), null, sw.Short, 0, $"{st.Name}, week {Week.IsoNumber(w.Monday)}: {D(sw.Counted)} counted" + (sw.Target is { } tg ? $", target {D(tg)}" : ""))
                            : new TermCell(on.ToString(), null, false, 0, $"{st.Name}, week {Week.IsoNumber(w.Monday)}: {Cccc.Web.Features.Week.Looks.Count(on, "class", "classes")}");
                        studentHours += sw.Counted;
                        studentClasses += on;
                        studentWeeks++;
                        if (sw.Short) studentShort++;
                    }
                    if (studentWeeks > 0)
                        totals[st.Id] = hours
                            ? new TermCell(D(studentHours), studentShort > 0 ? $"{studentShort} short" : null, studentShort > 0, 0,
                                $"{st.Name}: {D(studentHours)} counted over {Weeks(studentWeeks)}" + (studentShort > 0 ? $", {Weeks(studentShort)} short of the target" : ""))
                            : new TermCell(studentClasses.ToString(), null, false, 0, $"{st.Name}: {Cccc.Web.Features.Week.Looks.Count(studentClasses, "class", "classes")} over {Weeks(studentWeeks)}");
                    break;
                }
                // Hours are a person's own and an administrator's to see; anybody else sees how many classes.
                bool anyHours = t.Facilitators.Any(f => access.MayHaveTheWeekOf(f.Id));
                measure = access.MayUseAdminTools
                    ? "Each person's hours, teaching and time rules, as their own week counts them; over their weekly limit in orange."
                    : anyHours ? "How many classes each person is in each week; your own row, your hours." : "How many classes each person is in each week.";
                // Shaded against the hours this reader may see, so a shade says nothing about anybody else's.
                var maxHours = weeks.SelectMany(w => w.PersonHours.Where(x => access.MayHaveTheWeekOf(x.Key)).Select(x => x.Value.Total)).DefaultIfEmpty(TimeSpan.Zero).Max();
                var maxClasses = weeks.SelectMany(w => w.PersonClasses.Values).DefaultIfEmpty(0).Max();
                foreach (var f in t.Facilitators.OrderBy(f => f.IsGuest).ThenBy(f => f.Name, NameOrder.Comparer))
                {
                    if (focus is not null && focus.Id != f.Id) continue;
                    // Nobody needs a row of nothing: a person is listed when they have a class in these weeks, or when asked for.
                    if (focus is null && !weeks.Any(w => w.PersonClasses.ContainsKey(f.Id) || w.PersonHours.ContainsKey(f.Id))) continue;
                    rows.Add(new TermRow(f.Id, f.Name, f.Colour, f.IsGuest ? "Guests" : "Facilitators", "facilitator"));
                    bool theirHours = access.MayHaveTheWeekOf(f.Id);
                    foreach (var w in weeks)
                    {
                        int n = w.PersonClasses.GetValueOrDefault(f.Id);
                        var split = w.PersonHours.GetValueOrDefault(f.Id);
                        if (n == 0 && (split is null || split.Total == TimeSpan.Zero)) continue;
                        var label = $"{f.Name}, week {Week.IsoNumber(w.Monday)}: {Cccc.Web.Features.Week.Looks.Count(n, "class", "classes")}";
                        if (theirHours && split is not null)
                        {
                            bool over = f.MaxWeeklyHours is { } max && split.Total > max;
                            cells[(f.Id, w.Monday)] = new TermCell(D(split.Total), null, over, Fill(split.Total, maxHours),
                                label + $", {D(split.Total)}" + (f.MaxWeeklyHours is { } m ? $" (limit {D(m)})" : ""));
                        }
                        else
                            cells[(f.Id, w.Monday)] = new TermCell(n.ToString(), null, false, maxClasses == 0 ? 0 : (double)n / maxClasses, label);
                    }
                    var theirs = weeks.Where(w => cells.ContainsKey((f.Id, w.Monday))).ToList();
                    if (theirs.Count == 0) continue;
                    if (theirHours)
                    {
                        var sum = theirs.Aggregate(TimeSpan.Zero, (a, w) => a + (w.PersonHours.GetValueOrDefault(f.Id)?.Total ?? TimeSpan.Zero));
                        int overWeeks = theirs.Count(w => cells[(f.Id, w.Monday)].Warn);
                        totals[f.Id] = new TermCell(D(sum), overWeeks > 0 ? $"{overWeeks} over" : null, overWeeks > 0, 0,
                            $"{f.Name}: {D(sum)} over {Weeks(theirs.Count)}" + (overWeeks > 0 ? $", over their limit in {Weeks(overWeeks)}" : ""));
                    }
                    else
                    {
                        int sum = theirs.Sum(w => w.PersonClasses.GetValueOrDefault(f.Id));
                        totals[f.Id] = new TermCell(sum.ToString(), null, false, 0, $"{f.Name}: {Cccc.Web.Features.Week.Looks.Count(sum, "class", "classes")} over {Weeks(theirs.Count)}");
                    }
                }
                break;
            }
        }
        return new TermModel { Weeks = weeks, Rows = rows, Semester = semester, Cells = cells, Totals = totals, Measure = measure, HiddenGuestGroups = hiddenGuests };
    }

    private static double Fill(TimeSpan x, TimeSpan max) => max <= TimeSpan.Zero ? 0 : Math.Clamp(x / max, 0, 1);
}
