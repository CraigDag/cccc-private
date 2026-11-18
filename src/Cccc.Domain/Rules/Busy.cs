using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>What keeps somebody or something a class needs from a stretch of a day.</summary>
public enum BusyKind
{
    /// <summary>One of its facilitators in another class's room, teaching it or covering it, for their part of it.</summary>
    Teaching,
    /// <summary>One of its facilitators recorded away: for the times given, or the whole day.</summary>
    Away,
    /// <summary>An event the reader may see that names one of its facilitators, or some of its students.</summary>
    Event,
    /// <summary>Another class on for one of its groups. An open session is not: nobody is required at one.</summary>
    Group,
    /// <summary>Another class some of its students are on, that is not already on for one of its groups.</summary>
    Students,
    /// <summary>Its space: in another class, or booked by an event.</summary>
    Space,
}

/// <summary>
/// A stretch of a day when somebody or something a class needs is elsewhere. Whose it is: one of its
/// facilitators (<see cref="PersonId"/>), some of its groups (<see cref="GroupIds"/>), how many of its students
/// (<see cref="Students"/>), or its space (<see cref="RoomId"/>). What they are at: another class
/// (<see cref="Class"/>) or an event (<see cref="Event"/>); neither for somebody away.
/// </summary>
public sealed record BusyStretch(BusyKind Kind, TimeOnly From, TimeOnly Until)
{
    public Guid? PersonId { get; init; }
    public IReadOnlyList<Guid> GroupIds { get; init; } = [];
    public int Students { get; init; }
    public Guid? RoomId { get; init; }
    public Occurrence? Class { get; init; }

    /// <summary>The event, when the reader may see it. A space booked by an event they may not see is only booked.</summary>
    public SchoolEvent? Event { get; init; }

    /// <summary>Whether it takes any moment between start and end. One that ends as the other starts does not.</summary>
    public bool Overlaps(TimeOnly start, TimeOnly end) => From < end && start < Until;
}

/// <summary>
/// When the people, groups and space a class needs are elsewhere on a day: WHO is in what SPACE and WHEN,
/// asked on behalf of one class. Find a time leaves a time out by it and the plan shades the day by it, so
/// the list of times and the picture cannot disagree. A facilitator is busy in another class's room for their
/// part of it (cover included), while recorded away, and at an event the reader may see that names them. Its
/// groups are busy in any other class on for them, an open session aside; its students in any other class
/// they are on, an open session aside, and at an event the reader may see that names them. Its space is busy
/// in another class and at any event in it, whoever may read the event. The class itself never stands in its
/// own way, and cancelled and closed classes keep nobody busy. Being busy says nothing about whether a time
/// is allowed: bounds advise, never refuse.
/// </summary>
public static class Busy
{
    public static IReadOnlyList<BusyStretch> For(Timetable t, Session c, DateOnly day, EventViewer viewer) =>
        For(t, c, day, viewer, Occurrences.Between(t, day, day), EventSchedule.On(t, day));

    /// <summary>The same, over one day's occurrences and events, for a caller that already has them.</summary>
    public static IReadOnlyList<BusyStretch> For(Timetable t, Session c, DateOnly day, EventViewer viewer,
        IReadOnlyList<Occurrence> occurrences, IReadOnlyList<EventOnDay> events)
    {
        var found = new List<BusyStretch>();
        var people = c.FacilitatorIds.Where(t.Facilitators.Contains).Distinct().ToList();
        // A whole-building class needs every group.
        var groups = (c.Audience.IsEveryone ? t.Groups.Select(g => g.Id) : c.Audience.Cohorts.Where(t.Groups.Contains)).ToHashSet();
        var students = Roster.For(t, c).ToHashSet();
        var seen = events.Where(e => viewer.Sees(e.Event)).ToList();

        foreach (var (id, o, a, b) in Teaching.InRooms(occurrences, c.Id))
            if (people.Contains(id))
                found.Add(new(BusyKind.Teaching, a, b) { PersonId = id, Class = o });
        foreach (var id in people)
        {
            foreach (var away in t.Absences.Where(x => x.Is(AbsentPerson.Facilitator, id)))
                if (away.On(day) is { } w)
                    found.Add(new(BusyKind.Away, w.From, w.Until) { PersonId = id });
            foreach (var e in seen.Where(e => e.Event.FacilitatorIds.Contains(id)))
                found.Add(new(BusyKind.Event, e.From, e.Until) { PersonId = id, Event = e.Event });
        }

        foreach (var o in occurrences)
        {
            if (o.Session.Id == c.Id || !o.Happens || Hours.WorkloadOf(t, o.Session) == Workload.DropIn) continue;
            var theirs = groups.Where(o.Session.Audience.IsFor).OrderBy(g => t.Groups[g]!.Name, NameOrder.Comparer).ToList();
            if (theirs.Count > 0)
            {
                found.Add(new(BusyKind.Group, o.Start, o.End) { GroupIds = theirs, Class = o });
                continue;
            }
            if (students.Count == 0) continue;
            int shared = Roster.For(t, o.Session).Count(students.Contains);
            if (shared > 0) found.Add(new(BusyKind.Students, o.Start, o.End) { Students = shared, Class = o });
        }
        if (students.Count > 0)
            foreach (var e in seen)
                if (e.Event.StudentIds.Count(students.Contains) is var named and > 0)
                    found.Add(new(BusyKind.Event, e.From, e.Until) { Students = named, Event = e.Event });

        if (c.RoomId is { } room && t.Rooms.Contains(room))
        {
            foreach (var (r, o) in Spaces.Uses(occurrences, c.Id))
                if (r == room) found.Add(new(BusyKind.Space, o.Start, o.End) { RoomId = room, Class = o });
            foreach (var (r, e) in Spaces.Bookings(t, events))
                if (r == room) found.Add(new(BusyKind.Space, e.From, e.Until) { RoomId = room, Event = viewer.Sees(e.Event) ? e.Event : null });
        }

        return found.OrderBy(x => x.From).ThenBy(x => x.Until).ThenBy(x => x.Kind).ToList();
    }
}
