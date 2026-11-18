using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Shell;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Features.Plan;

/// <summary>What the rows of the plan are: the question being asked of the timetable.</summary>
public enum Lens { Spaces, Groups, People }

/// <summary>How much of the timetable the plan shows: a day, a week, or the weeks of a semester side by side.</summary>
public enum Scale { Day, Week, Term }

/// <summary>A thing the plan is narrowed to, from Find: a person's day, a space's day, a group's day.</summary>
public sealed record Focus(string Kind, Guid Id, string Name);

/// <summary>
/// One tab's way of looking at the plan. None of it is the project's: it is never saved, undone or seen
/// by anybody else. Shared between Now, Plan and Find within the tab, so Find can move the plan.
/// </summary>
public sealed class PlanView
{
    public DateOnly Day { get; private set; }
    public bool Started { get; private set; }
    public Lens Lens { get; set; } = Lens.Spaces;
    public Scale Scale { get; set; } = Scale.Day;
    public Focus? Focus { get; private set; }
    public Guid? Chosen { get; set; }
    /// <summary>Guest classes start hidden on every visit and that is remembered nowhere.</summary>
    public bool ShowGuests { get; set; }
    /// <summary>Open training is shown until somebody switches it off; it is the class's question, not the audience's.</summary>
    public bool ShowOpen { get; set; } = true;
    public bool IssuesOpen { get; set; }

    public event Action? Moved;

    public DateOnly Monday => Cccc.Domain.Week.MondayOf(Day);

    private DateOnly _realDay;

    /// <summary>
    /// Whether the tab is looking at the Scratch Week, whose days are dated in
    /// <see cref="Cccc.Domain.Week.ScratchMonday"/>, so the plan draws and edits it as it does any week.
    /// </summary>
    public bool InScratch => Cccc.Domain.Week.IsScratch(Day);

    /// <summary>
    /// The day on the real timetable: the one the tab was on before it went into the Scratch Week. For
    /// anything that must show a real week (Cover, Registers, a new event) and for going back.
    /// </summary>
    public DateOnly RealDay => InScratch ? _realDay : Day;

    public DateOnly RealMonday => Cccc.Domain.Week.MondayOf(RealDay);

    /// <summary>Into the Scratch Week, on the same day of the week the tab was on.</summary>
    public void OpenScratch() => GoTo(Cccc.Domain.Week.DateOf(Cccc.Domain.Week.ScratchMonday, Day.DayOfWeek));

    /// <summary>Back to the real day the tab came from.</summary>
    public void CloseScratch()
    {
        if (InScratch) GoTo(_realDay);
    }

    public void Start(DateOnly today)
    {
        if (Started) return;
        Day = today;
        Started = true;
    }

    public void GoTo(DateOnly day)
    {
        if (day == Day) return;
        if (Cccc.Domain.Week.IsScratch(day) && !InScratch) _realDay = Day;
        if (Cccc.Domain.Week.MondayOf(day) != Monday) Chosen = null;
        Day = day;
        Moved?.Invoke();
    }

    public void FocusOn(Focus? f)
    {
        Focus = f;
        Chosen = null;
        if (f is not null)
            Lens = f.Kind switch { "room" => Lens.Spaces, "group" or "programme" => Lens.Groups, _ => Lens.People };
        Moved?.Invoke();
    }

    public void Changed() => Moved?.Invoke();
}

/// <summary>One row of the plan: a space, a group or a person.</summary>
public sealed record PlanRow(string Key, Guid? Id, string Name, string? Colour, string Section, string? Note = null);

/// <summary>
/// A shaded stretch of a row's day behind a chosen class: the stretches its people, groups or space are
/// elsewhere that run into each other, drawn as one. Start and End are minutes from midnight.
/// </summary>
public sealed record BusyBand(int Start, int End, IReadOnlyList<BusyStretch> Stretches)
{
    public bool Away => Stretches.Any(s => s.Kind == BusyKind.Away);
}

/// <summary>A class drawn on a row. Start and End are minutes from midnight; for a person, their part of it.</summary>
public sealed record PlanBar(Occurrence O, string RowKey, DateOnly Date, int Start, int End, bool Part)
{
    public int Lane { get; init; }
}

