using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

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
    /// What a class needs (<see cref="Class.Needs"/>) that a space does not have (<see cref="Room.Has"/>), in the
    /// order a person reads names. Nothing for a class that needs nothing, a session with no class, or no space:
    /// a class with nowhere given is not in the wrong place, only in none. A piece of equipment the school has
    /// since taken off its list is needed by nobody.
    /// </summary>
    public static IReadOnlyList<Equipment> Lacking(Timetable t, Guid? classId, Guid? roomId)
    {
        if (t.Classes.Find(classId) is not { Needs.Count: > 0 } c || t.Rooms.Find(roomId) is not { } room) return [];
        return c.Needs.Where(n => !room.Has.Contains(n)).Select(n => t.Equipment[n]).OfType<Equipment>()
            .DistinctBy(e => e.Id).OrderBy(e => e.Name, NameOrder.Comparer).ToList();
    }

    /// <summary>What this class needs that its own space does not have.</summary>
    public static IReadOnlyList<Equipment> Lacking(Timetable t, Session s) => Lacking(t, s.ClassId, s.RoomId);

    /// <summary>Whether a space has everything the class needs: the spaces Find a time and a way out may offer it.</summary>
    public static bool Suits(Timetable t, Guid? classId, Guid roomId) => Lacking(t, classId, roomId).Count == 0;

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
