using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>Where the timetable has somebody at the moment a roll is called.</summary>
public enum RollWhere
{
    /// <summary>In a class now, so in its space.</summary>
    InClass,
    /// <summary>At an event now that names them, in its space.</summary>
    AtEvent,
    /// <summary>Between two of their classes today: in the building, in no space the timetable knows.</summary>
    Between,
    /// <summary>At an event now that names them and is in none of the spaces: a trip, somewhere else.</summary>
    Elsewhere,
    /// <summary>Their first class today is still to come.</summary>
    NotInYet,
    /// <summary>Their last class today is over.</summary>
    Gone,
    /// <summary>In a class now whose register, taken, says they are not there.</summary>
    MarkedAbsent,
    /// <summary>Recorded away now.</summary>
    Away,
}

/// <summary>Somebody the timetable has in the building today, and where it has them at the moment.</summary>
public sealed record RollPerson(Guid Id, string Name, bool IsStudent, bool IsGuest, RollWhere Where, Whereabouts Day)
{
    /// <summary>The event they are at now, when that is where they are.</summary>
    public EventOnDay? Event { get; init; }

    /// <summary>Whom the timetable expects in the building at the moment: in a class, at an event in a space, or between classes.</summary>
    public bool Expected => Where is RollWhere.InClass or RollWhere.AtEvent or RollWhere.Between;

    /// <summary>The space it has them in now, when it has them in one.</summary>
    public Guid? RoomId => Where switch
    {
        RollWhere.InClass => Day.Now!.O.Session.RoomId,
        RollWhere.AtEvent => Event!.Event.RoomId,
        _ => null,
    };
}

/// <summary>What is in a space at the moment that names nobody, or not everybody there: a class for the whole building, one whose groups have no students, an event.</summary>
public sealed record RollByEye(Occurrence? Class, EventOnDay? Event)
{
    /// <summary>One id for it, the class's or the event's, for whoever has counted it.</summary>
    public Guid Id => Class?.Session.Id ?? Event!.Event.Id;
}

/// <summary>One space at the moment (or none, for a class given no space): what is in it, who, and what to count by eye.</summary>
public sealed record RollSpace(Guid? RoomId, IReadOnlyList<Occurrence> Classes, IReadOnlyList<RollPerson> People, IReadOnlyList<RollByEye> ByEye);

/// <summary>A roll call's list: the moment it was called, the spaces with people in them, who is between classes, and who is not expected.</summary>
public sealed record RollList(DateOnly Date, TimeOnly At, IReadOnlyList<RollSpace> Spaces, IReadOnlyList<RollPerson> Between, IReadOnlyList<RollPerson> NotExpected)
{
    /// <summary>Everybody the timetable expects in the building: in the spaces, then between classes.</summary>
    public IEnumerable<RollPerson> Expected => Spaces.SelectMany(s => s.People).Concat(Between);
}

