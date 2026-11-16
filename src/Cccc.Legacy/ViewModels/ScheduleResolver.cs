using CCCCScheduler.Helpers;
using CCCCScheduler.Models;

namespace CCCCScheduler.ViewModels;

/// <summary>
/// One concrete occurrence of a weekly session on a specific calendar week, after holidays and
/// per-occurrence exceptions have been applied. <see cref="Start"/>/<see cref="End"/> already
/// reflect any reschedule, so callers don't re-read the template entry's times.
/// </summary>
/// <param name="Room">
/// <b>Null for a session whose space has not been decided, or whose room no longer exists.</b> Since
/// schema 10 a room is an attribute of a session rather than its container — see
/// <see cref="Building.Sessions"/> — so this can be absent where it never used to be. <b>Every
/// consumer must handle null</b>: a surface keyed on day and time (the week grid, the calendar-view
/// exports, the sessions list, the conflicts panel) still shows the session, with the room blank; a
/// surface keyed on rooms (the room grid, the floor plan) has no column or shape for one and reads
/// <see cref="Building.SessionsByRoom"/> instead, which never yields one of these in the first place.
/// </param>
/// <param name="Day">
/// <b>The day this occurrence actually falls on, which since schema 12 is not always
/// <see cref="ScheduleEntry.Day"/>:</b> a one-off change can move a class to another day of that week
/// alone. Every day-keyed surface reads THIS — the week grid's columns, the room grid's per-day
/// sheets, the day filter, the .ics dates, <see cref="SessionRow.Day"/> — and none of them re-reads
/// the entry, so a class moved for one week is drawn, printed and published where it is.
/// </param>
public sealed record ResolvedSession(
    Room? Room,
    ScheduleEntry Entry,
    DayOfWeek Day,
    TimeOnly Start,
    TimeOnly End,
    bool Rescheduled,
    string? Note,
    IReadOnlyList<Guid> FacilitatorIds,
    bool Covered = false,
    bool Cancelled = false,
    bool NeedsCover = false)
{
    /// <summary>
    /// <b>Who a needs-cover occurrence is waiting to replace</b> — schema 23. Null on every other
    /// occurrence. Before parts this was everybody on <see cref="FacilitatorIds"/> whenever
    /// <see cref="NeedsCover"/> was true; with parts, somebody in for the second hour may still be
    /// teaching while the first hour waits for cover. Read through <c>FacilitatorParts.IsIn</c>.
    /// </summary>
    public IReadOnlyList<Guid>? AwayFacilitatorIds { get; init; }

    /// <summary>
    /// <b>Who was named as cover on a covered occurrence</b>, as the one-off change says. Null on every
    /// other occurrence. <see cref="FacilitatorIds"/> cannot answer it: a co-teacher who stayed and a
    /// co-teacher who covers their partner are both on the roster and both on that list, and only the
    /// second is in for the partner's hours. Read through <c>FacilitatorParts.Window</c>.
    /// </summary>
    public IReadOnlyList<Guid>? CoverFacilitatorIds { get; init; }
}

/// <summary>What happens to one session on one date.</summary>
public enum OccurrenceState
{
    /// <summary>It runs, with its usual facilitators or an assigned substitute.</summary>
    Runs,

    /// <summary>It does not run — a closure, or a cancellation for that week.</summary>
    Suppressed,

    /// <summary>It runs, but every facilitator assigned to it is away and nobody has been given it yet.</summary>
    NeedsCover,
}

/// <summary>
/// The resolved state of one session on one date, and what decided it. Derived on demand from the
/// holidays, exceptions and absences it is handed, so removing a <em>holiday</em> — which nothing
/// else records — simply changes the answer.
///
/// Removing an <em>absence</em> does not, and deliberately so: assigning cover writes a
/// <see cref="SessionException"/> onto the entry, and that outlives the absence that prompted it. So
/// deleting an absence after cover has been assigned leaves the substitute teaching the session
/// rather than handing it back. That is the intended behaviour — the substitute was told they were
/// teaching it — and clearing it is a separate, explicit act (set the week back to "runs as normal"
/// in the One-off change dialog). What the absence DOES decide is who the substitute stands in for,
/// so deleting it puts the person who was away back on the session beside them; see
/// <c>ScheduleResolver.CoveredBy</c>.
/// </summary>
public sealed record ResolvedOccurrence(
    OccurrenceState State,
    Holiday? SuppressedBy,
    IReadOnlyList<Guid> AbsentFacilitatorIds,
    SessionException? Override);

/// <summary>
/// Pure, UI-free resolution of the weekly-recurring timetable into what actually runs on a given
/// week — suppressing holiday weeks, dropping cancelled occurrences and applying time changes — plus
/// the term-calendar helpers (which weeks are teaching weeks, is a date inside the term, …).
/// Everything here is deterministic and unit-tested; the ViewModel and exporters build on it.
/// </summary>
public static class ScheduleResolver
{
    /// <summary>The Monday of the week containing <paramref name="anyDate"/>.</summary>
    public static DateOnly WeekOf(DateOnly anyDate) => WeekLabel.MondayOf(anyDate);

    // ---- Holidays ----------------------------------------------------------

    /// <summary>
    /// The closure that stops <paramref name="session"/> running on <paramref name="date"/>, or null
    /// if it runs. Dates, not weeks — and this session's own audience, not the school's.
    ///
    /// <para>The rules, which are the whole of holiday scoping:</para>
    /// <list type="bullet">
    ///   <item><see cref="HolidayScope.WholeBuilding"/> suppresses EVERYTHING inside its dates,
    ///     including a session with no cohort at all — an External Hire booking, a one-off, an
    ///     untagged row. The doors are shut; there is nobody it does not apply to.</item>
    ///   <item><see cref="HolidayScope.Groups"/> suppresses a session only if EVERY cohort it is on
    ///     for is named in <see cref="Holiday.GroupIds"/> — a class BA and Prep share survives a
    ///     BA-only half term and runs for Prep, the same reading the cover rule takes of an absent
    ///     facilitator. A session with NO cohort is therefore never suppressed by one: it names none
    ///     of the listed groups, so it runs. Both directions of getting this wrong are real failures —
    ///     cancelling a hire the school was paid for, or teaching a class through a closure — which is
    ///     why each has its own test.</item>
    ///   <item>A <see cref="HolidayScope.Groups"/> closure with an EMPTY list suppresses nothing. It
    ///     is a holiday nobody is on, not a sneaky whole-building one; a half-ticked closure must not
    ///     black out the school while the owner is still choosing.</item>
    /// </list>
    ///
    /// <para>Several closures can cover one date — Prep's half term and Degree's reading week in the
    /// same week, a bank holiday inside either. If any APPLIES the session is off, and the first such
    /// is returned because the caller shows its name. Note "first that applies", not "first that
    /// covers the date": scope is tested inside the search, so a closure for somebody else can never
    /// mask the one that genuinely shuts this session.</para>
    ///
    /// <para>Replaces the group-blind <c>HolidayOn(holidays, date)</c>. Anything that still wants
    /// "is the building shut", with no session in hand, must say so by passing null and mean it.</para>
    /// </summary>
    public static Holiday? SuppressedBy(IEnumerable<Holiday> holidays, DateOnly date, ScheduleEntry? session)
        => holidays.FirstOrDefault(h => h.CoversDate(date) && AppliesTo(h, session));