/// <summary>Everything one render of the plan needs, worked out once from the domain's own rules.</summary>
public sealed class PlanModel
{
    public required IReadOnlyList<PlanRow> Rows { get; init; }
    public required IReadOnlyList<PlanBar> Bars { get; init; }
    /// <summary>How many lanes each row needs on each day: overlapping bars on one row stack.</summary>
    public required IReadOnlyDictionary<(string Row, DateOnly Date), int> Lanes { get; init; }
    public required IReadOnlyList<Occurrence> Week { get; init; }
    public required IReadOnlyList<DateOnly> Days { get; init; }
    public required int FrameStart { get; init; }
    public required int FrameEnd { get; init; }
    public required IReadOnlyList<Clash> Clashes { get; init; }
    public required IReadOnlySet<Guid> Ringed { get; init; }
    public required IReadOnlyList<Occurrence> CoverNeeded { get; init; }
    public required IReadOnlyList<Occurrence> RegistersToFile { get; init; }
    public required IReadOnlyList<EventOccurrence> Events { get; init; }
    public required bool Locked { get; init; }

    /// <summary>The timetable this plan was drawn from: the real one, or the Scratch Week's view of it.</summary>
    public required Timetable T { get; init; }

    /// <summary>Whether this is the Scratch Week: a plan that counts for nothing, with no dates.</summary>
    public bool Scratch { get; init; }

    /// <summary>An edit made on this plan, written back where it was drawn from: the Scratch Week's to the Scratch Week.</summary>
    public Func<Timetable, Batch> Edit(Func<Timetable, Batch> edit) => Scratch ? t => ScratchWeek.Edit(t, edit) : edit;

    /// <summary>"Monday 7", or "Monday" in the Scratch Week, which has no dates.</summary>
    public string DayName(DateOnly d, string format = "dddd d") =>
        d.ToString(Scratch ? format.Replace(" d", "") : format, System.Globalization.CultureInfo.InvariantCulture);
    /// <summary>
    /// Narrowed to one person, space or group, a day is a single short row; so the rows become the
    /// days of the week instead, and the timeline reads as that one thing's week.
    /// </summary>
    public bool RowsAreDays { get; init; }

    /// <summary>Whether the rows are drawn under headings: only when they fall in more than one part.</summary>
    public bool Headed => Sections.Headed(Rows.Select(r => r.Section));
    public required int HiddenGuests { get; init; }

    /// <summary>
    /// With a class chosen on a day, when its people, groups and space are elsewhere (<see cref="Busy"/>), by
    /// day: its own day, or each day of a week read as rows of days, where it may be dragged onto another one.
    /// Empty otherwise.
    /// </summary>
    public IReadOnlyDictionary<DateOnly, IReadOnlyList<BusyStretch>> Elsewhere { get; init; } = new Dictionary<DateOnly, IReadOnlyList<BusyStretch>>();

    /// <summary>
    /// How hard each group's day is on the body (<see cref="HardTraining"/>), by row and day: on the rows that are
    /// groups, or the days of the one group narrowed to. Empty for spaces and people.
    /// </summary>
    public IReadOnlyDictionary<(string Row, DateOnly Date), HardDay> Hard { get; init; } = new Dictionary<(string, DateOnly), HardDay>();

    /// <summary>The week's warnings about hard training, beside the classes they are about; never a ring.</summary>
    public IReadOnlyList<Clash> HardWarnings { get; init; } = [];

    /// <summary>The group's hard day on this row, or none: a row that is not a group's, or a day with no hard training.</summary>
    public HardDay? HardOn(string row, DateOnly day) =>
        Hard.TryGetValue((row, day), out var d) && d.Stretches.Count > 0 ? d : null;

