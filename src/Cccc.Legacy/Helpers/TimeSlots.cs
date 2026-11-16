using System.Globalization;

namespace CCCCScheduler.Helpers;

/// <summary>
/// The operating-hours time grid used for entering session times. Defaults to 07:00–23:00 in
/// 15-minute steps — change these constants to adjust the whole app's time entry.
/// </summary>
public static class TimeSlots
{
    public static readonly TimeOnly DayStart = new(7, 0);
    public static readonly TimeOnly DayEnd = new(23, 0);
    public const int StepMinutes = 15;

    /// <summary>All slot labels ("HH:mm") from DayStart to DayEnd inclusive.</summary>
    public static List<string> Texts() => SlotsBetween(DayStart, DayEnd, StepMinutes);

    /// <summary>
    /// Slot labels from <paramref name="start"/> to <paramref name="end"/> inclusive, stepping by
    /// <paramref name="stepMinutes"/>.
    ///
    /// Counts minutes rather than comparing a <see cref="TimeOnly"/> that has been stepped forward:
    /// <see cref="TimeOnly.AddMinutes"/> wraps past midnight, so the old "advance until t >= end" loop
    /// never terminated when <paramref name="end"/> didn't land exactly on a step boundary — it span
    /// round the clock appending as it went. The constants happen to land exactly today, but the class
    /// doc invites changing them and 23:59 is an obvious choice.
    /// </summary>
    public static List<string> SlotsBetween(TimeOnly start, TimeOnly end, int stepMinutes)
    {
        if (stepMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(stepMinutes));

        var list = new List<string> { Text(start) };

        int startMin = (int)start.ToTimeSpan().TotalMinutes;
        int endMin = (int)end.ToTimeSpan().TotalMinutes;
        if (endMin <= startMin) return list;   // empty or inverted range: just the opening slot

        for (int m = startMin + stepMinutes; m <= endMin; m += stepMinutes)
            list.Add(Text(TimeOnly.FromTimeSpan(TimeSpan.FromMinutes(m))));

        // Close the range when end isn't a whole number of steps from start.
        if ((endMin - startMin) % stepMinutes != 0) list.Add(Text(end));

        return list;
    }

    public static string Text(TimeOnly t) => t.ToString("HH:mm", CultureInfo.InvariantCulture);

    public static TimeOnly Parse(string text) => TimeOnly.ParseExact(text, "HH:mm", CultureInfo.InvariantCulture);
}
