using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Features.Week;

using Week = Cccc.Domain.Week;

public enum WeekShape { Calendar, List }

/// <summary>
/// One tab's way of looking at the week: which week, what it is narrowed to, what is chosen. None of
/// it is the project's, so none of it is saved, undone or seen by anybody else. The week is kept in the
/// tab's sessionStorage so a reload comes back to it.
/// </summary>
public sealed class WeekView
{
    public DateOnly Monday { get; private set; }
    public bool Started { get; private set; }
    public WeekShape Shape { get; set; } = WeekShape.Calendar;
    public string Search { get; set; } = "";
    public HashSet<Guid> Programmes { get; } = [];
    public HashSet<Guid> Groups { get; } = [];
    public HashSet<Guid> Rooms { get; } = [];
    public HashSet<Guid> Facilitators { get; } = [];
    public HashSet<Guid> Students { get; } = [];
    /// <summary>Open sessions (open training) are shown on every visit until somebody unticks them.</summary>
    public bool ShowOpen { get; set; } = true;
    /// <summary>Guest classes start hidden on every visit and that is remembered nowhere.</summary>
    public bool ShowGuests { get; set; }
    public bool ShowEvents { get; set; } = true;
    public HashSet<Guid> Chosen { get; } = [];
    /// <summary>The day the narrow (phone) list shows.</summary>
    public DateOnly? Day { get; set; }

    public event Action? Moved;

    public void Start(DateOnly today, DateOnly? remembered)
    {
        if (Started) return;
        Monday = Week.MondayOf(remembered ?? today);
        Started = true;
    }

    public void GoTo(DateOnly anyDate)
    {
        var m = Week.MondayOf(anyDate);
        if (m == Monday) return;
        Monday = m;
        Chosen.Clear();
        Day = null;
        Moved?.Invoke();
    }

    public void Changed() => Moved?.Invoke();

    public bool AnyFilter => Search.Trim().Length > 0 || Programmes.Count + Groups.Count + Rooms.Count + Facilitators.Count + Students.Count > 0;

    /// <summary>Filtered to exactly one person, the week is drawn as that person's: no lanes.</summary>
    public bool OnePerson => Facilitators.Count + Students.Count == 1 && Programmes.Count + Groups.Count == 0;

    public void ClearFilters()
    {
        Search = "";
        Programmes.Clear(); Groups.Clear(); Rooms.Clear(); Facilitators.Clear(); Students.Clear();
        Moved?.Invoke();
    }

    /// <summary>
    /// Whether a session is drawn. Every filter kind narrows (AND), and within a kind any value matches
    /// (OR). An Everyone session matches every group, because it is on for that group too. Hiding is
    /// drawing, never counting.
    /// </summary>
    public bool Shows(Timetable t, Session s)
    {
        if (!ShowOpen && Hours.WorkloadOf(t, s) == Workload.DropIn) return false;
        if (Programmes.Count > 0 && !Programmes.Any(p => s.Audience.ProgrammeId == p || t.GroupsOf(p).Any(g => s.Audience.IsFor(g.Id)))) return false;
        if (Groups.Count > 0 && !Groups.Any(s.Audience.IsFor)) return false;
        if (Rooms.Count > 0 && !(s.RoomId is { } r && Rooms.Contains(r))) return false;
        if (Facilitators.Count > 0 && !Facilitators.Any(f => s.FacilitatorIds.Contains(f) || s.CoverIds.Contains(f))) return false;
        if (Students.Count > 0 && !Students.Any(st => Roster.Includes(t, s, st))) return false;
        if (Search.Trim() is { Length: > 0 } q && !Matches(t, s, q)) return false;
        if (!ShowGuests && Guests.IsGuestOnly(t, s) && !AskedForGuests(t, s)) return false;
        return true;
    }

    /// <summary>A programme or group filter naming the guests, or a person filter for one of their own classes, shows them.</summary>
    private bool AskedForGuests(Timetable t, Session s) =>
        s.Audience.Cohorts.Any(g => Groups.Contains(g) || (t.Groups[g]?.ProgrammeId is { } p && Programmes.Contains(p)))
        || Facilitators.Count > 0 || Students.Count > 0;

    private static bool Matches(Timetable t, Session s, string q)
    {
        bool Has(string? x) => x is not null && x.Contains(q, StringComparison.CurrentCultureIgnoreCase);
        return Has(s.Title) || Has(t.Classes.Find(s.ClassId)?.Name) || Has(t.Rooms.Find(s.RoomId)?.Name)
               || s.FacilitatorIds.Concat(s.CoverIds).Any(f => Has(t.Facilitators[f]?.Name))
               || s.Audience.Cohorts.Any(g => Has(t.Groups[g]?.Name)) || Has(s.Notes);
    }

