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

    /// <summary>Each event in a space over one day's events, earliest first, whoever may read it.</summary>
    public static IEnumerable<(Guid Room, EventOnDay E)> Bookings(Timetable t, IReadOnlyList<EventOnDay> day)
    {
        foreach (var e in day.OrderBy(e => e.From))
            if (e.Event.RoomId is { } room && t.Rooms.Contains(room))
                yield return (room, e);
    }
}
