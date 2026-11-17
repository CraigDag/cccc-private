using System.Text.Json.Serialization;

namespace CCCCScheduler.Models;

/// <summary>
/// Root object serialized to <c>project.json</c>. <see cref="SchemaVersion"/> is the
/// migrate-on-load lever for future format changes.
/// </summary>
public sealed class ProjectFile
{
    /// <summary>
    /// The highest <see cref="SchemaVersion"/> this build understands.
    ///
    /// <para><b>9 made <see cref="Room.FloorIndex"/> optional</b>, so a room can be on no floor and a
    /// project can have no floors at all. It is a version wall like every other bump — a schema-8
    /// build meets <c>"FloorIndex"</c> simply absent from a room and reads it as floor 0, silently
    /// moving every floorless room onto the ground floor and saving the file back that way.</para>
    ///
    /// <para><b>10 gives every <see cref="ScheduleEntry"/> its own <see cref="ScheduleEntry.RoomId"/></b>
    /// — until then a session's room was never stored on the session itself, only implied by which
    /// room's <see cref="Room.LegacySchedule"/> list held it. The migration sets every entry's
    /// <c>RoomId</c> to the room it is found in. <b>"Nothing reads the property yet" stopped being
    /// true at Task 3</b>, which moved the sessions themselves off the room and onto
    /// <see cref="Building.Sessions"/> — so <c>RoomId</c>, not which room's list an entry sat inside,
    /// is now the only record of a session's room, and every room-keyed surface (<c>SessionsIn</c>,
    /// <c>SessionsByRoom</c>, the room grid, the floor plan, deleting a room without destroying its
    /// timetable) reads it. It is still a version wall like every other one: a schema-9 build has no
    /// such property on <see cref="ScheduleEntry"/> at all, so opening this file on one and saving it
    /// back would silently drop every session's room.</para>
    ///
    /// <para><b>11 gives a <see cref="ClassEntry"/> a <see cref="ClassEntry.Workload"/> kind in place
    /// of the old <c>CountsAsWorkload</c> bool</b>, and a <see cref="ScheduleEntry"/> its own
    /// <see cref="ScheduleEntry.Scope"/> and <see cref="ScheduleEntry.GroupIds"/>. The migration reads
    /// the bool through <see cref="ClassEntry.LegacyCountsAsWorkload"/>; nothing becomes
    /// <see cref="WorkloadKind.DropIn"/> by migration, because no schema-10 file knew the difference.
    /// <see cref="ScheduleEntry.Scope"/> needs no migration block at all: <see cref="SessionScope.Group"/>
    /// is ordinal 0, so a session with no <c>Scope</c> key already deserialises to exactly what it
    /// meant.</para>
    ///
    /// <para><b>12 gives a <see cref="SessionException"/> a <see cref="SessionException.NewDay"/></b>,
    /// so a one-off change can move a class to another day of that same week rather than only to
    /// another time of the same day. It needs no migration block: an absent key is null, and null is
    /// "the usual day", which is exactly what every schema-11 reschedule meant. It is a version wall
    /// like every other one all the same — a schema-11 build has no such property, so opening this
    /// file there and saving it back would put the class silently back on the day it was moved off,
    /// which is why <c>ProjectStore.Validate</c> refuses it outright instead. <see cref="ScheduleEntry.Day"/>
    /// is untouched: the master timetable does not move, one week's occurrence does.</para>
    ///
    /// <para><b>13 gives a <see cref="ScheduleEntry"/> its own <see cref="ScheduleEntry.WeekMonday"/>
    /// and a <see cref="Building"/> its <see cref="Building.WeeksAreSeparate"/></b>, so that each
    /// calendar week can hold a timetable of its own instead of every week drawing the one shared
    /// pattern. The owner's call, 2026-09-14, having watched an edit to week 39 change week 38.
    ///
    /// <para><b>Its migration block does nothing, and that is the decision rather than an
    /// omission.</b> A schema-12 file arrives with <see cref="Building.WeeksAreSeparate"/> false and
    /// every <see cref="ScheduleEntry.WeekMonday"/> null, which is precisely what it already was: one
    /// pattern, every week. It behaves identically until somebody converts it deliberately, through
    /// <c>MainViewModel.SeparateTheWeeks</c>. There is nothing else this migration could honestly do
    /// — the pattern carries no date anywhere, so any week it chose would be invented, and a term's
    /// timetable silently appearing on the wrong weeks is the failure this whole format change exists
    /// to prevent.</para>
    ///
    /// <para>It is a version wall like every other one all the same. A schema-12 build has neither
    /// property, so opening a converted file there and saving it back would drop every session's week
    /// and collapse a year of separate weeks into one undifferentiated pile — the single worst thing
    /// that could happen to this file — which is why <c>ProjectStore.Validate</c> refuses it outright
    /// instead.</para></para>
    ///
    /// <para><b>14 is TWO tranches of work under one number, because both landed in one release.</b>
    /// It turns the project's one <c>Term</c> into a <see cref="Terms"/> list and gives a
    /// <see cref="Building"/> its <see cref="Building.FrozenWeeks"/> — the two halves of the owner's
    /// <i>"one file per academic year"</i>, 2026-09-13/14 — and it gives an
    /// <see cref="AttendanceRecord"/> a second list, <see cref="AttendanceRecord.ExcusedStudentIds"/>,
    /// plus the two fields that say who filed it and when. Two independent branches each bumped 13 to
    /// 14 overnight on 2026-09-15 and neither knew of the other; they share the number rather than
    /// taking 14 and 15 in turn, because a schema number is a wall against OLDER builds and no build
    /// ever shipped between them. Nothing reads a "14" and asks which half it has: every property here
    /// is absent-means-what-it-meant, so a file from either branch alone loads correctly under both.
    ///
    /// <para><b>The term list migrates, and it is the only migration block since 11.</b> A file
    /// arriving with a <c>"Term"</c> key lands it on <see cref="LegacyTerm"/>; the block below moves
    /// it into <see cref="Terms"/> as the single term it always was and clears the old key. A file
    /// with no term at all migrates to an empty list, which means exactly what a null
    /// <c>Term</c> meant. Nothing is invented: a school that had one term still has one term, and
    /// the second one it probably wants is typed in <i>Term &amp; holidays</i>.</para>
    ///
    /// <para><b><see cref="Building.FrozenWeeks"/> needs no block</b>, for schema 13's own reason: a
    /// schema-13 file has no frozen weeks because no build before this one could freeze one, and an
    /// absent key is an empty list, which is precisely what it was.</para>
    ///
    /// <para><b>The register's own absence is not a decision but an identity.</b> A schema-13 file has
    /// no excused key, which deserialises to an empty list — meaning every enrolled student not
    /// present was absent with no word, which is exactly and entirely what those registers meant,
    /// because no build that wrote them had a third mark to record.
    /// <see cref="AttendanceRecord.FiledBy"/> and <see cref="AttendanceRecord.FiledAt"/> are null on
    /// such a file, which reads as <em>nobody wrote this down</em> rather than as an epoch timestamp
    /// somebody might believe.</para>
    ///
    /// <para>It is a version wall like every other one, and now for three reasons at once. A
    /// schema-13 build has none of these properties, so it would read a multi-term project as having
    /// <b>no term at all</b> — no year, no targets, every report falling back to one week — write back
    /// a file with every frozen week's sessions still in it and nothing saying which weeks they belong
    /// to, and drop every excused mark and every record of who filed what, silently re-marking a
    /// term's injured students as no-shows. <c>ProjectStore.Validate</c> refuses it outright
    /// instead.</para></para>
    ///
    /// <para><b>15 gives a <see cref="ClassEntry"/> its <see cref="ClassEntry.LegacyHasPrepTime"/> box.</b>
    /// Craig, 2026-09-18: <i>"Classes need an extra checkbox inside the creation which has 'Prep
    /// Time' … Prep time is added to weekly load to get the total load for a week."</i> A class with
    /// the box ticked earns whoever teaches it half the session's length again, and
    /// <c>PrepTime</c> is that rule.</para>
    ///
    /// <para><b>It needs no migration block</b>, for the reason the register's third mark needed
    /// none: an absent key deserialises to <c>false</c>, and false is exactly what every class in
    /// every existing project means — no build before this one could record prep time, so no
    /// facilitator was ever owed any. Nothing is inferred from a class's name, its area or its
    /// length; the school says which classes need preparing and nothing else can know.</para>
    ///
    /// <para><b>And it is a wall all the same, for the ordinary reason.</b> A schema-14 build has no
    /// such property, so opening a file where the school had ticked the box and saving it back would
    /// drop every tick — silently, since the hours it changes are a number on a dashboard rather than
    /// anything that would look broken. The one bump this app has made that costs an older build
    /// nothing to READ is still one that costs it something to WRITE, which is what the wall is
    /// about.</para>
    ///
    /// <para><b>16 gives a project its <see cref="Events"/></b>: the staff meetings, shows, auditions,
    /// deadlines, trips, visitors and hosted groups that go on the calendar beside the classes and
    /// never count as one. The administrator's ask, 2026-09-23; <see cref="SchoolEvent"/> carries the
    /// rules.</para>
    ///
    /// <para><b>It needs no migration block</b>: a schema-15 file has no <c>"Events"</c> key, that
    /// reads as an empty list, and an empty list is exactly what every earlier file meant — no build
    /// before this one could record an event.</para>
    ///
    /// <para><b>A wall like every other one, and an expensive one to cross backwards.</b> A
    /// schema-15 build has no such property, so opening this file there and saving it back would
    /// delete every event the school had entered, silently, since nothing on a schema-15 screen would
    /// look any different. The laptop rolls itself back when a night's update fails, which is exactly
    /// when an older build meets a newer file, so <c>ProjectStore.Validate</c> on that build refusing
    /// it outright is what keeps a failed update from becoming lost work.</para>
    ///
    /// <para><b>17 fills a class's list from its groups, and adds a class for a whole programme.</b>
    /// Craig's shape, 2026-09-24: BA is the programme, its years are groups inside it, and a register
    /// lists the students of the most detailed group the class names. <c>ScheduleEntry.StudentIds</c>
    /// changes meaning from the whole roster to the students added by hand, beside a new
    /// <c>ExcludedStudentIds</c>; a filed register keeps the list it was taken against in
    /// <c>AttendanceRecord.RosterStudentIds</c>; and <c>SessionScope.Programme</c> with
    /// <c>ScheduleEntry.ProgrammeId</c> is the new audience. <b>It has a migration block</b>, in
    /// <c>ProjectStore.Validate</c>, which keeps every hand-kept list and every filed register
    /// exactly as it was.</para>
    ///
    /// <para><b>A schema-16 build must refuse it, and this is the expensive direction.</b> Opened
    /// there, every class would read its hand-added few as its whole list, every whole-programme
    /// class would be read as nobody's, and a save would write that back.</para>
    ///
    /// <para><b>18 moves prep time into <see cref="Building.TimeRules"/></b>, beside the new rules that
    /// let a break such as lunch count as work time. Craig, 2026-09-24. <b>It has a migration
    /// block</b>: every class ticked with the schema-15 box becomes the list of one "Prep time" rule at
    /// 50%, so nobody's hours change, and the box is cleared.</para>
    ///
    /// <para><b>A schema-17 build must refuse it.</b> It has no rules, so it would read every class as
    /// earning no prep, take every facilitator's prep and lunch off their week, and write that back.</para>
    ///
    /// <para><b>19 lets an event be for the administrators alone</b> — <c>EventAudience.Administrators</c>,
    /// Craig, 2026-09-25. <b>It needs no migration block</b>: no earlier file can hold the word.</para>
    ///
    /// <para><b>A schema-18 build must refuse it, and the bump is what makes that refusal honest.</b>
    /// The audience is written as words, and a schema-18 build meeting <c>"Administrators"</c> fails to
    /// read the file at all — without the bump it would call the school's timetable corrupt, rather
    /// than saying it was saved by a newer version.</para>
    ///
    /// <para><b>20 lets a facilitator be a guest</b> — <see cref="Facilitator.IsGuest"/>, Craig,
    /// 2026-10-29: somebody who teaches a workshop and is tracked, and is never linked to an account.
    /// <b>It needs no migration block</b>: a schema-19 file has no <c>"IsGuest"</c> key, that reads as
    /// false, and false is what every earlier file meant.</para>
    ///
    /// <para><b>A schema-19 build must refuse it</b>, for the reason 15 gave: it would READ the file
    /// happily, show every guest as staff, and on the first save write the file back with every Guest
    /// box cleared — after which an account could be linked to a guest again, and through that link
    /// file their class's register.</para>
    ///
    /// <para><b>21 lets a programme be a guest</b> — <see cref="Strand.IsGuest"/>, Craig, 2026-11-02:
    /// a visiting group's classes are in the timetable, checked and counted, and off the week until
    /// somebody asks to see them. <b>No migration block</b>: an absent <c>"IsGuest"</c> on a programme
    /// reads as false, which is what every earlier file meant.</para>
    ///
    /// <para><b>A schema-20 build must refuse it</b>: it would read the file, draw every guest class
    /// on everybody's week with its own lane, and on the first save clear every Guest box.</para>
    ///
    /// <para><b>22 says whether a student's absence was planned, and why from a short list</b> —
    /// <see cref="Absence.Notice"/> and <see cref="Absence.Cause"/>, Craig's morning register,
    /// 2026-11-04. <b>No migration block</b>: an absence with neither key says nothing about either,
    /// which is what every earlier absence meant, and its old free text stays where it was.</para>
    ///
    /// <para><b>A schema-21 build must refuse it</b>: both are written as words it has never heard
    /// of, so without the bump it would call the school's timetable corrupt; and a build that skipped
    /// the keys instead would save every morning's absences back with planned, unplanned and the
    /// reason gone.</para>
    ///
    /// <para><b>23 lets a facilitator be in part of a session</b> —
    /// <see cref="ScheduleEntry.FacilitatorParts"/>, Craig, 2026-11-07: Circus Disciplines runs
    /// 13:00–15:00 and somebody teaching elsewhere until 14:00 is in it from 14:00, so their clash
    /// check, hours and preparation time read 14:00–15:00. <b>No migration block</b>: an absent
    /// <c>"FacilitatorParts"</c> reads as empty, which is everybody in the whole session — what every
    /// earlier file meant.</para>
    ///
    /// <para><b>A schema-22 build must refuse it</b>: it would read the file, count the whole session
    /// for everybody and raise the clash again, and on the first save drop every part.</para>
    ///
    /// <para><b>24 adds the Scratch Week</b> — <see cref="Building.ScratchSessions"/>, Craig,
    /// 2026-11-17: one undated planning week, shared by everybody who edits, whose classes count
    /// towards nothing until they are placed onto a real week. <b>No migration block</b>: an absent
    /// <c>"ScratchSessions"</c> reads as empty, which is an empty Scratch Week.</para>
    ///
    /// <para><b>A schema-23 build must refuse it</b>: it would read the file without the list and, on
    /// its first autosave, write it back without it, so the plan would be gone with nothing said.</para>
    /// </summary>
    public const int CurrentSchemaVersion = 24;

