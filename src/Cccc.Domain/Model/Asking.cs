using System.Collections.Immutable;

namespace Cccc.Domain.Model;

/// <summary>What an administrator said to something a facilitator asked or offered.</summary>
public enum Answer { Yes, No }

/// <summary>
/// A facilitator saying they cannot come: the days, and the times on each when it is not the whole of them.
/// A list of its own, never an <see cref="Absence"/>, so nothing that counts absences (cover, hours, clashes,
/// registers) can read one by accident: a request changes nothing on the timetable until an administrator
/// says yes, which records the absence it asks for (<see cref="AbsenceId"/>). There is no box for why, so
/// nothing about anybody's health is ever asked for here.
/// </summary>
public sealed record TimeOffRequest : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FacilitatorId { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }

    /// <summary>The school's date it was asked on: asked before its first day it is planned, on the day it is not.</summary>
    public DateOnly AskedOn { get; init; }
    public DateTimeOffset AskedAt { get; init; }
    public Answer? Answer { get; init; }
    public DateTimeOffset? AnsweredAt { get; init; }

    /// <summary>The absence a yes recorded.</summary>
    public Guid? AbsenceId { get; init; }

    /// <summary>The absence it asks for, as a yes records it: the same days and times, planned or not by when it was asked.</summary>
    public Absence AsAbsence(Guid id) => new()
    {
        Id = id, Person = AbsentPerson.Facilitator, PersonId = FacilitatorId, From = From, To = To,
        StartTime = StartTime, EndTime = EndTime, Notice = Absence.NoticeWhenSaid(AskedOn, From, cause: null),
    };
}

/// <summary>
/// A facilitator offering to cover a class that needs it. A yes makes them its cover. Whether one is still
/// waiting is worked out from the class (<see cref="Rules.Asking.StateOf(Timetable, CoverOffer, DateOnly)"/>),
/// never stored, so a class covered by anybody, however it was covered, asks nobody else.
/// </summary>
public sealed record CoverOffer : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid SessionId { get; init; }
    public Guid FacilitatorId { get; init; }
    public DateTimeOffset OfferedAt { get; init; }
    public Answer? Answer { get; init; }
    public DateTimeOffset? AnsweredAt { get; init; }
}

/// <summary>
/// A facilitator asking for a space at a time: extra practice with a group, a rehearsal, an act to work on. A
/// list of its own, never a class, so nothing that counts classes (hours, clashes, registers, the screens in
/// the corridor) can read one by accident: it is on the timetable only once an administrator says yes, which
/// adds the class it asks for (<see cref="SessionId"/>), taught by whoever asked. What it is called is the
/// class's name as everybody will read it, so the form says so, and there is no box for why.
/// </summary>
public sealed record SpaceAsk : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid FacilitatorId { get; init; }
    public DateOnly Date { get; init; }
    public TimeOnly Start { get; init; }
    public TimeOnly End { get; init; }
    public Guid RoomId { get; init; }

    /// <summary>Who it is for, as a class says it: a group, several, or the whole building.</summary>
    public Audience Audience { get; init; } = Audience.Everyone;

    /// <summary>A class from the catalogue, when it is one: its name, and whether it counts, come with it.</summary>
    public Guid? ClassId { get; init; }

    /// <summary>What it is called; empty when a class gives it its name.</summary>
    public string Title { get; init; } = "";

    public DateTimeOffset AskedAt { get; init; }
    public Answer? Answer { get; init; }
    public DateTimeOffset? AnsweredAt { get; init; }

    /// <summary>The class a yes added.</summary>
    public Guid? SessionId { get; init; }

    /// <summary>
    /// The class it asks for, as a yes adds it and the add form would: whoever asked teaching it, in that space,
    /// then, called what they called it or else by its class's name as it is now.
    /// </summary>
    public Session AsSession(Timetable t, Guid id) => new()
    {
        Id = id, Date = Date, Start = Start, End = End, RoomId = RoomId, Audience = Audience, ClassId = ClassId,
        Title = Title.Trim() is { Length: > 0 } own ? own : t.Classes.Find(ClassId)?.Name ?? "",
        Facilitators = ImmutableList.Create(new Assignment(FacilitatorId)),
    };
}

