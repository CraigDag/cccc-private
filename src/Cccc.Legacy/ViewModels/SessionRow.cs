using CCCCScheduler.Helpers;
using CCCCScheduler.Models;

namespace CCCCScheduler.ViewModels;

/// <summary>
/// A flat, grid-friendly view of one <see cref="ScheduleEntry"/> together with its room and the
/// resolved names of its group and facilitators. Rebuilt from the model whenever anything changes,
/// so it can stay read-only.
/// </summary>
public sealed class SessionRow
{
    /// <summary>
    /// <paramref name="strandIds"/> is required, not defaulted — <c>ExportFilters.Apply</c> states the
    /// rule this constructor now follows too: "every parameter is required rather than defaulted; a
    /// caller that quietly omitted one would widen somebody's timetable rather than fail to build."
    /// A defaulted <c>strandIds</c> used to fall back to deriving a single strand from
    /// <paramref name="strandId"/>, which is exactly wrong for a <see cref="SessionScope.WholeBuilding"/>
    /// or <see cref="SessionScope.Groups"/> row — a future row-builder that set <c>strandId</c> and
    /// forgot the plural answer would compile clean and produce a whole-building session invisible to
    /// every strand filter on the grid while the dock kept counting it. See
    /// <see cref="CCCCScheduler.ViewModels.ScheduleOps.StrandsFor"/> for how to build the value.
    /// </summary>
    public SessionRow(Room? room, ScheduleEntry entry, string? groupName, string? groupColourHex,
        string facilitatorNames, IReadOnlySet<Guid> strandIds, string floorName = "",
        TimeOnly? startOverride = null, TimeOnly? endOverride = null,
        bool rescheduled = false, string? weekNote = null, bool cancelled = false,
        Guid? strandId = null, string? strandName = null, bool needsCover = false,
        string? className = null, string? classColourHex = null,
        DayOfWeek? dayOverride = null, WorkloadKind workload = WorkloadKind.Counted,
        IReadOnlySet<Guid>? rosterIds = null, bool covered = false, string coveredForNames = "",
        bool isGuest = false, bool hasGuest = false)
    {
        Room = room;
        Entry = entry;
        GroupName = groupName ?? "";
        GroupColourHex = groupColourHex;
        FacilitatorNames = facilitatorNames;
        FloorName = floorName;
        _start = startOverride ?? entry.Start;
        _end = endOverride ?? entry.End;
        _day = dayOverride ?? entry.Day;
        IsRescheduled = rescheduled;
        WeekNote = weekNote;
        IsCancelled = cancelled;
        StrandId = strandId;
        StrandName = strandName;
        NeedsCover = needsCover;
        IsCovered = covered;
        CoveredForNames = coveredForNames;
        ClassName = className;
        ClassColourHex = classColourHex;
        Workload = workload;
        StrandIds = strandIds;
        RosterIds = rosterIds ?? entry.StudentIds.ToHashSet();
        IsGuest = isGuest;
        HasGuest = isGuest || hasGuest;
    }

    // Not readonly only so that PartFor can set them on its copy; nothing else writes them.
    private TimeOnly _start;
    private TimeOnly _end;
    private readonly DayOfWeek _day;
    private SessionRow? _original;

    /// <summary>
    /// <b>The row this one was drawn from</b>: itself, unless this is <see cref="PartFor"/>'s copy
    /// showing one facilitator's part of the class. The selection always holds the original, so a
    /// block drawn short on one person's calendar selects, edits and deletes the class itself.
    /// </summary>
    public SessionRow Original => _original ?? this;

    /// <summary>
    /// <b>Whether this row is one person's part of the class</b> rather than the class's own times —
    /// a block drawn from it cannot be dragged, because a drag would have to guess whether it meant
    /// the class or the person.
    /// </summary>
    public bool IsOnePersonsPart => _original is not null;

    /// <summary>Whether two rows are the same row, or one is the other's part. Null is nobody.</summary>
    public bool Is(SessionRow? other) => other is not null && ReferenceEquals(Original, other.Original);

    /// <summary>The time this row starts, which for a part is the person's own.</summary>
    public TimeOnly StartTime => _start;

