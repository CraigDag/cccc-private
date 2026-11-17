using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Shell;
using Cccc.Web.Features.Week;
using DWeek = Cccc.Domain.Week;

namespace Cccc.Web.Features.Screens;

/// <summary>
/// Something in a space for a stretch of today: a class, or an event placed in it. <see cref="Title"/> is
/// what the screen says; an event the corridor may not read about is only "Booked".
/// </summary>
public sealed record Booking(TimeOnly Start, TimeOnly End, string Title, string Detail, string? Colour, bool AllDay = false)
{
    public bool Covers(int minute) => AllDay || (ScreenBoard.Minutes(Start) <= minute && minute < ScreenBoard.Minutes(End));

    /// <summary>How far through it is, 0 to 100, for the bar under the class on now.</summary>
    public int Progress(int minute)
    {
        if (AllDay) return 0;
        int a = ScreenBoard.Minutes(Start), b = ScreenBoard.Minutes(End);
        return Math.Clamp((minute - a) * 100 / Math.Max(1, b - a), 0, 100);
    }
}

/// <summary>One space's line: what is in it now, what comes next today, and everything it has today.</summary>
public sealed record SpaceLine(Room Room, Booking? Now, Booking? Next, IReadOnlyList<Booking> Day)
{
    /// <summary>"until 11:00", "all day", "rest of the day".</summary>
    public string Until => Now is { AllDay: true } ? "all day"
        : Now is { } n ? "until " + DWeek.Hm(n.End)
        : Next is { } x ? "until " + DWeek.Hm(x.Start)
        : Day.Count == 0 ? "all day" : "rest of the day";
}

/// <summary>A class that is not on today, and the one word the corridor is told why.</summary>
public sealed record OffLine(Session Session, string Title, string Why);

/// <summary>A class of today's that changed since the weekday before, in the words a student needs.</summary>
public sealed record ChangeLine(string Title, string When, string What);

/// <summary>Something starting later today, and where: a line of the departures list.</summary>
public sealed record Upcoming(Booking Booking, string Space);

/// <summary>What a screen shows: the building as it is at one minute of one day.</summary>
public sealed record ScreenBoardModel(
    DateOnly Today, int Minute, string? Semester, Closure? Closed,
    IReadOnlyList<(string Section, IReadOnlyList<SpaceLine> Lines)> Sections,
    IReadOnlyList<OffLine> Off, IReadOnlyList<string> ClosedFor, IReadOnlyList<ChangeLine> Changes,
    IReadOnlyList<Booking> Events, IReadOnlyList<Booking> Elsewhere, IReadOnlyList<Upcoming> Coming)
{
    public bool Headed => Sections.Count > 1;

    public IEnumerable<SpaceLine> Lines => Sections.SelectMany(s => s.Lines);

    /// <summary>The spaces free right now, and until when.</summary>
    public IReadOnlyList<SpaceLine> FreeNow => Lines.Where(l => l.Now is null).ToList();
}

/// <summary>
/// What a screen in the corridor shows, decided in one place so the page and its tests read the same
/// answer. A corridor is read by everybody who walks past, visitors too, so it carries only what a
/// printed timetable on the wall would: the class, who it is for, the first names of who is teaching it,
/// and where. Never a student's name, a note, a reason for a cancellation, who is away or covering, or
/// an event the students may not see (that one is only "Booked"). A visiting group's class is on it:
/// hiding guests is about planning a readable week, and a space with a company in it is not free.
/// </summary>
public static class ScreenBoard
{
    /// <summary>The events a corridor may read about: what every student may see.</summary>
    public static readonly EventViewer Corridor = new(SeesEverything: false, IsEditor: false, IsStudent: true, IsFacilitator: false, IsStaff: false);

    /// <summary>How many lines the departures list carries: the next few, not the rest of the day.</summary>
    public const int ComingUp = 8;

    public static int Minutes(TimeOnly t) => t.Hour * 60 + t.Minute;

