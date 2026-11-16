using System.Text.Json.Serialization;

namespace CCCCScheduler.Models;

/// <summary>
/// <b>Which kind of person an <see cref="Absence"/> is about</b> — schema 14.
///
/// <para><b>Facilitator is ordinal 0 on purpose</b>, so a schema-13 file, which has no such key at
/// all, deserialises to exactly what every absence it holds already was. The same trick
/// <see cref="SessionScope.Group"/> uses, and for the same reason: a default that means what the
/// older format meant needs no migration to be right.</para>
///
/// <para><b>One list, not two, and the danger is entirely on the cover side.</b> Who is away is one
/// idea and people think of it as one — but this list is what the resolver reads to decide a session
/// NEEDS COVER, and a student being away must never produce a cover flag. That is why
/// <see cref="Absence.FacilitatorId"/> was removed outright rather than left beside these two: every
/// reader of it stopped compiling and had to say which kind it meant, which is the only way a rule
/// like this one gets applied to all of them rather than to the ones somebody remembered.</para>
/// </summary>
public enum AbsentPerson
{
    /// <summary>A member of staff. <b>The only kind the cover machinery ever looks at.</b></summary>
    Facilitator,

    /// <summary>A student — schema 14. Read by the register and by nothing that decides cover.</summary>
    Student,
}

/// <summary>
/// <b>Whether a student's absence was known about in advance</b> — schema 22, Craig's morning
/// register of 2026-11-04. Written as words, so a schema-21 build meeting one refuses the file as
/// newer rather than calling it corrupt.
/// </summary>
public enum AbsenceNotice
{
    /// <summary>Arranged beforehand: a trip, an appointment, leave that was asked for.</summary>
    Planned,

    /// <summary>Found out on the day: a phone call in the morning, or nobody knew.</summary>
    Unplanned,
}

/// <summary>
/// <b>Why a student is away, as one of a few words</b> — schema 22. A fixed list rather than a
/// text box, deliberately: see <see cref="Absence.Cause"/>.
///
/// <para><b>Kept short and general on purpose.</b> "Illness" covers an injury too; anything more
/// specific belongs in the injury record, which is not this file.</para>
/// </summary>
public enum AbsenceCause
{
    /// <summary>Ill or injured. Health data, so administrators only.</summary>
    Illness,

    /// <summary>A personal or family reason.</summary>
    Personal,

    /// <summary>Leave the school agreed to: a trip, an audition, an appointment.</summary>
    ApprovedLeave,

    /// <summary>Anything else.</summary>
    Other,
}