    /// <summary>
    /// The oldest format still migrated — and what an absent <see cref="SchemaVersion"/> key means.
    /// </summary>
    public const int OldestSchemaVersion = 1;

    private int? _schemaVersion;

    /// <summary>
    /// The format this project was written in. A brand-new <see cref="ProjectFile"/> built in memory
    /// is current by definition, which is why the getter falls back to
    /// <see cref="CurrentSchemaVersion"/> — but a file that arrives without the key is not: see
    /// <see cref="IsVersioned"/>, and read that rather than this when deciding what to migrate.
    /// </summary>
    public int SchemaVersion
    {
        get => _schemaVersion ?? CurrentSchemaVersion;
        set => _schemaVersion = value;
    }

    /// <summary>
    /// False when the JSON carried no <c>SchemaVersion</c> key at all. An absent version means an OLD
    /// file, never a new one: every build that has ever written this key wrote it, so a file without
    /// it predates the key itself. Defaulting the property straight to
    /// <see cref="CurrentSchemaVersion"/> made such a file deserialise as the newest format and skip
    /// every migration — a schema-1 project's holidays then loaded with <c>From = 0001-01-01</c> and
    /// stopped applying altogether, and the strand migration never ran either. Ignored on write; it
    /// is a fact about the bytes that were read, not part of the format.
    /// </summary>
    [JsonIgnore]
    public bool IsVersioned => _schemaVersion.HasValue;

