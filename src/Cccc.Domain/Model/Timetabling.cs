using System.Collections.Immutable;

namespace Cccc.Domain.Model;

public enum AudienceKind
{
    /// <summary>One group.</summary>
    Group,
    /// <summary>Several named groups.</summary>
    Groups,
    /// <summary>A whole programme; <see cref="Audience.GroupIds"/> holds its groups as they were chosen.</summary>
    Programme,
    /// <summary>The whole building: on for every group, and nobody's roster.</summary>
    Everyone,
}

/// <summary>Who a session is on for.</summary>
public sealed record Audience
{
    public AudienceKind Kind { get; init; } = AudienceKind.Everyone;
    public ImmutableList<Guid> GroupIds { get; init; } = [];
    public Guid? ProgrammeId { get; init; }

    public static Audience Everyone { get; } = new();

    public static Audience ForGroup(Guid groupId) => new() { Kind = AudienceKind.Group, GroupIds = [groupId] };

    public static Audience ForGroups(IEnumerable<Guid> groupIds) =>
        new() { Kind = AudienceKind.Groups, GroupIds = groupIds.Distinct().ToImmutableList() };

    public bool IsEveryone => Kind == AudienceKind.Everyone;

    /// <summary>The groups this audience names; empty for Everyone.</summary>
    public IEnumerable<Guid> Cohorts => IsEveryone ? [] : GroupIds.Distinct();

    /// <summary>
    /// Whether the session is on for this group. An Everyone session is on for every group, which is
    /// why filtering the week to one group keeps it on screen.
    /// </summary>
    public bool IsFor(Guid groupId) => IsEveryone || GroupIds.Contains(groupId);

    public bool Equivalent(Audience other) =>
        Kind == other.Kind && ProgrammeId == other.ProgrammeId
        && GroupIds.ToHashSet().SetEquals(other.GroupIds);
}

/// <summary>
/// A facilitator assigned to a session. With no times they are in the whole session; with a From, an
/// Until or both, only in that part (schema 23, Craig 2026-11-07).
/// </summary>
public sealed record Assignment(Guid FacilitatorId, TimeOnly? From = null, TimeOnly? Until = null);

/// <summary>Where an occurrence was before somebody rescheduled it, so the reader can be told.</summary>
public sealed record MovedFrom(DateOnly Date, TimeOnly Start, TimeOnly End);

/// <summary>
/// One class meeting, on one date. There is no standing pattern: a week's timetable is the sessions
/// dated inside it.
/// </summary>
public sealed record Session : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateOnly Date { get; init; }
    public TimeOnly Start { get; init; } = new(9, 0);
    public TimeOnly End { get; init; } = new(10, 0);
    public string Title { get; init; } = "";
    public Guid? ClassId { get; init; }
    public Audience Audience { get; init; } = Audience.Everyone;
    public Guid? RoomId { get; init; }
    public ImmutableList<Assignment> Facilitators { get; init; } = [];
    public ImmutableList<Guid> AddedStudentIds { get; init; } = [];
    public ImmutableList<Guid> LeftOffStudentIds { get; init; } = [];
    public string? Notes { get; init; }
    public bool Provisional { get; init; }
    public bool Cancelled { get; init; }
    public string? CancelNote { get; init; }
    public ImmutableList<Guid> CoverIds { get; init; } = [];
    public string? CoverNote { get; init; }
    public MovedFrom? MovedFrom { get; init; }

    public DateOnly WeekMonday => Week.MondayOf(Date);

    public IEnumerable<Guid> FacilitatorIds => Facilitators.Select(a => a.FacilitatorId).Distinct();

    public TimeSpan Length => End > Start ? End - Start : TimeSpan.Zero;

    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "(untitled)" : Title.Trim();
}

public enum Mark { Present, Excused, Absent }

/// <summary>
/// The register of one session. Its <see cref="Id"/> IS the session's id, so a session has at most one.
/// The mark is stored, never derived from absences; absent is the mark by saying nothing.
/// </summary>
public sealed record Register : IEntity
{
    public Guid Id { get; init; }
    public Guid SessionId => Id;
    public ImmutableList<Guid> PresentIds { get; init; } = [];
    public ImmutableList<Guid> ExcusedIds { get; init; } = [];
    /// <summary>The roster it was taken against, so a later group change cannot rewrite who was absent.</summary>
    public ImmutableList<Guid> RosterIds { get; init; } = [];
    public string? FiledBy { get; init; }
    public DateTimeOffset? FiledAt { get; init; }

    public Mark MarkFor(Guid studentId) =>
        PresentIds.Contains(studentId) ? Mark.Present
        : ExcusedIds.Contains(studentId) ? Mark.Excused
        : Mark.Absent;

