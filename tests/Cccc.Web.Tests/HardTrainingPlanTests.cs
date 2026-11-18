using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Store;
using Cccc.Web.Features.Plan;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Tests;

/// <summary>
/// The plan puts each group's hard training where the group is a row, worked out by the rule over the whole week
/// whatever is drawn; and its warnings sit beside the classes they are about, never in the clashes and never a ring.
/// </summary>
public class HardTrainingPlanTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);
    private static readonly Timetable T = SampleSchool.Create(Monday, Monday);
    private static readonly Access Admin = new(new Account(Guid.NewGuid(), "admin", "Admin", Role.Administrator, default, null, false, false, "s", DateTimeOffset.UnixEpoch, true));

    private static Group G(string name) => T.Groups.Single(g => g.Name == name);

    private static string Row(string group) => G(group).Id.ToString();

    private static PlanModel Plan(Lens lens, Scale scale = Scale.Day, Focus? focus = null, DateOnly? day = null, Timetable? t = null)
    {
        var v = new PlanView { Lens = lens, Scale = scale };
        v.Start(day ?? Monday);
        if (focus is not null) { v.FocusOn(focus); v.Lens = lens; }
        return PlanModel.Build(t ?? T, v, Admin, Monday);
    }

    private static string? Said(PlanModel m, string row, DateOnly day) => m.HardOn(row, day) is { } d ? HardWords.Sum(d) + (d.Over ? " over" : "") : null;

    [Fact]
    public void OnADayEachGroupsRowCarriesThatGroupsHardTrainingThatDay()
    {
        var m = Plan(Lens.Groups);
        Assert.Equal("4h 45m hard over", Said(m, Row("BA 1"), Monday));
        Assert.Equal("3h 30m hard", Said(m, Row("BA 2"), Monday));
        Assert.Equal("1h 30m hard", Said(m, Row("Prep"), Monday));
        // The day on screen and no other, and only on the rows that are groups.
        Assert.All(m.Hard.Keys, k => Assert.Equal(Monday, k.Date));
        Assert.Equal(m.Rows.Select(r => r.Key).Order(), m.Hard.Keys.Select(k => k.Row).Order());
        // The rule's own figures: the plan works out nothing of its own.
        var rule = HardTraining.For(T, Occurrences.ForWeek(T, Monday), G("BA 1").Id, Monday);
        Assert.Equal(rule.Total, m.HardOn(Row("BA 1"), Monday)!.Total);
        Assert.Equal(rule.Stretches.Select(s => (s.Start, s.End, s.Hard)), m.HardOn(Row("BA 1"), Monday)!.Stretches.Select(s => (s.Start, s.End, s.Hard)));
    }

    [Fact]
    public void OnTheWeekEveryGroupHasEveryDayAndADayWithNoneSaysNothing()
    {
        var m = Plan(Lens.Groups, Scale.Week);
        Assert.Equal("2h 45m hard over", Said(m, Row("BA 2"), Wednesday));
        Assert.Equal("2h hard", Said(m, Row("BA 1"), Monday.AddDays(1)));
        Assert.Equal(m.Rows.Count * m.Days.Count, m.Hard.Count);
        // BA 1 has no hard training on Thursday (lunch only): nothing to say, rather than "0m hard".
        Assert.NotNull(m.Hard[(Row("BA 1"), Monday.AddDays(3))]);
        Assert.Null(m.HardOn(Row("BA 1"), Monday.AddDays(3)));
    }

    [Fact]
    public void NarrowedToOneGroupItsDaysAreItsRowsAndToAProgrammeItsGroupsAre()
    {
        var ba1 = G("BA 1");
        var days = Plan(Lens.Groups, focus: new Focus("group", ba1.Id, ba1.Name));
        Assert.True(days.RowsAreDays);
        Assert.Equal("4h 45m hard over", Said(days, PlanModel.DayKey(Monday), Monday));
        Assert.Equal("1h 30m hard", Said(days, PlanModel.DayKey(Wednesday), Wednesday));
        Assert.Null(days.HardOn(Row("BA 1"), Monday));

        // Narrowed to a week of one group, it is that group's row.
        var week = Plan(Lens.Groups, Scale.Week, new Focus("group", ba1.Id, ba1.Name));
        Assert.Equal("4h 45m hard over", Said(week, Row("BA 1"), Monday));
        Assert.DoesNotContain(week.Hard.Keys, k => k.Row != Row("BA 1"));

        var degree = T.Programmes.Single(p => p.Id == ba1.ProgrammeId);
        var programme = Plan(Lens.Groups, focus: new Focus("programme", degree.Id, degree.Name));
        Assert.False(programme.RowsAreDays);
        Assert.Equal("4h 45m hard over", Said(programme, Row("BA 1"), Monday));
        Assert.Equal("3h 30m hard", Said(programme, Row("BA 2"), Monday));
        Assert.Null(programme.HardOn(Row("Prep"), Monday));
    }

    [Fact]
    public void SpacesPeopleAndTheTermHaveNoneButEveryLensKnowsTheWarningsAndNoneIsAClash()
    {
        foreach (var m in new[] { Plan(Lens.Spaces), Plan(Lens.People), Plan(Lens.Spaces, Scale.Week), Plan(Lens.Groups, Scale.Term) })
        {
            Assert.Empty(m.Hard);
            Assert.Equal(3, m.HardWarnings.Count);
            Assert.All(m.HardWarnings, c => Assert.True(c.IsHardTraining));
            Assert.DoesNotContain(m.Clashes, c => c.IsHardTraining);
        }
        // A class the warnings are about is not ringed for it: the sample's Monday Acrobatics for BA 1 clashes with nothing.
        var groups = Plan(Lens.Groups);
        var acrobatics = groups.HardWarnings[0].SessionIds[1];
        Assert.Equal("Acrobatics", T.Sessions[acrobatics]!.DisplayTitle);
        Assert.DoesNotContain(acrobatics, groups.Ringed);
    }

    [Fact]
    public void WhatIsDrawnDoesNotChangeTheFigureAndNoLimitsIsNoWarningButStillAFigure()
    {
        // Open sessions shown or not, the day is the same; and a figure for a day still there when its classes are not drawn.
        var v = new PlanView { Lens = Lens.Groups, Scale = Scale.Day, ShowOpen = false };
        v.Start(Wednesday);
        var noOpen = PlanModel.Build(T, v, Admin, Monday);
        Assert.Contains(Plan(Lens.Groups, day: Wednesday).Bars, b => Hours.WorkloadOf(T, b.O.Session) == Workload.DropIn);
        Assert.DoesNotContain(noOpen.Bars, b => Hours.WorkloadOf(T, b.O.Session) == Workload.DropIn);
        Assert.Equal("2h 45m hard over", Said(noOpen, Row("BA 2"), Wednesday));
        Assert.Equal(Said(Plan(Lens.Groups, day: Wednesday), Row("BA 1"), Wednesday), Said(noOpen, Row("BA 1"), Wednesday));

        var none = T with { Settings = T.Settings with { MostHardInARow = null, MostHardInADay = null } };
        var m = Plan(Lens.Groups, t: none);
        Assert.Equal("4h 45m hard", Said(m, Row("BA 1"), Monday));
        Assert.Empty(m.HardWarnings);
    }

    [Fact]
    public void TheLongerLookSaysEachStretchTheTotalAndOnlyTheLimitsPassed()
    {
        var m = Plan(Lens.Groups, Scale.Week);
        Assert.Equal("Hard training 09:00–12:00 (2h 45m), 13:00–15:00 (2h); 4h 45m in all. More than 2h in a row without a 30m rest. More than 4h in a day.",
            HardWords.Title(T, m.HardOn(Row("BA 1"), Monday)!));
        Assert.Equal("Hard training 09:00–10:30 (1h 30m), 13:00–15:00 (2h); 3h 30m in all.", HardWords.Title(T, m.HardOn(Row("BA 2"), Monday)!));
        Assert.Equal("Hard training 09:00–12:00 (2h 45m); 2h 45m in all. More than 2h in a row without a 30m rest.", HardWords.Title(T, m.HardOn(Row("BA 2"), Wednesday)!));
        // The school's own rest is the one said.
        var hour = T with { Settings = T.Settings with { RestBetweenHard = TimeSpan.FromHours(1), MostHardInADay = null } };
        var day = HardTraining.For(hour, Occurrences.ForWeek(hour, Monday), G("BA 1").Id, Monday);
        Assert.EndsWith("More than 2h in a row without a 1h rest.", HardWords.Title(hour, day));
    }
}
