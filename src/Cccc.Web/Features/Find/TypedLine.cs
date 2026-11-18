using System.Globalization;
using System.Text.RegularExpressions;
using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;

namespace Cccc.Web.Features.Find;

/// <summary>
/// A line typed into Find, word by word, and what its words can say: names (whole and longest first, without
/// their spaces, by a first name, or by the start of exactly one), days and dates, times and lengths. Read as
/// a class to add (<see cref="QuickAdd"/>) and as somebody away (<see cref="QuickAway"/>). Nothing is logged.
/// </summary>
internal static partial class TypedLine
{
    internal sealed class Word(string raw)
    {
        public string Raw { get; } = raw;
        public bool Used { get; set; }
        public bool Stop => StopWords.Contains(Raw);
        /// <summary>Whether it is any of these, a phone's curly apostrophe ("can’t") read as a straight one.</summary>
        public bool Is(params string[] any) => any.Any(a => string.Equals(Raw.Replace('’', '\''), a, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A name a line can say: the whole of it, without its spaces, or for a person the first name.</summary>
    internal sealed record Named(string Kind, Guid Id, string Name, string[] Words, int Order);

    private static readonly HashSet<string> StopWords = new(
        ["add", "new", "a", "an", "the", "in", "at", "on", "with", "for", "and", "&", "+", "from", "to", "until", "till", "til",
         "by", "@", "kl", "kl.", "this", "class", "-", "–", "—"], StringComparer.OrdinalIgnoreCase);

    private static readonly string[][] Days =
    [
        ["mon", "monday"], ["tue", "tues", "tuesday"], ["wed", "weds", "wednesday"], ["thu", "thur", "thurs", "thursday"],
        ["fri", "friday"], ["sat", "saturday"], ["sun", "sunday"],
    ];

    private static readonly string[][] Months =
    [
        ["jan", "january"], ["feb", "february"], ["mar", "march"], ["apr", "april"], ["may"], ["jun", "june"],
        ["jul", "july"], ["aug", "august"], ["sep", "sept", "september"], ["oct", "october"], ["nov", "november"], ["dec", "december"],
    ];

    internal static List<Word> Words(string line) =>
        [.. line.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries).Select(w => new Word(w))];

    internal static Word? Next(List<Word> words, int i) => i + 1 < words.Count && !words[i + 1].Used ? words[i + 1] : null;

    // ---- Names ----------------------------------------------------------------------------------

    /// <summary>
    /// What a line can name, current ones only: classes, groups, programmes, spaces and facilitators, and
    /// students and what a space has when asked. The order is which kind a name two kinds share goes to: a group
    /// before a programme. A student and a facilitator share one, so a first name they both have is neither until
    /// it is said whole.
    /// </summary>
    internal static List<Named> Names(Timetable t, bool students = false, bool equipment = false)
    {
        var list = new List<Named>();
        void Add(string kind, Guid id, string name, int order, bool firstName = false)
        {
            var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            list.Add(new(kind, id, name, parts, order));
            // "ba1" for "BA 1", "bigtop" for "Big Top": what a person types without the space.
            if (parts.Length > 1) list.Add(new(kind, id, name, [string.Concat(parts)], order));
            if (firstName && parts.Length > 1) list.Add(new(kind, id, name, [parts[0]], order));
        }
        foreach (var c in t.Classes.Current()) Add("class", c.Id, c.Name, 0);
        foreach (var g in t.Groups) Add("group", g.Id, g.Name, 1);
        foreach (var p in t.Programmes) Add("programme", p.Id, p.Name, 2);
        foreach (var r in t.Rooms.Current()) Add("room", r.Id, r.Name, 3);
        foreach (var f in t.Facilitators.Current()) Add("facilitator", f.Id, f.Name, 4, firstName: true);
        if (students) foreach (var s in t.Students) Add("student", s.Id, s.Name, 4, firstName: true);
        if (equipment) foreach (var e in t.Equipment) Add("equipment", e.Id, e.Name, 5);
        return list;
    }

    /// <summary>
    /// Every name said whole, longest first, before anything else: "BA 1" is a group before "1" can be an hour,
    /// and "aerial hall" a space before "aerial" can be a class. Two kinds of one name go to the nearer kind;
    /// two of one kind are neither, and said in <paramref name="unclear"/>, each of them in
    /// <paramref name="ambiguous"/> for a reader that can answer about both.
    /// </summary>
    internal static List<Named> Whole(List<Word> words, List<Named> names, List<string> unclear, List<Named>? ambiguous = null)
    {
        var found = new List<Named>();
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i].Used) continue;
            var fits = names.Where(n => Fits(words, i, n.Words)).ToList();
            if (fits.Count == 0) continue;
            int longest = fits.Max(n => n.Words.Length);
            var top = fits.Where(n => n.Words.Length == longest).ToList();
            int order = top.Min(n => n.Order);
            var chosen = top.Where(n => n.Order == order).DistinctBy(n => n.Id).ToList();
            if (chosen.Count > 1)
            {
                unclear.Add(Unclear(words[i].Raw, chosen));
                ambiguous?.AddRange(chosen);
                words[i].Used = true;
                continue;
            }
            for (int j = 0; j < longest; j++) words[i + j].Used = true;
            found.Add(chosen[0]);
            i += longest - 1;
        }
        return found;
    }

    /// <summary>
    /// The start of one name and of no other: "acro", "lindq". Three letters at least, as two say too little;
    /// the start of two names is neither, and said in <paramref name="unclear"/>, each of them in
    /// <paramref name="ambiguous"/> for a reader that can answer about both.
    /// </summary>
    internal static List<Named> Starts(List<Word> words, List<Named> names, List<string> unclear, List<Named>? ambiguous = null)
    {
        var found = new List<Named>();
        var cmp = NameOrder.Culture.CompareInfo;
        const CompareOptions o = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        foreach (var w in words.Where(w => !w.Used && !w.Stop && w.Raw.Length >= 3 && w.Raw.Any(char.IsLetter)))
        {
            var starts = names.Where(n => n.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(part => cmp.IsPrefix(part, w.Raw, o)))
                .DistinctBy(n => n.Id).ToList();
            if (starts.Count == 1) { found.Add(starts[0]); w.Used = true; }
            else if (starts.Count > 1) { unclear.Add(Unclear(w.Raw, starts)); ambiguous?.AddRange(starts); w.Used = true; }
        }
        return found;
    }

    private static bool Fits(List<Word> words, int at, string[] name)
    {
        if (at + name.Length > words.Count) return false;
        var cmp = NameOrder.Culture.CompareInfo;
        for (int j = 0; j < name.Length; j++)
            if (words[at + j].Used || cmp.Compare(words[at + j].Raw, name[j], CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) != 0)
                return false;
        return true;
    }

    private static string Unclear(string said, IEnumerable<Named> could) =>
        $"{said} ({string.Join(" or ", could.Select(n => n.Name).Distinct().OrderBy(n => n, NameOrder.Comparer))}?)";

    /// <summary>The words left, in the order typed, none of them a small word.</summary>
    internal static List<int> Left(List<Word> words) =>
        [.. words.Select((w, i) => (w, i)).Where(x => !x.w.Used && !x.w.Stop).Select(x => x.i)];

    // ---- Times ----------------------------------------------------------------------------------

    [GeneratedRegex(@"^(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?(?<ap>am|pm)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClockRx();

    [GeneratedRegex(@"^(?<a>\d{1,2}(?:[:.]\d{2})?(?:am|pm)?)[-–—](?<b>\d{1,2}(?:[:.]\d{2})?(?:am|pm)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RangeRx();

    [GeneratedRegex(@"^\d{1,2}$")]
    private static partial Regex HourRx();

    [GeneratedRegex(@"^\d+(?:[.,]\d+)?$")]
    private static partial Regex NumberRx();

    internal static bool IsHour(string raw) => HourRx().IsMatch(raw);

    internal static bool IsNumber(string raw) => NumberRx().IsMatch(raw);

    internal static bool IsDash(Word w) => w.Is("-", "–", "—", "to", "until", "till", "til");

    private sealed record Said(int H, int M, string? Half, bool Marked);

    /// <summary>"13", "13:00", "9.30", "2pm": a time as said, marked when it has minutes or am/pm.</summary>
    private static Said? Parse(string raw)
    {
        var m = ClockRx().Match(raw);
        if (!m.Success) return null;
        int h = int.Parse(m.Groups["h"].Value, CultureInfo.InvariantCulture);
        int min = m.Groups["m"].Success ? int.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture) : 0;
        var half = m.Groups["ap"].Success ? m.Groups["ap"].Value.ToUpperInvariant() : null;
        if (min > 59 || (half is null ? h > 23 : h is < 1 or > 12)) return null;
        return new Said(h, min, half, m.Groups["m"].Success || half is not null);
    }

    /// <summary>
    /// The hour of the day: "pm" adds twelve, "12am" is midnight, and an hour from 1 to 6 with neither is in
    /// the afternoon, because the school's day runs from the morning into the evening and nobody means 3 a.m.
    /// </summary>
    private static int Hour(Said s, string? half) => half switch
    {
        "PM" => s.H % 12 + 12,
        "AM" => s.H % 12,
        _ => s.H is >= 1 and <= 6 ? s.H + 12 : s.H,
    };

    /// <summary>A time said alone: with minutes or am/pm, or a bare hour where <paramref name="bare"/> allows one.</summary>
    internal static TimeOnly? Clock(string raw, bool bare)
    {
        // "4.12" is the fourth of December, not twelve minutes past four.
        if (DotDate(raw) is not null) return null;
        if (Parse(raw) is not { } s || !bare && !s.Marked) return null;
        return new TimeOnly(Hour(s, s.Half), s.M);
    }

    /// <summary>"13-14:30" in one word.</summary>
    internal static (TimeOnly, TimeOnly)? RangeIn(string raw) =>
        RangeRx().Match(raw) is { Success: true } m ? Range(m.Groups["a"].Value, m.Groups["b"].Value) : null;

    /// <summary>"13-14:30", "1-2.30pm", "10-11pm", "11-1": the end after the start, as a person would mean it.</summary>
    internal static (TimeOnly, TimeOnly)? Range(string a, string b)
    {
        if (DotDate(a) is not null || DotDate(b) is not null || Parse(a) is not { } from || Parse(b) is not { } to) return null;
        var half = from.Half;
        // "10-11pm": the end's half of the day for the start too, when the start then comes first.
        if (half is null && to.Half is not null && from.H <= 12 && Hour(from, to.Half) * 60 + from.M < Hour(to, to.Half) * 60 + to.M) half = to.Half;
        int start = Hour(from, half) * 60 + from.M;
        int end = Hour(to, to.Half) * 60 + to.M;
        if (end <= start && to.Half is null && end < 12 * 60) end += 12 * 60;
        if (end <= start || end >= 24 * 60) return null;
        return (new TimeOnly(start / 60, start % 60), new TimeOnly(end / 60, end % 60));
    }

    [GeneratedRegex(@"^(?:(?<m>\d+)(?:m|min|mins|minute|minutes)|(?<h>\d+(?:[.,]\d+)?)(?:h|hr|hrs|hour|hours)|(?<hh>\d+)h(?<mm>\d{1,2})(?:m|min)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LengthRx();

    /// <summary>"90m", "1h30", "1.5h", "2 hours": how long, up to twelve hours.</summary>
    internal static TimeSpan? LengthOf(string raw)
    {
        var m = LengthRx().Match(raw);
        if (!m.Success) return null;
        double minutes = m.Groups["m"].Success ? double.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture)
            : m.Groups["h"].Success ? double.Parse(m.Groups["h"].Value.Replace(',', '.'), CultureInfo.InvariantCulture) * 60
            : int.Parse(m.Groups["hh"].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups["mm"].Value, CultureInfo.InvariantCulture);
        return minutes is > 0 and <= 12 * 60 ? TimeSpan.FromMinutes(Math.Round(minutes)) : null;
    }

    // ---- Days -----------------------------------------------------------------------------------

    /// <summary>
    /// A date said at word <paramref name="i"/>, marking the words it took: "4/12", "4.12", "2026-12-04",
    /// "27 nov", "nov 27th", "today", "tomorrow", "yesterday", "next fri", "fri", and a day name before its own
    /// date, "fri 4/12", which says the day twice. A day name before another day's date is two days, as it says.
    /// </summary>
    /// <remarks>
    /// With <paramref name="onOrAfter"/>, a day name is the first such day on or after it: "mon to wed" said on a
    /// Wednesday ends on the Wednesday after that Monday, not on the day it is said.
    /// </remarks>
    internal static DateOnly? DateAt(List<Word> words, int i, DateOnly today, DateOnly shown, DateOnly? onOrAfter = null)
    {
        var w = words[i];
        if (w.Used) return null;
        var next = Next(words, i);
        DateOnly? d = null;
        int taken = 1;
        if (Written(words, i, today) is { } written) (d, taken) = written;
        else if (next is not null && DayOf(w.Raw) is { } named && Written(words, i + 1, today) is { } after && after.Date.DayOfWeek == named)
        { d = after.Date; taken = 1 + after.Taken; }
        else if (w.Is("today")) d = today;
        else if (w.Is("tomorrow")) d = today.AddDays(1);
        else if (w.Is("yesterday")) d = today.AddDays(-1);
        else if (w.Is("next") && next is not null && DayOf(next.Raw) is { } nd) { d = Weekday(nd, shown, today, next: true); taken = 2; }
        else if (DayOf(w.Raw) is { } wd) d = onOrAfter is { } from ? OnOrAfter(wd, from) : Weekday(wd, shown, today, next: false);
        if (d is null) return null;
        for (int j = 0; j < taken; j++) words[i + j].Used = true;
        return d;
    }

    /// <summary>A date written out at word <paramref name="i"/>, and how many words it took: "4/12", "4.12", "27 nov", "nov 27th".</summary>
    private static (DateOnly Date, int Taken)? Written(List<Word> words, int i, DateOnly today)
    {
        var w = words[i];
        var next = Next(words, i);
        if (DateIn(w.Raw, today) is { } a) return (a, 1);
        if (next is not null && DayOfMonth(w.Raw) is { } dm && MonthOf(next.Raw) is { } mo && Dated(dm, mo, null, today) is { } b) return (b, 2);
        if (next is not null && MonthOf(w.Raw) is { } mo2 && DayOfMonth(next.Raw) is { } dm2 && Dated(dm2, mo2, null, today) is { } c) return (c, 2);
        return null;
    }

    private static DayOfWeek? DayOf(string raw)
    {
        for (int i = 0; i < Days.Length; i++)
            if (Days[i].Any(d => string.Equals(d, raw, StringComparison.OrdinalIgnoreCase))) return (DayOfWeek)((i + 1) % 7);
        return null;
    }

    private static int? MonthOf(string raw)
    {
        var w = raw.TrimEnd('.');
        for (int i = 0; i < Months.Length; i++)
            if (Months[i].Any(m => string.Equals(m, w, StringComparison.OrdinalIgnoreCase))) return i + 1;
        return null;
    }

    [GeneratedRegex(@"^(?<d>\d{1,2})(?:st|nd|rd|th|\.)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DayOfMonthRx();

    private static int? DayOfMonth(string raw) =>
        DayOfMonthRx().Match(raw) is { Success: true } m && int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture) is var d and >= 1 and <= 31 ? d : null;

    [GeneratedRegex(@"^(?<y>\d{4})-(?<m>\d{1,2})-(?<d>\d{1,2})$")]
    private static partial Regex IsoRx();

    [GeneratedRegex(@"^(?<d>\d{1,2})/(?<m>\d{1,2})(?:/(?<y>\d{2}|\d{4}))?$")]
    private static partial Regex SlashRx();

    [GeneratedRegex(@"^(?<d>\d{1,2})\.(?<m>\d{1,2})(?:\.(?<y>\d{2}|\d{4})?)?$")]
    private static partial Regex DotRx();

    /// <summary>
    /// "4.12." or "4.12" as a date, the Danish way, but "9.30" and "10.05" as times: a dotted pair is a date
    /// when it ends in a dot or a year, or when its second number is a month written as one (1 to 12, with no
    /// leading nought), which no minutes past the hour are written as.
    /// </summary>
    private static (int D, int M, int? Y)? DotDate(string raw)
    {
        var m = DotRx().Match(raw);
        if (!m.Success) return null;
        var month = m.Groups["m"].Value;
        int d = int.Parse(m.Groups["d"].Value, CultureInfo.InvariantCulture), mo = int.Parse(month, CultureInfo.InvariantCulture);
        bool marked = raw.Count(c => c == '.') == 2;
        if (!marked && (month.StartsWith('0') || mo is < 1 or > 12)) return null;
        return (d, mo, m.Groups["y"].Success ? int.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture) : null);
    }

    internal static DateOnly? DateIn(string raw, DateOnly today)
    {
        if (IsoRx().Match(raw) is { Success: true } iso)
            return Dated(int.Parse(iso.Groups["d"].Value, CultureInfo.InvariantCulture), int.Parse(iso.Groups["m"].Value, CultureInfo.InvariantCulture),
                int.Parse(iso.Groups["y"].Value, CultureInfo.InvariantCulture), today);
        if (SlashRx().Match(raw) is { Success: true } sl)
            return Dated(int.Parse(sl.Groups["d"].Value, CultureInfo.InvariantCulture), int.Parse(sl.Groups["m"].Value, CultureInfo.InvariantCulture),
                sl.Groups["y"].Success ? int.Parse(sl.Groups["y"].Value, CultureInfo.InvariantCulture) : null, today);
        return DotDate(raw) is { } dot ? Dated(dot.D, dot.M, dot.Y, today) : null;
    }

    /// <summary>A day and month with no year is the next one: "3/1" said in November is January.</summary>
    private static DateOnly? Dated(int day, int month, int? year, DateOnly today)
    {
        int y = year is { } given ? given < 100 ? 2000 + given : given : today.Year;
        if (month is < 1 or > 12 || y is < 2000 or > 2199 || day < 1 || day > DateTime.DaysInMonth(y, month)) return null;
        var d = new DateOnly(y, month, day);
        if (year is null && d < today.AddDays(-60) && day <= DateTime.DaysInMonth(y + 1, month)) d = new DateOnly(y + 1, month, day);
        return d;
    }

    /// <summary>
    /// "fri": that day in the week the plan shows, or the next one if it has gone; "next fri": the week after.
    /// In the Scratch Week a day name stays in it.
    /// </summary>
    private static DateOnly Weekday(DayOfWeek day, DateOnly shown, DateOnly today, bool next)
    {
        var shownMonday = Cccc.Domain.Week.MondayOf(shown);
        if (Cccc.Domain.Week.IsScratch(shown)) return Cccc.Domain.Week.DateOf(shownMonday, day);
        if (next)
        {
            var monday = shownMonday > Cccc.Domain.Week.MondayOf(today) ? shownMonday : Cccc.Domain.Week.MondayOf(today);
            return Cccc.Domain.Week.DateOf(monday.AddDays(7), day);
        }
        return OnOrAfter(day, shownMonday > today ? shownMonday : today);
    }

    private static DateOnly OnOrAfter(DayOfWeek day, DateOnly from) =>
        from.AddDays((Cccc.Domain.Week.DayIndex(day) - Cccc.Domain.Week.DayIndex(from.DayOfWeek) + 7) % 7);
}