    /// <summary>The two lists never overlap: present wins.</summary>
    public Register Normalised() => this with
    {
        PresentIds = PresentIds.Distinct().ToImmutableList(),
        ExcusedIds = ExcusedIds.Distinct().Where(id => !PresentIds.Contains(id)).ToImmutableList(),
        RosterIds = RosterIds.Distinct().ToImmutableList(),
    };

    public (int Present, int Excused, int Absent) Tally()
    {
        int p = 0, e = 0, a = 0;
        foreach (var id in RosterIds)
            switch (MarkFor(id))
            {
                case Mark.Present: p++; break;
                case Mark.Excused: e++; break;
                default: a++; break;
            }
        return (p, e, a);
    }
}

public enum AbsentPerson { Facilitator, Student }

public enum AbsenceNotice { Planned, Unplanned }

public enum AbsenceCause { Illness, Personal, ApprovedLeave, Other }

public sealed record Absence : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public AbsentPerson Person { get; init; }
    public Guid PersonId { get; init; }
    public DateOnly From { get; init; }
    public DateOnly To { get; init; }
    /// <summary>Free text, administrators only. Never health details.</summary>
    public string? Reason { get; init; }
    public AbsenceNotice? Notice { get; init; }
    public AbsenceCause? Cause { get; init; }
    public TimeOnly? StartTime { get; init; }
    public TimeOnly? EndTime { get; init; }

    public bool Is(AbsentPerson kind, Guid personId) => Person == kind && PersonId == personId;

    /// <summary>
    /// The notice an absence has when nobody says: planned when it is said before its first day, unplanned on the
    /// day itself, and an illness always unplanned, as nobody plans to be ill, even when they phone the night before.
    /// </summary>
    public static AbsenceNotice NoticeWhenSaid(DateOnly saidOn, DateOnly from, AbsenceCause? cause) =>
        cause != AbsenceCause.Illness && saidOn < from ? AbsenceNotice.Planned : AbsenceNotice.Unplanned;

    public bool CoversDate(DateOnly date) => date >= From && date <= To;

    /// <summary>
    /// Whether the absence covers this stretch of this date. A window is used only when both ends are
    /// given and End is after Start; otherwise the absence is whole days.
    /// </summary>
    public bool Covers(DateOnly date, TimeOnly start, TimeOnly end)
    {
        if (!CoversDate(date)) return false;
        if (Window is not { } w) return true;
        return w.From < end && start < w.Until;
    }

    /// <summary>The part of a date it covers: its window on each of its days, or the whole of them. Null on a date it does not cover.</summary>
    public (TimeOnly From, TimeOnly Until)? On(DateOnly date) =>
        !CoversDate(date) ? null : Window ?? (TimeOnly.MinValue, TimeOnly.MaxValue);

    private (TimeOnly From, TimeOnly Until)? Window => StartTime is { } s && EndTime is { } e && e > s ? (s, e) : null;
}

public enum EventCategory { Meeting, Performance, Audition, Deadline, Trip, Visitor, HostedGroup, Other }

[Flags]
public enum EventAudience
{
    Everyone = 0,
    Students = 1,
    Facilitators = 2,
    Staff = 4,
    Administrators = 8,
}

public enum RepeatKind { Daily, Weekly, MonthlyOnDate, MonthlyOnWeekday }

public sealed record Repeat
{
    public RepeatKind Kind { get; init; } = RepeatKind.Weekly;
    public int Every { get; init; } = 1;
    public ImmutableList<DayOfWeek> Days { get; init; } = [];
    public DateOnly? Until { get; init; }
    public int? Count { get; init; }
}

/// <summary>An event beside the classes. Nothing that counts classes reads one.</summary>
public sealed record SchoolEvent : IEntity
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Title { get; init; } = "";
    public EventCategory Category { get; init; }
    public DateOnly Date { get; init; }
    public DateOnly? EndDate { get; init; }
    public bool AllDay { get; init; }
    public TimeOnly Start { get; init; } = new(9, 0);
    public TimeOnly End { get; init; } = new(10, 0);
    public Repeat? Repeat { get; init; }
    public ImmutableList<DateOnly> Skipped { get; init; } = [];
    public bool SkipClosures { get; init; }
    public EventAudience Audience { get; init; }
    public Guid? RoomId { get; init; }
    public string Place { get; init; } = "";
    public ImmutableList<Guid> FacilitatorIds { get; init; } = [];
    public ImmutableList<Guid> StudentIds { get; init; } = [];
    public string Note { get; init; } = "";

    public int SpanDays => EndDate is { } end && end > Date ? end.DayNumber - Date.DayNumber + 1 : 1;
}