    /// <summary>
    /// Whether <paramref name="holiday"/> is one that <paramref name="session"/> is off for, ignoring
    /// dates. Written as "not Groups-scoped, or else every cohort listed" so that an unrecognised
    /// scope falls back to closing the building rather than quietly opening it. Over-closing is
    /// visible the moment somebody looks at the week; under-closing is a class taught to an empty room.
    ///
    /// <para><b>EVERY cohort, not any</b> — a class BA and Prep share survives a BA-only half term and
    /// runs for Prep. The same reading the cover rule takes of an absent facilitator: a co-taught
    /// session needs cover only when EVERY assigned facilitator is away.</para>
    ///
    /// <para><b>Both a <see cref="SessionScope.WholeBuilding"/> session and one with no cohort at all
    /// are handled by the SAME line: <c>cohorts.Count > 0</c>.</b> Both yield an empty
    /// <see cref="ScheduleEntry.CohortIds"/>, and <c>All</c> over an empty sequence is true — so without
    /// that count check either shape would be shut by any group closure, wrongly. <b>Do not delete
    /// <c>cohorts.Count > 0</c> as redundant with the explicit
    /// <c>session.Scope == SessionScope.WholeBuilding</c> check below it</b> — it is the other way
    /// round: <c>Count > 0</c> is what actually stops a <see cref="SessionScope.WholeBuilding"/>
    /// session being shut (its <c>CohortIds</c> is unconditionally empty by construction — see that
    /// property's own remarks), which makes the explicit `Scope` check unfalsifiable against today's
    /// test suite. It stays anyway, as belt-and-braces against a future change to
    /// <see cref="ScheduleEntry.CohortIds"/> making it fall through to <c>GroupIds</c> for
    /// <see cref="SessionScope.WholeBuilding"/> too — deleting it on the strength of "the count check
    /// already covers this" would be true today and false the day that property changes, and this is
    /// the one place cancelling a paid External Hire booking is one accidental edit away.</para>
    ///
    /// <para><b>That fallback covers a scope NUMBER only, not a scope NAME.</b>
    /// <c>JsonStringEnumConverter</c> accepts an out-of-range integer, so <c>"Scope": 7</c> arrives
    /// here as a value that is neither <see cref="HolidayScope.WholeBuilding"/> nor
    /// <see cref="HolidayScope.Groups"/> and closes the building, as described. A NAME this build does
    /// not have — <c>"Scope": "Strand"</c>, the shape a newer build would actually write — never
    /// reaches this method at all: the deserializer throws, and <c>ProjectStore.Load</c> refuses the
    /// file with the same message it gives for any other file it cannot read. There is no third
    /// behaviour hiding between them, and this comment used to claim one.</para>
    /// </summary>
    private static bool AppliesTo(Holiday holiday, ScheduleEntry? session)
    {
        if (holiday.Scope != HolidayScope.Groups) return true;
        if (session is null || session.Scope == SessionScope.WholeBuilding) return false;

        var cohorts = session.CohortIds.ToList();
        return cohorts.Count > 0 && cohorts.All(holiday.GroupIds.Contains);
    }

    /// <summary>
    /// The WHOLE-BUILDING closure covering any of the seven days of the week containing
    /// <paramref name="anyDateInWeek"/>, or null. A holiday need not span a whole week any more (a bank
    /// holiday is one day), so a caller reporting on a whole week — the week banner, the teaching-week
    /// count — must ask about every day in it, not just the one date it was handed.
    ///
    /// <para><b>It is deliberately blind to group-scoped closures, and named so nobody can call it by
    /// accident.</b> The question it answers is "is the BUILDING shut", which is the only question a
    /// caller with no session — and therefore no group — in hand is entitled to ask. A closure scoped
    /// to Prep does not shut the building, so it is not reported here: the week the banner is drawn
    /// over still has Degree's timetable in it. It used to be called <c>HolidayInWeek</c>, which
    /// invited exactly the assumption ("any holiday this week") that scoping made false.</para>
    ///
    /// <para>This is the conservative half of an open question, not the final answer. What a week
    /// view SHOULD say when only Prep is off — name the groups, say nothing — is the owner's call;
    /// see <c>MainViewModel.ExportWeekHoliday</c> and task 3's report. Saying nothing is what is
    /// implemented, because the one thing the banner knows how to say is "Holiday week", and that is
    /// a claim about the building which a scoped closure does not license.</para>
    ///
    /// <para><b>Not what the banner names.</b> "A building closure exists somewhere in this week" is
    /// a weaker claim than "this closure emptied the week", and using the first as though it were the
    /// second let a one-day bank holiday take credit for a week that Prep's half term emptied. The
    /// banner asks <see cref="ClosureThatEmptiedWeek"/> instead. This answers a genuine, narrower
    /// question — is the building shut at any point this week — and is what
    /// <see cref="IsWholeBuildingClosureWeek"/> is built on.</para>
    /// </summary>
    public static Holiday? WholeBuildingClosureInWeek(IEnumerable<Holiday> holidays, DateOnly anyDateInWeek)
    {
        var list = holidays as ICollection<Holiday> ?? holidays.ToList();
        var monday = WeekLabel.MondayOf(anyDateInWeek);
        for (int offset = 0; offset < 7; offset++)
        {
            // Group null is what makes this whole-building-only: AppliesTo answers false for every
            // Groups-scoped closure when there is no group, because a session in none of the listed
            // groups runs. Asking about a session with no group at all IS asking "are the doors shut".
            var h = SuppressedBy(list, monday.AddDays(offset), null);
            if (h is not null) return h;
        }
        return null;
    }