/// <summary>
/// A roll call: everybody the timetable has in the building at one moment, by the space they are in, for a
/// fire drill or an evacuation. Where each of them is comes from <see cref="Where"/>, person by person, so it
/// is the plan's own answer: a student by the class's roster, a facilitator by being in the room (cover
/// included, a part only for their part, guests too). Not in a class, somebody an event names (one the
/// reader may see, as everywhere) is at it while it is on: in its space, or away from the building when it
/// is in none. Anybody recorded away is not expected, and nor is a student whose register for the class
/// they are in now says they are not there; a register not taken yet expects the whole roster. Somebody
/// between two of their classes is expected, in no space; somebody whose first class is still to come, or
/// whose last is over, is listed apart, as somebody who may be in the building or may not. What names
/// nobody, or not everybody there (a class for the whole building, an event), is said with its space, to
/// count by eye. It knows only the timetable: a visitor, or somebody with nothing on today, is on no list.
/// </summary>
public static class RollCall
{
    public static RollList At(Timetable t, DateOnly date, TimeOnly at, EventViewer viewer)
    {
        var today = Occurrences.Between(t, date, date).Where(o => o.Happens).ToList();
        var eventsNow = EventSchedule.On(t, date).Where(e => viewer.Sees(e.Event) && e.Overlaps(at, Later(at))).ToList();
        var people = new List<RollPerson>();

        foreach (var f in t.Facilitators.Current())
        {
            var w = Where.Facilitator(t, f.Id, date, at);
            var atEvent = eventsNow.FirstOrDefault(e => e.Event.FacilitatorIds.Contains(f.Id));
            // Away, a facilitator is in no room, so their day says nothing of it: listed away when a class today is
            // theirs. One covering a class is still in its room, away or not, so their day says it already.
            bool theirs = HasADay(w) || atEvent is not null
                || w.Away && today.Any(o => o.Session.FacilitatorIds.Contains(f.Id));
            if (theirs && WhereFrom(w, atEvent, marked: false) is { } where)
                people.Add(new RollPerson(f.Id, f.Name, false, f.IsGuest, where, w) { Event = where is RollWhere.AtEvent or RollWhere.Elsewhere ? atEvent : null });
        }

        foreach (var s in t.Students)
        {
            var w = Where.Student(t, s.Id, date, at);
            var atEvent = eventsNow.FirstOrDefault(e => e.Event.StudentIds.Contains(s.Id));
            bool marked = w.Now is { } now && t.Registers[now.O.Session.Id] is { } register
                && register.RosterIds.Contains(s.Id) && register.MarkFor(s.Id) != Mark.Present;
            bool guest = s.GroupId is { } g && t.Programmes.Find(t.Groups[g]?.ProgrammeId) is { IsGuest: true };
            if ((HasADay(w) || atEvent is not null) && WhereFrom(w, atEvent, marked) is { } where)
                people.Add(new RollPerson(s.Id, s.Name, true, guest, where, w) { Event = where is RollWhere.AtEvent or RollWhere.Elsewhere ? atEvent : null });
        }

        // The spaces with somebody in them, or something to count by eye: by name, a class with no space last.
        var placed = people.Where(p => p.Where is RollWhere.InClass or RollWhere.AtEvent).ToLookup(p => p.RoomId);
        var classesNow = today.Where(o => o.Start <= at && at < o.End).ToList();
        var byEye = classesNow.Where(o => Roster.For(t, o.Session).Count == 0).Select(o => (o.Session.RoomId, Item: new RollByEye(o, null)))
            .Concat(eventsNow.Where(e => e.Event.RoomId is not null).Select(e => (e.Event.RoomId, Item: new RollByEye(null, e))))
            .ToLookup(x => x.RoomId, x => x.Item);
        var rooms = placed.Select(g => g.Key).Concat(byEye.Select(g => g.Key)).Distinct()
            .OrderBy(id => id is null).ThenBy(id => t.Rooms.Find(id)?.Name ?? "", NameOrder.Comparer).ThenBy(id => id);
        var spaces = rooms.Select(id => new RollSpace(id,
            classesNow.Where(o => o.Session.RoomId == id).ToList(),
            Ordered(placed[id]),
            byEye[id].OrderBy(x => x.Class is null).ThenBy(x => x.Class?.Start ?? x.Event!.From).ToList())).ToList();

        var notExpected = people.Where(p => !p.Expected)
            .OrderBy(p => p.Where).ThenBy(p => p.IsStudent).ThenBy(p => p.Name, NameOrder.Comparer).ThenBy(p => p.Id).ToList();
        return new RollList(date, at, spaces, Ordered(people.Where(p => p.Where == RollWhere.Between)), notExpected);
    }

    private static bool HasADay(Whereabouts w) => w.Now is not null || w.Last is not null || w.Next is not null;

    /// <summary>
    /// Where their day puts somebody at the moment: away first, then the class they are in (or, its register
    /// says, are not), then an event naming them, then between classes, before their first or after their last.
    /// </summary>
    private static RollWhere? WhereFrom(Whereabouts w, EventOnDay? atEvent, bool marked) =>
        w.Away ? RollWhere.Away
        : w.Now is not null ? marked ? RollWhere.MarkedAbsent : RollWhere.InClass
        : atEvent is not null ? atEvent.Event.RoomId is null ? RollWhere.Elsewhere : RollWhere.AtEvent
        : w.Last is not null && w.Next is not null ? RollWhere.Between
        : w.Next is not null ? RollWhere.NotInYet
        : w.Last is not null ? RollWhere.Gone
        : null;

    /// <summary>Facilitators first, then students, each by name.</summary>
    private static IReadOnlyList<RollPerson> Ordered(IEnumerable<RollPerson> people) =>
        people.OrderBy(p => p.IsStudent).ThenBy(p => p.Name, NameOrder.Comparer).ThenBy(p => p.Id).ToList();

    // The minute starting at the moment, as Where reads it: an event ending exactly then is over.
    private static TimeOnly Later(TimeOnly at) => at >= new TimeOnly(23, 59) ? TimeOnly.MaxValue : at.AddMinutes(1);
}
