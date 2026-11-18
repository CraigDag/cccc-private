using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Web.Features.Ask;

namespace Cccc.Web.Tests;

/// <summary>The words for what facilitators ask, the same on My week, on Now and in the answer form.</summary>
public class AskingWordsTests
{
    private static readonly DateOnly Monday = new(2030, 3, 4);

    [Fact]
    public void TheDaysAreOneDayOrAStretchAndAllDayOrBetweenTwoTimes()
    {
        Assert.Equal("Tue 5 Mar, all day", AskingWords.Days(Monday.AddDays(1), Monday.AddDays(1), null, null));
        Assert.Equal("Tue 5 – Thu 7 Mar, 09:00–12:00", AskingWords.Days(Monday.AddDays(1), Monday.AddDays(3), new(9, 0), new(12, 0)));
        Assert.Equal("Fri 29 Mar – Tue 2 Apr, all day", AskingWords.Days(new(2030, 3, 29), new(2030, 4, 2), null, null));
        Assert.Equal("Tue 31 Dec – Wed 1 Jan, all day", AskingWords.Days(new(2030, 12, 31), new(2031, 1, 1), null, null));
        // The same month of another year is not the same month: both dates are said in full.
        Assert.Equal("Tue 5 Mar – Wed 5 Mar, all day", AskingWords.Days(new(2030, 3, 5), new(2031, 3, 5), null, null));
        // One time alone is no window: whole days, as the absence it would record.
        Assert.Equal("Tue 5 Mar, all day", AskingWords.Days(Monday.AddDays(1), Monday.AddDays(1), new(9, 0), null));
    }

    [Fact]
    public void WhereEachStandsIsSaidInTheAskersWordsAndNeverAsAReason()
    {
        Assert.Equal(
            ["Waiting for an answer", "Yes: recorded as away", "Not this time", "Passed without an answer"],
            Enum.GetValues<RequestState>().Select(AskingWords.State));
        Assert.Equal(
            ["Offered, waiting for an answer", "Yes: you are covering it", "No thanks, this time", "Not needed now: it has cover", "Passed"],
            Enum.GetValues<OfferState>().Select(AskingWords.State));
    }

    [Fact]
    public void WhatAYesLeavesEachClassIsSaidFromTheResolverItself()
    {
        var t = SampleSchool.Create(Monday, Monday);
        Facilitator Named(string name) => t.Facilitators.Single(f => f.Name == name);
        var (ada, bo) = (Named("Ada Brightwater"), Named("Bo Lindqvist"));
        // Ada can't come on Monday: Bo still has all of their Acrobatics; Circus disciplines is Dag's from 14:00 only.
        var r = new TimeOffRequest { FacilitatorId = ada.Id, From = Monday, To = Monday, AskedOn = Monday.AddDays(-1) };
        var said = Asking.WhatAYesLeaves(t, r).Select(x => (x.Now.Session.Title, AskingWords.After(t, ada.Id, x.Now, x.After))).ToList();
        Assert.Equal([("Acrobatics", "Bo Lindqvist still teaching it"), ("Circus disciplines", "needs cover")], said);
        // A class somebody covers stays covered on paper when they are away, so it is said as theirs to change.
        var mondays = t.Sessions.Where(s => s.Date == Monday && s.Facilitators.Any(a => a.FacilitatorId == bo.Id) && s.Title == "Acrobatics" && s.Start == new TimeOnly(13, 0)).Single();
        var covered = Apply.Batch(t, Edits.SetCover(t, mondays.Id, [ada.Id], null));
        var away = Apply.Batch(covered, Edits.PutAbsence(covered, new Absence { Person = AbsentPerson.Facilitator, PersonId = bo.Id, From = Monday, To = Monday }));
        var covering = Asking.WhatAYesLeaves(away, r).Single(x => x.Now.Session.Id == mondays.Id);
        Assert.Equal("covering it: change its cover", AskingWords.After(away, ada.Id, covering.Now, covering.After));
    }
}