    /// <summary>
    /// The whole-building closure that ON ITS OWN accounts for the week containing
    /// <paramref name="anyDateInWeek"/> having no sessions in it, or null when none does. This is the
    /// closure the week banner may name.
    ///
    /// <para>The test is sufficiency, not overlap: a candidate earns the credit only if the week would
    /// STILL be empty with that closure and nothing else — the same resolver, over the same timetable,
    /// with the other closures taken away. Everything the banner claims follows from that. It cannot
    /// name a closure that emptied nothing, and it cannot stay silent about one that emptied
    /// everything.</para>
    ///
    /// <para>What the old attribution did instead — take the first whole-building closure touching any
    /// day of the week — was true of a bank holiday sitting inside a week that Prep's half term had
    /// emptied, and the banner then announced a building-wide closure that never happened, on screen
    /// and on every export. Duration is not the discriminator either: a Tuesday-only closure over a
    /// timetable that only teaches on Tuesdays genuinely does empty the week, and says so.</para>
    ///
    /// <para>Candidates are tried day by day from Monday, so among closures that each would empty the
    /// week the earliest-starting one is named — the same preference
    /// <see cref="WholeBuildingClosureInWeek"/> has always had, now applied only to closures that
    /// qualify. A Groups-scoped closure is never a candidate: the banner's wording is a claim about
    /// the building, which is the owner's open question, not ours to answer here.</para>
    ///
    /// <para><b>A partial closure is never credited with a week that had nothing in it to begin
    /// with, but a whole-week one still is.</b> A building with no sessions on its master template at
    /// all was already going to run nothing that week regardless of any closure, so a one-day bank
    /// holiday over it should not take the credit — but a closure spanning the WHOLE week (all seven
    /// days, Monday to Sunday) is still a true reason nothing runs: "Christmas" over a blank,
    /// freshly-created project is a fact about that week, not the trivial-sufficiency fallacy in
    /// disguise, and the owner's recorded decision keeps that sentence (see
    /// <c>WeekBannerAttributionTests.AWholeBuildingClosureOverAWeekWithNoTimetableAtAll_StillSaysSo</c>).
    /// So when <see cref="TemplateSessions"/> is empty, a candidate qualifies only if it covers every
    /// one of the week's seven days; when the template has sessions, the day-by-day sufficiency test
    /// below runs exactly as it always has, unconstrained by this.</para>
    /// </summary>
    public static Holiday? ClosureThatEmptiedWeek(
        Building building, IEnumerable<Holiday> holidays, IEnumerable<Absence> absences, DateOnly anyDateInWeek)
    {
        var list = holidays as ICollection<Holiday> ?? holidays.ToList();
        var absenceList = absences as ICollection<Absence> ?? absences.ToList();
        var monday = WeekLabel.MondayOf(anyDateInWeek);
        var templateIsEmpty = TemplateSessions(building, monday).Count == 0;

        var considered = new HashSet<Holiday>();      // by identity: two closures can be equal in every field
        for (int offset = 0; offset < 7; offset++)
        {
            var date = monday.AddDays(offset);
            foreach (var h in list.Where(h => h.CoversDate(date) && AppliesTo(h, null)))
            {
                if (!considered.Add(h)) continue;
                if (templateIsEmpty && !Enumerable.Range(0, 7).All(o => h.CoversDate(monday.AddDays(o)))) continue;
                if (SessionsForWeek(building, [h], absenceList, monday).Count == 0) return h;
            }
        }
        return null;
    }

    /// <summary>
    /// True if any day of the week containing <paramref name="anyDateInWeek"/> is shut to the whole
    /// building. Same blindness, same reason, as <see cref="WholeBuildingClosureInWeek"/>.
    /// </summary>
    public static bool IsWholeBuildingClosureWeek(IEnumerable<Holiday> holidays, DateOnly anyDateInWeek)
        => WholeBuildingClosureInWeek(holidays, anyDateInWeek) is not null;

    // ---- Per-occurrence exceptions -----------------------------------------

    /// <summary>The deviation recorded for <paramref name="entry"/> on the given week, if any.</summary>
    public static SessionException? ExceptionFor(ScheduleEntry entry, DateOnly anyDateInWeek)
    {
        var monday = WeekLabel.MondayOf(anyDateInWeek);
        var forWeek = entry.Exceptions.Where(x => WeekLabel.MondayOf(x.WeekMonday) == monday).ToList();
        if (forWeek.Count <= 1) return forWeek.FirstOrDefault();

        // SetException keeps one per week, but a hand-edited or older file can hold several. Resolve by
        // precedence rather than array order — otherwise the same file answers differently depending on
        // how it happened to be written. A cancellation wins: it removes a session rather than
        // inventing one, which is the safe way to be wrong.
        return forWeek.FirstOrDefault(x => x.Kind == ExceptionKind.Cancelled) ?? forWeek[0];
    }

    /// <summary>
    /// Stores (or, when <paramref name="exception"/> is null, clears) the single deviation for a session
    /// on a given week, normalising the week to its Monday. At most one exception per week is kept.
    /// </summary>
    public static void SetException(ScheduleEntry entry, DateOnly anyDateInWeek, SessionException? exception)
    {
        var monday = WeekLabel.MondayOf(anyDateInWeek);
        entry.Exceptions.RemoveAll(x => WeekLabel.MondayOf(x.WeekMonday) == monday);
        if (exception is not null)
        {
            exception.WeekMonday = monday;
            entry.Exceptions.Add(exception);
        }
    }

    // ---- Occurrence resolution ---------------------------------------------

