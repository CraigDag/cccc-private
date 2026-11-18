using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// Enough facilitators for a class's students, one to every so many (<see cref="Staffing"/>): a ring when the class is
/// short of them, cover when an absence nobody covers yet makes it short. Every date is fixed.
/// </summary>
public class StaffingTests
{
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Today = Monday.AddDays(-7);
    private static readonly TimeOnly Early = new(7, 0);

    /// <summary>A group of so many students, and a class for it with one facilitator to every <paramref name="each"/>.</summary>
    private static (Build B, Group Group, Class Class, Room Room, Facilitator Ada, Facilitator Bo, Facilitator Cleo) School(int students, int? each)
    {
        var b = new Build();
        var g = b.Group("BA 1");
        for (int i = 0; i < students; i++) b.Student($"Student {i + 1:00}", g);
        var c = b.Class("Trampoline");
        c = c with { StudentsEach = each };
        b.T = b.T with { Classes = b.T.Classes.Put(c) };
        return (b, g, c, b.Room("Gym"), b.Facilitator("Ada"), b.Facilitator("Bo"), b.Facilitator("Cleo"));
    }

    private static void StudentAway(Build b, Group g, int which, DateOnly date) =>
        b.T = b.T with
        {
            Absences = b.T.Absences.Put(new Absence
            {
                Person = AbsentPerson.Student, PersonId = b.T.Students.Where(s => s.GroupId == g.Id).OrderBy(s => s.Name).ElementAt(which).Id,
                From = date, To = date,
            }),
        };

