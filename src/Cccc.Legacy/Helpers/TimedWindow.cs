using CCCCScheduler.Models;

namespace CCCCScheduler.Helpers;

/// <summary>
/// <em>Part of the day</em> — the rule behind the tick and the two <see cref="TimeSlots"/> dropdowns
/// that turn a whole-day absence into an hours-of-each-day one.
///
/// <para><b>The invariant this carries is the resolver's, not a dialog's.</b>
/// <see cref="Absence"/>'s doc says a window that is inverted, zero-length or half-given is read as a
/// <b>whole day</b> (<see cref="Absence.NormaliseWindow"/>), and that such shapes arrive only
/// hand-edited or foreign because no dialog can produce one. Letting one out would put the list and
/// the timetable in flat contradiction — "away 16:00–16:00" on screen while every session that day is
/// flagged for cover.</para>
///
/// <list type="bullet">
/// <item><b>The tick decides.</b> Unticked is whole days, whatever the two dropdowns are still
/// showing — they keep their last values rather than being cleared, so their contents must not be
/// able to refuse an absence the user has switched the hours off for.</item>
/// <item><b>Both times or neither, never one.</b> Unreachable through either dialog today; refused
/// anyway, because the wrong answer to a cleared dropdown is to invent the missing end.</item>
/// <item><b>The end must be after the start.</b> Equal is refused with the inverted case:
/// <see cref="Absence.NormaliseWindow"/> cannot tell zero-length from backwards, so neither can
/// this.</item>
/// </list>
///
/// <para><b>Why it is in Core.</b> This rule was hand-copied into four dialogs — <c>AbsencesDialog</c>
/// and <c>FacilitatorsDialog</c> on WPF, and the same two on Avalonia through
/// <c>TimedWindowPicker</c>. Four copies is four chances to drop the <c>end &lt;= start</c> line, and
/// it is the shape this codebase has already paid for repeatedly: a rule restated per caller is a
/// rule that goes half-swept. Here it is stated once, tested once, and both apps read the same
/// answer.</para>
/// </summary>
public static class TimedWindow
{
    /// <summary>What the hours read when <em>Part of the day</em> is first ticked: an ordinary day.</summary>
    public static readonly TimeOnly DefaultStart = new(9, 0);

    /// <summary>The other end of that ordinary day.</summary>
    public static readonly TimeOnly DefaultEnd = new(17, 0);

    /// <summary>The refusal when a dropdown is unset — which cannot happen today, and must not become half a window if it ever does.</summary>
    public const string BothOrNeither = "Choose a start and end time, or untick \"Part of the day\".";

    /// <summary>The refusal when the window is inverted or zero-length. Both, deliberately: the resolver cannot tell them apart.</summary>
    public const string EndAfterStart = "The end time must be after the start time.";

    /// <summary>
    /// The hours to record, or <c>null</c> for whole days — and <c>false</c> when what is on screen is
    /// not an absence at all, in which case <paramref name="refusal"/> says why and nothing should be
    /// written.
    /// </summary>
    /// <param name="partOfDay">Whether <em>Part of the day</em> is ticked.</param>
    /// <param name="startText">The start dropdown's selection, in <see cref="TimeSlots"/>' own "HH:mm", or null if it has none.</param>
    /// <param name="endText">The end dropdown's selection, likewise.</param>
    public static bool TryRead(bool partOfDay, string? startText, string? endText,
                               out TimeOnly? startTime, out TimeOnly? endTime, out string? refusal)
    {
        startTime = null;
        endTime = null;
        refusal = null;
        if (!partOfDay) return true;

        if (startText is null || endText is null)
        {
            refusal = BothOrNeither;
            return false;
        }

        var start = TimeSlots.Parse(startText);
        var end = TimeSlots.Parse(endText);
        if (end <= start)
        {
            refusal = EndAfterStart;
            return false;
        }

        startTime = start;
        endTime = end;
        return true;
    }
}