    /// <summary>
    /// What happens to <paramref name="entry"/> on <paramref name="date"/>, in a fixed precedence:
    ///
    ///   1. A holiday wins outright — but only one this entry's own audience is actually off for,
    ///      which <see cref="SuppressedBy"/> decides from the entry itself. A session
    ///      that does not run cannot need cover, so this short-circuits everything below — including
    ///      an explicit cover assigned earlier, which becomes moot rather than wrong. A closure
    ///      scoped to somebody else short-circuits NOTHING: it must leave this session's absence
    ///      check to run exactly as if the holiday were not there, or a gap in Degree's timetable
    ///      would hide behind Prep's half term.
    ///   2. Then a manual one-off change for that week, but only the two kinds that actually answer
    ///      "who is teaching this?":
    ///        • <see cref="ExceptionKind.Cancelled"/> — it does not run. Resolved: Suppressed.
    ///        • <see cref="ExceptionKind.Covered"/> — somebody else is teaching it. Resolved: Runs.
    ///        • <see cref="ExceptionKind.Rescheduled"/> — only WHEN it runs moved: the times, the
    ///          day of that week (schema 12), or both. The facilitator is just as away as they were,
    ///          so this resolves NOTHING and falls through to step 3. The exception still travels
    ///          back on <see cref="ResolvedOccurrence.Override"/> so callers keep its new times and
    ///          day; it simply no longer suppresses the absence check.
    ///   3. Then absence: a gap only when EVERY assigned facilitator is away.
    ///
    /// <b><paramref name="date"/> IS THE DATE THE OCCURRENCE ACTUALLY FALLS ON, and the caller owes
    /// that.</b> Both the holiday check and the absence check are asked about it, so handing the
    /// template day's date for an occurrence a reschedule has moved asks about a day the class is not
    /// on, in both directions at once — the closure that shuts it is missed and one that does not is
    /// applied, and the person away when it now runs is not found. <see cref="EffectiveDay"/> is how
    /// the date is derived; <see cref="SessionsForWeekDetailed"/> and <c>MainViewModel.CoverGaps</c>
    /// both do it that way before calling this.
    ///
    /// Order 1-before-2 is deliberate and was approved explicitly: "manual always wins" holds against
    /// absence, where assigning cover is exactly how a gap is cleared, but not against a closure.
    ///
    /// Treating a reschedule as resolving the gap is what let a class be left with nobody rostered and
    /// nothing anywhere saying so: assign cover, then move that same week, and
    /// <see cref="SetException"/> replaces the Covered exception with the Rescheduled one — which then
    /// re-suppressed the absence check, so the session vanished from the cover list, the conflicts
    /// panel and the grid flag alike while still being assigned to the person who is away.
    /// </summary>
    public static ResolvedOccurrence Resolve(
        ScheduleEntry entry, DateOnly date,
        IEnumerable<Holiday> holidays, IEnumerable<Absence> absences)
    {
        if (SuppressedBy(holidays, date, entry) is { } holiday)
            return new ResolvedOccurrence(OccurrenceState.Suppressed, holiday, [], null);

        var over = ExceptionFor(entry, date);
        if (over is { Kind: ExceptionKind.Cancelled })
            return new ResolvedOccurrence(OccurrenceState.Suppressed, null, [], over);
        if (over is { Kind: ExceptionKind.Covered })
            return new ResolvedOccurrence(OccurrenceState.Runs, null, [], over);

        // A session nobody is assigned to is not a gap this feature created, and claiming it were one
        // would make the cover list untrustworthy.
        if (entry.FacilitatorIds.Count == 0)
            return new ResolvedOccurrence(OccurrenceState.Runs, null, [], over);

        var (start, end) = EffectiveTimes(entry, over);

        // AWAY FOR THEIR OWN HOURS, and a gap is a stretch nobody is in (schema 23). With no
        // FacilitatorParts both are the old rule exactly: everybody's hours are the session's, and a
        // stretch nobody is in is the session with everybody away.
        var away = entry.FacilitatorIds.Where(id =>
        {
            var (from, until) = FacilitatorParts.Window(entry, id, start, end);
            return until > from && IsAway(id, date, from, until, absences);
        }).ToList();

        return FacilitatorParts.LeavesAGap(entry, start, end, away)
            ? new ResolvedOccurrence(OccurrenceState.NeedsCover, null, away, over)
            : new ResolvedOccurrence(OccurrenceState.Runs, null, [], over);
    }

    /// <summary>
    /// The hours an occurrence <b>actually runs at</b>: the reschedule's, when there is a complete
    /// one, and the template's otherwise.
    ///
    /// <para>This is the pair the absence check is entitled to ask about, and it matters now that an
    /// absence can be an hour rather than a day. A reschedule deliberately falls through to that check
    /// — moving a class does not find it a teacher — but it moves the class, so asking about where the
    /// class used to be would flag a session the facilitator is standing in front of, and miss one
    /// they have been moved on top of. Neither is a judgement about the reschedule; both are just the
    /// wrong times.</para>
    ///
    /// <para><b>All-or-nothing, matching <see cref="SessionsForWeekDetailed"/> exactly</b>, which is
    /// what publishes these times to the grid and the exports. A half-written reschedule does not move
    /// the session there and must not move it here, or the flag and the row would disagree about when
    /// the class is. It would also be the more dangerous half to honour: mixing one end from the
    /// exception with the other from the template can invert the interval, and an inverted interval
    /// overlaps nothing at all — the session would silently stop being flagged.</para>
    ///
    /// <para>Whole-day absences do not read these times, so nothing about this changes any project
    /// that exists.</para>
    ///
    /// <para>Public so that <c>MainViewModel.CoverGaps</c>, which calls <see cref="Resolve"/> directly
    /// rather than going through <see cref="SessionsForWeekDetailed"/>, can ask the same question the
    /// same way when it looks up WHICH absence to name as the reason. Two copies of the
    /// all-or-nothing rule is how the flag and the words beside it come to disagree.</para>
    /// </summary>
    public static (TimeOnly Start, TimeOnly End) EffectiveTimes(ScheduleEntry entry, SessionException? over)
        => over is { Kind: ExceptionKind.Rescheduled, NewStart: { } start, NewEnd: { } end }
            ? (start, end)
            : (entry.Start, entry.End);

