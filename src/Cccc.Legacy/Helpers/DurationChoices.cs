namespace CCCCScheduler.Helpers;

/// <summary>
/// A length of time as somebody would <b>pick</b> it: the numbers an hours dropdown and a minutes
/// dropdown offer, and the split and join between that pair and one <see cref="TimeSpan"/>.
///
/// <para>Sibling to <see cref="DurationText"/>, which is the same duration as somebody would type it.
/// Both exist for the same reason and keep the same distinction: <c>null</c> is <b>no limit</b> and
/// <see cref="TimeSpan.Zero"/> is a <b>limit of none</b>, and confusing the two silently disarms a
/// warning or forbids a week. A pair of dropdowns cannot express the first at all — 0h 0m is a real
/// answer, not an empty box — so "no limit" is a separate control on the dialog, and nothing here
/// returns or accepts null as a value. This class only ever describes a limit that exists.</para>
///
/// <para><b>The list is not a ceiling.</b> A project in daily use can hold a limit no dropdown would
/// ever have offered: 1.8.0 shipped these limits as free text, so a 37-minute maximum is real data,
/// and so is an hours figure past the end of a range chosen to be short enough to pick from. Rounding
/// such a value onto the grid would rewrite somebody's setting because they opened the profile to
/// look at it. So <see cref="Hours"/> and <see cref="Minutes"/> take the value being shown and add it
/// to the list when it is not already there: it displays truthfully, it survives untouched, and it
/// can be kept while the other half of the pair is changed.</para>
/// </summary>
public static class DurationChoices
{
    /// <summary>
    /// Quarter hours, the granularity the whole app already enters time at — see
    /// <c>TimeSlots.StepMinutes</c>, and the 15/30/45/60 session lengths the school settled on.
    /// </summary>
    public const int MinuteStep = 15;

    /// <summary>
    /// A whole teaching week, so the range runs past a full-time contract with room above it. A
    /// weekly total and a single gap are different magnitudes and deliberately do not share a range.
    /// </summary>
    public const int WeeklyMaxHours = 60;

    /// <summary>
    /// One gap between two classes on one day — the owner's own example is 2h, and the building runs
    /// 07:00–23:00, so eight hours is already double any dead time somebody would set a limit about.
    /// Kept short on purpose: a dropdown of 61 entries to find "2" would be worse than the box it
    /// replaced. Anything larger still survives, via the rule in the class doc.
    /// </summary>
    public const int GapMaxHours = 8;

    /// <summary>
    /// 0…<paramref name="maxHours"/>, plus the hours of <paramref name="shown"/> when that falls past
    /// the end. Appended rather than inserted because the only way to miss the range is to exceed it:
    /// <see cref="Split"/> never returns a negative.
    /// </summary>
    public static IReadOnlyList<int> Hours(int maxHours, TimeSpan? shown)
    {
        var list = new List<int>(maxHours + 2);
        for (int h = 0; h <= maxHours; h++) list.Add(h);

        if (shown is { } value && Split(value).Hours is var hours && hours > maxHours) list.Add(hours);
        return list;
    }

    /// <summary>
    /// 0, 15, 30, 45 — plus the minutes of <paramref name="shown"/> when it sits between two of them,
    /// in order, so the list still reads as a clock face rather than putting the odd one last.
    /// </summary>
    public static IReadOnlyList<int> Minutes(TimeSpan? shown)
    {
        var list = new List<int>(5);
        for (int m = 0; m < 60; m += MinuteStep) list.Add(m);

        if (shown is { } value && Split(value).Minutes is var minutes && !list.Contains(minutes))
        {
            list.Add(minutes);
            list.Sort();
        }
        return list;
    }

    /// <summary>
    /// What the two dropdowns read for a stored limit. Seconds are dropped, because the pair has
    /// nowhere to show them — that is not a truncation of the stored value, only of what is displayed,
    /// and <see cref="Shows"/> is how the dialog keeps the difference.
    ///
    /// <para>A negative reads as 0h 0m rather than as negative hours and negative minutes.
    /// <c>ProjectStore.RepairLimit</c> clamps a hand-edited negative at load and no picker can produce
    /// one, so this is unreachable through any supported path; when it does arrive it must not put
    /// negative numbers in a dropdown, and <see cref="Shows"/> still reports it as displayed, so the
    /// dialog hands the stored value back rather than rewriting a defect the loader owns.</para>
    /// </summary>
    public static (int Hours, int Minutes) Split(TimeSpan value)
        => value <= TimeSpan.Zero ? (0, 0) : ((int)value.TotalHours, value.Minutes);

    /// <summary>The pair as one duration. Exact: no rounding, and 0h 0m is <see cref="TimeSpan.Zero"/>.</summary>
    public static TimeSpan Compose(int hours, int minutes) => new(hours, minutes, 0);

    /// <summary>
    /// Whether the pair is still displaying <paramref name="value"/> — i.e. whether the user has left
    /// it alone. <b>The guard against a profile opened only to look rewriting what it found.</b> True
    /// means hand the stored value straight back instead of <see cref="Compose"/>'s reading of it, so
    /// a limit the pair cannot express — one carrying seconds, or a hand-edited negative — survives
    /// exactly as stored. Any other pair of numbers is an edit the user made, and means what it says.
    /// </summary>
    public static bool Shows(TimeSpan value, int hours, int minutes) => Split(value) == (hours, minutes);
}
