namespace CCCCScheduler.Models;

/// <summary>
/// <b>What kind of event this is</b>, a fixed list: Design's seven and Other (Craig, 2026-09-23).
/// Each has its own colour and icon on screen. The order is the order the form and the filter panel
/// offer them in, and it is stored by NAME, so reordering this list never changes what a file means.
/// </summary>
public enum EventCategory
{
    Meeting,
    Performance,
    Audition,
    Deadline,
    Trip,
    Visitor,
    HostedGroup,
    Other,
}

/// <summary>
/// <b>Who an event is for.</b> <see cref="Everyone"/> is none of the three, which is what makes it the
/// default: an event nobody chose an audience for is an event for everybody, and a file whose key is
/// absent reads the same way. Students are accounts tagged Student, Staff accounts tagged Staff, and
/// Facilitators accounts LINKED to a facilitator in the open timetable — the link, never the address.
///
/// <para><b>Administrators see every event whatever this says, and Editors see every event but one
/// kind</b>: an event for <see cref="Administrators"/> and nobody else. Those rules live in
/// <see cref="ViewModels.EventViewer"/>, not here.</para>
/// </summary>
[Flags]
public enum EventAudience
{
    Everyone = 0,
    Students = 1,
    Facilitators = 2,
    Staff = 4,

    /// <summary>
    /// <b>The administrators — and, on its own, ONLY them</b>, Editors included. Craig, 2026-09-25:
    /// <i>"I know they see everything but there may be some things that only they should see."</i>
    /// Beside another group it adds nobody, since administrators already see every event: an event
    /// that students may read is not a secret from an editor. Schema 19, because a schema-18 build
    /// reading this word calls the whole file unreadable.
    /// </summary>
    Administrators = 8,
}

/// <summary>How an event repeats. Only the rules the form offers; a file naming anything else is repaired to a one-off.</summary>
public enum RepeatKind
{
    /// <summary>Every <see cref="EventRepeat.Every"/> days.</summary>
    Daily,

    /// <summary>On the chosen <see cref="EventRepeat.Days"/>, every <see cref="EventRepeat.Every"/> weeks.</summary>
    Weekly,

    /// <summary>On the first date's day of the month (the 15th), every <see cref="EventRepeat.Every"/> months.</summary>
    MonthlyOnDate,

    /// <summary>On the first date's weekday of the month (the second Tuesday), every <see cref="EventRepeat.Every"/> months.</summary>
    MonthlyOnWeekday,
}

/// <summary>
/// <b>A repeat rule, and it always has an end</b>: <see cref="Until"/>, a number of times in
/// <see cref="Count"/>, or both, whichever comes first. The spec's rule, so nothing runs on past a
/// year that has turned over. A rule arriving with neither is given an end by
/// <c>ProjectStore.Validate</c> rather than being allowed to run for ever.
/// </summary>
public sealed class EventRepeat
{
    public RepeatKind Kind { get; set; } = RepeatKind.Weekly;

    /// <summary>Every how many days, weeks or months. Below 1 reads as 1.</summary>
    public int Every { get; set; } = 1;

    /// <summary>
    /// The weekdays a <see cref="RepeatKind.Weekly"/> rule falls on. Empty means the first date's own
    /// weekday, which is what a one-day weekly rule means anyway.
    /// </summary>
    public List<DayOfWeek> Days { get; set; } = new();

    /// <summary>The last date an occurrence may START on, inclusive.</summary>
    public DateOnly? Until { get; set; }

    /// <summary>How many occurrences at most, counting from the first, and counting skipped ones.</summary>
    public int? Count { get; set; }
}

/// <summary>
/// <b>An event: something on the school's calendar that is not a class.</b> A staff meeting, a show,
/// an audition day, a deadline, a trip, a visitor, a group the school hosts.
///
/// <para><b>Nothing that counts classes ever reads these</b> — facilitator hours and limits, student
/// load, prep time, clashes, cover, registers, rooms, the admin dock. They live in their own list,
/// <see cref="ProjectFile.Events"/>, and never in <see cref="Building.Sessions"/>, which is what keeps
/// every earlier decision about what counts exactly as it was.</para>
///
/// <para><b>Dates and times are the school's wall clock</b> — a <see cref="DateOnly"/> and two
/// <see cref="TimeOnly"/>s, never an instant — so a 09:00 meeting is at 09:00 on either side of the
/// clocks changing, as every class already is.</para>
/// </summary>
public sealed class SchoolEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Title { get; set; } = "";

    public EventCategory Category { get; set; } = EventCategory.Meeting;

    /// <summary>The first day of the first occurrence.</summary>
    public DateOnly Date { get; set; }

    /// <summary>
    /// The last day of the first occurrence when it runs over several days, or null for one day. Every
    /// later occurrence runs for the same number of days.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>True when the event has no times. Then <see cref="Start"/> and <see cref="End"/> are ignored.</summary>
    public bool AllDay { get; set; }

    public TimeOnly Start { get; set; } = new(9, 0);

    public TimeOnly End { get; set; } = new(10, 0);

    /// <summary>Null for a one-off.</summary>
    public EventRepeat? Repeat { get; set; }

    /// <summary>
    /// The dates of the series nobody wants any more, each the date the occurrence would have STARTED
    /// on. <i>This one</i> — changed or deleted — adds its date here; a changed one is then a one-off
    /// event of its own.
    /// </summary>
    public List<DateOnly> Skipped { get; set; } = new();

    /// <summary>
    /// <b>Leave out any occurrence that starts on a day the whole building is closed.</b> Off by
    /// default, because admin work carries on when the building is shut to students.
    /// </summary>
    public bool SkipClosures { get; set; }

    public EventAudience Audience { get; set; } = EventAudience.Everyone;

    /// <summary>A room in this timetable, or null.</summary>
    public Guid? RoomId { get; set; }

    /// <summary>A place in words, for anything off site.</summary>
    public string Place { get; set; } = "";

    /// <summary>Facilitators named on the event, for information only. They change nobody's hours.</summary>
    public List<Guid> FacilitatorIds { get; set; } = new();

    /// <summary>Students named on the event, for information only.</summary>
    public List<Guid> StudentIds { get; set; } = new();

    public string Note { get; set; } = "";

    /// <summary>How many days one occurrence covers: 1 for a single day.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int SpanDays => EndDate is { } end && end > Date ? end.DayNumber - Date.DayNumber + 1 : 1;
}
