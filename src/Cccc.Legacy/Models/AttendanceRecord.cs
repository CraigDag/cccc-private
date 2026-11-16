namespace CCCCScheduler.Models;

/// <summary>
/// <b>What one register says about one student.</b> Three marks since schema 14, where there were
/// two — present, or not on the list.
///
/// <para><b>Why a third one and not a fourth.</b> The question anybody actually asks of an
/// attendance figure is <i>"is the missing part explained?"</i>, and two marks cannot answer it: at
/// a circus school injury is ordinary, so <em>away and we know why</em> and <em>did not turn
/// up</em> being the same mark is what makes 78% a number nobody can act on. <em>Late</em> was
/// offered and left out deliberately — a mark is a thing somebody will be asked to report on, and a
/// fourth column earns its place only by making every register slower to file.</para>
/// </summary>
public enum AttendanceMark
{
    /// <summary>They were in the room.</summary>
    Present,

    /// <summary>Away, and the school knew — injury, an approved trip, a cleared appointment.</summary>
    Excused,

    /// <summary>Not there, and no word. <b>The mark a register defaults to by saying nothing.</b></summary>
    Absent,
}

/// <summary>
/// Attendance taken for one weekly session on one specific week. Stores who was present and who was
/// away with the school's knowledge; any enrolled student in neither list is absent with no word.
/// No record for a week means attendance was simply not taken.
///
/// <para><b>TWO LISTS RATHER THAN A MARK PER STUDENT, and the reason is the migration.</b> Every
/// register ever filed by this app is a present-list, and a schema-13 file read here has no excused
/// key at all — which deserialises to an empty list, which is exactly what those registers meant:
/// everybody not present was absent with no word, because there was no other thing they could be.
/// So the bump needs no migration block, only the wall. A dictionary keyed by student would have
/// had to be built from the present list on the way in and would have said the same thing in more
/// places.</para>
///
/// <para><b>The two lists must not overlap, and that is enforced rather than assumed.</b>
/// <see cref="Normalise"/> is called at every write door and again by <c>ProjectStore.Validate</c>
/// on the way in, exactly as <c>Absence.NormaliseWindow</c> is — a record that reached both lists
/// would answer <see cref="MarkFor"/> one way and a count of the excused another, and the two
/// figures would disagree on a screen that prints them side by side. <see cref="MarkFor"/> reads
/// present first regardless, so even a hand-edited file cannot make a student both.</para>
/// </summary>
public sealed class AttendanceRecord
{
    /// <summary>The Monday of the week the register was taken for.</summary>
    public DateOnly WeekMonday { get; set; }

    /// <summary>The enrolled students who attended that week.</summary>
    public List<Guid> PresentStudentIds { get; set; } = new();

    /// <summary>
    /// The enrolled students who were away <b>and the school knew why</b> — schema 14. Absent from a
    /// schema-13 file, where it deserialises to an empty list and means what that file meant.
    ///
    /// <para><b>No reason is stored here</b>, on purpose: <em>"Anna — shoulder"</em> is health data
    /// about an identifiable person at a school small enough for the name to be enough, and it would
    /// sit in a project file that gets copied, renamed and carried about. The reason lives on the
    /// student's <c>Absence</c>, entered once for the whole stretch, and the register reads it to
    /// pre-fill this list and then stores its own answer.</para>
    ///
    /// <para><b>Stored rather than derived, and that is the load-bearing half.</b> Deriving the mark
    /// from whichever absences happen to cover the date would mean editing an absence in November
    /// silently rewrote what every register back to September said. A register is a record of what
    /// somebody wrote down at the time — which is the same reason an empty register is a real record
    /// here rather than a missing one.</para>
    /// </summary>
    public List<Guid> ExcusedStudentIds { get; set; } = new();

    /// <summary>
    /// Who filed it, as the address their account carries, or null for a register filed before
    /// schema 14 recorded one.
    ///
    /// <para>It costs nothing and answers a question a school will have: a register that looks wrong
    /// was, until now, anonymous — there was no way to tell who wrote it or whether it was filed on
    /// the day or three weeks later. It is a member of staff's own sign-in address, which this file
    /// already carries on <c>Facilitator.ContactEmail</c>; no student's identity is added by it.</para>
    /// </summary>
    public string? FiledBy { get; set; }

    /// <summary>
    /// When it was filed, in UTC as every instant in this file is — the browser converts. Null for a
    /// register filed before schema 14, which is not the same as one filed at the epoch.
    /// </summary>
    public DateTimeOffset? FiledAt { get; set; }

    /// <summary>
    /// <b>Who was on the class when this register was filed</b> — schema 17, or null for a register
    /// that carries none.
    ///
    /// <para>Since schema 17 a class's list is worked out from its groups, so it moves when a student
    /// joins or leaves a group. A filed register must not move with it: moving a student from Year
    /// One to Year Two in January would otherwise quietly change who was absent from a Year One class
    /// in September. So the list is written down at filing, and <c>SessionRoster.ForRegister</c>
    /// reads it back in preference to the live one. The schema-17 migration stamps it on every
    /// register filed before, from the hand-kept roster that register was taken against.</para>
    /// </summary>
    public List<Guid>? RosterStudentIds { get; set; }

    /// <summary>
    /// <b>What this register says about one student.</b> The one place the three marks are read off
    /// the two lists, so nothing has to remember which list wins.
    ///
    /// <para><b>Present is asked first</b>, which is what makes a hand-edited file naming a student
    /// in both lists answer once rather than twice. Somebody who turned up after all is present
    /// whatever anybody expected of them, which is also the only reading that cannot quietly count a
    /// person who was in the room as away.</para>
    /// </summary>
    public AttendanceMark MarkFor(Guid studentId)
        => PresentStudentIds.Contains(studentId) ? AttendanceMark.Present
         : ExcusedStudentIds.Contains(studentId) ? AttendanceMark.Excused
         : AttendanceMark.Absent;

    /// <summary>
    /// <b>How many of a roster this register accounts for under each mark.</b> One pass, so a screen
    /// printing all three cannot get a different total from the one printing the first.
    ///
    /// <para>Counted against the <b>roster</b> and not against the lists, because a list can name a
    /// student who has since left the class — <c>ApplyStudentChanges</c> scrubs a deleted student
    /// from every register, but a person merely unenrolled keeps their old marks, which is right:
    /// they really were in the room that week.</para>
    /// </summary>
    public (int Present, int Excused, int Absent) Tally(IReadOnlyList<Guid> roster)
    {
        ArgumentNullException.ThrowIfNull(roster);

        int present = 0, excused = 0, absent = 0;
        foreach (var id in roster)
            switch (MarkFor(id))
            {
                case AttendanceMark.Present: present++; break;
                case AttendanceMark.Excused: excused++; break;
                default: absent++; break;
            }

        return (present, excused, absent);
    }

    /// <summary>
    /// <b>Makes the two lists disjoint, present winning.</b> Called at every write door and by
    /// <c>ProjectStore.Validate</c> on load, so an overlap never reaches storage and never survives
    /// a read — the same belt-and-braces <c>Absence.NormaliseWindow</c> has.
    ///
    /// <para>It drops from the excused list rather than from the present one, agreeing with
    /// <see cref="MarkFor"/>. The two answering differently is the whole failure this exists to
    /// prevent: one register, two numbers.</para>
    /// </summary>
    public void Normalise()
    {
        PresentStudentIds ??= new List<Guid>();
        ExcusedStudentIds ??= new List<Guid>();
        ExcusedStudentIds.RemoveAll(PresentStudentIds.Contains);
    }
}