    /// <summary>The time this row ends, which for a part is the person's own.</summary>
    public TimeOnly EndTime => _end;

    /// <summary>
    /// <b>A copy of this row drawn at one facilitator's own times</b> (schema 23), for a calendar or
    /// sheet that is one person's week. Every other value is this row's, so the class, the room, the
    /// notes and the register are the class's own.
    /// </summary>
    public SessionRow PartFor(TimeOnly start, TimeOnly end)
    {
        var part = (SessionRow)MemberwiseClone();
        part._start = start;
        part._end = end;
        part._original = Original;
        return part;
    }

    /// <summary>Null for a session with no room, or whose room no longer exists. See <see cref="RoomName"/>.</summary>
    public Room? Room { get; }
    public ScheduleEntry Entry { get; }

    /// <summary>"" for a room-less session — a blank cell, the same treatment a room with no floor gets.</summary>
    public string RoomName => Room?.Name ?? "";
    public string FloorName { get; }

    /// <summary>
    /// <b>The day this row's occurrence falls on, which is not always its session's own day.</b> Read
    /// the same way <see cref="StartText"/> reads its time: the week-aware builder passes
    /// <c>ResolvedSession.Day</c>, and the template basis passes nothing and gets
    /// <see cref="ScheduleEntry.Day"/>. Since schema 12 a one-off change can move a class to another
    /// day of one week, so the two differ for a moved occurrence — and everything keyed on the day
    /// (<see cref="DayOrder"/>, <see cref="DayName"/>, <c>ScheduleOps.PassesFilters</c>,
    /// <c>WeekGridLayout.Arrange</c>, both grid PDFs) reads THIS and follows.
    /// </summary>
    public DayOfWeek Day => _day;
    public string DayName => Day.ToString();
    /// <summary>Monday = 0 … Sunday = 6, for a Monday-first sort.</summary>
    public int DayOrder => ((int)Day + 6) % 7;

    /// <summary>True when this row is a one-off reschedule — its times differ from the template, its
    /// day does (schema 12), or both.</summary>
    public bool IsRescheduled { get; }
    /// <summary>True when this occurrence is cancelled for the shown week (week view only).</summary>
    public bool IsCancelled { get; }
    /// <summary>True when every facilitator on this session is away that week and nobody has it yet.</summary>
    public bool NeedsCover { get; }

    /// <summary>
    /// Somebody else is teaching this occurrence this week — a <c>Covered</c> one-off.
    /// <see cref="FacilitatorNames"/> is then the people in the room, and
    /// <see cref="CoveredForNames"/> the assigned people they stand in for.
    /// </summary>
    public bool IsCovered { get; }

    /// <summary>
    /// On a covered occurrence, the assigned facilitators who are not in the room ("Carl Dahl"), so
    /// a printed block can say "MR for CD". Empty on anything else.
    /// </summary>
    public string CoveredForNames { get; }
    /// <summary>Optional per-occurrence note (reason for a reschedule/cover).</summary>
    public string? WeekNote { get; }

    public string StartText => TimeSlots.Text(_start);
    public string EndText => TimeSlots.Text(_end);
    public int StartMinutes => _start.Hour * 60 + _start.Minute;
    public int EndMinutes => _end.Hour * 60 + _end.Minute;

    public string Title => Entry.Title;
    public string? Notes => Entry.Notes;

    /// <summary>
    /// The name of the catalogue entry this session is linked to, or <c>null</c> when it is linked to
    /// none. Resolved at build time so filtering needs no lookup, exactly as
    /// <see cref="StrandName"/> is.
    ///
    /// <para><b>It is never displayed and it is not a second title.</b> <see cref="Title"/> is what
    /// every reader shows; this is what the session <em>is</em>, which after a catalogue merge is
    /// deliberately a different string — two sessions can show <i>Floor Acrobatic</i> and <i>Floor
    /// Acrobatics</i> and share one class. It exists so <see cref="ScheduleOps.Matches"/> can filter on
    /// identity as well as spelling; without it, <c>Export filtered report as PDF</c> silently omitted
    /// half of every merged class.</para>
    /// </summary>
    public string? ClassName { get; }

