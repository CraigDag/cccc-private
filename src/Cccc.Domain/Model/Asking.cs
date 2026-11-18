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
        StartTime = StartTime, EndTime = EndTime, Notice = AskedOn < From ? AbsenceNotice.Planned : AbsenceNotice.Unplanned,
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