    /// <summary>
    /// When true the app opens in Schedule mode — the room layout is read-only and the drawing
    /// tools are hidden, so teachers schedule without disturbing the space definition. The
    /// responsible user toggles this off ("Edit layout") to change rooms. Persisted with the project.
    /// </summary>
    public bool LayoutLocked { get; set; }

    /// <summary>
    /// The week a project opens on, or null for the current week. <b>Read once, when the project is
    /// opened, and never written non-null since 1.25.0</b>: the week box belongs to the window looking
    /// at the project (one per tab on the web), not to the file, so paging it is not an edit and is not
    /// saved. Kept so that a file an earlier build pinned to a week still opens on that week; the first
    /// save from 1.25.0 writes it back as null. <c>MainViewModel.OnProjectChanged</c> is where it is read.
    /// </summary>
    public DateOnly? ScheduleWeek { get; set; }

    /// <summary>
    /// <b>The teaching terms inside this project's year</b> — each a name, a first Monday and a
    /// length. Empty is a real and common answer: a project with no terms has no teaching span at
    /// all, which is exactly what a null <c>Term</c> meant before schema 14, and every weekly target
    /// then applies to every week.
    ///
    /// <para><b>A list of any length, seeded with two, never two fixed fields.</b> The owner's
    /// decision, 2026-09-13: <i>"I think two"</i> — so the app offers two and takes any number,
    /// because a school that runs three, or one, or a summer intensive on its own, must not have to
    /// find that out from a form that cannot hold it.</para>
    ///
    /// <para><b>They need NOT cover the year, and that is the feature rather than a gap.</b> Inside
    /// the year and inside no term is a real state — the building is open and nothing is timetabled
    /// — and it is what keeps the weekly hour targets quiet over the summer. See
    /// <see cref="TermFor"/>, which answers null for such a week, and
    /// <see cref="Helpers.AcademicYear"/>, whose twelve months are the building's and not the
    /// teaching year's.</para>
    ///
    /// <para><b>Overlaps and terms outside the year are warned about, never refused</b> — this
    /// project's own rule, twice stated: software that refuses gets worked around. A real timetable
    /// breaks its own rules in exceptional years too.</para>
    /// </summary>
    public List<TermInfo> Terms { get; set; } = new();