/// <summary>
/// <b>A person away over a range of days</b> — whole days, or the same hours on each day of the
/// range. A facilitator, or, since schema 14, a student: see <see cref="AbsentPerson"/> for why one
/// list and not two.
///
/// <para>Nothing is written to the timetable. For a FACILITATOR the resolver derives "needs cover"
/// for sessions this person would have taught and nobody else can, and a human decides each one;
/// recording the reason matters, because the cover list is read weeks later by someone deciding
/// whether to find a substitute or cancel. For a STUDENT nothing is derived at all — it pre-fills
/// the <em>away, we know</em> mark when somebody files the register for a class they are on, and the
/// register then stores its own answer. <b>Editing this absence in November must not change what a
/// register said in September</b>, which is why the mark is stored rather than read back through
/// here.</para>
///
/// <para><b>The reason lives here and not in the register</b>, and that is a privacy decision as
/// much as a modelling one: it is entered once for a whole stretch rather than fifteen times, and a
/// free-text note against a name inside every register would put health-adjacent detail about an
/// identifiable minor into a file that gets copied, renamed and carried about.</para>
///
/// <para><b>Not <c>Facilitator.Availability</c>.</b> That expresses a recurring weekly pattern —
/// "never available Monday afternoons". This is the one-off: <em>this</em> Monday, 16:00–17:00. Both
/// apply independently and neither replaces the other.</para>
/// </summary>
public sealed class Absence
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// <b>Which kind of person this is about</b> — schema 14. See <see cref="AbsentPerson"/> for why
    /// this is one list rather than two, and why <c>FacilitatorId</c> was deleted rather than kept.
    /// </summary>
    public AbsentPerson Person { get; set; } = AbsentPerson.Facilitator;

    /// <summary>
    /// The person away — a facilitator or a student, as <see cref="Person"/> says. <b>Never read
    /// without asking which kind it is</b>; the two id spaces are distinct but nothing in the type
    /// system says so, which is what <see cref="Is"/> exists for.
    /// </summary>
    public Guid PersonId { get; set; }

    /// <summary>
    /// What schema 13's <c>FacilitatorId</c> key deserialises into, and nothing else ever.
    /// <see cref="ProjectStore"/>'s 13 → 14 migration moves it to <see cref="PersonId"/> with
    /// <see cref="Person"/> left at its default, then clears it.
    ///
    /// <para><b>It has to be a real property rather than a migration reading raw JSON</b>, for the
    /// reason <c>Student.LegacyName</c> already is one: by the time a migration runs the file has
    /// been deserialised, and a key with no property behind it is discarded by
    /// <c>System.Text.Json</c> before any migration can see it. Written only when non-empty, so a
    /// schema-14 file carries no such key.</para>
    /// </summary>
    [JsonPropertyName("FacilitatorId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public Guid LegacyFacilitatorId { get; set; }

    /// <summary>
    /// <b>Whether this absence is about one particular person of one particular kind.</b> The one
    /// question every reader of this list actually has, asked in one place so that no caller can
    /// compare a student's id against a facilitator absence and get a true it did not mean.
    /// </summary>
    public bool Is(AbsentPerson kind, Guid personId)
        => Person == kind && PersonId == personId;

    /// <summary>Shorthand for the kind the cover machinery is allowed to see.
    /// <para><b>[JsonIgnore] because System.Text.Json writes a read-only property by default</b> and
    /// <c>ProjectStore</c>'s options never set <c>IgnoreReadOnlyProperties</c>. Derived entirely from
    /// <see cref="Person"/>, which IS written, so the key was a second copy of a fact the file already
    /// held — inert on read, since there is no setter to read it back through, and in every file
    /// saved between schema 14 and this.</para></summary>
    [JsonIgnore]
    public bool IsFacilitator => Person == AbsentPerson.Facilitator;

    /// <summary>First day away, inclusive.</summary>
    public DateOnly From { get; set; }

    /// <summary>Last day away, inclusive. A single day has <see cref="From"/> == <see cref="To"/>.</summary>
    public DateOnly To { get; set; }

    /// <summary>
    /// Why, in somebody's own words, e.g. "Course" or "Training". Shown beside each gap in the cover
    /// list.
    ///
    /// <para><b>A facilitator's, and since schema 22 never a new student's.</b> A student's absence
    /// says why with <see cref="Cause"/>, picked from a short list, because a box that takes any words
    /// is the box somebody types "shoulder" into — health data about a named student, in a file that
    /// gets copied. A student absence recorded before schema 22 keeps whatever text it had, and that
    /// text is shown to administrators only (<c>RegisterOpening.ReasonsFor</c>).</para>
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// <b>Whether the school knew in advance</b> — schema 22, for a student's absence. Null when
    /// nobody said, which is every absence recorded before schema 22 and every facilitator's.
    /// Facilitators see this beside the name on a register; it says nothing about why.
    /// </summary>
    public AbsenceNotice? Notice { get; set; }

    /// <summary>
    /// <b>Why a student is away, from a fixed list</b> — schema 22. Null when nobody said.
    ///
    /// <para><b>Read by administrators only.</b> Even <see cref="AbsenceCause.Illness"/> beside a
    /// name is health data under GDPR, so a register drawn for anybody else shows
    /// <see cref="Notice"/> and not this.</para>
    /// </summary>
    public AbsenceCause? Cause { get; set; }

    /// <summary>
    /// First hour away on each day of the range, or <c>null</c> for whole days. Paired with
    /// <see cref="EndTime"/>: <b>both null is the whole-day absence, and it is what every absence
    /// recorded before schema 7 is.</b>
    ///
    /// <para>Nullable rather than defaulted, because <c>default(TimeOnly)</c> is midnight — a real
    /// time somebody could mean — so there is no in-band value left to say "no time given". Null also
    /// costs nothing on disk: <c>ProjectStore</c> serialises with
    /// <c>JsonIgnoreCondition.WhenWritingNull</c>, so a whole-day absence writes no time keys at all
    /// and its JSON is byte-identical to what schema 6 wrote.</para>
    /// </summary>
    public TimeOnly? StartTime { get; set; }

    /// <summary>Last hour away on each day of the range, exclusive. See <see cref="StartTime"/>.</summary>
    public TimeOnly? EndTime { get; set; }

    /// <summary>
    /// The hours this absence covers on each of its days, or <c>null</c> when it covers whole days.
    ///
    /// <para><b>Null for anything that is not a usable window</b>, which is three shapes and one
    /// answer: both times unset (the ordinary whole-day absence), one end set and the other missing,
    /// and a window that is inverted or zero-length. The last two arrive only hand-edited or foreign
    /// — no dialog can produce either — and reading them as a whole day is the only reading that
    /// cannot silently stop flagging a session. A 16:00–16:00 absence taken literally overlaps
    /// nothing: the person shows as available all week while the absence sits in the list saying
    /// otherwise, with nothing on screen looking wrong. That is the failure this app keeps choosing
    /// against; an over-wide absence is visible and correctable, a vanished one is neither.</para>
    ///
    /// <para><c>ProjectStore.Validate</c> repairs the same shapes on the way in and
    /// <see cref="NormaliseWindow"/> refuses them at every write door, so a file never keeps them.
    /// This is the model declining to guess even so — the same belt-and-braces the resolver's
    /// zero-length-session guard has.</para>
    /// </summary>
    private (TimeOnly Start, TimeOnly End)? Window
        => NormaliseWindow(StartTime, EndTime) is ({ } start, { } end) ? (start, end) : null;

    /// <summary>
    /// One pair of times as this class will actually read them: <b>a well-formed window verbatim, or
    /// two nulls</b>. The rule written out on <see cref="Window"/>, in a form a writer can call —
    /// the two entry dialogs, <c>MainViewModel.AddAbsence</c>, <c>ApplyFacilitatorProfile</c>, and
    /// <c>ProjectStore</c>'s repair on load all owe their answer to this one expression rather than
    /// each carrying a copy of it.
    ///
    /// <para><b>Widened, never swapped or narrowed.</b> An inverted 17:00–16:00 is as likely a
    /// truncated edit as a transposition, and inventing 16:00–17:00 out of it would narrow somebody's
    /// whole day off to an hour they never chose. The whole day is what the record says everywhere
    /// else, and it is the only reading that cannot silently stop flagging a session.</para>
    ///
    /// <para>No duration is measured and no bound imposed: a fifteen-minute window and an
    /// all-but-midnight one both pass through untouched.</para>
    /// </summary>
    public static (TimeOnly? Start, TimeOnly? End) NormaliseWindow(TimeOnly? start, TimeOnly? end)
        => start is { } s && end is { } e && e > s ? (start, end) : (null, null);

    /// <summary>True when <paramref name="date"/> falls inside this absence. Both ends inclusive.</summary>
    public bool CoversDate(DateOnly date) => date >= From && date <= To;

    /// <summary>
    /// Whether this absence puts the facilitator out of a session running
    /// <paramref name="sessionStart"/>–<paramref name="sessionEnd"/> on <paramref name="date"/>: the
    /// date is covered <b>and</b> the hours intersect.
    ///
    /// <para><b>With no times set this is exactly <see cref="CoversDate"/>.</b> Not approximately,
    /// and not for the hours somebody thought to test — the times are never consulted, so the answer
    /// is the same one this app has always given for every absence ever recorded. That reduction is
    /// the whole safety of the feature and it is pinned by an equality test between the two methods
    /// rather than by a list of expected booleans.</para>
    ///
    /// <para><b>Any overlap counts.</b> An absence of 16:00–17:00 against a session of 16:30–18:00
    /// means that person cannot teach the whole class, so it is flagged. Flagging a session somebody
    /// can partly attend is a visible, correctable nuisance; missing one they cannot teach is silent
    /// and lands on the day.</para>
    ///
    /// <para><b>Half-open, so touching ends do not collide</b> —
    /// <c>start &lt; sessionEnd &amp;&amp; sessionStart &lt; end</c>, the identical convention
    /// <c>ScheduleConflicts.Overlap</c> and the grid's clash check already use. An absence ending at
    /// 17:00 does not take the 17:00 class: they are out of the chair as it begins. Using <c>&lt;=</c>
    /// instead would flag a session on every boundary in the timetable, which is most of them.</para>
    ///
    /// <para>The caller passes the hours the session <em>actually runs at</em>, which on a rescheduled
    /// occurrence are the new ones — see <c>ScheduleResolver.Resolve</c>. This method has no opinion
    /// about where they came from.</para>
    /// </summary>
    public bool CoversSession(DateOnly date, TimeOnly sessionStart, TimeOnly sessionEnd)
    {
        if (!CoversDate(date)) return false;
        if (Window is not { } window) return true;              // whole days: the hours are not consulted
        return window.Start < sessionEnd && sessionStart < window.End;
    }
}
