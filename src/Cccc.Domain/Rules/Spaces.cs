using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>A piece a class needs that a space lacks: not there at all, or there and out of use (<see cref="Out"/>).</summary>
public sealed record Lack(Equipment Piece, OutOfUse? Out);

/// <summary>
/// Which spaces are in use, and by what: the SPACE and WHEN of the sentence the app exists for, asked
/// from the space's side. The add form's busy marks and free chips read it.
/// </summary>
public static class Spaces
{
    /// <summary>
    /// Each space in use at any moment between <paramref name="start"/> and <paramref name="end"/> on
    /// <paramref name="date"/>, by the earliest class in it then. A class that ends as another starts does
    /// not overlap it. Cancelled and closed classes leave the space free; a guest class does not, because
    /// hiding is drawing, never counting. <paramref name="except"/> is the class being edited, which never
    /// makes its own space look busy.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Occurrence> InUse(Timetable t, DateOnly date, TimeOnly start, TimeOnly end, Guid? except = null) =>
        InUse(Occurrences.Between(t, date, date), start, end, except);

    /// <summary>The same, over one day's occurrences (<see cref="Occurrences.Between"/>), for a caller asking about many times of that day.</summary>
    public static IReadOnlyDictionary<Guid, Occurrence> InUse(IReadOnlyList<Occurrence> day, TimeOnly start, TimeOnly end, Guid? except = null)
    {
        var busy = new Dictionary<Guid, Occurrence>();
        if (end <= start) return busy;
        foreach (var (room, o) in Uses(day, except))
            if (o.Start < end && start < o.End)
                busy.TryAdd(room, o);
        return busy;
    }

    /// <summary>
    /// Each class in a space over one day's occurrences, earliest first, <paramref name="except"/> aside: what
    /// <see cref="InUse"/> asks about a time and <see cref="Busy"/> lays out.
    /// </summary>
    public static IEnumerable<(Guid Room, Occurrence O)> Uses(IReadOnlyList<Occurrence> day, Guid? except = null)
    {
        foreach (var o in day.OrderBy(o => o.Start))
            if (o.Session.Id != except && o.Happens && o.Session.RoomId is { } room)
                yield return (room, o);
    }

    /// <summary>
    /// Each space an event is in at any moment between <paramref name="start"/> and <paramref name="end"/> on
    /// <paramref name="date"/>, by the earliest such event. Every event counts, whoever may read it: the space
    /// is booked either way, and a reader who may not see the event is told only that it is booked.
    /// </summary>
    public static IReadOnlyDictionary<Guid, EventOnDay> Booked(Timetable t, DateOnly date, TimeOnly start, TimeOnly end) =>
        Booked(t, EventSchedule.On(t, date), start, end);

    /// <summary>The same, over one day's events (<see cref="EventSchedule.On"/>), for a caller asking about many times of that day.</summary>
    public static IReadOnlyDictionary<Guid, EventOnDay> Booked(Timetable t, IReadOnlyList<EventOnDay> day, TimeOnly start, TimeOnly end)
    {
        var booked = new Dictionary<Guid, EventOnDay>();
        if (end <= start) return booked;
        foreach (var (room, e) in Bookings(t, day))
            if (e.Overlaps(start, end))
                booked.TryAdd(room, e);
        return booked;
    }

    /// <summary>
    /// What a class needs (<see cref="Class.Needs"/>) that a space lacks on a day, in the order a person reads
    /// names: what the space does not have (<see cref="Room.Has"/>), and what it has that is out of use that day
    /// (<see cref="OutOn"/>). Nothing for a class that needs nothing, a session with no class, or no space: a
    /// class with nowhere given is not in the wrong place, only in none. A piece of equipment the school has
    /// since taken off its list is needed by nobody.
    /// </summary>
    public static IReadOnlyList<Equipment> Lacking(Timetable t, Guid? classId, Guid? roomId, DateOnly on) => [.. Lacks(t, classId, roomId, on).Select(l => l.Piece)];

    /// <summary>
    /// The same, each piece with the spell it is out of use when the space has it (<see cref="Lack.Out"/>), for
    /// saying which it is: "lacks Crash mats" is a different job from "its Crash mats are out of use until Friday".
    /// </summary>
    public static IReadOnlyList<Lack> Lacks(Timetable t, Guid? classId, Guid? roomId, DateOnly on) => LackingOn(t, classId, roomId, on);

    /// <summary>What this class needs that its own space lacks on its day.</summary>
    public static IReadOnlyList<Equipment> Lacking(Timetable t, Session s) => Lacking(t, s.ClassId, s.RoomId, s.Date);

    /// <summary>Whether a space has everything the class needs, in use, on a day: the spaces a way out and an answer may offer it then.</summary>
    public static bool Suits(Timetable t, Guid? classId, Guid roomId, DateOnly on) => Lacking(t, classId, roomId, on).Count == 0;

    /// <summary>
    /// What a class needs that a space does not have at all, whatever is out of use: the spaces Find a time looks
    /// in over many days, each day then asking <see cref="Suits"/>.
    /// </summary>
    public static IReadOnlyList<Equipment> Missing(Timetable t, Guid? classId, Guid? roomId) => [.. LackingOn(t, classId, roomId, null).Select(l => l.Piece)];

    /// <summary>Whether a space has everything a class needs, whatever is out of use (<see cref="Missing"/>).</summary>
    public static bool HasAll(Timetable t, Guid? classId, Guid roomId) => Missing(t, classId, roomId).Count == 0;