    /// <summary>
    /// <b>The single <c>Term</c> every file before schema 14 carried</b>, read on load and never
    /// written: the migration moves it into <see cref="Terms"/> and sets this back to null, and
    /// <see cref="ProjectStore.JsonOptions"/> omits a null. Nothing in the app assigns it.
    ///
    /// <para><b>Read it only under a <c>&lt; 14</c> gate</b>, the rule
    /// <see cref="ClassEntry.LegacyCountsAsWorkload"/> earns the hard way: an ungated read would put
    /// a fossil back over a list somebody has since edited.</para>
    /// </summary>
    [JsonPropertyName("Term")]
    public TermInfo? LegacyTerm { get; set; }

    /// <summary>
    /// <b>The term the year opens with</b> — the earliest-starting one, or null when there are none.
    /// <see cref="Helpers.AcademicYear"/> reads this and nothing else: the owner's <i>"no separate
    /// year-start field"</i>, so the year starts when the first term does and there is only ever one
    /// answer to when that is.
    /// </summary>
    [JsonIgnore]
    public TermInfo? FirstTerm
        => Terms.Count == 0 ? null : Terms.OrderBy(t => t.StartMonday).First();

    /// <summary>
    /// <b>The term the week containing <paramref name="anyDateInWeek"/> falls in</b>, or null when it
    /// falls in none — which is both "there are no terms" and "this is the summer", and the callers
    /// that must tell those two apart ask <see cref="Terms"/> how many there are rather than reading
    /// a null as either. <see cref="ViewModels.WeekTally"/> is the one that has to.
    ///
    /// <para>Where two terms overlap the earlier-starting one wins, so the answer is stable rather
    /// than whichever order the list happens to be in. An overlap is a warning in
    /// <i>Term &amp; holidays</i>, not a refusal, so this has to answer something sensible for one.</para>
    /// </summary>
    public TermInfo? TermFor(DateOnly anyDateInWeek)
        => Terms.OrderBy(t => t.StartMonday)
                .FirstOrDefault(t => ViewModels.ScheduleResolver.TermContains(t, anyDateInWeek));