    /// <summary>
    /// The shading for a day behind the chosen class: its stretches run together into bands, cut to the frame.
    /// Its space's stretches only <paramref name="withSpace"/>: on rows that are spaces, the space's own
    /// classes are already drawn where it is in use.
    /// </summary>
    public IReadOnlyList<BusyBand> Bands(DateOnly day, bool withSpace)
    {
        if (!Elsewhere.TryGetValue(day, out var stretches)) return [];
        var bands = new List<BusyBand>();
        int start = 0, end = -1;
        var run = new List<BusyStretch>();
        foreach (var b in stretches.Where(b => withSpace || b.Kind != BusyKind.Space).OrderBy(b => b.From).ThenBy(b => b.Until))
        {
            int a = Math.Max(FrameStart, Minutes(b.From)), z = Math.Min(FrameEnd, Until(b.Until));
            if (z <= a) continue;
            if (run.Count > 0 && a > end)
            {
                bands.Add(new BusyBand(start, end, run));
                run = [];
            }
            if (run.Count == 0) start = a;
            end = Math.Max(end, z);
            run.Add(b);
        }
        if (run.Count > 0) bands.Add(new BusyBand(start, end, run));
        return bands;
    }

    /// <summary>The end of a stretch in minutes: the end of the day for one that runs to midnight.</summary>
    public static int Until(TimeOnly t) => t == TimeOnly.MaxValue ? 24 * 60 : Minutes(t);

    public Occurrence? Find(Guid id) => Week.FirstOrDefault(o => o.Session.Id == id);

    /// <summary>
    /// The class a clash in the list chooses: the first of its classes not started yet, since only that one has ways
    /// out (<see cref="WaysOut.StillAhead"/>); its first when they have all started.
    /// </summary>
    public Guid ToChoose(Clash c, DateOnly today, TimeOnly now) =>
        c.SessionIds.FirstOrDefault(id => Find(id) is { } o && WaysOut.StillAhead(o.Session, today, now), c.SessionIds[0]);

    /// <summary>The other classes in a class's clashes that have not started: any of them could move instead of it.</summary>
    public IReadOnlyList<Occurrence> CouldMoveInstead(Guid id, DateOnly today, TimeOnly now) =>
        Clashes.Where(c => c.SessionIds.Contains(id)).SelectMany(c => c.SessionIds).Where(x => x != id).Distinct()
            .Select(Find).OfType<Occurrence>().Where(o => WaysOut.StillAhead(o.Session, today, now)).ToList();

    public int LanesOf(string row, DateOnly date) => Lanes.TryGetValue((row, date), out var n) ? n : 1;

    public int IssueCount(DateOnly? day) =>
        Clashes.Count(c => day is null || c.SessionIds.Any(id => Find(id)?.Date == day))
        + CoverNeeded.Count(o => day is null || o.Date == day)
        + RegistersToFile.Count(o => day is null || o.Date == day);

    public static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    public const string NoSpace = "none";

    public static string DayKey(DateOnly d) => "day-" + d.ToString("yyyy-MM-dd");

    public static DateOnly? FromDayKey(string key) =>
        key.StartsWith("day-", StringComparison.Ordinal) && DateOnly.TryParseExact(key[4..], "yyyy-MM-dd", out var d) ? d : null;