    /// <summary>
    /// The day an occurrence <b>actually falls on</b>: the reschedule's, when it names one, and the
    /// template's otherwise. <see cref="EffectiveTimes"/>' sibling, and read the same way — a day is
    /// honoured only under <see cref="ExceptionKind.Rescheduled"/>, so a <see cref="ExceptionKind.Cancelled"/>
    /// or <see cref="ExceptionKind.Covered"/> exception carrying one means nothing.
    ///
    /// <para><b>THIS IS THE ONE PLACE THE RULE LIVES, and every reader of a day goes through it</b> —
    /// <see cref="SessionsForWeekDetailed"/>, <c>MainViewModel.CoverGaps</c> and
    /// <c>IcsExporter</c>'s cancelled-placeholder loop. Two copies of it is how the week grid, the
    /// cover list and the calendar file come to place one class on three different days: each of them
    /// derives the date it asks the holidays and the absences about from this answer, so a second
    /// reading is not a cosmetic difference but a class flagged for cover on a day it is not on.</para>
    ///
    /// <para><b>Not all-or-nothing with the times, unlike them with each other.</b> A reschedule can
    /// move only the day, only the times, or both, and each is honoured on its own: an inverted
    /// interval is the hazard that makes the time pair all-or-nothing, and a lone day has no
    /// equivalent — every day of the week is a day the timetable already has a column for.</para>
    ///
    /// <para>Whether the day the exception names is the one the entry already had is not asked here.
    /// <see cref="SessionsForWeekDetailed"/> decides whether the occurrence counts as MOVED by
    /// comparing this answer with <see cref="ScheduleEntry.Day"/>, so a hand-edited file naming the
    /// day the class was already on reads as an ordinary week rather than as a move to nowhere.</para>
    /// </summary>
    public static DayOfWeek EffectiveDay(ScheduleEntry entry, SessionException? over)
        => over is { Kind: ExceptionKind.Rescheduled, NewDay: { } day } ? day : entry.Day;

    /// <summary>
    /// Whether a recorded absence puts this facilitator out of a session running
    /// <paramref name="start"/>–<paramref name="end"/> on that date.
    ///
    /// <para>An overlap rather than a date test since schema 7, and the two are the same thing for a
    /// whole-day absence — <see cref="Absence.CoversSession"/> does not consult the times unless they
    /// are set. Several absences for one person are a union, because each is asked about the session
    /// on its own: a morning's training and an afternoon at the dentist between them cover a session
    /// in either, and one in neither is still taught.</para>
    /// </summary>
    private static bool IsAway(
        Guid facilitatorId, DateOnly date, TimeOnly start, TimeOnly end, IEnumerable<Absence> absences)
        // FACILITATOR ABSENCES ONLY, asked through Absence.Is so the kind and the id are one
        // question. Since schema 14 this list holds student absences too, and a student being away
        // must never make a session need cover — they are not who teaches it. The two id spaces do
        // not overlap in practice, so a bare id comparison would almost always have been right,
        // which is exactly what makes it the wrong shape: it would be correct until the day a
        // collision or a hand-edited file made it silently wrong on one session.
        => absences.Any(a => a.Is(AbsentPerson.Facilitator, facilitatorId)
                          && a.CoversSession(date, start, end));

    /// <summary>
    /// Who is in the room for a COVERED occurrence: <b>the assigned facilitators, minus the ones away
    /// that day, plus the substitutes</b> — first occurrence wins, so nobody is named twice.
    ///
    /// <para><b>Cover substitutes; it does not replace the roster.</b> This used to hand back
    /// <see cref="SessionException.CoverFacilitatorIds"/> wholesale, which is right only for the case
    /// the cover list offers — a session every one of whose facilitators is away. On a CO-TAUGHT
    /// session it was silently destructive: "Ada's at a conference, Sam steps in" took Ben, who was
    /// still standing in the room, off the grid, out of the CSV and the PDF for that week, off his own
    /// hours, and out of clash detection — so a genuine double-booking of his that hour went
    /// unreported. Nothing anywhere said he had gone.</para>
    ///
    /// <para><b>A cover assigned when nobody is away adds the substitute rather than erasing the
    /// roster.</b> There is nobody for them to stand in for, so there is nobody to remove. The file
    /// cannot distinguish "Cara instead of Ada" from "Cara as well as Ada" — only an absence says who
    /// is being stood in for — and of the two ways to be wrong, an extra name on the grid is visible
    /// and correctable while a missing one is neither. It also keeps the rule uniform: nobody who is
    /// not recorded away can ever be dropped by a cover, so the destructive case above has no way
    /// back in. The cover list cannot produce this shape (it offers cover only for a full gap); the
    /// One-off change dialog can, for any session at all.</para>
    ///
    /// <para><b>A substitute already on the session is named once.</b> Ben picking up Ada's half of a
    /// session he co-teaches is one person in one room: listing him twice would bill him two hours for
    /// one, the same defect a hand-edited session naming somebody twice produces.</para>
    ///
    /// <para><b>An exception naming NO substitute leaves the roster untouched</b> — null and empty
    /// alike. There is no substitution to perform, and subtracting the absent facilitator anyway would
    /// leave an occurrence marked "covered" with nobody on it and no needs-cover flag either, since
    /// the exception is what suppressed that check. Only a hand-edited file holds either shape: every
    /// dialog names somebody, and <c>ApplyFacilitatorChanges</c> drops a Covered exception left with
    /// an empty list.</para>
    ///
    /// <para><b>Somebody named as the substitute is never subtracted, even if they are themselves
    /// recorded away.</b> An explicit assignment is a deliberate statement that this person is
    /// teaching this occurrence, and manual changes already win over absence in <see cref="Resolve"/>;
    /// the cover dialog also declines to offer anyone away that date, so this only arises from the
    /// One-off change dialog, where it is a choice somebody made.</para>
    ///
    /// <para><b>Known consequence, and the reason it is acceptable:</b> deleting the absence AFTER
    /// assigning cover brings the previously-absent facilitator back alongside the substitute. The
    /// substitute still keeps the session — which is what this type's summary promises — but the
    /// roster is no longer trimmed, because who was substituted is derived from the absences rather
    /// than recorded. Remembering it would mean writing the replaced ids into the exception, i.e. a
    /// schema change; deriving it is what makes this fix work on every project already saved.</para>
    /// </summary>
    private static List<Guid> CoveredBy(
        ScheduleEntry entry, SessionException over, DateOnly date, IEnumerable<Absence> absences)
    {
        var cover = over.CoverFacilitatorIds;
        if (cover is null || cover.Count == 0) return entry.FacilitatorIds.ToList();

        // The same overlap the gap check uses, over the same hours, so "away for this session" means
        // one thing in this file. A co-teacher whose absence is an hour somewhere else in the day is
        // still standing in this room and must not be subtracted — which is the whole-day version of
        // this rule (never drop somebody who is not recorded away) at a finer grain.
        var (start, end) = EffectiveTimes(entry, over);

        var inTheRoom = new List<Guid>();
        foreach (var id in entry.FacilitatorIds.Where(id =>
                 {
                     var (from, until) = FacilitatorParts.Window(entry, id, start, end);
                     return !(until > from && IsAway(id, date, from, until, absences));
                 }).Concat(cover))
            if (!inTheRoom.Contains(id)) inTheRoom.Add(id);
        return inTheRoom;
    }

