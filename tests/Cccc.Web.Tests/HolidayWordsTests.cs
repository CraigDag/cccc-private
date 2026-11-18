using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Web.Features.Data;
using Cccc.Web.Features.Find;

namespace Cccc.Web.Tests;

/// <summary>
/// Closing the public holidays (<see cref="HolidayWords"/>, <see cref="PublicHolidays"/>), read against the made-up
/// school on Wednesday 18 November 2026: its autumn semester runs to 21 February 2027, its spring from 8 March to 25 July.
/// </summary>
public class HolidayWordsTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Today = Monday.AddDays(2);
    private static readonly Timetable T = SampleSchool.Create(Monday, Today);

    private static string[] Said(IEnumerable<HolidayClosure> hs) => [.. hs.Select(h => h.Name + " · " + HolidayWords.Line(h))];

    [Fact]
    public void EachHolidayAheadOnASchoolDaySaysWhenAndWhatItHoldsAndWhyOneIsNotADayToClose()
    {
        Assert.Equal(
        [
            "Christmas Eve (Juleaftensdag) · Thu 24 Dec · not a public holiday, but many places close",
            "Christmas Day (Juledag) · Fri 25 Dec",
            "New Year's Eve (Nytårsaftensdag) · Thu 31 Dec · not a public holiday, but many places close",
            "New Year's Day (Nytårsdag) · Fri 1 Jan",
            "Easter (Påske) · Thu 25 Mar – Mon 29 Mar · Maundy Thursday, Good Friday, Easter Monday",
            "Ascension Day (Kristi himmelfartsdag) · Thu 6 May",
            "The Friday after Ascension (Fredag efter Kristi himmelfartsdag) · Fri 7 May · not a public holiday, but many places close",
            "Whit Monday (2. pinsedag) · Mon 17 May",
        ], Said(PublicHolidays.For(T, Today)));
        // Boxing Day is a Saturday and Constitution Day a Saturday too: nothing is on, so they close nothing.
        Assert.DoesNotContain(PublicHolidays.For(T, Today), h => h.Name.StartsWith("Boxing") || h.Name.StartsWith("Constitution"));
    }

    [Fact]
    public void OneClosedAlreadySaysByWhatAndClosingThemIsOneUndoNamedForWhatItClosed()
    {
        var easterBreak = new Closure { Id = Guid.NewGuid(), Name = " Easter break ", From = new(2027, 3, 22), To = new(2027, 4, 2) };
        var t = T with { Closures = T.Closures.Put(easterBreak) };
        var offered = PublicHolidays.For(t, Today);
        var easter = offered.Single(h => h.Name == "Easter (Påske)");
        Assert.Equal("Thu 25 Mar – Mon 29 Mar · Maundy Thursday, Good Friday, Easter Monday · closed already: Easter break", HolidayWords.Line(easter));
        Assert.Equal(1, offered.Count(h => h.AlreadyClosed));

        Assert.Equal("Add them", HolidayWords.Adds(0));
        Assert.Equal("Add 1 closure", HolidayWords.Adds(1));
        Assert.Equal("Add 4 closures", HolidayWords.Adds(4));
        Assert.Equal("Closed Easter (Påske)", HolidayWords.Done([easter]));
        Assert.Equal("Closed 2 public holidays", HolidayWords.Done([easter, offered[0]]));
    }

    [Fact]
    public void StartingNextYearListsTheHolidaysItWouldCloseEachOnItsOwnDayNextYear()
    {
        // Next year's Christmas Day and New Year's Day are Saturdays, so close nothing. Its spring is 6 March to
        // 23 July 2028: Easter is 13 to 17 April, Ascension 25 May, and Whit Monday 5 June.
        var plan = NextYear.For(T)!;
        Assert.Equal("Easter (Påske), Thu 13 Apr – Mon 17 Apr; Ascension Day (Kristi himmelfartsdag), Thu 25 May; Whit Monday (2. pinsedag), Mon 5 Jun",
            HolidayWords.Listed(plan.Holidays));
    }

    [Fact]
    public void CtrlKFindsItForAnAdministratorWhoAsksForItAndNobodyElse()
    {
        foreach (var asked in new[] { "holiday", "public holidays", "easter", "Christmas", "helligdage" })
            Assert.Contains(FindIndex.Search(T, asked, mayEdit: true, admin: true, today: Today, shown: Today), h => h.Kind == "close-holidays");
        // Once a year, so not among the actions offered before anything is typed.
        Assert.DoesNotContain(FindIndex.Search(T, "", mayEdit: true, admin: true, today: Today, shown: Today), h => h.Kind == "close-holidays");
        Assert.DoesNotContain(FindIndex.Search(T, "holiday", mayEdit: true, admin: false, today: Today, shown: Today), h => h.Kind == "close-holidays");
        Assert.DoesNotContain(FindIndex.Search(T, "holiday", mayEdit: false, admin: false, today: Today, shown: Today), h => h.Kind == "close-holidays");
    }
}