    public static ScreenBoardModel For(Timetable t, DateOnly today, int minute, IReadOnlyList<ClassChange> changed)
    {
        var (bookings, elsewhere) = BookingsOn(t, today, minute);

        // A former space is on the screen only on a day something is in it.
        var spaces = t.Rooms.Where(r => !r.IsFormer() || bookings.ContainsKey(r.Id))
            .OrderBy(r => r.Category is null).ThenBy(r => r.Category, NameOrder.Comparer).ThenBy(r => r.Name, NameOrder.Comparer);
        var sections = spaces.GroupBy(Shell.Sections.Of)
            .Select(g => (g.Key, (IReadOnlyList<SpaceLine>)g.Select(r => Line(r, bookings.GetValueOrDefault(r.Id) ?? [], minute)).ToList()))
            .ToList();

        var off = Occurrences.Between(t, today, today).Where(o => o.State == OccurrenceState.Cancelled)
            .Select(o => new OffLine(o.Session, Looks.Title(t, o.Session), "cancelled")).ToList();
        var closure = Occurrences.ClosureFor(t, today, null);
        var closedFor = t.Closures.Where(c => c.Scope == ClosureScope.Groups && c.Covers(today) && closure is null)
            .Select(c => (c.GroupIds.Select(g => t.Groups[g]?.Name).Where(n => n is not null).OrderBy(n => n, NameOrder.Comparer).ToList(), c.Name))
            .Where(x => x.Item1.Count > 0)
            .Select(x => $"{string.Join(", ", x.Item1)}: {(x.Name.Trim() is { Length: > 0 } name ? name : "closed")}").ToList();

        var events = EventSchedule.Between(t, today, today, Corridor)
            .Select(o => new Booking(o.IsAllDayStrip ? TimeOnly.MinValue : o.Event.Start, o.IsAllDayStrip ? TimeOnly.MaxValue : o.Event.End,
                Title(o.Event), t.Rooms.Find(o.Event.RoomId)?.Name ?? o.Event.Place.Trim(), null, o.IsAllDayStrip))
            .ToList();

        // The departures list: what starts next anywhere, in time order, for a student who knows their
        // class and not its space. A space's "Booked" says nothing to anybody walking past, so it is left out.
        var coming = bookings.SelectMany(kv => kv.Value.Select(b => new Upcoming(b, t.Rooms[kv.Key]!.Name)))
            .Concat(elsewhere.Select(b => new Upcoming(b, "")))
            .Where(u => !u.Booking.AllDay && Minutes(u.Booking.Start) > minute && u.Booking.Detail.Length > 0)
            .OrderBy(u => u.Booking.Start).ThenBy(u => u.Space.Length == 0).ThenBy(u => u.Space, NameOrder.Comparer)
            .Take(ComingUp).ToList();

        return new ScreenBoardModel(today, minute, t.SemesterFor(today)?.Name, closure, sections, off, closedFor,
            Changes(t, changed, today), events, elsewhere.OrderBy(b => b.Start).ToList(), coming);
    }

    /// <summary>One space's day alone, for a tablet by its door.</summary>
    public static SpaceLine? Space(ScreenBoardModel board, Guid roomId) => board.Lines.FirstOrDefault(l => l.Room.Id == roomId);

    /// <summary>
    /// The next few days after <paramref name="today"/> with something in the space, within a week, and what:
    /// a door sign's lists below today's, so a space with nothing more today still says when it is next in use.
    /// </summary>
    public static IReadOnlyList<(DateOnly Day, IReadOnlyList<Booking> Bookings)> DaysAheadIn(Timetable t, Guid roomId, DateOnly today, int days = 3)
    {
        var found = new List<(DateOnly, IReadOnlyList<Booking>)>();
        for (var day = today.AddDays(1); day <= today.AddDays(7) && found.Count < days; day = day.AddDays(1))
            if (BookingsOn(t, day, -1).InSpaces.GetValueOrDefault(roomId) is { Count: > 0 } list)
                found.Add((day, list.OrderBy(b => b.AllDay ? 0 : 1).ThenBy(b => b.Start).ThenBy(b => b.End).ToList()));
        return found;
    }

