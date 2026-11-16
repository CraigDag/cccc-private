using System.Globalization;
using System.Text.RegularExpressions;

namespace CCCCScheduler.Helpers;

/// <summary>
/// A length of time as somebody would type it and as somebody would say it — "2h 30m", "45m", "2h".
///
/// <para>Exists because the two workload limits are durations a human enters into a box, and the box
/// has to tell three answers apart: <b>blank means no limit</b>, <b><c>TimeSpan.Zero</c> is a real
/// limit</b> somebody deliberately typed (a career break, a bookings-only contact), and anything
/// unparseable must be <b>refused rather than guessed at</b>. Reading a blank box as zero would turn
/// "no limit" into "may teach nothing"; reading a typed zero as blank would do the exact inverse and
/// silently disarm the warning. Both inversions are invisible until somebody's overload goes
/// unflagged, so the distinction lives here, in one tested place, rather than in a dialog's
/// <c>string.IsNullOrEmpty</c>.</para>
///
/// <para><see cref="Format"/> is also what the conflicts panel speaks, so a limit typed into the
/// profile reads back in the warning as the thing that was typed rather than as "02:30:00".</para>
/// </summary>
public static class DurationText
{
    /// <summary>"2h", "2h 30m", "2h30m", "45m" — hours optionally fractional, minutes whole.</summary>
    private static readonly Regex UnitForm = new(
        @"^(?:(?<h>\d+(?:\.\d+)?)\s*h)?\s*(?:(?<m>\d+)\s*m)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Past this, the number was a typo rather than a duration. Guards <see cref="TimeSpan.FromMinutes"/>,
    /// which throws on a double large enough to overflow — the same shape as the mistyped "2226" in the
    /// Absences dialog that froze the whole app for ten minutes.
    /// </summary>
    private const double MaxMinutes = 100 * 24 * 60;

    /// <summary>
    /// Reads a typed duration. Returns false only for text that means nothing; <c>true</c> with a
    /// <c>null</c> <paramref name="value"/> is the real answer for a blank box — <b>no limit</b>.
    /// </summary>
    public static bool TryParse(string? text, out TimeSpan? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text)) return true;   // blank is not a failure: it is "no limit"

        string s = text.Trim();

        // "2:30" — the clock-ish form, and the one that must not be mistaken for a decimal.
        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            if (parts.Length != 2) return false;
            if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out int hh)) return false;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int mm)) return false;
            if (mm > 59) return false;
            return Minutes(hh * 60d + mm, out value);
        }

        // "2h 30m" and every spacing of it. A match capturing neither group is the empty string, which
        // was handled above — anything else that matches nothing (a bare number) falls through.
        var m2 = UnitForm.Match(s);
        if (m2.Success && (m2.Groups["h"].Success || m2.Groups["m"].Success))
        {
            double total = 0;
            if (m2.Groups["h"].Success)
                total += double.Parse(m2.Groups["h"].Value, CultureInfo.InvariantCulture) * 60d;
            if (m2.Groups["m"].Success)
                total += double.Parse(m2.Groups["m"].Value, CultureInfo.InvariantCulture);
            return Minutes(total, out value);
        }

        // A bare number is hours — "12" is twelve hours, which is what somebody typing a weekly maximum
        // means. NumberStyles.AllowDecimalPoint and nothing else, so a leading sign is rejected rather
        // than quietly accepted: a negative maximum is not a limit anybody can mean.
        if (double.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double hours))
            return Minutes(hours * 60d, out value);

        return false;
    }

    private static bool Minutes(double total, out TimeSpan? value)
    {
        value = null;
        if (total < 0 || total > MaxMinutes) return false;
        value = TimeSpan.FromMinutes(Math.Round(total));
        return true;
    }

    /// <summary>
    /// A duration the way somebody would say it out loud. Whole hours drop the minutes and sub-hour
    /// spans drop the hours, so what comes back is what was typed; zero reads "0h", because a
    /// deliberate zero has to look like an answer rather than like an empty box.
    /// </summary>
    public static string Format(TimeSpan t)
    {
        string sign = t < TimeSpan.Zero ? "-" : "";
        var abs = t.Duration();
        int hours = (int)abs.TotalHours, minutes = abs.Minutes;
        return hours != 0 && minutes != 0 ? $"{sign}{hours}h {minutes}m"
             : minutes != 0 ? $"{sign}{minutes}m"
             : $"{sign}{hours}h";
    }

    /// <summary>What a limit box shows: the duration, or an empty box for "no limit".</summary>
    public static string Format(TimeSpan? t) => t is { } span ? Format(span) : "";
}
