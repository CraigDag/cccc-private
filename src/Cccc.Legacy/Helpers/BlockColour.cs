using System.Globalization;

namespace CCCCScheduler.Helpers;

/// <summary>
/// <b>The calendar block's colour, which is the owner's data rather than the palette's.</b>
///
/// <para>A block takes its room's colour or its group's, exactly as both desktop calendars do. Those
/// are hex values the owner chose in <i>Manage ▸ Rooms</i>, so the text on top of them cannot come
/// from a theme: a palette colour has no guaranteed contrast against a colour somebody else picked.
/// The desktop answers that with black or white chosen by the fill's luminance, and so does
/// this — <b>with one deliberate improvement</b>.</para>
///
/// <para><b>The desktop picks by a luminance threshold; this picks by the actual WCAG ratio.</b>
/// <c>Luminance &gt; 0.6 ? black : white</c> is close but not safe: a mid-grey fill of
/// <c>#808080</c> has a luminance of 0.5 and so gets white text at <b>3.95:1</b>, which is under AA.
/// Comparing the two candidate ratios instead and taking the larger is the same five lines and is
/// provably never worse than <b>4.58:1</b> on any fill whatsoever — the two curves cross at a
/// relative luminance of 0.179, where both read 4.58:1. So the whole calendar clears AA for every
/// colour the owner can choose, and <c>ThemeContrastBrowserTests</c> sweeps it to say so.</para>
/// </summary>
public static class BlockColour
{
    /// <summary>What an unparseable or missing colour becomes — both desktop calendars' own fallback.</summary>
    public static (int R, int G, int B) Fallback { get; } = (0x55, 0x66, 0x88);

    /// <summary>
    /// <c>#RGB</c>, <c>#RRGGBB</c> and <c>#AARRGGBB</c>, which is the form the project file uses.
    /// Anything else is <see cref="Fallback"/> rather than an exception — this runs inside a render.
    /// </summary>
    public static (int R, int G, int B) Parse(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Fallback;
        string s = hex.Trim().TrimStart('#');

        // #AARRGGBB — the alpha is dropped rather than composited: nothing in this project stores a
        // translucent room colour, and guessing at what it would sit on would be worse than opaque.
        if (s.Length == 8) s = s[2..];

        if (s.Length == 3)
            s = new string(new[] { s[0], s[0], s[1], s[1], s[2], s[2] });

        if (s.Length != 6 || !int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
            return Fallback;

        return ((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
    }

    public static string ToHex((int R, int G, int B) c)
        => string.Create(CultureInfo.InvariantCulture, $"#{c.R:X2}{c.G:X2}{c.B:X2}");

    /// <summary>
    /// Black or white, whichever actually reads better on this fill. Never below 4.58:1.
    /// </summary>
    public static string ReadableOn((int R, int G, int B) fill)
    {
        double l = RelativeLuminance(fill);
        double onBlack = (l + 0.05) / 0.05;
        double onWhite = 1.05 / (l + 0.05);
        return onBlack >= onWhite ? "#000000" : "#FFFFFF";
    }

    /// <summary>WCAG 2.x relative luminance.</summary>
    public static double RelativeLuminance((int R, int G, int B) c)
        => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(int v)
    {
        double s = v / 255.0;
        return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    /// <summary>The WCAG contrast ratio between two colours — used by the tests that pin the rule.</summary>
    public static double Ratio((int R, int G, int B) a, (int R, int G, int B) b)
    {
        double la = RelativeLuminance(a), lb = RelativeLuminance(b);
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }
}
