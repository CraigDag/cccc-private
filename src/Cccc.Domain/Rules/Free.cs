using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>What a question about being free is about: a space, a facilitator, a group or a student.</summary>
public enum Whose { Space, Facilitator, Group, Student }

/// <summary>
/// Whether somebody or something is free over a stretch of a day. Free when nothing is
/// <see cref="InTheWay"/>, and then free <see cref="Since"/> one time <see cref="Until"/> another (null for the
/// start or the end of the day), with <see cref="Next"/> what ends it.
/// </summary>
public sealed record FreeAnswer(IReadOnlyList<BusyStretch> InTheWay, TimeOnly? Since, TimeOnly? Until, BusyStretch? Next)
{
    public bool IsFree => InTheWay.Count == 0;
}

/// <summary>
/// Who and what is free: <see cref="Busy"/> asked about a time rather than on behalf of a class, so Find a time,
/// the plan's shading and a question typed into Find cannot disagree about it. A space is busy in a class and
/// at any event in it, whoever may read the event; a facilitator in a class's room for their part of it (cover
/// included), while recorded away, and at an event the reader may see that names them; a group in a class on
/// for it, an open session aside, while some of its students are in another, and at an event the reader may
/// see that names some of them; a student in a class they are on, an open session aside, while recorded away,
/// and at an event the reader may see that names them. A cancelled or closed class keeps nobody busy. Being
/// free says nothing about whether a time is allowed: bounds advise, never refuse.
/// </summary>
public static class Free
{
    /// <summary>Everything keeping them from any part of a day, earliest first.</summary>
    public static IReadOnlyList<BusyStretch> Day(Timetable t, Whose whose, Guid id, DateOnly day, EventViewer viewer) =>
        Day(t, whose, id, day, viewer, Occurrences.Between(t, day, day), EventSchedule.On(t, day));

    /// <summary>The same, over one day's occurrences and events, for a caller asking about many.</summary>
    public static IReadOnlyList<BusyStretch> Day(Timetable t, Whose whose, Guid id, DateOnly day, EventViewer viewer,
        IReadOnlyList<Occurrence> occurrences, IReadOnlyList<EventOnDay> events)
    {
        // A class nobody has, needing only them: nothing of the timetable's own steps aside for it, and it is on
        // for no group unless the question is a group's, so nobody else's day counts.
        var asked = new Session { Id = Guid.NewGuid(), Date = day, Audience = new Audience { Kind = AudienceKind.Group } };
        asked = whose switch
        {
            Whose.Space => asked with { RoomId = id },
            Whose.Facilitator => asked with { Facilitators = [new Assignment(id)] },
            Whose.Group => asked with { Audience = Audience.ForGroup(id) },
            _ => asked with { AddedStudentIds = [id] },
        };
        var busy = Busy.For(t, asked, day, viewer, occurrences, events).ToList();
        // A class does not need any one student, so Busy has no word for one away: their own question does.
        if (whose == Whose.Student)
            foreach (var a in t.Absences.Where(a => a.Is(AbsentPerson.Student, id)))
                if (a.On(day) is { } w)
                    busy.Add(new BusyStretch(BusyKind.Away, w.From, w.Until) { PersonId = id });
        return busy.OrderBy(b => b.From).ThenBy(b => b.Until).ThenBy(b => b.Kind).ToList();
    }

    /// <summary>
    /// Whether they are free from <paramref name="start"/> until <paramref name="end"/>, given a day's busy
    /// stretches (<see cref="Day"/>): what is in the way if not, and if so since when, until when and what then. A
    /// stretch that ends as the time starts, or starts as it ends, is not in the way.
    /// </summary>
    public static FreeAnswer Over(IReadOnlyList<BusyStretch> day, TimeOnly start, TimeOnly end)
    {
        var inTheWay = day.Where(b => b.Overlaps(start, end)).OrderBy(b => b.From).ThenBy(b => b.Until).ToList();
        if (inTheWay.Count > 0) return new FreeAnswer(inTheWay, null, null, null);
        var since = day.Where(b => b.Until <= start).Select(b => (TimeOnly?)b.Until).Max();
        var next = day.Where(b => b.From >= end).OrderBy(b => b.From).ThenBy(b => b.Until).FirstOrDefault();
        return new FreeAnswer([], since, next?.From, next);
    }

    /// <summary>A moment as a stretch to ask about: the minute starting then, so a class ending then is over.</summary>
    public static (TimeOnly Start, TimeOnly End) Moment(TimeOnly at) => (at, at >= new TimeOnly(23, 59) ? TimeOnly.MaxValue : at.AddMinutes(1));

    /// <summary>
    /// The stretches of a day nothing is in, in order: (null, x) from the start of the day, (x, null) to its end,
    /// (null, null) the whole of it. None when something takes all of it.
    /// </summary>
    public static IReadOnlyList<(TimeOnly? From, TimeOnly? Until)> Stretches(IReadOnlyList<BusyStretch> day)
    {
        var free = new List<(TimeOnly?, TimeOnly?)>();
        TimeOnly? from = null;
        foreach (var b in day.OrderBy(b => b.From).ThenBy(b => b.Until))
        {
            if (b.From > (from ?? TimeOnly.MinValue)) free.Add((from, b.From));
            if (from is not { } f || b.Until > f) from = b.Until;
            if (from == TimeOnly.MaxValue) return free;
        }
        free.Add((from, null));
        return free;
    }

    /// <summary>
    /// Why nothing can be said about who is free on a day, or null when something can: the school is closed
    /// (for one of <paramref name="groups"/> too, when a group is asked about), or the week has nothing in it yet,
    /// so everybody would look free. The reasons Find a time gives for a day it does not look in.
    /// </summary>
    public static string? Unknowable(Timetable t, DateOnly day, IReadOnlyList<Guid> groups) => Openings.NotLookedIn(t, day, groups);
}
