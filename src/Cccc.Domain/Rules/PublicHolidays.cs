using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// One of Denmark's public holidays (helligdage), or a day many places close beside them that is not one
/// (<see cref="Official"/> false): Christmas Eve, New Year's Eve, Constitution Day, the Friday after Ascension.
/// <see cref="Season"/> names the holidays that come together (Easter, Whitsun, Christmas), for a closure of
/// several of them.
/// </summary>
public sealed record PublicHoliday(DateOnly Date, string Name, string Danish, bool Official, string? Season = null);

/// <summary>
/// One closure that closing the school year's public holidays would add: one holiday, or several that only a
/// weekend with nothing on parts, and the whole-building closure that already closes every one of them, if any does.
/// </summary>
public sealed record HolidayClosure(string Name, DateOnly From, DateOnly To, IReadOnlyList<PublicHoliday> Days, Closure? ClosedBy)
{
    /// <summary>Every day in it is a public holiday. A day many places close beside them is one of its own, never joined to one.</summary>
    public bool Official => Days.All(d => d.Official);

    public bool AlreadyClosed => ClosedBy is not null;

    /// <summary>The closure it adds: the whole building, from its first day to its last.</summary>
    public Closure ToClosure(Guid id) => new() { Id = id, Name = Name, From = From, To = To, Scope = ClosureScope.WholeBuilding };
}

/// <summary>
/// Denmark's public holidays, for closing them. The rebuild's own: a school year starts with no closures, and
/// the holidays that move with Easter (Maundy Thursday to Easter Monday, Ascension, Whit Monday) land on
/// different days every year, which is why a closure carried from last year is wrong for them
/// (<see cref="Editing.NextYear"/>). Nothing here closes anything by itself: the form shows what it would add,
/// ticked or not, and adds what is ticked.
/// </summary>
public static class PublicHolidays
{
    /// <summary>Easter Sunday in the Gregorian calendar (the anonymous algorithm, as Meeus gives it).</summary>
    public static DateOnly EasterSunday(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7, m = (a + 11 * h + 22 * l) / 451;
        int n = h + l - 7 * m + 114;
        return new DateOnly(year, n / 31, n % 31 + 1);
    }

    /// <summary>Denmark's public holidays in a calendar year, and the days many places close beside them, by date.</summary>
    public static IReadOnlyList<PublicHoliday> Denmark(int year)
    {
        const string easter = "Easter (Påske)", whitsun = "Whitsun (Pinse)", christmas = "Christmas (Jul)";
        var e = EasterSunday(year);
        var days = new List<PublicHoliday>
        {
            new(new DateOnly(year, 1, 1), "New Year's Day", "Nytårsdag", true),
            new(e.AddDays(-3), "Maundy Thursday", "Skærtorsdag", true, easter),
            new(e.AddDays(-2), "Good Friday", "Langfredag", true, easter),
            new(e, "Easter Sunday", "Påskedag", true, easter),
            new(e.AddDays(1), "Easter Monday", "2. påskedag", true, easter),
            new(e.AddDays(39), "Ascension Day", "Kristi himmelfartsdag", true),
            new(e.AddDays(40), "The Friday after Ascension", "Fredag efter Kristi himmelfartsdag", false),
            new(e.AddDays(49), "Whit Sunday", "Pinsedag", true, whitsun),
            new(e.AddDays(50), "Whit Monday", "2. pinsedag", true, whitsun),
            new(new DateOnly(year, 6, 5), "Constitution Day", "Grundlovsdag", false),
            new(new DateOnly(year, 12, 24), "Christmas Eve", "Juleaftensdag", false, christmas),
            new(new DateOnly(year, 12, 25), "Christmas Day", "Juledag", true, christmas),
            new(new DateOnly(year, 12, 26), "Boxing Day", "2. juledag", true, christmas),
            new(new DateOnly(year, 12, 31), "New Year's Eve", "Nytårsaftensdag", false),
        };
        // Great Prayer Day, the fourth Friday after Easter, was a public holiday until 2023.
        if (year <= 2023) days.Add(new(e.AddDays(26), "Great Prayer Day", "Store bededag", true));
        return [.. days.OrderBy(d => d.Date)];
    }

    /// <summary>
    /// What closing the school year's public holidays would add, oldest first: each holiday inside a semester,
    /// from <paramref name="from"/> on, that falls on a school day (Monday to Friday, or a weekend day with a
    /// class on it, as Find a time counts them). One that has passed is not offered: a closure there would say
    /// a day that ran was closed, and a week is kept as it ran. Public holidays that only a weekend with nothing
    /// on parts are one closure, so Easter is Maundy Thursday to Easter Monday when nothing is on that weekend,
    /// and two when something is; the weeks between two semesters always part them. A day many places close
    /// beside them is a closure of its own, to tick or not. Each says the whole-building closure that closes all
    /// of its days already, when one does; a closure for some groups closes the building for nobody else.
    /// </summary>
    public static IReadOnlyList<HolidayClosure> For(Timetable t, DateOnly from)
    {
        if (t.Semesters.Count == 0) return [];
        var firstMonday = t.Semesters.Min(s => s.FirstMonday);
        var first = from > firstMonday ? from : firstMonday;
        var last = t.Semesters.Max(s => s.LastMonday).AddDays(6);
        var used = t.Sessions.Select(s => s.Date).ToHashSet();
        bool InASemester(DateOnly d) => t.Semesters.Any(s => s.Contains(d));
        bool SchoolDay(DateOnly d) => InASemester(d) && (d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) || used.Contains(d));
        // Constitution Day is Whit Sunday or Whit Monday in some years: the public holiday is the one to close.
        var holidays = Enumerable.Range(first.Year, last.Year - first.Year + 1).SelectMany(Denmark)
            .Where(h => h.Date >= first && h.Date <= last && SchoolDay(h.Date))
            .GroupBy(h => h.Date).Select(g => g.OrderByDescending(h => h.Official).First()).ToList();

        var closures = new List<HolidayClosure>();
        var run = new List<PublicHoliday>();
        foreach (var h in holidays)
        {
            // Joined over a weekend with nothing on, never over the weeks between two semesters.
            var before = run.Count > 0 ? run[^1] : null;
            bool joins = before is not null && before.Official && h.Official
                && Enumerable.Range(1, h.Date.DayNumber - before.Date.DayNumber - 1).Select(before.Date.AddDays).All(d => InASemester(d) && !SchoolDay(d));
            if (!joins && run.Count > 0)
            {
                closures.Add(Closing(t, run));
                run = [];
            }
            run.Add(h);
        }
        if (run.Count > 0) closures.Add(Closing(t, run));
        return closures;
    }

    private static HolidayClosure Closing(Timetable t, IReadOnlyList<PublicHoliday> days)
    {
        // Only a weekend parts the days of one closure, and only the holidays of one season fall that close.
        var name = days.Count > 1 && days[0].Season is { } season ? season : $"{days[0].Name} ({days[0].Danish})";
        var wholeBuilding = t.Closures.Where(c => c.Scope == ClosureScope.WholeBuilding).OrderBy(c => c.From).ThenBy(c => c.Id).ToList();
        var closedBy = days.All(d => wholeBuilding.Any(c => c.Covers(d.Date))) ? wholeBuilding.First(c => c.Covers(days[0].Date)) : null;
        return new HolidayClosure(name, days[0].Date, days[^1].Date, days, closedBy);
    }
}
