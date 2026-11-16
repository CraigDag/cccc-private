using CCCCScheduler.Models;

namespace CCCCScheduler.Helpers;

/// <summary>
/// How an absence is put into words in a list. <b>One implementation, read by both editors</b> —
/// <c>Manage ▸ Absences…</c> and the facilitator profile — so the same absence cannot be described
/// two ways depending on which window somebody opened it from.
/// </summary>
public static class AbsenceText
{
    /// <summary>
    /// The days: one date, or a range with both ends. Inclusive at both ends, so a single day is
    /// <c>From == To</c> and prints as itself rather than as a range of one.
    /// </summary>
    public static string DateRange(DateOnly from, DateOnly to)
        => from == to
            ? from.ToString("d MMM yyyy")
            : $"{from:d MMM yyyy} – {to:d MMM yyyy}";

    /// <summary>
    /// The days, and the hours when there are hours — <c>"22 Jun 2026 – 26 Jun 2026, 14:00–17:00 on
    /// each day"</c>.
    ///
    /// <para><b>"on each day" is doing real work.</b> A range with times means those hours on
    /// <em>every</em> day in it, and "22 – 26 Jun, 14:00–17:00" on its own reads just as naturally as
    /// one continuous stretch from Monday teatime to Friday teatime. Those are wildly different
    /// absences and only one of them is what the model means, so the list says which. A single day
    /// omits the phrase: one day is not "each day", and the sentence would be noise.</para>
    ///
    /// <para><b>The hours are shown only when the model would read them</b>, through
    /// <see cref="Absence.NormaliseWindow"/>. A hand-edited 16:00–16:00 covers whole days as far as
    /// the resolver is concerned; listing it as "16:00–16:00" would put the list and the timetable in
    /// flat contradiction, with nothing on screen saying which one to believe.</para>
    /// </summary>
    public static string When(DateOnly from, DateOnly to, TimeOnly? startTime, TimeOnly? endTime)
    {
        string dates = DateRange(from, to);
        if (Hours(startTime, endTime) is not { } hours) return dates;

        return from == to ? $"{dates}, {hours}" : $"{dates}, {hours} on each day";
    }

    /// <summary>
    /// The hours on their own — <c>"14:00–17:00"</c> — or <c>null</c> when the window is one the
    /// model reads as whole days, which is the only condition under which an absence has no hours to
    /// show. Same rule and same en dash as <see cref="When"/>, because it <em>is</em>
    /// <see cref="When"/>'s: the two lists and the administrator's dock all come through here, so one
    /// absence cannot be written two ways depending on which panel is looking at it.
    ///
    /// <para>Separate from <see cref="When"/> for the dock's sake, and only the dock's: it describes a
    /// person's <em>week</em> by day name — <i>"away Mon 14:00–17:00"</i> — so it has the hours to say
    /// and no date range to say them against. Formatting them there rather than here is how a second
    /// spelling of an absence gets into the app.</para>
    /// </summary>
    public static string? Hours(TimeOnly? startTime, TimeOnly? endTime)
        => Absence.NormaliseWindow(startTime, endTime) is ({ } start, { } end)
            ? $"{TimeSlots.Text(start)}–{TimeSlots.Text(end)}"
            : null;

    /// <summary><c>"Planned"</c> or <c>"Unplanned"</c> — schema 22's two words, said one way everywhere.</summary>
    public static string NoticeWord(AbsenceNotice notice)
        => notice == AbsenceNotice.Planned ? "Planned" : "Unplanned";

    /// <summary>The words a person picks a cause by, and reads it back as. One list, one spelling.</summary>
    public static string CauseWord(AbsenceCause cause) => cause switch
    {
        AbsenceCause.Illness => "Illness",
        AbsenceCause.Personal => "Personal",
        AbsenceCause.ApprovedLeave => "Approved leave",
        _ => "Other",
    };

    /// <summary>The causes in the order they are offered.</summary>
    public static IReadOnlyList<AbsenceCause> Causes { get; } =
        [AbsenceCause.Illness, AbsenceCause.Personal, AbsenceCause.ApprovedLeave, AbsenceCause.Other];

    /// <summary>
    /// <b>Why a STUDENT is away, as far as this reader may know</b> — the one place that decides how
    /// much of an absence a screen says.
    ///
    /// <list type="bullet">
    /// <item><b>Anybody</b> is told whether it was planned: <c>"Planned"</c>, <c>"Unplanned"</c>, or
    /// nothing when nobody said.</item>
    /// <item><b>An administrator</b> is also told why: <c>"Unplanned: Illness"</c>. A student absence
    /// from before schema 22 has free text instead, and an administrator sees that too.</item>
    /// </list>
    ///
    /// <para><b>Null means there is nothing this reader may be told</b>, not "not away".</para>
    /// </summary>
    public static string? WhyFor(Absence absence, bool seesWhy)
    {
        ArgumentNullException.ThrowIfNull(absence);

        string? notice = absence.Notice is { } n ? NoticeWord(n) : null;
        if (!seesWhy) return notice;

        string? why = absence.Cause is { } c ? CauseWord(c)
                    : string.IsNullOrWhiteSpace(absence.Reason) ? null
                    : absence.Reason.Trim();

        return (notice, why) switch
        {
            (null, null) => null,
            ({ } a, null) => a,
            (null, { } b) => b,
            ({ } a, { } b) => $"{a}: {b}",
        };
    }
}
