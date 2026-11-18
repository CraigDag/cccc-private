using System.Collections.Immutable;
using System.Globalization;
using System.Text.RegularExpressions;
using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Features.Find;

/// <summary>
/// A class typed into Find in one line, the way a person says it: "aerial ba 1 fri 13-14:30 aerial hall dag".
/// The line is read for what it names (a class, groups or a programme, a space, facilitators, a day or a
/// date, a time or a length) and whatever it does not say is taken from the class's most like session
/// (<see cref="Habits.Like"/>). Nothing is saved from here: Enter opens the add form on the draft, to be
/// looked over first, so a misreading is seen twice before it is anything. Nothing here is logged.
/// </summary>
public static partial class QuickAdd
{
    /// <summary>What a line was read as: the class to add, the session the rest came from, and the words not understood.</summary>
    public sealed record Reading(Session Draft, Session? Like, IReadOnlyList<string> NotUnderstood);

    private sealed class Word(string raw)
    {
        public string Raw { get; } = raw;
        public bool Used { get; set; }
        public bool Stop => StopWords.Contains(Raw);
        public bool Is(params string[] any) => any.Any(a => string.Equals(Raw, a, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A name a line can say: the whole of it, or for a facilitator the first name.</summary>
    private sealed record Named(string Kind, Guid Id, string Name, string[] Words, int Order);

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

    /// <summary>
    /// The line read as a class to add, or null when it says no class (or title) and no time to add it at: a
    /// time, or a day with a class whose usual time can be taken. <paramref name="shown"/> is the day the plan
    /// is on, so "fri" means that week's Friday if it has not gone, and otherwise the next.
    /// </summary>
    public static Reading? Read(Timetable t, string line, DateOnly today, DateOnly shown)
    {
        var words = Split(line).Select(w => new Word(w)).ToList();
        if (words.Count == 0) return null;
        var found = new List<Named>();
        var unclear = new List<string>();

        // 1. Names, longest first, before anything else: "BA 1" is a group before "1" can be an hour.
        var names = Names(t);
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
                words[i].Used = true;
                continue;
            }
            for (int j = 0; j < longest; j++) words[i + j].Used = true;
            found.Add(chosen[0]);
            i += longest - 1;
        }

        bool everyone = false;
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i].Used) continue;
            if (words[i].Is("everyone", "everybody")) { words[i].Used = true; everyone = true; }
            else if (i + 1 < words.Count && !words[i + 1].Used
                     && (words[i].Is("whole") && words[i + 1].Is("school", "building") || words[i].Is("all") && words[i + 1].Is("groups")))
            { words[i].Used = words[i + 1].Used = true; everyone = true; }
        }

        // 2. A time from one to another: "13-14:30" in one word, or "13 to 14:30" in three.
        TimeOnly? start = null, end = null;
        for (int i = 0; i < words.Count && start is null; i++)
        {
            if (words[i].Used) continue;
            if (RangeRx().Match(words[i].Raw) is { Success: true } m && Range(m.Groups["a"].Value, m.Groups["b"].Value) is { } r)
            { (start, end) = r; words[i].Used = true; }
            else if (i + 2 < words.Count && !words[i + 1].Used && !words[i + 2].Used && words[i + 1].Is("-", "–", "—", "to", "until", "till", "til")
                     && Range(words[i].Raw, words[i + 2].Raw) is { } r3)
            { (start, end) = r3; words[i].Used = words[i + 1].Used = words[i + 2].Used = true; }
        }

        // 3. Days, dates, times and lengths.
        DateOnly? date = null;
        TimeSpan? length = null;
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (w.Used) continue;
            var next = i + 1 < words.Count && !words[i + 1].Used ? words[i + 1] : null;
            if (date is null && DateIn(w.Raw, today) is { } d) { date = d; w.Used = true; }
            else if (date is null && next is not null && DayOfMonth(w.Raw) is { } dm && MonthOf(next.Raw) is { } mo && Dated(dm, mo, null, today) is { } d2)
            { date = d2; w.Used = next.Used = true; }
            else if (date is null && next is not null && MonthOf(w.Raw) is { } mo2 && DayOfMonth(next.Raw) is { } dm2 && Dated(dm2, mo2, null, today) is { } d3)
            { date = d3; w.Used = next.Used = true; }
            else if (date is null && w.Is("today")) { date = today; w.Used = true; }
            else if (date is null && w.Is("tomorrow")) { date = today.AddDays(1); w.Used = true; }
            else if (date is null && w.Is("next") && next is not null && DayOf(next.Raw) is { } nd)
            { date = Weekday(nd, shown, today, next: true); w.Used = next.Used = true; }
            else if (date is null && DayOf(w.Raw) is { } wd) { date = Weekday(wd, shown, today, next: false); w.Used = true; }
            else if (w.Is("at", "@", "from", "kl", "kl.") && next is not null && start is null && Clock(next.Raw, bare: true) is { } at)
            { start = at; w.Used = next.Used = true; }
            else if (w.Is("noon", "midday") && start is null) { start = new TimeOnly(12, 0); w.Used = true; }
            else if (Clock(w.Raw, bare: false) is { } clock && (start is null || end is null && clock > start))
            {
                if (start is null) start = clock; else end = clock;
                w.Used = true;
            }
            else if (length is null && LengthOf(w.Raw) is { } len) { length = len; w.Used = true; }
            else if (length is null && next is not null && NumberRx().IsMatch(w.Raw) && LengthOf(w.Raw + next.Raw) is { } len2)
            { length = len2; w.Used = next.Used = true; }
        }

        // 4. A bare number is the hour only when nothing else said one: "aerial fri 13".
        if (start is null && words.FirstOrDefault(w => !w.Used && HourRx().IsMatch(w.Raw)) is { } bare && Clock(bare.Raw, bare: true) is { } hour)
        { start = hour; bare.Used = true; }

        // 5. The start of one name and of no other: "acro", "lindq".
        var cmp = NameOrder.Culture.CompareInfo;
        const CompareOptions o = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;
        foreach (var w in words.Where(w => !w.Used && !w.Stop && w.Raw.Length >= 3 && w.Raw.Any(char.IsLetter)))
        {
            var starts = names.Where(n => n.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(part => cmp.IsPrefix(part, w.Raw, o)))
                .DistinctBy(n => n.Id).ToList();
            if (starts.Count == 1) { found.Add(starts[0]); w.Used = true; }
            else if (starts.Count > 1) { unclear.Add(Unclear(w.Raw, starts)); w.Used = true; }
        }

        var cls = found.FirstOrDefault(n => n.Kind == "class");
        var groups = found.Where(n => n.Kind == "group").Select(n => n.Id).Distinct().ToList();
        var programme = found.FirstOrDefault(n => n.Kind == "programme");
        var room = found.FirstOrDefault(n => n.Kind == "room");
        var people = found.Where(n => n.Kind == "facilitator").Select(n => n.Id).Distinct().ToList();
        var notUnderstood = new List<string>();
        // A second class, programme or space is not what the line is about: said, not quietly used.
        notUnderstood.AddRange(found.Where(n => n.Kind is "class" or "programme" or "room").GroupBy(n => n.Kind).SelectMany(g => g.Skip(1)).Select(n => n.Name));

        // What is left is the title when no class is named ("staff meeting"), from its first word to its last.
        var left = words.Select((w, i) => (w, i)).Where(x => !x.w.Used && !x.w.Stop).Select(x => x.i).ToList();
        string title = "";
        if (left.Count > 0)
        {
            var span = string.Join(" ", words.Skip(left[0]).Take(left[^1] - left[0] + 1).Where(w => !w.Used).Select(w => w.Raw));
            if (cls is null) title = char.ToUpper(span[0], NameOrder.Culture) + span[1..];
            else notUnderstood.AddRange(left.Select(i => words[i].Raw));
        }
        notUnderstood.AddRange(unclear);
        if (cls is null && title.Length == 0) return null;

        var day = date ?? shown;
        var classId = cls?.Id;
        var forGroups = groups.Union(programme is null ? [] : t.Groups.Where(g => g.ProgrammeId == programme.Id).Select(g => g.Id)).ToList();
        var like = classId is { } id && (start is not null || date is not null || length is not null)
            ? Habits.Like(t, id, day, start, forGroups, people) : null;
        bool usedLike = false;
        if (start is null)
        {
            if (like is null) return null;
            start = like.Start;
            usedLike = true;
        }
        if (end is null)
        {
            var len = length ?? like?.Length ?? TimeSpan.FromHours(1);
            if (length is null && like is not null) usedLike = true;
            int until = start.Value.Hour * 60 + start.Value.Minute + (int)len.TotalMinutes;
            end = until >= 24 * 60 ? new TimeOnly(23, 45) : new TimeOnly(until / 60, until % 60);
        }
        var s = Edits.Snap(start.Value);
        var e = Edits.Snap(end.Value);

        Audience audience;
        if (everyone) audience = Audience.Everyone;
        else if (groups.Count == 0 && programme is not null) audience = Edits.ForProgramme(t, programme.Id);
        else if (forGroups.Count == 1) audience = Audience.ForGroup(forGroups[0]);
        else if (forGroups.Count > 1) audience = Audience.ForGroups(forGroups);
        else if (like is not null) { audience = like.Audience; usedLike = true; }
        else audience = new Audience { Kind = AudienceKind.Group };

        Guid? roomId = room?.Id;
        if (roomId is null && like?.RoomId is { } likeRoom) { roomId = likeRoom; usedLike = true; }

        ImmutableList<Assignment> who = [.. people.Select(p => new Assignment(p))];
        if (who.IsEmpty && like is { Facilitators.IsEmpty: false })
        {
            // Who was in only part of it keeps that part only at the very same times.
            who = like.Start == s && like.End == e ? like.Facilitators : [.. like.Facilitators.Select(a => new Assignment(a.FacilitatorId))];
            usedLike = true;
        }

        var draft = new Session
        {
            Date = day, Start = s, End = e, Title = cls?.Name ?? title, ClassId = classId,
            Audience = audience, RoomId = roomId, Facilitators = who,
        };
        return new Reading(draft, usedLike ? like : null, notUnderstood);
    }

    /// <summary>The line as Find shows it, first in the list: what would be added, and where the rest came from.</summary>
    public static FindHit Hit(Timetable t, Reading r)
    {
        var s = r.Draft;
        var when = Cccc.Domain.Week.IsScratch(s.Date) ? "Scratch Week " + Cccc.Domain.Week.ShortDay(s.Date.DayOfWeek) : Cccc.Domain.Week.ShortDate(s.Date);
        var parts = new List<string>
        {
            s.Audience.Kind == AudienceKind.Group && s.Audience.GroupIds.Count == 0 ? "no group yet" : Looks.Audience(t, s),
            t.Rooms.Find(s.RoomId)?.Name ?? "no space",
            s.Facilitators.Count == 0 ? "nobody teaching yet" : Looks.Names(t, s.FacilitatorIds),
        };
        if (r.Like is { } like) parts.Add("the rest as on " + Cccc.Domain.Week.ShortDate(like.Date));
        if (r.NotUnderstood.Count > 0) parts.Add("not understood: " + string.Join(", ", r.NotUnderstood));
        return new FindHit("quick-add", $"Add {Looks.Title(t, s)} · {when} {Looks.When(s)}", string.Join(" · ", parts), null, Looks.ColourOf(t, s), s);
    }

    private static IEnumerable<string> Split(string line) =>
        line.Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries);

    private static List<Named> Names(Timetable t)
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
        return list;
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

    // ---- Times ----------------------------------------------------------------------------------

    [GeneratedRegex(@"^(?<h>\d{1,2})(?:[:.](?<m>\d{2}))?(?<ap>am|pm)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ClockRx();

    [GeneratedRegex(@"^(?<a>\d{1,2}(?:[:.]\d{2})?(?:am|pm)?)[-–—](?<b>\d{1,2}(?:[:.]\d{2})?(?:am|pm)?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RangeRx();

    [GeneratedRegex(@"^\d{1,2}$")]
    private static partial Regex HourRx();

    [GeneratedRegex(@"^\d+(?:[.,]\d+)?$")]
    private static partial Regex NumberRx();

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
    private static TimeOnly? Clock(string raw, bool bare)
    {
        // "4.12" is the fourth of December, not twelve minutes past four.
        if (DotDate(raw) is not null) return null;
        if (Parse(raw) is not { } s || !bare && !s.Marked) return null;
        return new TimeOnly(Hour(s, s.Half), s.M);
    }

    /// <summary>"13-14:30", "1-2.30pm", "10-11pm", "11-1": the end after the start, as a person would mean it.</summary>
    private static (TimeOnly, TimeOnly)? Range(string a, string b)
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
    private static TimeSpan? LengthOf(string raw)
    {
        var m = LengthRx().Match(raw);
        if (!m.Success) return null;
        double minutes = m.Groups["m"].Success ? double.Parse(m.Groups["m"].Value, CultureInfo.InvariantCulture)
            : m.Groups["h"].Success ? double.Parse(m.Groups["h"].Value.Replace(',', '.'), CultureInfo.InvariantCulture) * 60
            : int.Parse(m.Groups["hh"].Value, CultureInfo.InvariantCulture) * 60 + int.Parse(m.Groups["mm"].Value, CultureInfo.InvariantCulture);
        return minutes is > 0 and <= 12 * 60 ? TimeSpan.FromMinutes(Math.Round(minutes)) : null;
    }

    // ---- Days -----------------------------------------------------------------------------------

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

    private static DateOnly? DateIn(string raw, DateOnly today)
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
        var from = shownMonday > today ? shownMonday : today;
        return from.AddDays((Cccc.Domain.Week.DayIndex(day) - Cccc.Domain.Week.DayIndex(from.DayOfWeek) + 7) % 7);
    }
}
