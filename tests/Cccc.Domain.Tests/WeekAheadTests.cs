using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>Next week, from Thursday on, for whoever plans the timetable. Every date is fixed.</summary>
public class WeekAheadTests
{
    private static readonly DateOnly Next = Monday.AddDays(7);
    private static readonly DateOnly Thursday = Monday.AddDays(3);

    private static WeekAheadView? Ahead(Build b, DateOnly today) =>
        WeekAhead.For(b.T, today, EventViewer.Administrator, RegisterReader.Administrator);

    [Fact]
    public void NextWeekIsLookedAtFromThursdayThroughTheWeekendAndNeverEarlier()
    {
        Assert.Equal([null, null, null, Next, Next, Next, Next], Enumerable.Range(0, 7).Select(i => WeekAhead.Monday(Monday.AddDays(i))));
        Assert.Equal(Next.AddDays(7), WeekAhead.Monday(Next.AddDays(3)));
        // A blank next week is not said on a Wednesday, and is from the Thursday.
        var b = new Build();
        b.Session(Monday, "09:00", "10:00");
        Assert.Null(Ahead(b, Monday.AddDays(2)));
        Assert.Equal(Next, Ahead(b, Thursday)!.Week.Monday);
        Assert.Equal(Next, Ahead(b, Monday.AddDays(6))!.Week.Monday);
    }

    [Fact]
    public void ABlankWeekWithSchoolInItIsSaidWithTheWeekToCopyAndOneWithNoSchoolIsNot()
    {
        var b = new Build();
        b.Session(Monday.AddDays(-7), "09:00", "10:00");
        b.Session(Monday, "09:00", "10:00");
        var blank = Ahead(b, Thursday)!;
        Assert.True(blank.Week.Blank);
        Assert.False(blank.Ready);
        // The last week built is the one to copy onto it.
        Assert.Equal(Monday, blank.Week.ComparedWith);
        Assert.Empty(blank.Clashes);
        Assert.Empty(blank.Cover);

        // The building closed every school day of it: nothing to plan, so nothing said, whatever is written in it.
        var closed = new Build { T = b.T };
        closed.Closure(new Closure { Name = "Winter break", From = Next, To = Next.AddDays(4) });
        Assert.Null(Ahead(closed, Thursday));
        closed.Session(Next, "09:00", "10:00");
        Assert.Null(Ahead(closed, Thursday));
        // Closed on two closures that between them shut every school day, the same.
        var twice = new Build { T = b.T };
        twice.Closure(new Closure { Name = "Easter", From = Next, To = Next.AddDays(1) });
        twice.Closure(new Closure { Name = "Study days", From = Next.AddDays(2), To = Next.AddDays(4) });
        Assert.Null(Ahead(twice, Thursday));
        // Closed on some days only, it is still a week to plan, and says which.
        var some = new Build { T = b.T };
        some.Closure(new Closure { Name = "Ascension Day", From = Next.AddDays(3), To = Next.AddDays(3) });
        Assert.Equal([Next.AddDays(3)], Ahead(some, Thursday)!.Week.ClosedDays.Select(d => d.Day));
        // A group closed all week is not the building: a week to plan for everybody else.
        var group = new Build { T = b.T };
        var ba1 = group.Group("BA 1");
        group.Closure(new Closure { Name = "Tour", From = Next, To = Next.AddDays(4), Scope = ClosureScope.Groups, GroupIds = [ba1.Id] });
        Assert.True(Ahead(group, Thursday)!.Week.Blank);

        // Outside every semester and blank: no school, so nothing said; a class written in it is said all the same.
        var after = new Build { T = b.T with { Semesters = EntitySet<Semester>.Of([new Semester { Name = "Spring", StartMonday = Monday.AddDays(-28), WeekCount = 5 }]) } };
        Assert.Null(Ahead(after, Thursday));
        after.Session(Next.AddDays(1), "09:00", "10:00");
        Assert.Equal(1, Ahead(after, Thursday)!.Week.Happening);
        // With no semesters at all, every week is a school week.
        var none = new Build { T = b.T with { Semesters = EntitySet<Semester>.Of([]) } };
        Assert.True(Ahead(none, Thursday)!.Week.Blank);
        // A semester starting with it makes it a week of school.
        var starts = new Build { T = b.T with { Semesters = EntitySet<Semester>.Of([new Semester { Name = "Late", StartMonday = Next, WeekCount = 4 }]) } };
        Assert.True(Ahead(starts, Thursday)!.Week.Blank);
    }

