namespace CCCCScheduler.Models;

/// <summary>
/// One circus specialism a student chooses and focuses on — Partner Acro, Aerial Hoop, Juggling,
/// Chinese Pole. Admin-defined: the school types the list it actually teaches, and nothing here ships
/// a guess at it.
///
/// <para>The leaf of the specialism hierarchy. <see cref="Student.DisciplineId"/> points here and the
/// <see cref="Branch"/> is derived through <see cref="BranchId"/> — the same shape
/// <see cref="Group"/>/<see cref="Group.StrandId"/> has, and for the same reason: persisting only the
/// leaf makes it impossible to save a student as Acrobatics + Aerial Hoop.</para>
///
/// <para><b>Always shown, always optional, never enforced.</b> "BA students choose a discipline and
/// Prep students do not" is a fact about the <em>strand</em>, not about any one group, so anything
/// encoding it against a group silently stops working the day BA Y1 and BA Y2 both exist. There is
/// therefore no rule to encode: a blank discipline is a legitimate permanent state.</para>
/// </summary>
public sealed class Discipline
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Hex colour, e.g. "#5E35B1".</summary>
    public string ColourHex { get; set; } = "#5E35B1";

    /// <summary>
    /// The <see cref="Branch"/> this discipline belongs to, mirroring <see cref="Group.StrandId"/>.
    /// Null means unassigned — how a migrated or hand-edited project can arrive, and what
    /// <c>MainViewModel.ApplyBranchChanges</c> leaves behind when a branch is deleted, following
    /// exactly what <c>ApplyStrandChanges</c> does to the groups under a deleted strand: losing the
    /// family must never take the specialisms, or the students who chose them, with it.
    /// </summary>
    public Guid? BranchId { get; set; }
}
