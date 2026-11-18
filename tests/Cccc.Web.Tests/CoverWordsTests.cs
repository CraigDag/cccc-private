using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Web.Features.Sessions;

namespace Cccc.Web.Tests;

/// <summary>
/// What the Cover page says about who could take everything one person being away leaves (<see cref="CoverWords"/>),
/// asked of the made-up school on Wednesday 18 November 2026 at 10:15, with Esme away the next Monday to Wednesday.
/// </summary>
public class CoverWordsTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Today = Monday.AddDays(2);
    private static readonly DateOnly NextMonday = Monday.AddDays(7);
    private static readonly Timetable Sample = SampleSchool.Create(Monday, Today);

    private static Facilitator Named(Timetable t, string first) => t.Facilitators.Single(f => f.Name.StartsWith(first + " ", StringComparison.Ordinal));

    private static Timetable Away(Timetable t, string first, DateOnly from, DateOnly to) =>
        t with { Absences = t.Absences.Put(new Absence { Person = AbsentPerson.Facilitator, PersonId = Named(t, first).Id, From = from, To = to }) };

    private static (Timetable T, AwayStretch Stretch) EsmeAway(Timetable t)
    {
        t = Away(t, "Esme", NextMonday, NextMonday.AddDays(2));
        return (t, Assert.Single(CoverAll.Stretches(t, Occurrences.ForWeek(t, NextMonday), Today, new TimeOnly(10, 15))));
    }

    private static IEnumerable<string> Said(Timetable t, AwayStretch s) =>
        CoverAll.Ranked(t, s, Today).Select(c => c.Person.Name.Split(' ')[0] + ": " + CoverWords.Taker(t, c, s.Gaps.Count));

    [Fact]
    public void EachSaysHowMuchOfItTheyAreFreeForWhatTheyHaveTaughtOfItAndTheirWeek()
    {
        var (t, s) = EsmeAway(Sample);
        Assert.Equal(["Strength and conditioning", "Creation lab", "Strength and conditioning"], s.Gaps.Select(o => o.Session.DisplayTitle));
        // Cleo first: she has been in the room for Creation lab. Dag is busy on Tuesday and Finn on Monday; Gwen is a guest.
        Assert.Equal([
            "Cleo: Free for all 3 · has taught Creation lab · 10h this week",
            "Ada: Free for all 3 · 5h 45m this week",
            "Bo: Free for all 3 · 7h 15m this week",
            "Dag: Free for 2 of 3, busy for 1 · has taught Strength and conditioning · 5h 45m this week",
            "Finn: Free for 2 of 3, busy for 1 · has taught Creation lab · 10h 30m this week",
            "Gwen: Free for all 3 · 0m this week",
        ], Said(t, s));
    }

    [Fact]
    public void AnOfferIsSaidFirstAndSoIsBeingAwayForSomeOfItAndGoingOverALimit()
    {
        var (t, s) = EsmeAway(Sample);
        var ada = Named(t, "Ada");
        var cleo = Named(t, "Cleo");
        t = Away(t, "Bo", NextMonday.AddDays(2), NextMonday.AddDays(2));
        t = t with
        {
            Offers = t.Offers.Put(new CoverOffer { SessionId = s.Gaps[1].Session.Id, FacilitatorId = ada.Id, OfferedAt = DateTimeOffset.UnixEpoch }),
            Facilitators = t.Facilitators.Put(cleo with { MaxWeeklyHours = TimeSpan.FromHours(15.5) }),
        };
        // Bo's own Wednesday is one class needing cover: nobody's stretch but Esme's.
        s = Assert.Single(CoverAll.Stretches(t, Occurrences.ForWeek(t, NextMonday), Today, new TimeOnly(10, 15)));
        Assert.Equal([
            "Ada: Offered · Free for all 3 · 5h 45m this week",
            "Cleo: Free for all 3 · has taught Creation lab · 10h this week · would go over their limit",
            "Dag: Free for 2 of 3, busy for 1 · has taught Strength and conditioning · 5h 45m this week",
            "Finn: Free for 2 of 3, busy for 1 · has taught Creation lab · 10h 30m this week",
            "Bo: Free for 2 of 3, away for 1 · 3h 15m this week",
            "Gwen: Free for all 3 · 0m this week",
        ], Said(t, s));
        var ranked = CoverAll.Ranked(t, s, Today);
        Assert.Equal(["Ada Brightwater covers all of it", "Bo Lindqvist covers 2 of them"],
            new[] { ranked[0], ranked[4] }.Select(c => CoverWords.Press(c, s.Gaps.Count)));
    }

    [Fact]
    public void WhatTheyHaveTaughtIsSaidInNameOrder()
    {
        var (t, s) = EsmeAway(Sample);
        // Strength and conditioning first, as the stretch comes: said after Creation lab all the same.
        var classes = s.Gaps.Select(o => o.Session.ClassId!.Value).Distinct().ToList();
        var c = new StretchTaker(Named(t, "Cleo"), s.Gaps, [], [.. classes], 4, false, TimeSpan.FromHours(10));
        Assert.Equal("Free for all 3 · has taught Creation lab and Strength and conditioning · 10h this week", CoverWords.Taker(t, c, 3));
    }
}
