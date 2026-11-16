namespace CCCCScheduler.Models;

/// <summary>
/// A family of circus specialisms — Acrobatic, Aerial, Manipulation, Other. The parent level of the
/// second of the school's two hierarchies, and the one <see cref="Discipline"/>s hang off.
///
/// <para><b>It is not a <see cref="Strand"/>, and the two must never be conflated.</b> A strand is a
/// <em>programme</em> — Degree, Prep, Open Training, Event Hire, Adult Classes — with
/// <see cref="Group.StrandId"/> pointing at it, sessions deriving through it and the timetable filter
/// bar filtering on it. This is what a student <em>specialises in</em>. The first draft of the design
/// called this level "Strand" too; the structure was right and the word was already taken, so the
/// specialism parent is Branch.</para>
///
/// <para>A student never stores a branch: they store a <see cref="Student.DisciplineId"/> and the
/// branch is derived through it, exactly as a session's strand is derived through its group. Storing
/// both would make it possible to save somebody as Acrobatics + Aerial Hoop.</para>
///
/// <para><b>"Other" is fine if it means a real specialism that is not acro, aerial or manipulation</b>
/// — rope, clown. If it comes to mean "not sure yet", that is a <c>null</c>
/// <see cref="Student.DisciplineId"/> and the branch should be removed.</para>
///
/// <para>There are no defaults. A new project starts with no branches and no disciplines, exactly as
/// a migrated one does, so the two can never disagree about what the school teaches — and nothing
/// here invents a name the school did not type.</para>
/// </summary>
public sealed class Branch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Hex colour, e.g. "#607D8B". Disciplines keep their own; this is for branch-level views.</summary>
    public string ColourHex { get; set; } = "#607D8B";
}
