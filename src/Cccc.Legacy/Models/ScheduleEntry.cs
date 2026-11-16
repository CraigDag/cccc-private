using System.Text.Json.Serialization;

namespace CCCCScheduler.Models;

/// <summary>
/// One weekly-recurring session in a room: which day, the time span, what it is,
/// and which group is using the space. The whole timetable is just the collection
/// of these across all rooms, grouped by day and start time.
/// </summary>
public sealed class ScheduleEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DayOfWeek Day { get; set; } = DayOfWeek.Monday;

    /// <summary>
    /// <b>The Monday of the one calendar week this session runs in</b> — or <c>null</c> in a project
    /// whose weeks are not separate, where the single set of sessions is the pattern every week is
    /// drawn from. See <see cref="Building.WeeksAreSeparate"/>, which says which of the two a project
    /// is, and <see cref="Building.SessionsInWeek"/>, the one place the distinction is read.
    ///
    /// <para><b>Schema 13, and the owner's call of 2026-09-14</b>, made having watched an edit to
    /// week 39 change week 38: <i>"I thought we had discussed each week being its own individual set
    /// in the scope of a 1 year .cccc file?"</i> It was not — until this property, a session carried a
    /// <see cref="Day"/> and no date at all, so week 39 <i>was</i> week 38, the same objects seen
    /// through different dates, in both directions and for ever.</para>
    ///
    /// <para><b>Always a Monday.</b> Every write normalises through <c>WeekLabel.MondayOf</c> and
    /// <c>ProjectStore.Validate</c> repairs one that is not, because the whole model rests on this
    /// value being comparable by equality: a session stamped with a Wednesday would be in no week at
    /// all, visible nowhere, deleted by nothing, and carried silently through every save.</para>
    ///
    /// <para><b>It does not replace <see cref="Day"/>, and the two are not derivable from each
    /// other.</b> This names the week; <see cref="Day"/> names the day inside it. A one-off change can
    /// still move an occurrence to another day of its own week — see <see cref="SessionException
    /// .NewDay"/> — which is a different fact from the week the session belongs to.</para>
    /// </summary>
    public DateOnly? WeekMonday { get; set; }

    public TimeOnly Start { get; set; } = new(9, 0);

    public TimeOnly End { get; set; } = new(10, 0);

    /// <summary>Activity, e.g. "Trapeze – Intermediate".</summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// The catalogue <see cref="ClassEntry"/> this session is an occurrence of, or <c>null</c> for
    /// none. <b>Additive: <see cref="Title"/> stays, and is still what every reader displays.</b> The
    /// grid, both exports, the ICS summary, the conflicts panel and every confirmation go on naming a
    /// booking exactly as the timetable does; this is what lets a tally group by identity instead.
    ///
    /// <para><b>Why it exists.</b> <see cref="Title"/> is free text and the catalogue only populates a
    /// dropdown, so hours-per-activity was previously only as reliable as consistent typing — and the
    /// course administrator's own sheet carries <i>Floor Acrobatic</i> beside <i>Floor Acrobatics</i>,
    /// two spellings of one activity differing only in case, and <i>Conditioning</i> with a double
    /// space. Three tallies where the school has one activity.</para>
    ///
    /// <para><b>The two must never be derived from each other.</b> Two sessions may show the same
    /// title and carry different links, and — after a catalogue merge, deliberately — two may show
    /// different titles and share one link. Merging the catalogue is not permission to rewrite what a
    /// session is called, so anything that re-derived the link from the title on a copy or an edit
    /// would quietly undo a merge the user made on purpose.</para>
    ///
    /// <para><c>null</c> is a legitimate, persistable "unlinked" state, not a gap to be filled in: it
    /// is how every session in every project arrives before the migration, how an untitled session
    /// stays, and what deleting a class through Manage ▸ Classes leaves behind. An id naming a class
    /// the project does not contain is corruption rather than a state to honour, and is scrubbed back
    /// to <c>null</c> on load — never repaired by inventing an entry, and never by touching the
    /// title. See the orphan scrub in <c>ProjectStore.Validate</c>.</para>
    /// </summary>
    public Guid? ClassId { get; set; }

    /// <summary>The managed <see cref="Group"/> using the space (null = untagged).</summary>
    public Guid? GroupId { get; set; }

    /// <summary>
    /// Whether this session is one cohort's, several cohorts', or the whole school's. Defaults to
    /// <see cref="SessionScope.Group"/>, which is what every session written before schema 11 meant.
    /// </summary>
    public SessionScope Scope { get; set; }

    /// <summary>
    /// The cohorts this session is on for, meaningful only when <see cref="Scope"/> is
    /// <see cref="SessionScope.Groups"/>. Ids rather than names so renaming a cohort in
    /// Manage ▸ Groups does not silently detach it from its own class.
    ///
    /// <para><b>Kept, not cleared, when the scope moves off <see cref="SessionScope.Groups"/></b> —
    /// it is only ever read under that scope, so a stale value costs nothing and switching back is
    /// lossless. The same holds for <see cref="GroupId"/> under the other two scopes.</para>
    /// </summary>
    public List<Guid> GroupIds { get; set; } = new();

    /// <summary>
    /// The programme this session is on for, meaningful only when <see cref="Scope"/> is
    /// <see cref="SessionScope.Programme"/> — schema 17. Kept, not cleared, when the scope moves off
    /// it, for the same lossless reason <see cref="GroupIds"/> is.
    /// </summary>
    public Guid? ProgrammeId { get; set; }

    /// <summary>
    /// The cohorts this session is on for, under whichever scope it has. Empty means no cohort —
    /// which is a real state (an External Hire booking) and never means "everybody".
    ///
    /// <para><b>A <see cref="SessionScope.WholeBuilding"/> session yields nothing here</b>, because it
    /// names no cohorts; it is on for all of them, which is <see cref="IsFor"/>'s question and not
    /// this one. Callers wanting "is it on for X" must ask <see cref="IsFor"/>, never
    /// <c>CohortIds.Contains</c>.</para>
    ///
    /// <para><b>Derived, and deliberately not persisted.</b> It is computed from
    /// <see cref="Scope"/>, <see cref="GroupId"/> and <see cref="GroupIds"/> on every read, all three
    /// of which ARE written; without <see cref="JsonIgnoreAttribute"/> here, System.Text.Json writes
    /// read-only properties by default and every session in every newly-saved <c>project.json</c>
    /// would carry a fourth, redundant array duplicating one of the other three — inert on read, but a
    /// file-format change nobody decided, on a schema this plan just bumped.</para>
    /// </summary>
    [JsonIgnore]
    public IEnumerable<Guid> CohortIds => Scope switch
    {
        SessionScope.Groups or SessionScope.Programme => GroupIds,
        SessionScope.Group when GroupId is Guid id => new[] { id },
        _ => Array.Empty<Guid>(),
    };

    /// <summary>
    /// Whether this session is on for <paramref name="groupId"/>. <b>The one implementation</b>, called
    /// by every filter — two would let the grid and the dock describe different weeks.
    ///
    /// <para>The catch-all covers <see cref="SessionScope.Group"/> itself and any scope value this
    /// build does not recognise — narrower rather than wider, the same direction
    /// <c>ScheduleResolver.AppliesTo</c> takes for an unrecognised <c>HolidayScope</c>: it can only ever
    /// answer <c>true</c> for the one cohort <see cref="GroupId"/> names, never for every cohort. A
    /// session wrongly invisible to a filter is one somebody notices missing; a session wrongly visible
    /// to every filter is one nobody would think to check.</para>
    /// </summary>
    public bool IsFor(Guid groupId) => Scope switch
    {
        SessionScope.WholeBuilding => true,
        SessionScope.Groups or SessionScope.Programme => GroupIds.Contains(groupId),
        _ => GroupId == groupId,
    };

    /// <summary>
    /// <b>Which room this session happens in, or <c>null</c> for a session whose space has not been
    /// decided yet.</b>
    ///
    /// <para>Until schema 10 a session had no room of its own: it was in a room because it sat in
    /// that room's <c>Schedule</c> list. That made the room the session's <em>owner</em> rather than
    /// one of its attributes — which is why deleting a room destroyed a term's teaching, why a
    /// session could not be authored before its space was chosen, and why a real timetable with no
    /// rooms in it could not be represented at all.</para>
    ///
    /// <para><b>Null is a legitimate state and not a broken link.</b> An id naming a room the project
    /// has not got is the broken one; the two are kept apart the same way <c>Room.FloorIndex</c>
    /// keeps them apart, and for the same reason.</para>
    /// </summary>
    public Guid? RoomId { get; set; }

    /// <summary>The <see cref="Facilitator"/>(s) teaching this session — a class may be co-taught.</summary>
    public List<Guid> FacilitatorIds { get; set; } = new();

    /// <summary>
    /// <b>The facilitators who are in this session for only part of it</b> — schema 23, Craig's ask of
    /// 2026-11-07: Circus Disciplines runs 13:00–15:00, and somebody teaching elsewhere until 14:00
    /// is in it from 14:00. <b>Somebody not listed here is in the whole session</b>, which is every
    /// facilitator on every session written before schema 23, and still almost every one.
    ///
    /// <para><b>A supplement to <see cref="FacilitatorIds"/>, never a second list of who teaches.</b>
    /// A part naming somebody not on <see cref="FacilitatorIds"/> means nothing and is dropped on
    /// load. Read it through <c>FacilitatorParts</c> and never directly: that is where a part is
    /// shifted with a moved session and clamped to the session's own hours.</para>
    /// </summary>
    public List<FacilitatorPart> FacilitatorParts { get; set; } = new();

    public string? Notes { get; set; }

    /// <summary>Optional per-session colour override.</summary>
    public string? ColourHex { get; set; }

    /// <summary>Provisional / "hold" (e.g. an External Hire enquiry not yet confirmed).</summary>
    public bool Provisional { get; set; }

    /// <summary>
    /// One-off, week-specific deviations from this weekly session (a cancellation or a time change
    /// for a single week). The master timetable stays unchanged; see <see cref="SessionException"/>.
    /// </summary>
    public List<SessionException> Exceptions { get; set; } = new();

    /// <summary>
    /// <b>Students put on this session BY HAND, on top of its groups' own</b> — schema 17. Until then
    /// this was the whole roster, ticked one student at a time; now the roster is the session's
    /// groups' students plus these, less <see cref="ExcludedStudentIds"/>, and
    /// <c>SessionRoster.For</c> is the one place that sum is done. Craig's call, 2026-09-24: a BA Y1
    /// class lists the BA Y1 students without anybody ticking them.
    ///
    /// <para><b>Never read directly as "who is on this class".</b> A reader that did would see
    /// nobody on a class its whole group attends.</para>
    /// </summary>
    public List<Guid> StudentIds { get; set; } = new();

    /// <summary>
    /// <b>Students of this session's groups who are left OFF it</b> — schema 17. Enrolment's other
    /// half: everybody in BA Year One except Jun. See <see cref="StudentIds"/>.
    /// </summary>
    public List<Guid> ExcludedStudentIds { get; set; } = new();

    /// <summary>Per-week attendance registers taken for this session.</summary>
    public List<AttendanceRecord> Attendance { get; set; } = new();
}