    /// <summary>Notes prefixed with this week's status (cancelled/moved/cover) when in week view.</summary>
    public string NotesAndStatus =>
        string.Join("  ·  ", new[] { WeekNote, Notes }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public string GroupName { get; }
    public string? GroupColourHex { get; }

    /// <summary>
    /// <b>The linked class's own colour, and since 2026-09-01 the ONLY thing a session block is
    /// coloured by.</b> Null when the session is linked to no class, or to one the project no longer
    /// holds — the callers fall back to a neutral, never to the group's colour.
    ///
    /// <para>The owner's decision, on the real BA timetable: <i>"the colour coding on the calendar is
    /// coded by the class colour and not the cohort colour across all views"</i>. That sheet has no
    /// rooms and one cohort, so both of the bases this replaced painted every block on the week
    /// identically — the class is the only thing in a real timetable that distinguishes one block from
    /// the next.</para>
    /// </summary>
    public string? ClassColourHex { get; }

    /// <summary>
    /// <b>The linked class's working-load kind</b>, resolved at build time so the filter needs no
    /// lookup — <see cref="SessionWorkload.Of"/> is where the rule lives, including that a session
    /// with no class, or one naming a class the project no longer holds, is
    /// <see cref="WorkloadKind.Counted"/>.
    ///
    /// <para><b>Defaulted, unlike <see cref="StrandIds"/>, and deliberately.</b> A row-builder that
    /// omits it gets <see cref="WorkloadKind.Counted"/>, which is the answer that puts the session on
    /// the week — exactly what every row did before this property existed. The strand argument next
    /// to it is required because omitting it produced a WRONG answer for existing data; omitting this
    /// one produces the answer this app gave yesterday, which is the safe direction for a dimension
    /// whose only job is to take sessions away.</para>
    ///
    /// <para>Read by <see cref="ScheduleOps.PassesFilters"/> for the <i>Open sessions</i> filter and by
    /// nothing else. It is not shown anywhere: what a person reads about a session's working load is
    /// the marker on the students pane, which comes from <c>StudentWeeklyLoad.WhyUncounted</c>.</para>
    /// </summary>
    public WorkloadKind Workload { get; }

    public string FacilitatorNames { get; }

    /// <summary>The strand of this session's group, derived at build time so filtering needs no lookup.</summary>
    public Guid? StrandId { get; }
    public string? StrandName { get; }

    /// <summary>
    /// Every strand this session is on for — <see cref="ScheduleOps.StrandsFor"/>'s answer, resolved
    /// at build time. <b>What the strand filter actually asks</b> (see
    /// <see cref="ScheduleOps.PassesFilters"/>); <see cref="StrandId"/> above stays the single strand
    /// shown on screen and is not enough on its own once a session can be on for more than one — a
    /// <see cref="SessionScope.WholeBuilding"/> or <see cref="SessionScope.Groups"/> session has none
    /// or several, not one.
    /// </summary>
    public IReadOnlySet<Guid> StrandIds { get; }

    /// <summary>
    /// <b>The students on this session</b> — <see cref="SessionRoster.For"/>'s answer, resolved at
    /// build time so the student filter needs no lookup, exactly as <see cref="StrandIds"/> is for the
    /// strand filter. Schema 17: the class's groups' students, plus and less the hand changes.
    ///
    /// <para>Defaulted to the hand-kept list for a row built without a building to ask, which only a
    /// test does; <c>MainViewModel.BuildRow</c> always passes the real answer.</para>
    /// </summary>
    public IReadOnlySet<Guid> RosterIds { get; }

    public bool IsProvisional => Entry.Provisional;

    /// <summary>
    /// <b>A visiting group's class</b> — <see cref="GuestSessions.IsGuestOnly"/>'s answer, resolved at
    /// build time like <see cref="StrandIds"/>, so the Guests filter needs no lookup. False on a row
    /// built without a building to ask, which only a test does.
    /// </summary>
    public bool IsGuest { get; }

    /// <summary>
    /// <b>A class a visiting group is on</b>, alone or jointly with the school's own groups —
    /// <see cref="GuestSessions.Involves"/>'s answer, and what <i>Guests only</i> keeps. Every guest
    /// class (<see cref="IsGuest"/>) is one.
    /// </summary>
    public bool HasGuest { get; }
}