    [Fact]
    public void OneFacilitatorToEverySoManyStudentsWantsEnoughForThemAllLessThoseRecordedAway()
    {
        var (b, g, c, gym, ada, bo, cleo) = School(14, 6);
        var two = b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(g.Id), gym, c, In(ada), In(bo));
        Shortfall? Short() => Staffing.Short(b.T, Occurrences.Resolve(b.T, b.T.Sessions[two.Id]!));
        Assert.Equal(new Shortfall(14, 6, 3, 2), Short());
        // Three is enough for fourteen, and two for twelve, to the student.
        b.Put(two with { Facilitators = [In(ada), In(bo), In(cleo)] });
        Assert.Null(Short());
        b.Put(two);
        StudentAway(b, g, 0, Tuesday);
        Assert.Equal(new Shortfall(13, 6, 3, 2), Short());
        StudentAway(b, g, 1, Tuesday);
        Assert.Null(Short());
        // A student away another day is on it that day.
        StudentAway(b, g, 2, Tuesday.AddDays(1));
        Assert.Null(Short());
        Assert.Equal(12, Staffing.Students(b.T, b.T.Sessions[two.Id]!));
        Assert.Equal((1, 2, 3, 3), (Staffing.Wanted(1, 6), Staffing.Wanted(7, 6), Staffing.Wanted(13, 6), Staffing.Wanted(18, 6)));
    }

    [Fact]
    public void AClassThatSaysNoNumberOrHasNobodyOnItWantsNothing()
    {
        var (b, g, c, gym, ada, _, _) = School(14, null);
        var s = b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(g.Id), gym, c, In(ada));
        Assert.Null(Staffing.Each(b.T, s));
        Assert.Null(Staffing.Short(b.T, Occurrences.Resolve(b.T, s)));
        Assert.DoesNotContain(Clashes.ForWeek(b.T, Tuesday), x => x.Kind == ClashKind.ShortStaffed);
        // Nought is no number, as an empty box is.
        b.T = b.T with { Classes = b.T.Classes.Put(c with { StudentsEach = 0 }) };
        Assert.Null(Staffing.Each(b.T, s));
        b.T = b.T with { Classes = b.T.Classes.Put(c with { StudentsEach = 6 }) };
        Assert.Equal(6, Staffing.Each(b.T, s));
        // A class for the whole building has nobody on it, so nobody to count; nor has one nobody is enrolled in.
        var everyone = b.Session(Tuesday, "13:00", "14:00", Audience.Everyone, gym, c);
        Assert.Null(Staffing.Short(b.T, Occurrences.Resolve(b.T, everyone)));
        var empty = b.Group("BA 2");
        Assert.Null(Staffing.Short(b.T, Occurrences.Resolve(b.T, b.Session(Tuesday, "15:00", "16:00", Audience.ForGroup(empty.Id), gym, c))));
        // A cancelled class happens to nobody.
        var cancelled = b.Session(Tuesday, "16:00", "17:00", Audience.ForGroup(g.Id), gym, c) with { Cancelled = true };
        b.Put(cancelled);
        Assert.Null(Staffing.Short(b.T, Occurrences.Resolve(b.T, cancelled)));
    }

    [Fact]
    public void TheFewestInTheRoomAtOnceAreWhatCountsEachForTheirOwnPartOfIt()
    {
        var (b, g, c, gym, ada, bo, cleo) = School(7, 6);
        var s = b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(g.Id), gym, c, In(ada), In(bo, from: "10:00"));
        // Ada alone until ten, with seven students: one short then.
        Assert.Equal(new Shortfall(7, 6, 2, 1), Staffing.Short(b.T, Occurrences.Resolve(b.T, s)));
        Assert.Equal(1, Staffing.Fewest(s, [], [ada.Id, bo.Id]));
        // Cleo until ten makes two all the way through; so does Bo there throughout.
        b.Put(s with { Facilitators = [In(ada), In(bo, from: "10:00"), In(cleo, until: "10:00")] });
        Assert.Null(Staffing.Short(b.T, Occurrences.Resolve(b.T, b.T.Sessions[s.Id]!)));
        Assert.Equal(2, Staffing.Fewest(b.T.Sessions[s.Id]!, [], [ada.Id, bo.Id, cleo.Id]));
        // Bo going at ten leaves Ada alone after it: there from the start together, one short from ten.
        var early = s with { Facilitators = [In(ada), In(bo, until: "10:00")] };
        b.Put(early);
        Assert.Equal(new Shortfall(7, 6, 2, 1), Staffing.Short(b.T, Occurrences.Resolve(b.T, b.T.Sessions[s.Id]!)));
        Assert.Equal(1, Staffing.Fewest(early, [], [ada.Id, bo.Id]));
        // Two who never overlap are one at a time.
        Assert.Equal(1, Staffing.Fewest(s with { Facilitators = [In(ada, until: "10:00"), In(bo, from: "10:00")] }, [], [ada.Id, bo.Id]));
        Assert.Equal(0, Staffing.Fewest(s, [], []));
    }

    [Fact]
    public void ShortOnPaperIsARingThatMovingItCannotPutRight()
    {
        var (b, g, c, gym, ada, bo, _) = School(14, 6);
        var s = b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(g.Id), gym, c, In(ada), In(bo));
        var ring = Assert.Single(Clashes.ForWeek(b.T, Tuesday), x => x.Kind == ClashKind.ShortStaffed);
        Assert.Equal("\"Trampoline\" has 14 students and 2 facilitators: one to every 6 wants 3 (Tue 09:00–11:00)", ring.Description);
        Assert.Equal([s.Id], ring.SessionIds);
        Assert.True(ring.IsRing);
        Assert.False(ring.MovingCanFix);
        // Short on paper is not why a class needs cover: it runs, so nothing is left short by an absence.
        Assert.Null(Staffing.LeftShort(b.T, Occurrences.Resolve(b.T, s)));
        Assert.Contains(s.Id, Clashes.RingedSessions(Clashes.ForWeek(b.T, Tuesday)));
        // The facilitators go with it wherever it goes, so no way out is offered for it.
        Assert.Empty(WaysOut.For(b.T, s.Id, Today, Early, EventViewer.Administrator));
        // Nor does it stand against every time Find a time finds for it.
        var ask = new OpeningAsk { Class = s, Spaces = [gym.Id], From = Tuesday, To = Tuesday, DayStart = new(9, 0), DayEnd = new(13, 0) };
        var found = Openings.Find(b.T, ask, Today, Early, EventViewer.Administrator).Openings;
        Assert.NotEmpty(found);
        Assert.All(found, o => Assert.DoesNotContain(o.Notes, n => n.Text.Contains("one to every")));

        // In the Gym with another class too, the ways out of that are offered all the same, and take it away from it.
        var other = b.Session(Tuesday, "10:00", "12:00", Audience.ForGroup(b.Group("BA 2").Id), gym, b.Class("Juggling"), In(b.Facilitator("Dag")));
        b.Room("Studio");
        var ways = WaysOut.For(b.T, s.Id, Today, Early, EventViewer.Administrator);
        Assert.NotEmpty(ways);
        Assert.Contains(Clashes.ForWeek(b.T, Tuesday), x => x.Kind == ClashKind.RoomDoubleBooked && x.SessionIds.Contains(other.Id));
        Assert.All(ways, w => Assert.DoesNotContain(WaysOut.InClash(b.T with { Sessions = b.T.Sessions.Put(w.Moved) }, w.Moved), x => x.Kind == ClashKind.RoomDoubleBooked));
    }

    [Fact]
    public void SomebodyAwayLeavingTooFewNeedsCoverSaysWhyAndCoverPutsItRight()
    {
        var (b, g, c, gym, ada, bo, cleo) = School(10, 6);
        var s = b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(g.Id), gym, c, In(ada), In(bo));
        Occurrence Now() => Occurrences.Resolve(b.T, b.T.Sessions[s.Id]!);
        Assert.Equal(OccurrenceState.Runs, Now().State);
        b.Away(bo, Tuesday);
        var gap = Now();
        Assert.Equal(OccurrenceState.NeedsCover, gap.State);
        Assert.Equal([ada.Id], gap.InTheRoom);
        Assert.Equal([bo.Id], gap.AwayIds);
        Assert.Equal(new Shortfall(10, 6, 2, 1), Staffing.LeftShort(b.T, gap));
        Assert.Equal([s.Id], Cover.StillNeeded(b.T, Tuesday, Today).Select(o => o.Session.Id));
        var week = Clashes.ForWeek(b.T, Tuesday);
        var cover = Assert.Single(week, x => x.Kind == ClashKind.NeedsCover);
        Assert.Equal("\"Trampoline\" needs cover — Bo away, leaving 1 facilitator for 10 students, one to every 6 (Tue 09:00–11:00)", cover.Description);
        // Said once, as cover, which is what puts it right: never a ring as well.
        Assert.DoesNotContain(week, x => x.Kind == ClashKind.ShortStaffed);
        // Ada is in the room, so her hours count it; and the students', as ever for a class awaiting cover.
        Assert.Equal(TimeSpan.FromHours(2), Hours.ByFacilitator(b.T, Occurrences.HappeningInWeek(b.T, Tuesday))[ada.Id].Teaching);

        // Cleo covering, it runs with two, and nothing is said.
        b.Put(b.T.Sessions[s.Id]! with { CoverIds = [cleo.Id] });
        Assert.Equal(OccurrenceState.Runs, Now().State);
        Assert.Null(Staffing.Short(b.T, Now()));
        Assert.DoesNotContain(Clashes.ForWeek(b.T, Tuesday), x => x.Kind is ClashKind.NeedsCover or ClashKind.ShortStaffed);

        // Without a number for its class, Ada teaching it on her own is enough, as it always was.
        b.Put(b.T.Sessions[s.Id]! with { CoverIds = [] });
        b.T = b.T with { Classes = b.T.Classes.Put(c with { StudentsEach = null }) };
        Assert.Equal(OccurrenceState.Runs, Now().State);
        // And six to a facilitator is enough for six.
        b.T = b.T with { Classes = b.T.Classes.Put(c with { StudentsEach = 10 }) };
        Assert.Equal(OccurrenceState.Runs, Now().State);
    }

    [Fact]
    public void CoverIsNotOfferedToSomebodyInTheRoomForAllOfItAlreadyButIsToOneThereForPart()
    {
        var (b, g, c, gym, ada, bo, cleo) = School(10, 6);
        var s = b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(g.Id), gym, c, In(ada), In(bo));
        b.Away(bo, Tuesday);
        var gap = Occurrences.Resolve(b.T, b.T.Sessions[s.Id]!);
        Assert.True(gap.NeedsCover);
        // Ada is there for all of it already: covering it herself would leave it as short as it is.
        var offered = Cover.Ranked(b.T, gap).Select(x => x.Person.Id).ToList();
        Assert.DoesNotContain(ada.Id, offered);
        Assert.Equal([cleo.Id], offered);
        // Ada until ten and Bo from ten, Bo away, leaves ten to eleven with nobody: Ada can stay on for it.
        b.Put(s with { Facilitators = [In(ada, until: "10:00"), In(bo, from: "10:00")] });
        var part = Occurrences.Resolve(b.T, b.T.Sessions[s.Id]!);
        Assert.True(part.NeedsCover);
        Assert.Contains(Cover.Ranked(b.T, part), x => x.Person.Id == ada.Id && x.IsFree);
        // Bo until ten and Cleo from ten both away: Ada, there for Bo's part, can still stay on for Cleo's.
        b.Away(cleo, Tuesday);
        b.Put(s with { Facilitators = [In(ada, until: "10:00"), In(bo, until: "10:00"), In(cleo, from: "10:00")] });
        var two = Occurrences.Resolve(b.T, b.T.Sessions[s.Id]!);
        Assert.Equal(2, two.AwayIds.Count);
        Assert.True(two.NeedsCover);
        Assert.Contains(Cover.Ranked(b.T, two), x => x.Person.Id == ada.Id);
    }

    [Fact]
    public void NobodyLeftIsSaidAsEverAndShortOnPaperAsWellIsSaidOnceCoverIsFound()
    {
        var (b, g, c, gym, ada, bo, cleo) = School(14, 6);
        var s = b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(g.Id), gym, c, In(ada));
        b.Away(ada, Tuesday);
        var cover = Assert.Single(Clashes.ForWeek(b.T, Tuesday), x => x.Kind == ClashKind.NeedsCover);
        Assert.Equal("\"Trampoline\" needs cover — Ada away (Tue 09:00–11:00)", cover.Description);
        Assert.Null(Staffing.LeftShort(b.T, Occurrences.Resolve(b.T, s)));

        // Two on it for fourteen, Bo away: cover first; covered, still short of the three it wants, so a ring.
        b.Put(s with { Facilitators = [In(ada), In(bo)] });
        b.T = b.T with { Absences = EntitySet<Absence>.Of([]) };
        b.Away(bo, Tuesday);
        var week = Clashes.ForWeek(b.T, Tuesday);
        Assert.Contains(week, x => x.Kind == ClashKind.NeedsCover);
        Assert.DoesNotContain(week, x => x.Kind == ClashKind.ShortStaffed);
        b.Put(b.T.Sessions[s.Id]! with { CoverIds = [cleo.Id] });
        week = Clashes.ForWeek(b.T, Tuesday);
        Assert.DoesNotContain(week, x => x.Kind == ClashKind.NeedsCover);
        Assert.Equal("\"Trampoline\" has 14 students and 2 facilitators: one to every 6 wants 3 (Tue 09:00–11:00)", Assert.Single(week, x => x.Kind == ClashKind.ShortStaffed).Description);
    }
}
