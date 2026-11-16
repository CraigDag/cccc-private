namespace CCCCScheduler.Models;

/// <summary>
/// Defines the span of a teaching term: a name, the Monday of its first week, and how many
/// weeks long it runs. Holidays (see <see cref="Holiday"/>) punch blackout weeks out of this
/// span, leaving the "teaching weeks". Stored on the <see cref="ProjectFile"/> (project-level
/// config, like layout-lock — not part of the undoable building edits).
/// </summary>
public sealed class TermInfo
{
    public string Name { get; set; } = "";

    /// <summary>The Monday of the term's first week. Any date is normalised to its Monday in logic.</summary>
    public DateOnly StartMonday { get; set; }

    /// <summary>Number of calendar weeks the term spans (including any holiday weeks).</summary>
    public int WeekCount { get; set; } = 12;

    /// <summary>
    /// The ceiling <c>ProjectStore.Validate</c> repairs <see cref="WeekCount"/> down to on load.
    ///
    /// <para>Ten years of weeks: two orders of magnitude past the thirty <c>Term &amp; holidays</c>
    /// offers, so no term anybody could have meant is touched, and far below the ~416,000 at which
    /// <see cref="CCCCScheduler.ViewModels.ScheduleResolver.EndMonday"/>'s <c>AddDays</c> leaves the
    /// range of <see cref="DateOnly"/> and throws. A hand-edited count past this is not a long term,
    /// it is a number nothing could have written.</para>
    /// </summary>
    public const int MaxWeekCount = 520;
}
