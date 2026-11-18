using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Web.Features.Now;

namespace Cccc.Web.Tests;

/// <summary>
/// Next week on Now (<see cref="WeekAheadWords"/>), read against the made-up school on Thursday 19 November 2026:
/// next week is week 48, built, and the week after it blank.
/// </summary>
public class WeekAheadWordsTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Thursday = Monday.AddDays(3);

    private static WeekAheadView Ahead(Timetable t, DateOnly today) =>
        WeekAhead.For(t, today, EventViewer.Administrator, RegisterReader.Administrator)!;

    [Fact]
    public void TheWeekIsNamedAndABlankOneOffersTheLastWeekBuiltOrThePlan()
    {
        Assert.Equal("Next week · Week 48", WeekAheadWords.Head(Monday.AddDays(7)));
        Assert.Equal("Copy week 47 onto it…", WeekAheadWords.Fill(Monday));
        Assert.Equal("Open it in the plan to build it", WeekAheadWords.Fill(null));
        var after = Ahead(SampleSchool.Create(Monday, Monday.AddDays(10)), Monday.AddDays(10));
        Assert.True(after.Week.Blank);
        Assert.Equal("Copy week 48 onto it…", WeekAheadWords.Fill(after.Week.ComparedWith));
    }

    [Fact]
    public void ClashesAreCountedAndTheFirstSaidInTheClashChecksWordsAndCoverByClassAndWhen()
    {
        Clash C(string d) => new(ClashKind.RoomDoubleBooked, d, []);
        Assert.Equal(("1 clash", "Space \"Gym\" is double-booked"), (WeekAheadWords.Clashes([C("Space \"Gym\" is double-booked")]), WeekAheadWords.FirstClash([C("Space \"Gym\" is double-booked")])));
        Assert.Equal(("3 clashes", "First and 2 more"), (WeekAheadWords.Clashes([C("First"), C("Second"), C("Third")]), WeekAheadWords.FirstClash([C("First"), C("Second"), C("Third")])));
        Assert.Equal("", WeekAheadWords.FirstClash([]));

        // Ada away next Thursday: her acrobatics needs cover; with Cleo away next Tuesday and Wednesday, three do.
        var t = SampleSchool.Create(Monday, Thursday);
        Guid Id(string name) => t.Facilitators.Single(f => f.Name == name).Id;
        Absence Away(string name, int from, int to) => new() { Person = AbsentPerson.Facilitator, PersonId = Id(name), From = Monday.AddDays(from), To = Monday.AddDays(to) };
        var one = t with { Absences = t.Absences.Put(Away("Ada Brightwater", 10, 10)) };
        var cover = Ahead(one, Thursday).Cover;
        Assert.Equal(("1 class needs cover", "Acrobatics Thu 26 Nov 09:00"), (WeekAheadWords.Cover(cover), WeekAheadWords.FirstCover(one, cover)));
        var more = one with { Absences = one.Absences.Put(Away("Cleo Marchetti", 8, 9)) };
        var three = Ahead(more, Thursday).Cover;
        Assert.True(three.Count > 2, $"{three.Count} need cover");
        Assert.Equal($"{three.Count} classes need cover", WeekAheadWords.Cover(three));
        string When(Occurrence o) => $"{Cccc.Web.Features.Week.Looks.Title(more, o.Session)} {Week.ShortDate(o.Date)} {Week.Hm(o.Start)}";
        Assert.Equal($"{When(three[0])}, {When(three[1])} and {three.Count - 2} more", WeekAheadWords.FirstCover(more, three));
        Assert.StartsWith(Cccc.Web.Features.Week.Looks.Title(more, three[0].Session) + " Tue 24 Nov ", WeekAheadWords.FirstCover(more, three));
        Assert.Equal(When(three[0]) + ", " + When(three[1]), WeekAheadWords.FirstCover(more, three.Take(2).ToList()));
    }

    [Fact]
    public void DaysClosedAreSaidByTheirClosureAndABuiltWeekByWhatChangedAndThatItIsReady()
    {
        var ascension = new Closure { Name = "Ascension Day", From = Monday.AddDays(10), To = Monday.AddDays(10) };
        var easter = new Closure { Name = "Easter (Påske)", From = Monday.AddDays(10), To = Monday.AddDays(11) };
        var study = new Closure { Name = "Study day", From = Monday.AddDays(7), To = Monday.AddDays(7) };
        Assert.Equal("Closed Thursday: Ascension Day", WeekAheadWords.Closed([(Monday.AddDays(10), ascension)]));
        Assert.Equal("Closed Thursday and Friday: Easter (Påske)", WeekAheadWords.Closed([(Monday.AddDays(10), easter), (Monday.AddDays(11), easter)]));
        Assert.Equal("Closed Monday: Study day; Thursday and Friday: Easter (Påske)",
            WeekAheadWords.Closed([(Monday.AddDays(7), study), (Monday.AddDays(10), easter), (Monday.AddDays(11), easter)]));

        // Next week drops this week's two guest workshops, so it is two changes from this one.
        var next = Ahead(SampleSchool.Create(Monday, Thursday), Thursday);
        Assert.Equal("2 changes from week 47", WeekAheadWords.Changes(next.Week));
        // Compared with the last week built, which need not be the one before it.
        Assert.Equal("1 change from week 46", WeekAheadWords.Changes(next.Week with { ComparedWith = Monday.AddDays(-7), Changes = [next.Week.Changes[0]] }));
        Assert.Equal($"Ready: {next.Week.Happening} classes, nothing clashes, nobody needs cover.", WeekAheadWords.Ready(next.Week));
        Assert.True(next.Week.Happening > 1);
        Assert.Equal("Ready: 1 class, nothing clashes, nobody needs cover.", WeekAheadWords.Ready(next.Week with { Happening = 1 }));
    }
}