    public IEnumerable<(string Kind, Guid Id, string Name)> Chips(Timetable t)
    {
        foreach (var id in Programmes) yield return ("programme", id, t.Programmes[id]?.Name ?? "A programme");
        foreach (var id in Groups) yield return ("group", id, t.Groups[id]?.Name ?? "A group");
        foreach (var id in Rooms) yield return ("room", id, t.Rooms[id]?.Name ?? "A room");
        foreach (var id in Facilitators) yield return ("facilitator", id, t.Facilitators[id]?.Name ?? "A facilitator");
        foreach (var id in Students) yield return ("student", id, t.Students[id]?.Name ?? "A student");
    }

    public void Remove(string kind, Guid id)
    {
        (kind switch
        {
            "programme" => Programmes, "group" => Groups, "room" => Rooms, "facilitator" => Facilitators, _ => Students,
        }).Remove(id);
        Moved?.Invoke();
    }

    /// <summary>Words for the sheet: what this week was narrowed to.</summary>
    public string Describe(Timetable t)
    {
        var parts = Chips(t).Select(c => c.Name).ToList();
        if (Search.Trim().Length > 0) parts.Add($"“{Search.Trim()}”");
        if (!ShowOpen) parts.Add("no open sessions");
        if (ShowGuests) parts.Add("with guests");
        return parts.Count == 0 ? "Whole school" : string.Join(", ", parts);
    }
}

/// <summary>Everything one render of the week needs, worked out once.</summary>
public sealed class WeekModel
{
    public required DateOnly Monday { get; init; }
    public required IReadOnlyList<DateOnly> Days { get; init; }
    public required IReadOnlyList<Occurrence> All { get; init; }
    public required IReadOnlyList<Occurrence> Visible { get; init; }
    public required IReadOnlyList<Placed> Placed { get; init; }
    public required int FrameStart { get; init; }
    public required int FrameEnd { get; init; }
    public required IReadOnlyList<Clash> Clashes { get; init; }
    public required IReadOnlySet<Guid> Ringed { get; init; }
    public required IReadOnlyList<Occurrence> CoverNeeded { get; init; }
    public required IReadOnlyList<Occurrence> RegistersToFile { get; init; }
    public required IReadOnlyList<EventOccurrence> Events { get; init; }
    public required bool Locked { get; init; }
    public required int HiddenGuests { get; init; }
    /// <summary>When the week is one facilitator's, whose: their part of a class is drawn, and not dragged.</summary>
    public required Guid? PartOf { get; init; }

    public Occurrence? Find(Guid id) => All.FirstOrDefault(o => o.Session.Id == id);

    public static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    public static WeekModel Build(Timetable t, WeekView v, Access access, DateOnly today, Func<Session, bool>? also = null, Guid? forPerson = null)
    {
        var monday = v.Monday;
        var all = Occurrences.ForWeek(t, monday);
        var visible = all.Where(o => v.Shows(t, o.Session) && (also?.Invoke(o.Session) ?? true)).ToList();
        int hiddenGuests = v.ShowGuests ? 0 : all.Count(o => Guests.IsGuestOnly(t, o.Session) && !visible.Contains(o));
        var events = v.ShowEvents ? EventSchedule.Between(t, monday, monday.AddDays(6), access.Events) : [];

        // Filtered to one facilitator, the week is that person's: their part of a class is what is drawn.
        Guid? part = forPerson ?? (v.OnePerson && v.Facilitators.Count == 1 ? v.Facilitators.First() : null);
        var items = visible.Select(o =>
        {
            if (part is not { } f || !o.InTheRoom.Contains(f)) return LaneItem.Of(o.Session);
            var (a, b) = Windows.InRoom(o, f);
            return new LaneItem(o.Session.Id, o.Date, Minutes(a), Minutes(b), o.Session.Audience);
        }).ToList();
        var lanes = Lanes.ForWeek(items, t, v.OnePerson || forPerson is not null);
        var placed = Lanes.Place(items, lanes);

        // The frame is asked the WEEK's rows, never the filtered ones, so narrowing does not rescale the grid.
        var spans = all.Select(o => (Minutes(o.Start), Minutes(o.End)))
            .Concat(events.Where(e => !e.Event.AllDay).Select(e => (Minutes(e.Event.Start), Minutes(e.Event.End))));
        var (fs, fe) = GridFrame.FittedTo(spans);

        var days = Cccc.Domain.Week.Days(monday).Where(d =>
            d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)
            || visible.Any(o => o.Date == d) || events.Any(e => e.Covers(d))).ToList();

        var clashes = Cccc.Domain.Rules.Clashes.ForWeek(t, monday);
        var missing = Registers.MissingInWeek(t, monday, today);
        return new WeekModel
        {
            Monday = monday,
            Days = days,
            All = all,
            Visible = visible,
            Placed = placed,
            FrameStart = fs,
            FrameEnd = fe,
            Clashes = clashes.Where(c => c.Kind != ClashKind.NeedsCover).ToList(),
            Ringed = Cccc.Domain.Rules.Clashes.RingedSessions(clashes),
            CoverNeeded = Cover.StillNeeded(t, monday, today),
            RegistersToFile = Registers.FileableBy(missing, access.Registers),
            Events = events,
            Locked = Cccc.Domain.Week.HasEnded(monday, today),
            HiddenGuests = hiddenGuests,
            PartOf = part,
        };
    }
}
