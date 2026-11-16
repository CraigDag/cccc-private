using System.Text.Json.Serialization;

namespace CCCCScheduler.Models;

/// <summary>
/// An individual student — a managed, editable base list at the building level. Sessions reference
/// students by id through <see cref="ScheduleEntry.StudentIds"/> (the roster) and
/// <see cref="AttendanceRecord.PresentStudentIds"/> (the register).
///
/// <para><b>Every field below the name is optional and stays optional.</b> A blank discipline is a
/// legitimate <em>permanent</em> state rather than an error — Prep students have none, by design —
/// and so is a blank group and a blank contact. Nothing anywhere enforces or flags them; this is the
/// stance <see cref="ClassEntry.AreaId"/> already takes.</para>
/// </summary>
public sealed class Student
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Their first name — and, on <b>every</b> student migrated from schema 7 or earlier, their whole
    /// name, because that is what was stored and nothing here guesses at more.
    /// </summary>
    public string FirstName { get; set; } = "";

    /// <summary>
    /// Their surname, or blank. <b>Blank on every migrated student</b>: schema 7 stored one name per
    /// student and the upgrade puts it whole into <see cref="FirstName"/> rather than splitting it on
    /// the last space, which would break on "van der Berg" and on anyone entered surname-first — and
    /// would do so <i>silently, on a handful</i> of real students, rather than visibly on all of them.
    /// The owner chose this: nothing is ever guessed, and the surname is typed in once, by a human.
    /// </summary>
    public string Surname { get; set; } = "";

    /// <summary>
    /// Where an individual timetable would be sent. Deliberately named <b>contact</b> email rather
    /// than Email: if the student is a minor the address held is likely a parent's, so the field is
    /// self-documenting rather than misleading.
    ///
    /// <para>Holding the address is not the same as building the send — that is separate work, whose
    /// hard half is change detection. This is the address to send to; the individual timetable it
    /// would carry is what the student filter and the student weekly load deliver.</para>
    /// </summary>
    public string ContactEmail { get; set; } = "";

    /// <summary>
    /// The <see cref="Group"/> they are timetabled with — BA Y1, Prep, and so on. Null means
    /// unassigned, which is how every migrated student arrives and a state any student may stay in.
    ///
    /// <para>The strand is <b>derived through the group</b> and never stored beside it, the rule the
    /// model already follows for sessions: storing both would make it possible to save a student as
    /// Degree + Adult Courses.</para>
    ///
    /// <para>An id naming a group the project does not contain is corruption, never a state to
    /// honour, and is scrubbed back to null on load — never repaired by inventing a group. See the
    /// orphan scrub in <c>ProjectStore.Validate</c> and the delete cascade in
    /// <c>MainViewModel.ApplyGroupChanges</c>; both halves are swept, so the load path and the live
    /// path cannot disagree about what the project contains.</para>
    /// </summary>
    public Guid? GroupId { get; set; }

    /// <summary>
    /// What they specialise in — the leaf of the second hierarchy, with its <see cref="Branch"/>
    /// derived through it exactly as a group's strand is. Null is unassigned and is a legitimate
    /// permanent state: <b>Prep students have no discipline and are never flagged for it.</b>
    ///
    /// <para><b>"N/A" is null, not a record.</b> A real <see cref="Discipline"/> called N/A would need
    /// a branch, would appear in every dropdown, would show in reports as though it were a specialism,
    /// and would need special-casing in every count.</para>
    ///
    /// <para>Scrubbed to null on load when it names a discipline the project does not contain, and by
    /// <c>MainViewModel.ApplyDisciplineChanges</c> when one is deleted — same pair of halves as
    /// <see cref="GroupId"/>.</para>
    /// </summary>
    public Guid? DisciplineId { get; set; }

    /// <summary>
    /// <b>Legacy input only, and the single reason it exists is the 7→8 upgrade.</b> Schema 7 and
    /// earlier stored one <c>"Name"</c> per student; this is the property that key deserialises into,
    /// so the migration in <c>ProjectStore.Validate</c> has the old value to move whole into
    /// <see cref="FirstName"/>. It is cleared on every load once read — ungated, so a stray
    /// <c>"Name"</c> hand-edited onto a schema-8 file cannot survive to contradict the two real fields
    /// on every later save.
    ///
    /// <para>This is <see cref="Holiday.WeekMonday"/>'s idiom one object across, and for the same
    /// reason: a migrated record that keeps its legacy field writes a value contradicting its own new
    /// ones into every save from then on. That staleness has already cost this project one regression
    /// — the Term &amp; holidays dialog read <c>WeekMonday</c>, showed a bogus date, and fed it back.</para>
    ///
    /// <para><b>Do not read this anywhere else.</b> <see cref="Name"/> is the display name; this is a
    /// fact about the bytes an old file arrived with.</para>
    /// </summary>
    [JsonPropertyName("Name")]
    public string? LegacyName { get; set; }

    /// <summary>
    /// Their name as anything shows it: <see cref="FirstName"/> and <see cref="Surname"/> joined by a
    /// single space, with either half alone rendering without a stray one.
    ///
    /// <para><b>Computed, and get-only on purpose.</b> The split is additive — <c>Name</c> did not
    /// stop being the field when the two real ones arrived, which is exactly what let every existing
    /// reader (the enrolment dialog, the attendance dialog, every report, every export, every picker)
    /// carry on untouched. Being unassignable is the guard: a computed property cannot be written, so
    /// the compiler, not a reviewer, is what proves no reader quietly overwrites a split name with a
    /// whole one.</para>
    ///
    /// <para><b>[JsonIgnore] for two independent reasons.</b> It would otherwise collide with
    /// <see cref="LegacyName"/> over the <c>"Name"</c> key, which <c>System.Text.Json</c> refuses
    /// outright — and it is derived, so writing it to <c>project.json</c> would put a fourth copy of
    /// the name on disk, free to drift out of step with the two fields it is derived from.</para>
    /// </summary>
    [JsonIgnore]
    public string Name =>
        string.IsNullOrWhiteSpace(Surname) ? FirstName
        : string.IsNullOrWhiteSpace(FirstName) ? Surname
        : FirstName + " " + Surname;
}
