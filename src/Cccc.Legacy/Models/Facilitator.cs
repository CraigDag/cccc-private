namespace CCCCScheduler.Models;

/// <summary>A teacher who runs sessions. A managed, editable list at the building level.</summary>
public sealed class Facilitator
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = "";

    /// <summary>Hex colour for grids/plan colour-coding, e.g. "#00838F".</summary>
    public string ColourHex { get; set; } = "#00838F";

    /// <summary>
    /// Where their individual timetable would be sent. Named the same as
    /// <see cref="Student.ContactEmail"/> and blank by default for the same reasons — see that
    /// property. Holding the address is not building the send; the send is separate work.
    /// </summary>
    public string ContactEmail { get; set; } = "";

    /// <summary>
    /// <b>Somebody who comes in to teach a workshop or a class and is not on the staff</b> — Craig's
    /// ask, 2026-10-29: the building has to know who taught, and nobody should be making them an
    /// account. Schema 20.
    ///
    /// <para><b>Everything that TRACKS teaching treats a guest exactly like anybody else</b>: they are
    /// put on sessions, their hours count, they are on the reports and the printed sheets, and their
    /// limits are the same optional limits. <b>Only the join to a sign-in is refused</b>: Administration
    /// ▸ Accounts offers no guest to link, the address match never names one, and a link made before the
    /// box was ticked counts for nothing — because a link is what lets an account file a class's
    /// register. The cover picker still offers them, after the staff.</para>
    /// </summary>
    public bool IsGuest { get; set; }

    /// <summary>
    /// Weekly availability windows. Empty = available any time (no warnings). When non-empty, a
    /// session assigned to this facilitator outside every window is flagged as a clash.
    /// </summary>
    public List<AvailabilityWindow> Availability { get; set; } = new();

    /// <summary>
    /// Most they should teach in one week. <c>null</c> means <b>no limit</b>, which is what every
    /// facilitator arrives as and stays as until somebody sets one — a single school-wide default
    /// would be wrong, since a full-timer and someone teaching one class a week are not the same case.
    ///
    /// <para>Exceeding it <b>warns, never blocks</b>: real timetables break their own rules in
    /// exceptional weeks, and an app that refuses gets worked around instead of obeyed.</para>
    ///
    /// <para><c>TimeSpan.Zero</c> is NOT the same as <c>null</c>. Zero is a limit somebody can
    /// deliberately type — a facilitator on a career break, or a room-booking-only contact — so only
    /// <c>null</c> means unset. Testing <c>is null or { Ticks: 0 }</c> would silently turn a
    /// deliberate zero into "unlimited", the exact inversion of what was typed.</para>
    /// </summary>
    public TimeSpan? MaxWeeklyHours { get; set; }

    /// <summary>
    /// Most dead time this facilitator should be left with between two of their classes — someone
    /// stuck in the building waiting. One rule, about gaps that are too LONG; there is no
    /// minimum-turnaround rule and this is not it.
    ///
    /// <para>Measured between consecutive sessions <b>on the same day</b>. The time before their first
    /// class and after their last is not a gap, it is the start and end of their day, and neither is
    /// the overnight span between one day's last session and the next day's first.</para>
    ///
    /// <para><c>null</c> means no limit, and <c>TimeSpan.Zero</c> does not — see
    /// <see cref="MaxWeeklyHours"/>. Warns, never blocks.</para>
    /// </summary>
    public TimeSpan? MaxGapBetweenSessions { get; set; }
}
