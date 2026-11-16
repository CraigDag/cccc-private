namespace CCCCScheduler.Models;

/// <summary>What happens to a single weekly session on one specific week.</summary>
public enum ExceptionKind
{
    /// <summary>The session does not run that week.</summary>
    Cancelled,

    /// <summary>
    /// The session runs that week but at a different time
    /// (<see cref="SessionException.NewStart"/>/<see cref="SessionException.NewEnd"/>), or on a
    /// different day of that same week (<see cref="SessionException.NewDay"/>), or both.
    /// </summary>
    Rescheduled,

    /// <summary>The session runs as normal but a substitute facilitator covers it (<see cref="SessionException.CoverFacilitatorIds"/>).</summary>
    Covered,
}

/// <summary>
/// A one-off deviation from a weekly-recurring <see cref="ScheduleEntry"/> for a single week,
/// identified by that week's Monday. Lets a term's master timetable stay intact while still
/// recording "no class this Tuesday" or "this week only, 6pm not 7pm". Lives on the entry, so it
/// travels with the session and is covered by undo/redo and save.
/// </summary>
public sealed class SessionException
{
    /// <summary>The Monday of the week this deviation applies to.</summary>
    public DateOnly WeekMonday { get; set; }

    public ExceptionKind Kind { get; set; } = ExceptionKind.Cancelled;

    /// <summary>
    /// The day this occurrence runs on instead, when <see cref="Kind"/> is
    /// <see cref="ExceptionKind.Rescheduled"/> — <b>null keeps the usual day</b>, which is what every
    /// project written before schema 12 means and what almost every reschedule still means.
    ///
    /// <para><b>Within the same week, always.</b> The week is <see cref="WeekMonday"/> and this only
    /// says which of its seven days; a class cannot be moved into next week, which would be an edit to
    /// a different week's exception rather than a value here.</para>
    ///
    /// <para><b>Read it through <c>ScheduleResolver.EffectiveDay</c> and never directly</b>, exactly as
    /// the two times above are read through <c>EffectiveTimes</c>. That function is the one place the
    /// rule lives — a day is honoured only under <see cref="ExceptionKind.Rescheduled"/> — and a second
    /// reading of it is how the week grid and the cover list come to disagree about which day a class
    /// is on. <c>ProjectStore.Validate</c> repairs a day on any other kind, and an out-of-range one,
    /// to null on every load, so nothing downstream has to defend against either.</para>
    ///
    /// <para><b>Moving the day is a reschedule on its own</b>: the times need not change with it, and
    /// <c>ResolvedSession.Rescheduled</c> is true for a day-only move.</para>
    /// </summary>
    public DayOfWeek? NewDay { get; set; }

    /// <summary>New start time when <see cref="Kind"/> is <see cref="ExceptionKind.Rescheduled"/> (null = keep the usual start).</summary>
    public TimeOnly? NewStart { get; set; }

    /// <summary>New end time when <see cref="Kind"/> is <see cref="ExceptionKind.Rescheduled"/> (null = keep the usual end).</summary>
    public TimeOnly? NewEnd { get; set; }

    /// <summary>
    /// Substitute facilitator(s) for this occurrence when <see cref="Kind"/> is
    /// <see cref="ExceptionKind.Covered"/> — they replace the session's usual facilitators that week.
    /// </summary>
    public List<Guid>? CoverFacilitatorIds { get; set; }

    /// <summary>Optional reason, e.g. "Facilitator away" or "Venue clash".</summary>
    public string? Note { get; set; }
}
