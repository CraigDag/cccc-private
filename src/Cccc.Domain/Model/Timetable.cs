using System.Collections;
using System.Collections.Immutable;

namespace Cccc.Domain.Model;

/// <summary>An immutable set of entities keyed by id.</summary>
public sealed class EntitySet<T> : IReadOnlyCollection<T> where T : class, IEntity
{
    public static EntitySet<T> Empty { get; } = new(ImmutableDictionary<Guid, T>.Empty);

    private readonly ImmutableDictionary<Guid, T> _items;

    private EntitySet(ImmutableDictionary<Guid, T> items) => _items = items;

    public static EntitySet<T> Of(IEnumerable<T> items)
    {
        var b = ImmutableDictionary.CreateBuilder<Guid, T>();
        foreach (var i in items) b[i.Id] = i;
        return new EntitySet<T>(b.ToImmutable());
    }

    public int Count => _items.Count;

    public T? this[Guid id] => _items.GetValueOrDefault(id);

    public T? Find(Guid? id) => id is { } g ? _items.GetValueOrDefault(g) : null;

    public bool Contains(Guid id) => _items.ContainsKey(id);

    public EntitySet<T> Put(T item) => new(_items.SetItem(item.Id, item));

    public EntitySet<T> Remove(Guid id) => _items.ContainsKey(id) ? new(_items.Remove(id)) : this;

    public IEnumerator<T> GetEnumerator() => _items.Values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// The whole of one project's timetable, immutable. A change makes a new one; the old one is untouched,
/// which is what lets every tab render from a consistent state while another tab writes.
/// </summary>
public sealed record Timetable
{
    public Settings Settings { get; init; } = new();
    public EntitySet<Semester> Semesters { get; init; } = EntitySet<Semester>.Empty;
    public EntitySet<Closure> Closures { get; init; } = EntitySet<Closure>.Empty;
    public EntitySet<Room> Rooms { get; init; } = EntitySet<Room>.Empty;
    public EntitySet<Equipment> Equipment { get; init; } = EntitySet<Equipment>.Empty;
    public EntitySet<Programme> Programmes { get; init; } = EntitySet<Programme>.Empty;
    public EntitySet<Group> Groups { get; init; } = EntitySet<Group>.Empty;
    public EntitySet<Branch> Branches { get; init; } = EntitySet<Branch>.Empty;
    public EntitySet<Discipline> Disciplines { get; init; } = EntitySet<Discipline>.Empty;
    public EntitySet<Student> Students { get; init; } = EntitySet<Student>.Empty;
    public EntitySet<Facilitator> Facilitators { get; init; } = EntitySet<Facilitator>.Empty;
    public EntitySet<Area> Areas { get; init; } = EntitySet<Area>.Empty;
    public EntitySet<Class> Classes { get; init; } = EntitySet<Class>.Empty;
    public EntitySet<TimeRule> TimeRules { get; init; } = EntitySet<TimeRule>.Empty;
    public EntitySet<Session> Sessions { get; init; } = EntitySet<Session>.Empty;
    public EntitySet<Register> Registers { get; init; } = EntitySet<Register>.Empty;
    public EntitySet<Absence> Absences { get; init; } = EntitySet<Absence>.Empty;
    public EntitySet<SchoolEvent> Events { get; init; } = EntitySet<SchoolEvent>.Empty;

    /// <summary>
    /// The Scratch Week's classes: a plan, dated in <see cref="Week.ScratchMonday"/>, and a collection of
    /// its own so that nothing reading <see cref="Sessions"/> can count it. See
    /// <see cref="Editing.ScratchWeek"/>.
    /// </summary>
    public EntitySet<Session> Scratch { get; init; } = EntitySet<Session>.Empty;

    /// <summary>
    /// Facilitators saying they cannot come, waiting for an administrator's answer or answered: a list of its
    /// own, so nothing that counts absences can read one before a yes records it. See <see cref="Rules.Asking"/>.
    /// </summary>
    public EntitySet<TimeOffRequest> Requests { get; init; } = EntitySet<TimeOffRequest>.Empty;

    /// <summary>Facilitators offering to cover a class that needs it. See <see cref="Rules.Asking"/>.</summary>
    public EntitySet<CoverOffer> Offers { get; init; } = EntitySet<CoverOffer>.Empty;

    public static Timetable Empty { get; } = new();

    /// <summary>The sessions dated Monday to Sunday of this week. A week nobody filled is blank.</summary>
    public IEnumerable<Session> SessionsInWeek(DateOnly anyDateInWeek)
    {
        var monday = Week.MondayOf(anyDateInWeek);
        return Sessions.Where(s => s.WeekMonday == monday);
    }

    public Semester? SemesterFor(DateOnly date) =>
        Semesters.OrderBy(s => s.FirstMonday).FirstOrDefault(s => s.Contains(date));

    /// <summary>The semester containing the date, or the nearest one.</summary>
    public Semester? SemesterNear(DateOnly date)
    {
        if (SemesterFor(date) is { } s) return s;
        var m = Week.MondayOf(date);
        return Semesters.MinBy(t =>
            m < t.FirstMonday ? t.FirstMonday.DayNumber - m.DayNumber : m.DayNumber - t.LastMonday.DayNumber);
    }

    /// <summary>The last Monday of the last semester, or null when there are none.</summary>
    public DateOnly? YearEndMonday => Semesters.Count == 0 ? null : Semesters.Max(s => s.LastMonday);

    public DateOnly? YearStartMonday => Semesters.Count == 0 ? null : Semesters.Min(s => s.FirstMonday);

    public IEnumerable<Group> GroupsOf(Guid programmeId) => Groups.Where(g => g.ProgrammeId == programmeId);
}
