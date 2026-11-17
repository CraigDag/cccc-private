namespace Cccc.Web.Features.Data;

/// <summary>Hours typed by a person: "6", "6.5", "6:30" or "6h 30m". Empty means none.</summary>
public static class HoursText
{
    public static string Show(TimeSpan? t) => t is { } x ? $"{(int)x.TotalHours}:{x.Minutes:00}" : "";

    public static bool TryRead(string? s, out TimeSpan? value)
    {
        value = null;
        s = s?.Trim().ToLowerInvariant() ?? "";
        if (s.Length == 0) return true;
        s = s.Replace("h", ":").Replace("m", "").Replace(" ", "");
        if (s.Contains(':'))
        {
            var parts = s.Split(':', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], out var h) || h < 0) return false;
            int m = 0;
            if (parts.Length == 2 && (!int.TryParse(parts[1], out m) || m is < 0 or > 59)) return false;
            value = new TimeSpan(h, m, 0);
            return true;
        }
        if (double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) && d >= 0 && d < 200)
        {
            value = TimeSpan.FromMinutes(Math.Round(d * 60));
            return true;
        }
        return false;
    }
}
