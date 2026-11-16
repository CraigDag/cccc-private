namespace CCCCScheduler.Models;

/// <summary>
/// A curriculum area: the heading a <see cref="ClassEntry"/>'s hours are summed under, and the level
/// the school sets weekly targets at. The course administrator's real five are <i>Circus – Body –
/// Move</i>, <i>Creation</i>, <i>Rotating small-subject window</i>, <i>Self-structured scheduled
/// study</i> and <i>Common programme activity</i> — but the list is data, not an enum, because it is
/// theirs to change without a release.
///
/// <para>A managed, editable list at the building level, like <see cref="ClassEntry"/> and
/// <see cref="Strand"/>. A class references an area by id and may reference none: not everything
/// scheduled belongs to the curriculum, so <see cref="ClassEntry.AreaId"/> is nullable and unassigned
/// is a legitimate, persistable state rather than a gap to be filled in.</para>
/// </summary>
public sealed class Area
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>
    /// Hours of this area the week is meant to contain — <i>"20 CBM hours"</i> in the administrator's
    /// sheet. Weekly, and about the week being looked at; there is no term or programme total.
    ///
    /// <para><c>null</c> means <b>no target</b>, which is what every area arrives as and what most
    /// stay as: only one of the five in the real sheet carries one. An area without a target is still
    /// a heading its hours are summed under; it simply has no line to fall short of.</para>
    ///
    /// <para><c>TimeSpan.Zero</c> is NOT the same as <c>null</c>, and the distinction matters more here
    /// than for a limit because <b>the headline is the gap</b>. A zero target says "expect none of this
    /// and report the surplus"; no target says "do not report this line at all". Testing
    /// <c>is null or { Ticks: 0 }</c> would silently make one mean the other — the same trap
    /// <see cref="Facilitator.MaxWeeklyHours"/>'s doc exists to prevent, one object across.</para>
    ///
    /// <para>A target is something to <b>advise</b> against, never to enforce. Not every week is a full
    /// operating week, and a week with a closure in it is legitimately under target; falling short is
    /// information, not a failure.</para>
    /// </summary>
    public TimeSpan? WeeklyTarget { get; set; }
}