    [Fact]
    public void ABuiltWeekSaysItsRingedClashesAndTheClassesNeedingCoverInThePlansOrderAndIsReadyWithoutThem()
    {
        var b = new Build();
        var ba1 = b.Group("BA 1");
        var gym = b.Room("Gym");
        var studio = b.Room("Studio");
        var ada = b.Facilitator("Ada", max: 1);
        var bo = b.Facilitator("Bo");
        var cleo = b.Facilitator("Cleo");
        var one = Audience.ForGroup(ba1.Id);
        b.Session(Monday, "09:00", "10:00", one, gym, null, In(ada));
        var tuesday = b.Session(Next.AddDays(1), "09:00", "11:00", one, gym, null, In(ada));
        var friday = b.Session(Next.AddDays(4), "13:00", "14:00", one, studio, null, In(bo));
        var ready = Ahead(b, Thursday)!;
        // Ada over her one hour a week is a limit, said elsewhere: never a ring, so never one of these.
        Assert.Contains(Clashes.ForWeek(b.T, Next), c => c.Kind == ClashKind.FacilitatorOverHours);
        Assert.Empty(ready.Clashes);
        Assert.True(ready.Ready);
        Assert.Equal((false, 2, (DateOnly?)Monday), (ready.Week.Blank, ready.Week.Happening, ready.Week.ComparedWith));

        // Bo away next Friday, and Ada next Tuesday: both need cover, Tuesday's first, as the plan lists them.
        b.Away(bo, Next.AddDays(4));
        b.Away(ada, Next.AddDays(1));
        var cover = Ahead(b, Thursday)!;
        Assert.Equal([tuesday.Id, friday.Id], cover.Cover.Select(o => o.Session.Id));
        Assert.False(cover.Ready);
        Assert.Equal(cover.Week.CoverNeeded, cover.Cover.Count);

        // Covered, Tuesday needs nobody; another class for BA 1 in the studio on Tuesday rings, as the week's issues count it.
        b.Put(tuesday with { CoverIds = [cleo.Id] });
        b.Session(Next.AddDays(1), "10:00", "12:00", one, studio, null, In(bo));
        var clash = Ahead(b, Thursday)!;
        Assert.Equal([friday.Id], clash.Cover.Select(o => o.Session.Id));
        Assert.NotEmpty(clash.Clashes);
        Assert.All(clash.Clashes, c => Assert.True(c.IsRing));
        Assert.Equal(clash.Week.Clashes, clash.Clashes.Count);
        Assert.Equal(Clashes.ForWeek(b.T, Next).Where(c => c.IsRing).Select(c => c.Description), clash.Clashes.Select(c => c.Description));
        Assert.False(clash.Ready);

        // Friday covered too, the clashes alone keep it from being ready; another on Wednesday, the Gym twice over, is said
        // after Tuesday's, as the plan lists them.
        b.Put(friday with { CoverIds = [cleo.Id] });
        var ba2 = b.Group("BA 2");
        b.Session(Next.AddDays(2), "09:00", "10:00", one, gym, null, In(ada));
        b.Session(Next.AddDays(2), "09:00", "10:00", Audience.ForGroup(ba2.Id), gym, null, In(bo));
        var two = Ahead(b, Thursday)!;
        Assert.Empty(two.Cover);
        var said = two.Clashes.Select(c => c.Description).ToList();
        Assert.True(said.Count >= 2 && said.Distinct().Count() == said.Count, string.Join(" / ", said));
        Assert.Equal(Clashes.ForWeek(b.T, Next).Where(c => c.IsRing).Select(c => c.Description), said);
        Assert.Contains("Gym", said[^1]);
        Assert.False(two.Ready);
    }

    [Fact]
    public void TheSampleSchoolsNextWeekIsBuiltFromItsThursdayAndTheWeekAfterIsBlankFromTheNext()
    {
        var monday = new DateOnly(2026, 11, 16);
        var t = SampleSchool.Create(monday, monday.AddDays(3));
        var next = WeekAhead.For(t, monday.AddDays(3), EventViewer.Administrator, RegisterReader.Administrator)!;
        Assert.Equal(monday.AddDays(7), next.Week.Monday);
        Assert.False(next.Week.Blank);
        // Next week drops this week's two guest workshops.
        Assert.Equal(monday, next.Week.ComparedWith);
        Assert.Equal(2, next.Week.Changes.Count);
        var after = WeekAhead.For(SampleSchool.Create(monday, monday.AddDays(10)), monday.AddDays(10), EventViewer.Administrator, RegisterReader.Administrator)!;
        Assert.True(after.Week.Blank);
        Assert.Equal(monday.AddDays(7), after.Week.ComparedWith);
    }
}
