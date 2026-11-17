using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>Reports ▸ Facilitator hours: the average week, the weeks over a limit, the quietest and the busiest.</summary>
public class HoursOverWeeksTests
{
    private static readonly DateOnly W1 = Monday, W2 = Monday.AddDays(7), W3 = Monday.AddDays(14), W4 = Monday.AddDays(21), W5 = Monday.AddDays(28);

    [Fact]
    public void TheAverageIsOverTheWeeksTheSchoolRanAndAWeekItDidNotRunIsNeverTheQuietest()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada", max: 5);
        var bo = b.Facilitator("Bo");
        b.Session(W1, "09:00", "15:00", who: In(ada));                 // 6h: over her 5h limit
        b.Session(W2.AddDays(1), "09:00", "12:00", who: In(ada));      // the week the building is closed
        b.Closure(new Closure { Name = "Winter break", From = W2, To = W2.AddDays(6), Scope = ClosureScope.WholeBuilding });
        b.Session(W3, "09:00", "14:00", who: In(ada));                 // 5h: at her limit, not over it
        b.Session(W3.AddDays(2), "13:00", "14:00", who: In(bo));
        // W4: nobody has timetabled anything.
        b.Session(W5.AddDays(3), "10:00", "11:00", who: In(bo));       // the school runs; Ada has nothing

        var rows = Hours.OverWeeks(b.T, [W5, W1, W2, W3, W4]);
        var a = rows.Single(r => r.Person.Id == ada.Id);
        Assert.Equal([W1, W2, W3, W4, W5], a.Weeks.Select(w => w.Monday));
        Assert.Equal([true, false, true, false, true], a.Weeks.Select(w => w.SchoolRan));
        Assert.Equal(3, a.WeeksRan);
        Assert.Equal(TimeSpan.FromHours(11), a.Total);
        Assert.Equal(TimeSpan.FromMinutes(220), a.Average);            // 11h over the three weeks that ran, not over five
        Assert.Equal(1, a.WeeksOver);                                  // the week at the limit is not over it
        Assert.Equal(W5, a.Quietest!.Monday);                          // nothing in a week the school ran; never the closed week
        Assert.Equal(TimeSpan.Zero, a.Quietest.Total);
        Assert.Equal(W1, a.Busiest!.Monday);

        var o = rows.Single(r => r.Person.Id == bo.Id);
        Assert.Equal(TimeSpan.FromMinutes(40), o.Average);
        Assert.Equal(W1, o.Quietest!.Monday);
        Assert.Equal(W3, o.Busiest!.Monday);                           // 1h twice: the earliest of equals
        Assert.Equal(0, o.WeeksOver);                                  // no limit, so never over it
    }

    [Fact]
    public void WithNoHoursThereIsNoBusiestWeekAndWithNoWeekRunNoAverageOrQuietest()
    {
        var b = new Build();
        var cleo = b.Facilitator("Cleo", max: 10);
        var dag = b.Facilitator("Dag");
        b.Session(W1, "09:00", "10:00", who: In(dag));

        var rows = Hours.OverWeeks(b.T, [W1, W2]);
        var c = rows.Single(r => r.Person.Id == cleo.Id);
        Assert.Null(c.Busiest);
        Assert.Equal(W1, c.Quietest!.Monday);
        Assert.Equal(TimeSpan.Zero, c.Average);

        var none = Hours.OverWeeks(b.T, [W2]).Single(r => r.Person.Id == dag.Id);
        Assert.Equal(0, none.WeeksRan);
        Assert.Null(none.Average);
        Assert.Null(none.Quietest);
        Assert.Null(none.Busiest);
    }

    [Fact]
    public void ATimeRulesCreditIsInTheTotalAndItsOwnColumnAndCanTakeAWeekOverTheLimit()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada", max: 3);
        var creation = b.Class("Creation");
        var prep = new TimeRule { Name = "Prep", Kind = TimeRuleKind.Preparation, Percent = 50, ClassIds = [creation.Id] };
        b.TimeRule(prep);
        b.Session(W1, "09:00", "11:00", null, null, creation, In(ada));      // 2h teaching and 1h prep: at the limit
        b.Session(W2, "09:00", "11:30", null, null, creation, In(ada));      // 2h30 teaching under it, 1h15 prep takes it over

        var a = Assert.Single(Hours.OverWeeks(b.T, [W1, W2]));
        Assert.Equal(TimeSpan.FromMinutes(270), a.Teaching);
        Assert.Equal(TimeSpan.FromMinutes(135), a.ForRule(prep.Id));
        Assert.Equal(TimeSpan.Zero, a.ForRule(Guid.NewGuid()));
        Assert.Equal(TimeSpan.FromMinutes(405), a.Total);
        Assert.Equal(1, a.WeeksOver);
        Assert.Equal(W2, a.Busiest!.Monday);
    }

    [Fact]
    public void TheSchoolsOwnComeBeforeGuestsEachInDanishNameOrder()
    {
        var b = new Build();
        b.T = b.T with { Facilitators = b.T.Facilitators.Put(new Facilitator { Name = "Bente Gæst", IsGuest = true }) };
        b.Facilitator("Øystein");
        b.Facilitator("Zita");
        b.Facilitator("Aaron");
        Assert.Equal(["Zita", "Øystein", "Aaron", "Bente Gæst"], Hours.OverWeeks(b.T, [W1]).Select(r => r.Person.Name));
    }
}