    // ---- Week resolution ---------------------------------------------------

    /// <summary>
    /// Every template entry as an unmodified occurrence, in room → schedule order: the MASTER
    /// TEMPLATE basis, which is what normally happens rather than what happens on any particular
    /// week. Holidays, per-occurrence exceptions and absences are all deliberately unread — a
    /// session cancelled for one week is still in the template, and a covered one still names its
    /// usual facilitator.
    ///
    /// <para>Lives here, shared, rather than being rebuilt by each caller, so that everything
    /// offering "master timetable" as an alternative to a resolved week — the conflicts panel, the
    /// weekly load — is answering from the same list by construction and cannot drift into a third
    /// answer.</para>
    ///
    /// <para><b><paramref name="anyDateInWeek"/> says which week's sessions those are</b>, and it is
    /// required rather than optional for the reason every dimension of
    /// <see cref="ScheduleOps.PassesFilters"/> is: with separate weeks (schema 13,
    /// <see cref="Building.WeeksAreSeparate"/>) a caller that forgot it would silently measure every
    /// week of the year at once — a year's clashes on one panel, a year's hours in one total — rather
    /// than failing to compile. On a project with one shared pattern the argument changes nothing,
    /// because <see cref="Building.SessionsInWeek"/> answers the whole pattern for every week.</para>
    ///
    /// <para>Order is room → schedule, THEN any room-less session, and unsorted within that, because
    /// <see cref="ScheduleConflicts.Find"/> has always reported clashes in that order and its tests
    /// read positionally. A caller wanting the week in day → time order sorts it itself.</para>
    ///
    /// <para><b>Room-less sessions are included, appended after every roomed one.</b> This reads
    /// <see cref="Building.SessionsByRoom"/> for the roomed sessions — preserving the exact order
    /// every existing caller already depends on — and then <see cref="Building.AllSessions"/> for
    /// whatever that left out: a session with no <see cref="ScheduleEntry.RoomId"/>, or one naming a
    /// room the building no longer has. Reading only <c>SessionsByRoom</c> here would silently drop
    /// those from the conflicts panel, which needs them for facilitator and student clashes even
    /// though a room clash cannot apply to a session with no room.</para>
    /// </summary>
    public static List<ResolvedSession> TemplateSessions(Building building, DateOnly anyDateInWeek)
    {
        // This week's sessions, then narrowed by room — rather than SessionsByRoom() narrowed by
        // week. Same set either way; taken in this order because SessionsInWeek is the one place
        // WeeksAreSeparate is read, and a second reading of it here is how the roomed half and the
        // room-less half would eventually come to describe different weeks.
        var inWeek = new HashSet<ScheduleEntry>(building.SessionsInWeek(anyDateInWeek));
        var withRoom = building.SessionsByRoom().Where(p => inWeek.Contains(p.Entry)).ToList();
        var covered = new HashSet<ScheduleEntry>(withRoom.Select(p => p.Entry));

        return withRoom
            .Select(p => new ResolvedSession((Room?)p.Room, p.Entry, p.Entry.Day, p.Entry.Start, p.Entry.End,
                false, null, p.Entry.FacilitatorIds))
            .Concat(inWeek.Where(e => !covered.Contains(e))
                .Select(e => new ResolvedSession(null, e, e.Day, e.Start, e.End,
                    false, null, e.FacilitatorIds)))
            .ToList();
    }

    /// <summary>
    /// The sessions that actually run in the week containing <paramref name="anyDateInWeek"/>, in
    /// day→time→room order. Cancelled occurrences (holiday or manual) are dropped; rescheduled ones
    /// carry their overridden times <b>and their overridden day</b>. With no
    /// holidays/exceptions/absences this is just the full timetable.
    /// </summary>
    public static List<ResolvedSession> SessionsForWeek(
        Building building, IEnumerable<Holiday> holidays, IEnumerable<Absence> absences, DateOnly anyDateInWeek)
        => SessionsForWeekDetailed(building, holidays, absences, anyDateInWeek).Where(s => !s.Cancelled).ToList();

