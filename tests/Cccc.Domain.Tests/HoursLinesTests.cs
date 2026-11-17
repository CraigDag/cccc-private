using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// <see cref="Hours.Lines"/>: every piece of time in a facilitator's hours, one line each, adding up to
/// the total the reports and My week show.
/// </summary>
public class HoursLinesTests
{
    [Fact]
    public void EveryFacilitatorsLinesAddUpToTheirTotalInEveryWeekOfTheMadeUpSchool()
    {
        var day = new DateOnly(2030, 3, 6);
        var t = SampleSchool.Create(day, day);
        var kinds = new HashSet<string>();
        int weeksWithHours = 0;
        foreach (var monday in t.Semesters.SelectMany(s => s.Mondays()).Distinct())
        {
            var week = Occurrences.HappeningInWeek(t, monday);
            var totals = Hours.ByFacilitator(t, week);
            foreach (var f in t.Facilitators)
            {
                var lines = Hours.Lines(t, week, f.Id);
                var sum = lines.Aggregate(TimeSpan.Zero, (a, l) => a + l.Time);
                Assert.True(totals.GetValueOrDefault(f.Id)?.Total == sum || (sum == TimeSpan.Zero && !totals.ContainsKey(f.Id)),
                    $"{f.Name}, week of {monday}: the lines add up to {sum}, the total is {totals.GetValueOrDefault(f.Id)?.Total}");
                Assert.All(lines, l => Assert.True(l.Time > TimeSpan.Zero, "a line of no time"));
                if (sum > TimeSpan.Zero) weeksWithHours++;
                foreach (var l in lines) kinds.Add(l.Kind == HoursLineKind.Rule ? "rule:" + l.Rule!.Kind : l.Kind.ToString());
            }
        }
        // Not about nothing: the made-up school has teaching, cover, preparation and a paid break in it.
        Assert.True(weeksWithHours > 20, $"only {weeksWithHours} facilitator-weeks with hours");
        Assert.Equal(["Covering", "Teaching", "rule:Break", "rule:Preparation"], kinds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ACoveredClassIsALineOfTheCoverersNamingWhoTheyStoodInForAndNoneOfThePersonAway()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var cleo = b.Facilitator("Cleo");
        var s = b.Session(Monday, "09:00", "11:00", null, null, null, In(ada));
        b.Away(ada, Monday);
        b.Put(s with { CoverIds = [cleo.Id] });
        var week = Occurrences.HappeningInWeek(b.T, Monday);

        var line = Assert.Single(Hours.Lines(b.T, week, cleo.Id));
        Assert.Equal(HoursLineKind.Covering, line.Kind);
        Assert.Equal([ada.Id], line.Covering);
        Assert.Equal(TimeSpan.FromHours(2), line.Time);
        Assert.Empty(Hours.Lines(b.T, week, ada.Id));
    }

    [Fact]
    public void APersonInPartOfAClassHasALineOfTheirOwnPartOnly()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        b.Session(Monday, "13:00", "16:00", null, null, null, In(ada), In(bo, from: "14:00", until: "15:00"));
        var week = Occurrences.HappeningInWeek(b.T, Monday);

        var line = Assert.Single(Hours.Lines(b.T, week, bo.Id));
        Assert.Equal((new TimeOnly(14, 0), new TimeOnly(15, 0), TimeSpan.FromHours(1), HoursLineKind.Teaching), (line.From, line.Until, line.Time, line.Kind));
        Assert.Equal(TimeSpan.FromHours(3), Assert.Single(Hours.Lines(b.T, week, ada.Id)).Time);
    }

    [Fact]
    public void ACreditFollowsTheClassItWasEarnedOnAndABreakSitsAtItsOwnTime()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var creation = b.Class("Creation");
        var lunch = b.Class("Lunch", Workload.NotCounted);
        var prep = new TimeRule { Name = "Prep", Kind = TimeRuleKind.Preparation, Percent = 50, ClassIds = [creation.Id] };
        var paid = new TimeRule { Name = "Paid lunch", Kind = TimeRuleKind.Break, Minutes = 30, ClassIds = [lunch.Id] };
        b.TimeRule(prep);
        b.TimeRule(paid);
        b.Session(Monday, "13:00", "16:00", null, null, creation, In(ada), In(bo, from: "14:00", until: "15:00"));
        b.Session(Monday, "12:00", "12:30", null, null, lunch);
        b.Session(Monday, "09:00", "12:00", null, null, null, In(ada));
        var week = Occurrences.HappeningInWeek(b.T, Monday);

        var lines = Hours.Lines(b.T, week, ada.Id);
        Assert.Equal(
            [("09:00", HoursLineKind.Teaching, null, 180), ("12:00", HoursLineKind.Rule, "Paid lunch", 30),
             ("13:00", HoursLineKind.Teaching, null, 180), ("13:00", HoursLineKind.Rule, "Prep", 90)],
            lines.Select(l => (Week.Hm(l.Occurrence.Start), l.Kind, l.Rule?.Name, (int)l.Time.TotalMinutes)));
        Assert.Equal(TimeSpan.FromHours(3), lines[3].Of);
        Assert.Equal(Hours.For(b.T, week, ada.Id).Total, lines.Aggregate(TimeSpan.Zero, (a, l) => a + l.Time));
        // Somebody in part of the class earns on their part, and the line says what it was taken of.
        var hers = Hours.Lines(b.T, week, bo.Id);
        Assert.Equal([(HoursLineKind.Teaching, 60), (HoursLineKind.Rule, 30)], hers.Select(l => (l.Kind, (int)l.Time.TotalMinutes)));
        Assert.Equal(TimeSpan.FromHours(1), hers[1].Of);
    }

    [Fact]
    public void ACancelledClassAndAGapAwaitingCoverAreNobodysLine()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var gap = b.Session(Monday, "09:00", "11:00", null, null, null, In(ada));
        b.Away(ada, Monday);
        var cancelled = b.Session(Monday.AddDays(1), "09:00", "11:00", null, null, null, In(bo));
        b.Put(cancelled with { Cancelled = true });
        b.Session(Monday.AddDays(2), "09:00", "10:00", null, null, null, In(bo));
        var week = Occurrences.HappeningInWeek(b.T, Monday);

        Assert.Contains(week, o => o.Session.Id == gap.Id && o.NeedsCover);
        Assert.Empty(Hours.Lines(b.T, week, ada.Id));
        Assert.Equal(Monday.AddDays(2), Assert.Single(Hours.Lines(b.T, week, bo.Id)).Date);
    }
}
