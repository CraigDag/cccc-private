using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;

namespace Cccc.Domain.Tests;

/// <summary>
/// <see cref="PublicHolidays"/>: Denmark's public holidays, and what closing them for a school year would add.
/// Built on 2027, whose Easter is 28 March, so Easter runs Thursday 25 to Monday 29 March.
/// </summary>
public class PublicHolidaysTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static Build Spring2027()
    {
        var b = new Build();
        b.T = b.T with { Semesters = EntitySet<Semester>.Of([new Semester { Name = "Spring", StartMonday = D(2027, 1, 4), WeekCount = 26 }]) };
        return b;
    }

    /// <summary>What is offered when every day of the semesters is still ahead.</summary>
    private static IReadOnlyList<HolidayClosure> For(Build b) => PublicHolidays.For(b.T, DateOnly.MinValue);

    private static string[] Said(IEnumerable<HolidayClosure> hs) =>
        [.. hs.Select(h => $"{h.Name} {h.From:yyyy-MM-dd}–{h.To:yyyy-MM-dd}{(h.Official ? "" : " (not official)")}{(h.ClosedBy is { } c ? " closed by " + c.Name : "")}")];

    [Fact]
    public void EasterFallsWhereTheCalendarsSayItDoesFromItsEarliestToItsLatest()
    {
        var known = new Dictionary<int, DateOnly>
        {
            [1818] = D(1818, 3, 22), [1943] = D(1943, 4, 25), [2008] = D(2008, 3, 23), [2011] = D(2011, 4, 24), [2019] = D(2019, 4, 21),
            [2024] = D(2024, 3, 31), [2025] = D(2025, 4, 20), [2026] = D(2026, 4, 5), [2027] = D(2027, 3, 28), [2028] = D(2028, 4, 16),
            [2038] = D(2038, 4, 25), [2285] = D(2285, 3, 22),
        };
        foreach (var (year, easter) in known) Assert.Equal(easter, PublicHolidays.EasterSunday(year));
        Assert.All(Enumerable.Range(1900, 300), y => Assert.Equal(DayOfWeek.Sunday, PublicHolidays.EasterSunday(y).DayOfWeek));
    }

    [Fact]
    public void DenmarksPublicHolidaysInAYearComeWithTheDaysManyPlacesCloseBesideThem()
    {
        Assert.Equal(
        [
            "2027-01-01 New Year's Day / Nytårsdag", "2027-03-25 Maundy Thursday / Skærtorsdag", "2027-03-26 Good Friday / Langfredag",
            "2027-03-28 Easter Sunday / Påskedag", "2027-03-29 Easter Monday / 2. påskedag", "2027-05-06 Ascension Day / Kristi himmelfartsdag",
            "2027-05-07 The Friday after Ascension / Fredag efter Kristi himmelfartsdag (not official)", "2027-05-16 Whit Sunday / Pinsedag",
            "2027-05-17 Whit Monday / 2. pinsedag", "2027-06-05 Constitution Day / Grundlovsdag (not official)",
            "2027-12-24 Christmas Eve / Juleaftensdag (not official)", "2027-12-25 Christmas Day / Juledag", "2027-12-26 Boxing Day / 2. juledag",
            "2027-12-31 New Year's Eve / Nytårsaftensdag (not official)",
        ], PublicHolidays.Denmark(2027).Select(h => $"{h.Date:yyyy-MM-dd} {h.Name} / {h.Danish}{(h.Official ? "" : " (not official)")}"));
        // Great Prayer Day was a public holiday until 2023, the fourth Friday after Easter.
        Assert.Contains(PublicHolidays.Denmark(2023), h => h.Name == "Great Prayer Day" && h.Date == D(2023, 5, 5) && h.Official);
        Assert.DoesNotContain(PublicHolidays.Denmark(2024), h => h.Name == "Great Prayer Day");
        Assert.Equal(PublicHolidays.Denmark(2023).OrderBy(h => h.Date).Select(h => h.Name), PublicHolidays.Denmark(2023).Select(h => h.Name));
        // The holidays that come together say so, which names a closure of several of them.
        Assert.Equal(
        [
            "Easter (Påske): Maundy Thursday, Good Friday, Easter Sunday, Easter Monday",
            "Whitsun (Pinse): Whit Sunday, Whit Monday",
            "Christmas (Jul): Christmas Eve, Christmas Day, Boxing Day",
        ], PublicHolidays.Denmark(2027).Where(h => h.Season is not null).GroupBy(h => h.Season).Select(g => $"{g.Key}: {string.Join(", ", g.Select(h => h.Name))}"));
    }

    [Fact]
    public void TheSchoolYearsHolidaysOnSchoolDaysAreAClosureEachAndEasterIsOneOverItsWeekend()
    {
        var b = Spring2027();
        Assert.Equal(
        [
            "Easter (Påske) 2027-03-25–2027-03-29",
            "Ascension Day (Kristi himmelfartsdag) 2027-05-06–2027-05-06",
            "The Friday after Ascension (Fredag efter Kristi himmelfartsdag) 2027-05-07–2027-05-07 (not official)",
            "Whit Monday (2. pinsedag) 2027-05-17–2027-05-17",
        ], Said(For(b)));
        Assert.Equal(["Maundy Thursday", "Good Friday", "Easter Monday"], For(b)[0].Days.Select(d => d.Name));

        // What it adds is a closure for the whole building, its days as one.
        var easter = For(b)[0].ToClosure(Guid.Empty);
        Assert.Equal(("Easter (Påske)", D(2027, 3, 25), D(2027, 3, 29), ClosureScope.WholeBuilding, 0), (easter.Name, easter.From, easter.To, easter.Scope, easter.GroupIds.Count));
    }

    [Fact]
    public void AClassOnAWeekendMakesItASchoolDaySoEasterIsTwoAndConstitutionDayIsOffered()
    {
        var b = Spring2027();
        b.Session(D(2027, 3, 27), "10:00", "12:00");   // Easter Saturday
        b.Session(D(2027, 6, 5), "10:00", "12:00");    // Constitution Day, a Saturday
        Assert.Equal(
        [
            "Easter (Påske) 2027-03-25–2027-03-26",
            "Easter Monday (2. påskedag) 2027-03-29–2027-03-29",
            "Ascension Day (Kristi himmelfartsdag) 2027-05-06–2027-05-06",
            "The Friday after Ascension (Fredag efter Kristi himmelfartsdag) 2027-05-07–2027-05-07 (not official)",
            "Whit Monday (2. pinsedag) 2027-05-17–2027-05-17",
            "Constitution Day (Grundlovsdag) 2027-06-05–2027-06-05 (not official)",
        ], Said(For(b)));

        // A class on Easter Sunday makes it a school day too: Sunday and Monday are Easter again, apart from Thursday and Friday.
        b.Session(D(2027, 3, 28), "10:00", "12:00");
        Assert.Equal(["Easter (Påske) 2027-03-25–2027-03-26", "Easter (Påske) 2027-03-28–2027-03-29"], Said(For(b)).Take(2));

        // A semester's last day is a Sunday, and a school day with a class on it: a semester ending on Easter
        // Sunday has Easter from Thursday to that Sunday, its Saturday with nothing on between.
        var c = new Build();
        c.T = c.T with { Semesters = EntitySet<Semester>.Of([new Semester { Name = "Short spring", StartMonday = D(2027, 3, 22), WeekCount = 1 }]) };
        c.Session(D(2027, 3, 28), "10:00", "12:00");
        Assert.Equal(["Easter (Påske) 2027-03-25–2027-03-28"], Said(For(c)));
    }

    [Fact]
    public void AHolidayThatHasPassedIsNotOfferedSoOnGoodFridayEasterIsWhatIsLeftOfIt()
    {
        var b = Spring2027();
        // On Good Friday, Maundy Thursday has passed: closing it would say a day that ran was closed.
        Assert.Equal(
        [
            "Easter (Påske) 2027-03-26–2027-03-29",
            "Ascension Day (Kristi himmelfartsdag) 2027-05-06–2027-05-06",
            "The Friday after Ascension (Fredag efter Kristi himmelfartsdag) 2027-05-07–2027-05-07 (not official)",
            "Whit Monday (2. pinsedag) 2027-05-17–2027-05-17",
        ], Said(PublicHolidays.For(b.T, D(2027, 3, 26))));
        Assert.Equal(["Good Friday", "Easter Monday"], PublicHolidays.For(b.T, D(2027, 3, 26))[0].Days.Select(d => d.Name));
        // On Easter Monday it is Easter Monday alone, a day of its own; the day after, Easter is gone.
        Assert.Equal("Easter Monday (2. påskedag) 2027-03-29–2027-03-29", Said(PublicHolidays.For(b.T, D(2027, 3, 29)))[0]);
        Assert.Equal("Ascension Day (Kristi himmelfartsdag) 2027-05-06–2027-05-06", Said(PublicHolidays.For(b.T, D(2027, 3, 30)))[0]);
        // After the last semester's last day, nothing is left to close.
        Assert.Empty(PublicHolidays.For(b.T, D(2027, 7, 5)));
    }

    [Fact]
    public void AWholeBuildingClosureOnEveryDayOfItSaysSoWhileOneForSomeGroupsOrSomeOfTheDaysDoesNot()
    {
        var b = Spring2027();
        var ba = b.Group("BA 1");
        b.Closure(new Closure { Name = "Easter break", From = D(2027, 3, 22), To = D(2027, 3, 29) });
        b.Closure(new Closure { Name = "BA trip", From = D(2027, 5, 6), To = D(2027, 5, 6), Scope = ClosureScope.Groups, GroupIds = [ba.Id] });
        b.Closure(new Closure { Name = "Staff day", From = D(2027, 5, 17), To = D(2027, 5, 17), Scope = ClosureScope.Groups, GroupIds = [] });
        Assert.Equal(
        [
            "Easter (Påske) 2027-03-25–2027-03-29 closed by Easter break",
            "Ascension Day (Kristi himmelfartsdag) 2027-05-06–2027-05-06",
            "The Friday after Ascension (Fredag efter Kristi himmelfartsdag) 2027-05-07–2027-05-07 (not official)",
            "Whit Monday (2. pinsedag) 2027-05-17–2027-05-17",
        ], Said(For(b)));

        // Closed on Thursday and Friday but not on the Monday, Easter is not closed yet. Two closures between them close it.
        b.T = b.T with { Closures = EntitySet<Closure>.Empty };
        b.Closure(new Closure { Name = "Thursday and Friday", From = D(2027, 3, 25), To = D(2027, 3, 26) });
        Assert.False(For(b)[0].AlreadyClosed);
        b.Closure(new Closure { Name = "The Monday", From = D(2027, 3, 29), To = D(2027, 3, 29) });
        Assert.Equal("Thursday and Friday", For(b)[0].ClosedBy?.Name);
        // Of two that close it, the one that starts first is named, whichever was entered first.
        b.Closure(new Closure { Name = "Spring break", From = D(2027, 3, 20), To = D(2027, 4, 4) });
        Assert.Equal("Spring break", For(b)[0].ClosedBy?.Name);
    }

    [Fact]
    public void OnlyTheSemestersDaysAreLookedInAndWhitsunOnConstitutionDayIsOneHoliday()
    {
        var b = new Build();
        b.T = b.T with { Semesters = EntitySet<Semester>.Empty };
        Assert.Empty(For(b));
        // Easter falls between the semesters: nothing is on, so nothing is offered for it.
        b.T = b.T with
        {
            Semesters = EntitySet<Semester>.Of(
            [
                new Semester { Name = "Autumn", StartMonday = D(2026, 8, 17), WeekCount = 22 },
                new Semester { Name = "Spring", StartMonday = D(2027, 4, 5), WeekCount = 13 },
            ]),
        };
        Assert.Equal(
        [
            "Ascension Day (Kristi himmelfartsdag) 2027-05-06–2027-05-06",
            "The Friday after Ascension (Fredag efter Kristi himmelfartsdag) 2027-05-07–2027-05-07 (not official)",
            "Whit Monday (2. pinsedag) 2027-05-17–2027-05-17",
        ], Said(For(b)).Where(s => s.Contains("2027-05")));
        Assert.DoesNotContain(For(b), h => h.Name.StartsWith("Easter"));
        // The autumn reaches into January 2027: Christmas Eve, Christmas Day and New Year's Eve are school days (Thursday and Friday), and New Year's Day.
        Assert.Equal(
        [
            "Christmas Eve (Juleaftensdag) 2026-12-24–2026-12-24 (not official)",
            "Christmas Day (Juledag) 2026-12-25–2026-12-25",
            "New Year's Eve (Nytårsaftensdag) 2026-12-31–2026-12-31 (not official)",
            "New Year's Day (Nytårsdag) 2027-01-01–2027-01-01",
        ], Said(For(b)).Where(s => s.Contains("2026-12") || s.Contains("2027-01")));

        // A weekend joins two holidays, the weeks between two semesters never: New Year's Day and an Easter
        // Monday the next semester starts on are two closures, not one over the winter.
        b.T = b.T with
        {
            Semesters = EntitySet<Semester>.Of(
            [
                new Semester { Name = "Winter week", StartMonday = D(2026, 12, 28), WeekCount = 1 },
                new Semester { Name = "Spring", StartMonday = D(2027, 3, 29), WeekCount = 2 },
            ]),
        };
        Assert.Equal(
        [
            "New Year's Eve (Nytårsaftensdag) 2026-12-31–2026-12-31 (not official)",
            "New Year's Day (Nytårsdag) 2027-01-01–2027-01-01",
            "Easter Monday (2. påskedag) 2027-03-29–2027-03-29",
        ], Said(For(b)));

        // In 2017 Whit Monday is 5 June, Constitution Day: one closure, the public holiday.
        b.T = b.T with { Semesters = EntitySet<Semester>.Of([new Semester { Name = "Spring", StartMonday = D(2017, 5, 29), WeekCount = 2 }]) };
        Assert.Equal(["Whit Monday (2. pinsedag) 2017-06-05–2017-06-05"], Said(For(b)));
    }

    [Fact]
    public void StartingNextYearClosesNextYearsOwnPublicHolidaysWhenAskedWhereAClosureCarriedByWeekNumberMissesEaster()
    {
        var b = new Build();
        b.T = b.T with
        {
            Semesters = EntitySet<Semester>.Of(
            [
                new Semester { Name = "Autumn semester", StartMonday = D(2026, 8, 17), WeekCount = 22 },
                new Semester { Name = "Spring semester", StartMonday = D(2027, 2, 1), WeekCount = 20 },
            ]),
        };
        b.Closure(new Closure { Name = "Easter break", From = D(2027, 3, 22), To = D(2027, 3, 29) });   // weeks 12 and 13

        // Next year's spring is weeks 5 to 24 of 2028: Easter is 13 to 17 April, Ascension 25 May, and Whit Monday is 5 June, Constitution Day too.
        var plan = NextYear.For(b.T)!;
        string[] holidays = ["Easter (Påske) 2028-04-13–2028-04-17", "Ascension Day (Kristi himmelfartsdag) 2028-05-25–2028-05-25", "Whit Monday (2. pinsedag) 2028-06-05–2028-06-05"];
        Assert.Equal(holidays, Said(plan.Holidays));

        Assert.Empty(NextYear.Make(b.T, "Next", keepClosures: false).Closures);
        var closed = NextYear.Make(b.T, "Next", keepClosures: false, closeHolidays: true).Closures.OrderBy(c => c.From).ToList();
        Assert.Equal(holidays, closed.Select(c => $"{c.Name} {c.From:yyyy-MM-dd}–{c.To:yyyy-MM-dd}"));
        Assert.All(closed, c => Assert.Equal(ClosureScope.WholeBuilding, c.Scope));
        Assert.Equal(3, closed.Select(c => c.Id).Distinct().Count());

        // The Easter break kept moves with its week numbers, to 20–27 March 2028, so it closes no holiday and Easter is still closed on its days.
        var both = NextYear.Make(b.T, "Next", keepClosures: true, closeHolidays: true).Closures.OrderBy(c => c.From).ToList();
        Assert.Equal(["Easter break 2028-03-20–2028-03-27", .. holidays], both.Select(c => $"{c.Name} {c.From:yyyy-MM-dd}–{c.To:yyyy-MM-dd}"));

        // A closure this year that is dated on next year's Easter is not next year's, kept or not: the list is
        // what next year would close, and it still closes Easter.
        b.Closure(new Closure { Name = "Booked early", From = D(2028, 4, 13), To = D(2028, 4, 17) });
        Assert.Equal(holidays, Said(NextYear.For(b.T)!.Holidays));
        Assert.Contains(NextYear.Make(b.T, "Next", keepClosures: false, closeHolidays: true).Closures, c => c.Name == "Easter (Påske)");

        // A kept closure that does close one, a winter break over Ascension's week, is not closed twice.
        b.Closure(new Closure { Name = "Ascension week", From = D(2027, 5, 24), To = D(2027, 5, 28) });   // week 21: 22–26 May 2028
        var kept = NextYear.Make(b.T, "Next", keepClosures: true, closeHolidays: true).Closures;
        Assert.DoesNotContain(kept, c => c.Name.StartsWith("Ascension Day"));
        Assert.Contains(kept, c => c.Name == "Ascension week" && c.From == D(2028, 5, 22));
    }
}