    public static PlanModel Build(Timetable t, PlanView v, Access access, DateOnly today)
    {
        var monday = v.Monday;
        // The Scratch Week is drawn from its own view: its classes in place of the real ones, nothing dated.
        // Somebody who may not see it is drawn an empty one, whatever the tab was left on.
        bool scratch = Cccc.Domain.Week.IsScratch(monday);
        if (scratch) t = ScratchWeek.View(access.MaySeeScratch ? t : t with { Scratch = EntitySet<Session>.Empty });
        var week = Occurrences.ForWeek(t, monday);
        var focus = v.Focus;
        bool AskedForGuests(Session s) => focus is not null && (focus.Kind is "facilitator" or "student"
            || s.Audience.Cohorts.Any(g => focus.Id == g || t.Groups[g]?.ProgrammeId == focus.Id));
        bool Drawn(Occurrence o) =>
            (v.ShowOpen || Hours.WorkloadOf(t, o.Session) != Workload.DropIn)
            && (v.ShowGuests || !Guests.IsGuestOnly(t, o.Session) || AskedForGuests(o.Session));
        var drawn = week.Where(Drawn).ToList();
        int hiddenGuests = week.Count(o => !v.ShowGuests && Guests.IsGuestOnly(t, o.Session) && !drawn.Contains(o));
        bool rowsAreDays = v.Scale == Scale.Day && focus is { Kind: "room" or "facilitator" or "student" or "group" };
        var range = v.Scale == Scale.Day && !rowsAreDays ? drawn.Where(o => o.Date == v.Day).ToList() : drawn;

        var rows = new List<PlanRow>();
        var bars = new List<PlanBar>();
        switch (v.Lens)
        {
            case Lens.Spaces:
                foreach (var r in t.Rooms.OrderBy(r => r.Category is null).ThenBy(r => r.Category, NameOrder.Comparer).ThenBy(r => r.Name, NameOrder.Comparer))
                    // A former space is a row only where it was used, or when somebody asked for it.
                    if ((focus is not { Kind: "room" } || focus.Id == r.Id)
                        && (!r.IsFormer() || focus?.Id == r.Id || range.Any(o => o.Session.RoomId == r.Id)))
                        rows.Add(new PlanRow(r.Id.ToString(), r.Id, r.Name, r.Colour, Sections.Of(r)));
                if (focus is null && range.Any(o => o.Session.RoomId is null || t.Rooms[o.Session.RoomId.Value] is null))
                    rows.Add(new PlanRow(NoSpace, null, "No space given", null, Sections.OtherSpaces));
                foreach (var o in range)
                {
                    var key = o.Session.RoomId is { } rid && t.Rooms[rid] is not null ? rid.ToString() : NoSpace;
                    bars.Add(new PlanBar(o, key, o.Date, Minutes(o.Start), Minutes(o.End), false));
                }
                break;

            case Lens.Groups:
                foreach (var g in t.Groups
                             .Where(g => focus is null || focus.Id == g.Id || g.ProgrammeId == focus.Id)
                             .Where(g => v.ShowGuests || focus is not null || t.Programmes.Find(g.ProgrammeId) is not { IsGuest: true })
                             .OrderBy(g => g.ProgrammeId is null).ThenBy(g => t.Programmes.Find(g.ProgrammeId)?.Name, NameOrder.Comparer).ThenBy(g => g.Name, NameOrder.Comparer))
                    rows.Add(new PlanRow(g.Id.ToString(), g.Id, g.Name, g.Colour, Sections.Of(t, g)));
                // A whole-building class is on for every group, so it sits on every group's row.
                foreach (var o in range)
                    foreach (var row in rows)
                        if (o.Session.Audience.IsFor(row.Id!.Value))
                            bars.Add(new PlanBar(o, row.Key, o.Date, Minutes(o.Start), Minutes(o.End), false));
                break;

            case Lens.People:
                if (focus is { Kind: "student" } st)
                {
                    rows.Add(new PlanRow(st.Id.ToString(), st.Id, st.Name, null, "Student"));
                    foreach (var o in range.Where(o => Roster.Includes(t, o.Session, st.Id)))
                        bars.Add(new PlanBar(o, st.Id.ToString(), o.Date, Minutes(o.Start), Minutes(o.End), false));
                    break;
                }
                var people = t.Facilitators.Where(f => focus is null || focus.Id == f.Id).OrderBy(f => f.Name, NameOrder.Comparer).ToList();
                foreach (var f in people)
                {
                    var key = f.Id.ToString();
                    foreach (var o in range.Where(o => o.Session.FacilitatorIds.Contains(f.Id) || o.Session.CoverIds.Contains(f.Id)))
                    {
                        // A person's row draws their part of a class, as their own week does.
                        var (a, b) = o.InTheRoom.Contains(f.Id) ? TimeWindows.InRoom(o, f.Id) : (o.Start, o.End);
                        bars.Add(new PlanBar(o, key, o.Date, Minutes(a), Minutes(b), a != o.Start || b != o.End));
                    }
                    // Nobody needs thirty empty rows: a person with nothing on is listed only when asked for.
                    if (focus is not null || bars.Any(x => x.RowKey == key))
                        rows.Add(new PlanRow(key, f.Id, f.Name, f.Colour, f.IsGuest ? "Guests" : "Facilitators"));
                }
                break;
        }

        // A bar only counts on a row that is drawn: narrowed to one space, the other spaces' classes go.
        var drawnRows = rows.Select(r => r.Key).ToHashSet();
        bars = bars.Where(b => drawnRows.Contains(b.RowKey)).ToList();
        var days = Cccc.Domain.Week.Days(monday).Where(d =>
            d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) || week.Any(o => o.Date == d)).ToList();
        if (rowsAreDays)
        {
            // One focused row becomes one row per day; a bar keeps its own date, which is now its row.
            var name = rows.FirstOrDefault()?.Name ?? focus!.Name;
            var colour = rows.FirstOrDefault()?.Colour;
            rows = days.Select(d => new PlanRow(DayKey(d), null, d.ToString(scratch ? "dddd" : "dddd d", System.Globalization.CultureInfo.InvariantCulture), colour, name)).ToList();
            bars = bars.Select(b => b with { RowKey = DayKey(b.Date) }).ToList();
        }