    /// <summary>
    /// Like <see cref="SessionsForWeek"/> but also includes cancelled occurrences (flagged
    /// <see cref="ResolvedSession.Cancelled"/>) so the UI can show them struck-through, and flags
    /// occurrences nobody can currently teach (<see cref="ResolvedSession.NeedsCover"/>).
    ///
    /// Each entry's own occurrence date decides its fate via <see cref="Resolve"/> — not the week as a
    /// whole — so a single-day closure suppresses only the sessions actually inside it rather than
    /// blacking out every other day too. A holiday-suppressed occurrence is omitted outright (the
    /// caller shows a banner instead of a struck-through row); a manual cancellation still shows,
    /// struck through, so the user can see it was a deliberate choice.
    /// </summary>
    /// <param name="includeClosedByHoliday">
    /// Whether a holiday-suppressed occurrence comes back as a cancelled row carrying the closure's
    /// name, instead of being omitted. <b>False — the default — is the behaviour every existing
    /// caller has always had</b>, and the one the grid, the CSV, the ICS and the six-column report
    /// all keep by not passing it.
    ///
    /// <para>The student timetable PDF passes true, because it prints a closed session struck
    /// through with its reason rather than leaving a gap: a hole where a class used to be is
    /// indistinguishable from a bug, and generates the email asking whether the system is broken.
    /// The reason is <see cref="ResolvedOccurrence.SuppressedBy"/>'s own name — this branch reads
    /// what <see cref="Resolve"/> decided and does not re-derive which closure applies, so group
    /// scoping is honoured exactly as it is everywhere else.</para>
    /// </param>
    public static List<ResolvedSession> SessionsForWeekDetailed(
        Building building, IEnumerable<Holiday> holidays, IEnumerable<Absence> absences, DateOnly anyDateInWeek,
        bool includeClosedByHoliday = false)
    {
        var holidayList = holidays as ICollection<Holiday> ?? holidays.ToList();
        var absenceList = absences as ICollection<Absence> ?? absences.ToList();
        var monday = WeekLabel.MondayOf(anyDateInWeek);

        var result = new List<ResolvedSession>();
        foreach (var entry in building.SessionsInWeek(monday))
            {
                // SessionsInWeek rather than SessionsByRoom: this is a day/time-keyed surface (the grid,
                // the CSV, the ICS, the student PDF all build on it), and the owner's ruling is that a
                // room-less session stays on the calendar. RoomOf answers null both for an entry with
                // no RoomId and for one naming a room this building no longer has — the two behave
                // identically downstream, same as an orphaned Room.FloorIndex.
                var room = RoomOf(building, entry);

                // THE DAY IS DECIDED BEFORE THE DATE, AND THE DATE BEFORE THE RESOLVE. A one-off
                // change can move an occurrence to another day of this same week (schema 12), so the
                // date this occurrence is judged on is the EFFECTIVE day's, not the template day's:
                // somebody away on the Thursday is away from a class moved onto the Thursday, and a
                // Thursday closure shuts it while the Tuesday one it used to be on does not.
                //
                // ONE LOOKUP, not two. `over` is read here and used throughout below rather than
                // taken back off resolved.Override: Resolve does its own ExceptionFor over `date`,
                // which is in this same week by construction (monday + 0..6), so the two are the same
                // object — but reading it once is what keeps them from ever becoming two answers.
                var over = ExceptionFor(entry, monday);
                var day = EffectiveDay(entry, over);
                var date = monday.AddDays(((int)day + 6) % 7);          // Monday = 0 … Sunday = 6
                var resolved = Resolve(entry, date, holidayList, absenceList);

                if (resolved.State == OccurrenceState.Suppressed)
                {
                    if (resolved.SuppressedBy is { } closure)
                    {
                        if (!includeClosedByHoliday) continue;         // holiday: no row at all

                        // The closure's own name is the reason, already worded — "Half term", "Bank
                        // holiday". A closure saved without one still says something, because a
                        // struck-through class with no explanation reads as a printing fault.
                        result.Add(new ResolvedSession(room, entry, day,
                            entry.Start, entry.End, false,
                            string.IsNullOrWhiteSpace(closure.Name) ? "Closed" : closure.Name.Trim(),
                            entry.FacilitatorIds, Cancelled: true));
                        continue;
                    }

                    result.Add(new ResolvedSession(room, entry, day,
                        entry.Start, entry.End, false, over?.Note, entry.FacilitatorIds, Cancelled: true));
                }
                else if (over is { Kind: ExceptionKind.Rescheduled })
                {
                    // All-or-nothing: mixing one end from the exception with the other from the
                    // template can invert the interval, which clash detection and the week grid both
                    // treat as impossible. A half-written reschedule simply doesn't move the session.
                    bool timesMoved = over.NewStart is not null && over.NewEnd is not null;

                    // AND THE DAY IS ITS OWN HALF OF "MOVED". Rescheduled used to mean "the times
                    // differ", so a day-only move would have come back flagged as an ordinary week —
                    // the row would carry no "moved this week", the ICS would say nothing, and the
                    // only sign a class had moved would be the column it was drawn in. Compared
                    // against the template's day rather than read off NewDay, so a hand-edited file
                    // naming the day the class was already on is not a move to nowhere.
                    bool dayMoved = day != entry.Day;

                    // Moving a class does not find it a teacher: the new day and times are kept AND
                    // the gap is still flagged, exactly as on an unrescheduled occurrence.
                    result.Add(new ResolvedSession(room, entry, day,
                        timesMoved ? over.NewStart!.Value : entry.Start,
                        timesMoved ? over.NewEnd!.Value : entry.End,
                        timesMoved || dayMoved, over.Note, entry.FacilitatorIds,
                        NeedsCover: resolved.State == OccurrenceState.NeedsCover)
                    { AwayFacilitatorIds = AwayOf(resolved) });
                }
                else if (over is { Kind: ExceptionKind.Covered })
                    result.Add(new ResolvedSession(room, entry, day,
                        entry.Start, entry.End, false, over.Note,
                        CoveredBy(entry, over, date, absenceList), Covered: true)
                    { CoverFacilitatorIds = over.CoverFacilitatorIds?.ToList() });
                else
                    result.Add(new ResolvedSession(room, entry, day,
                        entry.Start, entry.End, false, null, entry.FacilitatorIds,
                        NeedsCover: resolved.State == OccurrenceState.NeedsCover)
                    { AwayFacilitatorIds = AwayOf(resolved) });
            }

        // A room-less session sorts as "" — first within its day and time — under NameOrder.Comparer,
        // the same tie-break every other room name here already goes through. Nothing about this
        // method's ordering guarantee depended on Room being non-null; it depended on Room.Name being
        // comparable, and "" is.
        return result
            .OrderBy(s => ((int)s.Day + 6) % 7)
            .ThenBy(s => s.Start)
            .ThenBy(s => s.Room?.Name ?? "", NameOrder.Comparer)
            .ToList();
    }

    /// <summary>
    /// The room a session names, or null when it names none — or names one this building no longer
    /// has, which behaves identically since a dangling <see cref="ScheduleEntry.RoomId"/> is scrubbed
    /// to null on every load anyway (<c>ProjectStore.Validate</c>). Shared by
    /// <see cref="TemplateSessions"/> and <see cref="SessionsForWeekDetailed"/> so the two cannot
    /// answer the question two different ways.
    /// </summary>
    private static IReadOnlyList<Guid>? AwayOf(ResolvedOccurrence resolved)
        => resolved.State == OccurrenceState.NeedsCover ? resolved.AbsentFacilitatorIds : null;

    private static Room? RoomOf(Building building, ScheduleEntry entry)
        => entry.RoomId is Guid id ? building.Rooms.FirstOrDefault(r => r.Id == id) : null;

    // ---- Term calendar -----------------------------------------------------

    /// <summary>
    /// The Monday of the term's last week (= start when the term is one week long).
    ///
    /// <para><b>Saturates rather than throwing.</b> This sits on the dock's render path —
    /// <c>WeekTally.For</c> calls <see cref="TermContains"/> on every rebuild — so a
    /// <see cref="TermInfo"/> carrying an absurd <see cref="TermInfo.WeekCount"/> must not be able to
    /// take the window down with an <see cref="ArgumentOutOfRangeException"/> out of <c>AddDays</c>.
    /// <c>ProjectStore.Validate</c> repairs the count to <see cref="TermInfo.MaxWeekCount"/> on load
    /// and is the real fix; this is the guard for a term that never went through it — one built in
    /// code, or in a test. The comparison is made BEFORE the multiply, because <c>7 * WeekCount</c>
    /// overflows <see cref="int"/> long before the date runs out of room.</para>
    /// </summary>
    public static DateOnly EndMonday(TermInfo term)
    {
        var start = WeekLabel.MondayOf(term.StartMonday);
        int weeks = Math.Max(0, term.WeekCount - 1);
        int weeksThatFit = (DateOnly.MaxValue.DayNumber - start.DayNumber) / 7;
        return weeks > weeksThatFit ? DateOnly.MaxValue : start.AddDays(7 * weeks);
    }