    private static IReadOnlyList<Lack> LackingOn(Timetable t, Guid? classId, Guid? roomId, DateOnly? on)
    {
        if (t.Classes.Find(classId) is not { Needs.Count: > 0 } c || t.Rooms.Find(roomId) is not { } room) return [];
        return c.Needs.Select(n => t.Equipment[n]).OfType<Equipment>().DistinctBy(e => e.Id)
            .Select(e => room.Has.Contains(e.Id) ? on is { } day && OutOn(t, room.Id, e.Id, day) is { } spell ? new Lack(e, spell) : null : new Lack(e, null))
            .OfType<Lack>().OrderBy(l => l.Piece.Name, NameOrder.Comparer).ToList();
    }

    /// <summary>
    /// What a space lacks for a class, said for the clash check: what it has not, then what it has out of use
    /// (<see cref="SayOut"/>): "Crash mats; Tumble track out of use, back Mon 30 Nov".
    /// </summary>
    public static string Say(IReadOnlyList<Lack> lacks) =>
        string.Join("; ", new[] { string.Join(", ", lacks.Where(l => l.Out is null).Select(l => l.Piece.Name)) }
            .Concat(lacks.Where(l => l.Out is not null).Select(SayOut)).Where(x => x.Length > 0));

    /// <summary>
    /// A piece out of use, said the same wherever it is: "Tumble track out of use, back Mon 30 Nov", the day after
    /// its last day out of use, or only "out of use" until somebody says when it is back. Nothing for a piece that
    /// is not out of use.
    /// </summary>
    public static string SayOut(Lack l) => l.Out is not { } o ? ""
        : $"{l.Piece.Name} out of use{(o.Until is { } last ? ", back " + Week.ShortDate(last.AddDays(1)) : "")}";

    /// <summary>
    /// The spell a piece is out of use in a space on a day (<see cref="OutOfUse"/>), or null when it is in use
    /// there. When two say so, the one lasting longest: until somebody says it is back, then the latest end.
    /// Never on a day of the Scratch Week, which has no date for anything to be out of use on.
    /// </summary>
    public static OutOfUse? OutOn(Timetable t, Guid roomId, Guid equipmentId, DateOnly on) => Week.IsScratch(on) ? null
        : t.OutOfUse.Where(o => o.RoomId == roomId && o.EquipmentId == equipmentId && o.Covers(on))
            .OrderBy(o => o.Until is null ? 0 : 1).ThenByDescending(o => o.Until).ThenBy(o => o.Id).FirstOrDefault();

    /// <summary>
    /// Every piece out of use on a day, one spell for each (<see cref="OutOn"/>), by space and then piece in the order a
    /// person reads names: only in a space still the school's that still has it, since a spell left behind when a piece
    /// was taken off a space keeps nothing from anybody. None in the Scratch Week.
    /// </summary>
    public static IReadOnlyList<OutOfUse> OutOfUseOn(Timetable t, DateOnly day) => Week.IsScratch(day) ? []
        : t.OutOfUse.Where(o => o.Covers(day) && t.Rooms[o.RoomId] is { FormerSince: null } r && r.Has.Contains(o.EquipmentId) && t.Equipment.Contains(o.EquipmentId))
            .Select(o => OutOn(t, o.RoomId, o.EquipmentId, day)!).DistinctBy(o => o.Id)
            .OrderBy(o => t.Rooms[o.RoomId]!.Name, NameOrder.Comparer).ThenBy(o => t.Equipment[o.EquipmentId]!.Name, NameOrder.Comparer).ThenBy(o => o.Id).ToList();

    /// <summary>
    /// The classes a piece out of use leaves without it in its space, from one day to another: each that happens
    /// there on one of its days with a class that needs it, soonest first. What the form says before a piece goes out
    /// of use, and Now beside one that is. Given a time <paramref name="after"/>, a class on the first day that has
    /// ended by then needs nothing any more and is left out.
    /// </summary>
    public static IReadOnlyList<Occurrence> Affected(Timetable t, OutOfUse o, DateOnly from, DateOnly to, TimeOnly? after = null)
    {
        var first = from > o.From ? from : o.From;
        var last = o.Until is { } until && until < to ? until : to;
        if (last < first) return [];
        return Occurrences.Between(t, first, last)
            .Where(x => x.Happens && x.Session.RoomId == o.RoomId && t.Classes.Find(x.Session.ClassId) is { } c && c.Needs.Contains(o.EquipmentId))
            .Where(x => after is not { } at || x.Date > from || x.End > at)
            .OrderBy(x => x.Date).ThenBy(x => x.Start).ThenBy(x => x.Session.Id).ToList();
    }

    /// <summary>What a class needs, in the order a person reads names: the pieces still on the school's list.</summary>
    public static IReadOnlyList<Equipment> Needed(Timetable t, Guid? classId) =>
        t.Classes.Find(classId) is { } c
            ? c.Needs.Select(n => t.Equipment[n]).OfType<Equipment>().DistinctBy(e => e.Id).OrderBy(e => e.Name, NameOrder.Comparer).ToList()
            : [];

    /// <summary>Each event in a space over one day's events, earliest first, whoever may read it.</summary>
    public static IEnumerable<(Guid Room, EventOnDay E)> Bookings(Timetable t, IReadOnlyList<EventOnDay> day)
    {
        foreach (var e in day.OrderBy(e => e.From))
            if (e.Event.RoomId is { } room && t.Rooms.Contains(room))
                yield return (room, e);
    }
}