    /// <summary>
    /// Everything in each space on one day: the classes that happen (a visiting group's too) and the events
    /// placed in a space, titled when the corridor may read about them and only "Booked" when not. A class
    /// with no space, or one whose space has gone, is elsewhere.
    /// </summary>
    private static (Dictionary<Guid, List<Booking>> InSpaces, List<Booking> Elsewhere) BookingsOn(Timetable t, DateOnly day, int minute)
    {
        var bookings = new Dictionary<Guid, List<Booking>>();
        var elsewhere = new List<Booking>();
        foreach (var o in Occurrences.Between(t, day, day).Where(o => o.Happens))
        {
            var b = new Booking(o.Start, o.End, Looks.Title(t, o.Session), Detail(t, o, minute), Looks.ColourOf(t, o.Session));
            if (o.Session.RoomId is { } room && t.Rooms.Contains(room)) Add(bookings, room, b);
            else elsewhere.Add(b);
        }
        foreach (var e in EventSchedule.On(t, day).Where(e => e.Event.RoomId is { } r && t.Rooms.Contains(r)))
        {
            bool seen = Corridor.Sees(e.Event);
            Add(bookings, e.Event.RoomId!.Value, new Booking(e.From, e.Until, seen ? Title(e.Event) : "Booked", seen ? "Event" : "", null, AllDay: e.AllDay));
        }
        return (bookings, elsewhere);
    }

    private static SpaceLine Line(Room r, List<Booking> day, int minute)
    {
        var ordered = day.OrderBy(b => b.AllDay ? 0 : 1).ThenBy(b => b.Start).ThenBy(b => b.End).ToList();
        return new SpaceLine(r, ordered.FirstOrDefault(b => b.Covers(minute)),
            ordered.FirstOrDefault(b => !b.AllDay && Minutes(b.Start) > minute), ordered);
    }

    private static void Add(Dictionary<Guid, List<Booking>> map, Guid room, Booking b)
    {
        if (!map.TryGetValue(room, out var list)) map[room] = list = [];
        list.Add(b);
    }

    private static string Title(SchoolEvent e) => e.Title.Trim() is { Length: > 0 } title ? title : "Event";

    /// <summary>"BA 1 · Ada and Bo": who it is for, and the first names of who is in the room at this minute.</summary>
    private static string Detail(Timetable t, Occurrence o, int minute)
    {
        var names = o.InTheRoom
            .Where(id => TimeWindows.InRoom(o, id) is var (a, b) && (Minutes(a) <= minute && minute < Minutes(b) || minute < Minutes(o.Start) || minute >= Minutes(o.End)))
            .Select(id => t.Facilitators[id]?.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault())
            .Where(n => n is not null).Select(n => n!).Distinct().OrderBy(n => n, NameOrder.Comparer).ToList();
        var who = names.Count switch { 0 => "", 1 => names[0], _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1] };
        var audience = Looks.Audience(t, o.Session);
        return who.Length == 0 ? audience : audience + " · " + who;
    }

    /// <summary>
    /// What changed about today's classes, as a student reads it: on or off, moved, a new time or space.
    /// Who teaches or covers is left out, since that says who is away, and so is anything cancelled,
    /// which the list of what is off already says.
    /// </summary>
    private static IReadOnlyList<ChangeLine> Changes(Timetable t, IReadOnlyList<ClassChange> changed, DateOnly today)
    {
        var lines = new List<ChangeLine>();
        foreach (var c in changed)
        {
            if (c.Now is not { } now)
            {
                if (c.Was is { } was && was.Date == today) lines.Add(new(Looks.Title(t, was), Looks.When(was), "No longer on today"));
                continue;
            }
            if (now.Cancelled) continue;
            if (c.Was is null)
            {
                if (now.Date == today) lines.Add(new(Looks.Title(t, now), Looks.When(now), "New today" + In(t, now)));
                continue;
            }
            var w = new List<string>();
            if (c.CancelledChanged) w.Add("back on");
            if (c.DayChanged) w.Add(now.Date == today ? "moved here from " + DWeek.ShortDate(c.Was.Date) : "moved to " + DWeek.ShortDate(now.Date));
            if (c.TimesChanged) w.Add("was " + Looks.When(c.Was));
            if (c.SpaceChanged) w.Add(now.RoomId is null ? "no space now" : "now in " + Looks.Room(t, now));
            if (w.Count == 0) continue;
            var what = string.Join(" · ", w);
            lines.Add(new(Looks.Title(t, now), now.Date == today ? Looks.When(now) : Looks.When(c.Was), char.ToUpperInvariant(what[0]) + what[1..]));
        }
        return lines;
    }

    private static string In(Timetable t, Session s) => Looks.Room(t, s) is { Length: > 0 } r ? ", in " + r : "";
}
