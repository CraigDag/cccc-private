using CCCCScheduler.Helpers;

namespace CCCCScheduler.Models;

/// <summary>
/// Who a closure applies to. Prep, Degree and Adult keep different holiday schedules — mostly the
/// same, diverging a few times a year — so a closure has to be able to shut some of the school
/// without shutting all of it.
///
/// <para><see cref="WholeBuilding"/> is deliberately first, and therefore the value a
/// <see cref="Holiday"/> takes when the JSON carries no <c>Scope</c> key at all. Every holiday
/// written before schema 4 closed the entire school, so that default is what makes an existing
/// project behave identically after migration — for free, without the loader having to guess.
/// Reordering these two would silently reinterpret every closure in every existing file as
/// "applies to the groups in this empty list", i.e. to nobody.</para>
///
/// <para>An enum rather than a bool so a third answer can be added without breaking the format —
/// scoping to a <c>Strand</c> is the obvious candidate, since a strand survives a programme gaining
/// a new year group and a hand-ticked list of groups does not. Deliberately not added yet: it needs
/// the owner's decision, not ours.</para>
/// </summary>
public enum HolidayScope
{
    /// <summary>
    /// The doors are shut. Nothing runs — including sessions belonging to no group at all, such as
    /// an External Hire booking or a one-off.
    /// </summary>
    WholeBuilding,

    /// <summary>
    /// Only the groups listed in <see cref="Holiday.GroupIds"/> are off; everyone else runs as
    /// normal. A session with no group is therefore unaffected, and an empty list closes nothing.
    /// </summary>
    Groups,
}

/// <summary>
/// A closure over a range of whole days — half term, a bank holiday, a Christmas shutdown. Sessions
/// inside the range do not run, and a session that does not run cannot need cover.
///
/// Dates rather than weeks because real term dates do not respect week boundaries: a term can end on
/// a Wednesday, and a bank holiday is one day. A closure can shut the whole building or only certain
/// groups — see <see cref="Scope"/>.
/// </summary>
public sealed class Holiday
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Why it's off, e.g. "Half term". Shown on the banner and on exports.</summary>
    public string Name { get; set; } = "";

    /// <summary>First day off, inclusive.</summary>
    public DateOnly From { get; set; }

    /// <summary>Last day off, inclusive.</summary>
    public DateOnly To { get; set; }

    /// <summary>
    /// Who is off. Defaults to <see cref="HolidayScope.WholeBuilding"/> — the behaviour of every
    /// holiday this app wrote before schema 4, and what a file predating the key deserialises as.
    /// </summary>
    public HolidayScope Scope { get; set; }

    /// <summary>
    /// The groups this closure applies to, meaningful only when <see cref="Scope"/> is
    /// <see cref="HolidayScope.Groups"/>. Ids rather than names so renaming a cohort in
    /// Manage ▸ Groups does not silently detach it from its own half term.
    /// </summary>
    public List<Guid> GroupIds { get; set; } = new();

    /// <summary>
    /// Schema-2 and earlier stored a whole week keyed by its Monday. Read only by the migration in
    /// <c>ProjectStore.Validate</c> and never written again.
    /// </summary>
    public DateOnly WeekMonday { get; set; }

    /// <summary>True when <paramref name="date"/> falls inside this closure. Both ends inclusive.</summary>
    public bool CoversDate(DateOnly date) => date >= From && date <= To;

    /// <summary>
    /// A whole-week closure, Monday to Sunday, from any date inside that week. The shape every caller
    /// meant before holidays became date ranges — and the one the Term &amp; holidays dialog still
    /// offers, since it asks for "any date in the week". Uses <see cref="WeekLabel.MondayOf"/> directly
    /// rather than <c>ScheduleResolver.WeekOf</c>'s wrapper, so a model doesn't depend on a view model.
    /// </summary>
    public static Holiday ForWeek(DateOnly anyDateInWeek, string name = "")
    {
        var monday = WeekLabel.MondayOf(anyDateInWeek);
        return new Holiday { Name = name, From = monday, To = monday.AddDays(6) };
    }
}