    /// <summary>True if the given date falls in a week within the term span (holidays included).</summary>
    public static bool TermContains(TermInfo term, DateOnly anyDateInWeek)
    {
        // A term of no weeks contains nothing. Without this it claimed to contain its own start week
        // while TermWeeks reported none — EndMonday clamps with WeekCount-1, TermWeeks with WeekCount —
        // so the two disagreed for any WeekCount <= 0.
        if (term.WeekCount <= 0) return false;

        var m = WeekLabel.MondayOf(anyDateInWeek);
        return m >= WeekLabel.MondayOf(term.StartMonday) && m <= EndMonday(term);
    }

    /// <summary>Every week (by Monday) the term spans, in order — including holiday weeks.</summary>
    public static IEnumerable<DateOnly> TermWeeks(TermInfo term)
    {
        var m = WeekLabel.MondayOf(term.StartMonday);
        for (int i = 0; i < Math.Max(0, term.WeekCount); i++)
            yield return m.AddDays(7 * i);
    }

    /// <summary>
    /// The days of the week beginning <paramref name="monday"/> on which the WHOLE BUILDING is closed
    /// (0–7).
    ///
    /// <para><b>Deliberately blind to group-scoped closures, and named so.</b> A day counts as closed
    /// only when the doors are shut to everybody: a Prep-only closure adds nothing here, nothing to
    /// <see cref="TeachingWeekCount"/> and nothing to <see cref="ClosedDayCount"/>. That is the
    /// truthful reading at the building level, which is the level these figures report at — a day on
    /// which Degree taught was not a day the building was closed, and counting it as one would
    /// understate the term for everyone in order to describe one programme.</para>
    ///
    /// <para>Open question for the owner, flagged rather than guessed: a per-PROGRAMME utilisation
    /// figure needs a per-group count instead, which is a different and richer question — "how many
    /// teaching weeks does Prep have" rather than "how many does the term have". Nothing here
    /// prevents adding it; <see cref="SuppressedBy"/> already takes the session, and with it every
    /// cohort it names.</para>
    /// </summary>
    public static int WholeBuildingClosedDaysInWeek(IEnumerable<Holiday> holidays, DateOnly monday)
    {
        var list = holidays as ICollection<Holiday> ?? holidays.ToList();
        int closed = 0;
        for (int offset = 0; offset < 7; offset++)
            if (SuppressedBy(list, WeekLabel.MondayOf(monday).AddDays(offset), null) is not null)
                closed++;
        return closed;
    }

    /// <summary>
    /// The term's weeks in which classes still run — a week is lost only when a closure takes ALL
    /// SEVEN of its days.
    ///
    /// Counted by days rather than by "does any day touch a holiday", which is what dropped a whole
    /// week for a single bank holiday and understated the teaching count (12 → 11). Holidays became
    /// date ranges precisely so a Wednesday closure need not black out Monday; the week count has to
    /// follow. Callers wanting the finer figure — how much of the term is actually closed — should
    /// read <see cref="ClosedDayCount"/> alongside this, since a week with four days shut still counts
    /// in full here.
    ///
    /// <para>A BUILDING-level figure: a week in which Prep is closed but Degree taught is a teaching
    /// week, because teaching happened. See <see cref="WholeBuildingClosedDaysInWeek"/> for why, and
    /// for the per-group count that is the owner's to ask for.</para>
    /// </summary>
    public static IEnumerable<DateOnly> TeachingWeeks(TermInfo term, IEnumerable<Holiday> holidays)
    {
        var list = holidays.ToList();
        return TermWeeks(term).Where(w => WholeBuildingClosedDaysInWeek(list, w) < 7);
    }

    public static int TeachingWeekCount(TermInfo term, IEnumerable<Holiday> holidays)
        => TeachingWeeks(term, holidays).Count();

    /// <summary>
    /// <b>The next week after <paramref name="fromAnyDateInWeek"/> in which the school is open at
    /// all</b> — the owner's <i>"the Monday following whatever the current week is"</i>, with the
    /// Christmas fortnight jumped rather than landed in. Null when the year runs out first.
    ///
    /// <para><b>"Open at all" is <see cref="TeachingWeeks"/>' own rule and not a second one</b>: a
    /// week is skipped only when a closure takes ALL SEVEN of its days, so a week with a bank holiday
    /// in it is still the next working week and a fortnight shut is not. Sharing the rule is the point
    /// — a copy that landed on a week the teaching-week count says does not exist would put a
    /// timetable somewhere no report would ever look at it.</para>
    ///
    /// <para>Bounded by <paramref name="withinWeeks"/>, the weeks this project can put a timetable on
    /// — <see cref="Helpers.AcademicYear.WeeksFor"/> is what supplies it — rather than by the term:
    /// the building's year runs past the teaching year, and copying a week into July is a thing the
    /// school may legitimately do with a summer intensive.</para>
    /// </summary>
    public static DateOnly? NextWorkingWeek(
        DateOnly fromAnyDateInWeek, IEnumerable<DateOnly> withinWeeks, IEnumerable<Holiday> holidays)
    {
        var list = holidays as ICollection<Holiday> ?? holidays.ToList();
        var here = WeekLabel.MondayOf(fromAnyDateInWeek);
        foreach (var w in withinWeeks.Select(WeekLabel.MondayOf).Where(w => w > here).OrderBy(w => w))
            if (WholeBuildingClosedDaysInWeek(list, w) < 7)
                return w;
        return null;
    }

    /// <summary>
    /// Total whole-building closed days across the term — the day-level figure a whole-week count
    /// cannot express, so a partial closure is reported rather than rounded away to nothing. Partial
    /// in TIME, that is: a group-scoped closure contributes nothing, per
    /// <see cref="WholeBuildingClosedDaysInWeek"/>.
    /// </summary>
    public static int ClosedDayCount(TermInfo term, IEnumerable<Holiday> holidays)
    {
        var list = holidays.ToList();
        return TermWeeks(term).Sum(w => WholeBuildingClosedDaysInWeek(list, w));
    }
}
