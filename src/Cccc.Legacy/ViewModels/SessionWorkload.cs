using CCCCScheduler.Models;

namespace CCCCScheduler.ViewModels;

/// <summary>
/// <b>What kind of working load one session carries — the one implementation of the lookup.</b>
///
/// <para>The kind is the linked <see cref="ClassEntry.Workload"/>, and the whole of the subtlety is
/// what happens when there is no class to read it from. Both cases answer
/// <see cref="WorkloadKind.Counted"/>, and for the same reason: a session with no class at all is an
/// ordinary session somebody has not filed yet, and a session naming a class this project no longer
/// holds is a catalogue defect. Neither is a statement that attendance is optional, so neither may
/// quietly mark a line as optional — <c>StudentWeeklyLoad.IsRequired</c> has read it that way since
/// the kinds were introduced, and this is that rule stated once where a second reader can share
/// it.</para>
///
/// <para><b>Why it moved out of <c>StudentWeeklyLoad</c>.</b> The <i>Open sessions</i> filter asks the
/// same question of the same fact — is this one of the sessions that is open to anyone and required
/// of nobody — and a second private copy of "no class means counted" is precisely the shape this
/// repository keeps paying for: the two would agree on the day they were written and disagree the
/// first time either grew a case. A student's hours and the week they are looking at must never
/// disagree about which sessions are optional.</para>
/// </summary>
public static class SessionWorkload
{
    /// <summary>
    /// The kind, from a class that has <b>already been looked up</b> — <c>null</c> for a session with
    /// no class, or one whose class the project no longer holds. Both are
    /// <see cref="WorkloadKind.Counted"/>; see the type's own remarks for why.
    /// </summary>
    public static WorkloadKind OfClass(ClassEntry? linkedClass)
        => linkedClass?.Workload ?? WorkloadKind.Counted;

    /// <summary>
    /// The kind for <paramref name="entry"/>, resolving its class against
    /// <paramref name="building"/>'s catalogue. The lookup is the ordinary one — an id naming nothing
    /// resolves to <c>null</c> and lands on <see cref="OfClass"/>'s answer rather than throwing.
    /// </summary>
    public static WorkloadKind Of(ScheduleEntry entry, Building building)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(building);

        return OfClass(entry.ClassId is Guid id
            ? building.Classes.FirstOrDefault(c => c.Id == id)
            : null);
    }

    /// <summary>
    /// <b>Whether this is an "open session"</b> — real activity, open to anyone, required of nobody.
    /// Open Training is the case it exists for, and the phrase is the owner's:
    /// <i>"open to anyone means that it's not a required class for the schedule"</i> (2026-09-13).
    ///
    /// <para>It is <see cref="WorkloadKind.DropIn"/> and nothing else. <see cref="WorkloadKind.NotCounted"/>
    /// — lunch, cleaning — is deliberately NOT an open session: it is in the daily time in the school
    /// and belongs on a student's week, it is simply not working load. Two kinds that both fail to
    /// count towards a total are not thereby the same kind of thing, and folding them together here
    /// would take lunch off the timetable the first time anybody pressed <i>Hide</i>.</para>
    /// </summary>
    public static bool IsOpenToAnyone(WorkloadKind kind) => kind == WorkloadKind.DropIn;
}