    /// <summary>
    /// <b>The term a report about <paramref name="anyDateInWeek"/> covers</b>: the one containing that
    /// week, or — when it is in none — the nearest one. Null only when there are no terms at all.
    ///
    /// <para><b>Why this is not <see cref="TermFor"/>.</b> The reports, the register grid and the
    /// facilitator's term figures all span a TERM rather than a week, and they are opened from
    /// whatever week the box happens to be on. Before schema 14 there was one term and they were
    /// handed it whatever week that was — so keying them on "the term containing this week" would
    /// quietly empty every one of them the moment somebody paged into the summer, which is a register
    /// grid reading <i>nothing outstanding</i> because of where a date box was left. Falling back to
    /// the nearest term is what keeps that behaviour identical for a project with one term, by
    /// construction, and gives a person standing in the summer the term they are standing next
    /// to.</para>
    ///
    /// <para>Ties go to the earlier term, which is arbitrary and reachable only from a week exactly
    /// between two of them; what matters is that it is deterministic, so the same file does not
    /// report two different spans.</para>
    /// </summary>
    public TermInfo? TermNear(DateOnly anyDateInWeek)
    {
        if (TermFor(anyDateInWeek) is { } containing) return containing;
        if (Terms.Count == 0) return null;

        var here = Helpers.WeekLabel.MondayOf(anyDateInWeek);
        return Terms.OrderBy(t => t.StartMonday)
                    .MinBy(t =>
                    {
                        var first = Helpers.WeekLabel.MondayOf(t.StartMonday);
                        var last = ViewModels.ScheduleResolver.EndMonday(t).AddDays(6);
                        return here < first ? first.DayNumber - here.DayNumber : here.DayNumber - last.DayNumber;
                    });
    }

    /// <summary>
    /// Closures within the term — date ranges in which no sessions run, either for the whole
    /// building or for particular groups. See <see cref="Holiday.Scope"/>.
    /// </summary>
    public List<Holiday> Holidays { get; set; } = new();

    /// <summary>
    /// Who is away — facilitators, and students since schema 14. <b>One list</b>; see
    /// <see cref="AbsentPerson"/> for why, and for the one rule that makes it safe (nothing about
    /// cover may ever read a student's row).
    /// </summary>
    public List<Absence> Absences { get; set; } = new();

    /// <summary>
    /// <b>The school's events</b> — never classes, and never read by anything that counts classes.
    /// Schema 16. See <see cref="SchoolEvent"/>.
    /// </summary>
    public List<SchoolEvent> Events { get; set; } = new();

    public Building Building { get; set; } = new();

    public DateTimeOffset LastSavedUtc { get; set; }

    public string LastSavedBy { get; set; } = Environment.UserName;
}