        var lanes = new Dictionary<(string, DateOnly), int>();
        var laid = new List<PlanBar>();
        foreach (var grp in bars.GroupBy(b => (b.RowKey, b.Date)))
        {
            var ends = new List<int>();
            foreach (var b in grp.OrderBy(b => b.Start).ThenByDescending(b => b.End))
            {
                int lane = ends.FindIndex(e => e <= b.Start);
                if (lane < 0) { lane = ends.Count; ends.Add(b.End); } else ends[lane] = b.End;
                laid.Add(b with { Lane = lane });
            }
            lanes[grp.Key] = Math.Max(1, ends.Count);
        }

        // The frame is the WEEK's, never the filtered day's, so stepping a day does not rescale the grid.
        var events = EventSchedule.Between(t, monday, monday.AddDays(6), access.Events);
        var (fs, fe) = GridFrame.FittedTo(week.Select(o => (Minutes(o.Start), Minutes(o.End)))
            .Concat(events.Where(e => !e.Event.AllDay).Select(e => (Minutes(e.Event.Start), Minutes(e.Event.End)))));
        days = days.Union(Cccc.Domain.Week.Days(monday).Where(d => events.Any(e => e.Covers(d)))).Order().ToList();
        var clashes = Cccc.Domain.Rules.Clashes.ForWeek(t, monday);
        // A group's hard training sits under its own row: each group on its row, or the one group's days.
        var hard = new Dictionary<(string, DateOnly), HardDay>();
        if (v.Lens == Lens.Groups && v.Scale != Scale.Term)
        {
            if (rowsAreDays && focus is { Kind: "group" } fg)
                foreach (var row in rows)
                {
                    if (FromDayKey(row.Key) is { } d) hard[(row.Key, d)] = HardTraining.For(t, week, fg.Id, d);
                }
            else
                foreach (var row in rows.Where(r => r.Id is not null))
                    foreach (var d in v.Scale == Scale.Day ? [v.Day] : days)
                        hard[(row.Key, d)] = HardTraining.For(t, week, row.Id!.Value, d);
        }
        var elsewhere = new Dictionary<DateOnly, IReadOnlyList<BusyStretch>>();
        if (v.Scale == Scale.Day && v.Chosen is { } chosenId && week.FirstOrDefault(o => o.Session.Id == chosenId) is { } chosen)
        {
            IEnumerable<DateOnly> on = rowsAreDays ? days : chosen.Date == v.Day ? [v.Day] : [];
            foreach (var d in on) elsewhere[d] = Busy.For(t, chosen.Session, d, access.Events);
        }
        return new PlanModel
        {
            Rows = rows,
            RowsAreDays = rowsAreDays,
            Bars = laid,
            Lanes = lanes,
            Week = week,
            Days = days,
            FrameStart = fs,
            FrameEnd = fe,
            Clashes = clashes.Where(c => c.IsRing).ToList(),
            Ringed = Cccc.Domain.Rules.Clashes.RingedSessions(clashes),
            CoverNeeded = Cover.StillNeeded(t, monday, today),
            RegistersToFile = Registers.FileableBy(Registers.MissingInWeek(t, monday, today), access.Registers),
            Events = events,
            Locked = Cccc.Domain.Week.HasEnded(monday, today),
            HiddenGuests = hiddenGuests,
            Elsewhere = elsewhere,
            Hard = hard,
            HardWarnings = clashes.Where(c => c.IsHardTraining).ToList(),
            T = t,
            Scratch = scratch,
        };
    }
}
