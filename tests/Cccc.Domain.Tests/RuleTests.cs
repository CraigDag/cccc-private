using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

public class OccurrenceTests
{
    [Fact]
    public void AClosureShutsASessionBeforeAnythingElseIsAsked()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var s = b.Session(Monday, "09:00", "10:00", null, null, null, In(ada));
        b.Put(s with { Cancelled = true });
        b.Away(ada, Monday);
        b.Closure(new Closure { Name = "Holiday", From = Monday, To = Monday });
        var o = Occurrences.Resolve(b.T, b.T.Sessions[s.Id]!);
        Assert.Equal(OccurrenceState.Closed, o.State);
        Assert.Equal("Holiday", o.ClosedBy!.Name);
    }

    [Fact]
    public void AGroupClosureShutsOnlyASessionWhoseEveryGroupItNames()
    {
        var b = new Build();
        var g1 = b.Group("One");
        var g2 = b.Group("Two");
        var onlyOne = b.Session(Monday, "09:00", "10:00", Audience.ForGroup(g1.Id));
        var both = b.Session(Monday, "11:00", "12:00", Audience.ForGroups([g1.Id, g2.Id]));
        var all = b.Session(Monday, "13:00", "14:00", Audience.Everyone);
        b.Closure(new Closure { From = Monday, To = Monday, Scope = ClosureScope.Groups, GroupIds = [g1.Id] });
        Assert.Equal(OccurrenceState.Closed, Occurrences.Resolve(b.T, onlyOne).State);
        Assert.Equal(OccurrenceState.Runs, Occurrences.Resolve(b.T, both).State);
        Assert.Equal(OccurrenceState.Runs, Occurrences.Resolve(b.T, all).State);
    }

    [Fact]
    public void ASessionNeedsCoverOnlyWhenEveryAssignedPersonIsAway()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var s = b.Session(Monday, "09:00", "11:00", null, null, null, In(ada), In(bo));
        b.Away(ada, Monday);
        var o = Occurrences.Resolve(b.T, s);
        Assert.Equal(OccurrenceState.Runs, o.State);
        Assert.Equal([bo.Id], o.InTheRoom);
        b.Away(bo, Monday);
        Assert.Equal(OccurrenceState.NeedsCover, Occurrences.Resolve(b.T, s).State);
    }

    [Fact]
    public void APartTimePersonPresentLeavesAGapWhereTheirPartDoesNotReach()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var s = b.Session(Monday, "13:00", "15:00", null, null, null, In(ada), In(bo, from: "14:00"));
        b.Away(ada, Monday);
        Assert.Equal(OccurrenceState.NeedsCover, Occurrences.Resolve(b.T, s).State);
    }

    [Fact]
    public void AnAbsenceWindowThatMissesThePersonsPartDoesNotTakeThemOut()
    {
        var b = new Build();
        var bo = b.Facilitator("Bo");
        var s = b.Session(Monday, "13:00", "15:00", null, null, null, In(bo, from: "14:00"));
        b.Away(bo, Monday, "12:00", "14:00");
        Assert.Equal(OccurrenceState.Runs, Occurrences.Resolve(b.T, s).State);
        b.Away(bo, Monday, "14:30", "16:00");
        Assert.Equal(OccurrenceState.NeedsCover, Occurrences.Resolve(b.T, s).State);
    }

    [Fact]
    public void CoverSubstitutesTheAbsentAndKeepsTheCoTeacher()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var cleo = b.Facilitator("Cleo");
        var s = b.Session(Monday, "09:00", "11:00", null, null, null, In(ada), In(bo));
        b.Away(ada, Monday);
        b.Away(bo, Monday);
        b.Put(s with { CoverIds = [cleo.Id] });
        var o = Occurrences.Resolve(b.T, b.T.Sessions[s.Id]!);
        Assert.Equal(OccurrenceState.Runs, o.State);
        Assert.True(o.Covered);
        Assert.Equal([cleo.Id], o.InTheRoom);
    }
}

public class HoursTests
{
    [Fact]
    public void HoursGoToWhoeverIsInTheRoomAndAGapCountsForNobody()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var cleo = b.Facilitator("Cleo");
        var s = b.Session(Monday, "09:00", "11:00", null, null, null, In(ada));
        b.Away(ada, Monday);
        var week = Occurrences.HappeningInWeek(b.T, Monday);
        Assert.Equal(TimeSpan.Zero, Hours.Teaching(week, ada.Id));
        b.Put(s with { CoverIds = [cleo.Id] });
        week = Occurrences.HappeningInWeek(b.T, Monday);
        Assert.Equal(TimeSpan.FromHours(2), Hours.Teaching(week, cleo.Id));
        Assert.Equal(TimeSpan.Zero, Hours.Teaching(week, ada.Id));
    }

    [Fact]
    public void ACoTaughtSessionCountsInFullForBoth()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        b.Session(Monday, "09:00", "11:00", null, null, null, In(ada), In(bo));
        var week = Occurrences.HappeningInWeek(b.T, Monday);
        Assert.Equal(TimeSpan.FromHours(2), Hours.Teaching(week, ada.Id));
        Assert.Equal(TimeSpan.FromHours(2), Hours.Teaching(week, bo.Id));
    }

    [Fact]
    public void APersonInPartOfASessionCountsTheirPart()
    {
        var b = new Build();
        var bo = b.Facilitator("Bo");
        b.Session(Monday, "13:00", "15:00", null, null, null, In(bo, from: "14:00"));
        Assert.Equal(TimeSpan.FromHours(1), Hours.Teaching(Occurrences.HappeningInWeek(b.T, Monday), bo.Id));
    }

    [Fact]
    public void CoverTakesOnTheWindowOfThePersonReplaced()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var cleo = b.Facilitator("Cleo");
        var s = b.Session(Monday, "13:00", "15:00", null, null, null, In(ada, until: "14:00"), In(bo, from: "14:00"));
        b.Away(ada, Monday);
        b.Put(s with { CoverIds = [cleo.Id] });
        Assert.Equal(TimeSpan.FromHours(1), Hours.Teaching(Occurrences.HappeningInWeek(b.T, Monday), cleo.Id));
    }

    [Fact]
    public void PreparationIsAPercentagePerPersonAndNeverDivided()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var creation = b.Class("Creation");
        b.TimeRule(new TimeRule { Name = "Prep", Kind = TimeRuleKind.Preparation, Percent = 50, ClassIds = [creation.Id] });
        b.Session(Monday, "13:00", "16:00", null, null, creation, In(ada), In(bo));
        var week = Occurrences.HappeningInWeek(b.T, Monday);
        var split = Hours.For(b.T, week, ada.Id);
        Assert.Equal(TimeSpan.FromHours(3), split.Teaching);
        Assert.Equal(TimeSpan.FromMinutes(90), split.Added);
        Assert.Equal(TimeSpan.FromMinutes(90), Hours.For(b.T, week, bo.Id).Added);
    }

    [Fact]
    public void ABreakIsCreditedOncePerPersonPerDayWhenTheyTeachBeforeAndAfter()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var lunch = b.Class("Lunch", Workload.NotCounted);
        b.TimeRule(new TimeRule { Name = "Paid lunch", Kind = TimeRuleKind.Break, Minutes = 30, ClassIds = [lunch.Id] });
        b.Session(Monday, "12:00", "12:30", null, null, lunch);
        b.Session(Monday, "12:30", "13:00", null, null, lunch);      // a second lunch row the same day
        b.Session(Monday, "09:00", "12:00", null, null, null, In(ada));
        b.Session(Monday, "13:00", "15:00", null, null, null, In(ada));
        b.Session(Monday, "09:00", "11:00", null, null, null, In(bo));  // only before
        var week = Occurrences.HappeningInWeek(b.T, Monday);
        Assert.Equal(TimeSpan.FromMinutes(30), Hours.For(b.T, week, ada.Id).Added);
        Assert.Equal(TimeSpan.Zero, Hours.For(b.T, week, bo.Id).Added);
    }

    [Fact]
    public void AStudentsLoadCountsOnlyCountedClassesAndStillCountsASessionAwaitingCover()
    {
        var b = new Build();
        var g = b.Group("BA");
        var st = b.Student("Alba", g);
        var ada = b.Facilitator("Ada");
        var open = b.Class("Open", Workload.DropIn);
        var acro = b.Class("Acro");
        b.Session(Monday, "09:00", "11:00", Audience.ForGroup(g.Id), null, acro, In(ada));
        b.Session(Monday, "13:00", "15:00", Audience.ForGroup(g.Id), null, open, In(ada));
        b.Away(ada, Monday);
        var week = Occurrences.HappeningInWeek(b.T, Monday);
        Assert.Equal(TimeSpan.FromHours(2), Hours.StudentLoad(b.T, week, st.Id));
    }
}

public class RosterAndRegisterTests
{
    [Fact]
    public void TheRosterIsTheGroupsPlusAddedLessLeftOff()
    {
        var b = new Build();
        var g = b.Group("BA");
        var a = b.Student("A", g);
        var c = b.Student("C", g);
        var other = b.Student("X");
        var s = b.Session(Monday, "09:00", "10:00", Audience.ForGroup(g.Id)) with { AddedStudentIds = [other.Id], LeftOffStudentIds = [c.Id] };
        b.Put(s);
        Assert.Equal(new[] { a.Id, other.Id }.ToHashSet(), Roster.For(b.T, s).ToHashSet());
        Assert.Empty(Roster.For(b.T, s with { Audience = Audience.Everyone }));
    }

    [Fact]
    public void AFiledRegisterKeepsTheRosterItWasTakenAgainst()
    {
        var b = new Build();
        var g = b.Group("BA");
        var g2 = b.Group("Prep");
        var a = b.Student("A", g);
        var s = b.Session(Monday.AddDays(-7), "09:00", "10:00", Audience.ForGroup(g.Id));
        var r = Registers.File(b.T, s, [a.Id], [], "x", DateTimeOffset.UnixEpoch);
        b.T = b.T with { Registers = b.T.Registers.Put(r), Students = b.T.Students.Put(a with { GroupId = g2.Id }) };
        Assert.Empty(Roster.For(b.T, s));
        Assert.Equal([a.Id], Roster.ForRegister(b.T, s));
    }

    [Fact]
    public void ARegisterIsMissingOnlyWhenItRanHasPeopleAndIsPast()
    {
        var b = new Build();
        var g = b.Group("BA");
        b.Student("A", g);
        var past = b.Session(Monday.AddDays(-1), "09:00", "10:00", Audience.ForGroup(g.Id));
        var todays = b.Session(Monday, "09:00", "10:00", Audience.ForGroup(g.Id));
        var everyone = b.Session(Monday.AddDays(-2), "09:00", "10:00");
        var cancelled = b.Session(Monday.AddDays(-3), "09:00", "10:00", Audience.ForGroup(g.Id)) with { Cancelled = true };
        b.Put(cancelled);
        RegisterState State(Session s) => Registers.StateOf(b.T, Occurrences.Resolve(b.T, s), Today);
        Assert.Equal(RegisterState.Missing, State(past));
        Assert.Equal(RegisterState.ToCome, State(todays));
        Assert.Equal(RegisterState.NotRunning, State(everyone));
        Assert.Equal(RegisterState.NotRunning, State(cancelled));
        b.T = b.T with { Registers = b.T.Registers.Put(Registers.File(b.T, past, [], [], null, DateTimeOffset.UnixEpoch)) };
        Assert.Equal(RegisterState.Taken, State(past));   // nobody ticked is still a register
    }

    [Fact]
    public void OnlySomebodyInTheRoomOrAnAdministratorMayChangeARegister()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var cleo = b.Facilitator("Cleo");
        var other = b.Facilitator("Other");
        var s = b.Session(Monday, "09:00", "10:00", null, null, null, In(ada)) with { CoverIds = [cleo.Id] };
        Assert.True(Registers.MayChange(new RegisterReader(false, ada.Id), s));
        Assert.True(Registers.MayChange(new RegisterReader(false, cleo.Id), s));
        Assert.False(Registers.MayChange(new RegisterReader(false, other.Id), s));
        Assert.False(Registers.MayChange(new RegisterReader(false, null), s));
        Assert.True(Registers.MayChange(RegisterReader.Administrator, s));
    }

    [Fact]
    public void APersonsRegistersStillToTakeAreTheirOwnMissingOnesFromTheLastEightWeeks()
    {
        var b = new Build();
        var g = b.Group("BA");
        b.Student("A", g);
        var ada = b.Facilitator("Ada");
        var cleo = b.Facilitator("Cleo");
        var hers = b.Session(Monday.AddDays(-3), "09:00", "10:00", Audience.ForGroup(g.Id), null, null, In(ada));
        var covered = b.Session(Monday.AddDays(-10), "09:00", "10:00", Audience.ForGroup(g.Id), null, null, In(cleo)) with { CoverIds = [ada.Id] };
        b.Put(covered);
        var earliest = b.Session(Registers.EarlierFrom(Today), "09:00", "10:00", Audience.ForGroup(g.Id), null, null, In(ada));
        b.Session(Registers.EarlierFrom(Today).AddDays(-1), "09:00", "10:00", Audience.ForGroup(g.Id), null, null, In(ada));   // too long ago
        b.Session(Monday.AddDays(-1), "09:00", "10:00", Audience.ForGroup(g.Id), null, null, In(cleo));                         // somebody else's
        b.Session(Today, "08:00", "09:00", Audience.ForGroup(g.Id), null, null, In(ada));                                        // today: not missing yet
        var taken = b.Session(Monday.AddDays(-2), "09:00", "10:00", Audience.ForGroup(g.Id), null, null, In(ada));
        b.T = b.T with { Registers = b.T.Registers.Put(Registers.File(b.T, taken, [], [], null, DateTimeOffset.UnixEpoch)) };

        var ids = Registers.StillToTake(b.T, ada.Id, Today).Select(o => o.Session.Id).ToHashSet();
        Assert.Equal(new[] { hers.Id, covered.Id, earliest.Id }.ToHashSet(), ids);
        // Cleo's own: the class Ada covered stays Cleo's to put right too, and the other is hers alone.
        Assert.Equal(2, Registers.StillToTake(b.T, cleo.Id, Today).Count);
        Assert.Equal(Monday.AddDays(-56), Registers.EarlierFrom(Today.AddDays(6)));
    }

    [Fact]
    public void PresentAndExcusedNeverOverlap()
    {
        var r = new Register { PresentIds = [Guid.Empty], ExcusedIds = [Guid.Empty] }.Normalised();
        Assert.Empty(r.ExcusedIds);
    }
}

public class ClashTests
{
    [Fact]
    public void AWiderSessionOverAnarrowerOneIsAGroupClashEvenWithNoLanes()
    {
        var b = new Build();
        var g1 = b.Group("One");
        var g2 = b.Group("Two");
        b.Session(Monday, "12:00", "13:00", Audience.Everyone);
        b.Session(Monday, "12:30", "13:30", Audience.ForGroup(g1.Id));
        _ = g2;
        var clashes = Clashes.ForWeek(b.T, Monday);
        Assert.Contains(clashes, c => c.Kind == ClashKind.GroupDoubleBooked && c.Description.StartsWith("One"));
    }

    [Fact]
    public void RoomFacilitatorAndStudentClashesAreFound()
    {
        var b = new Build();
        var g = b.Group("BA");
        b.Student("A", g);
        var room = b.Room("Big top");
        var ada = b.Facilitator("Ada");
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(g.Id), room, null, In(ada));
        b.Session(Monday, "09:30", "10:30", Audience.ForGroup(g.Id), room, null, In(ada));
        var kinds = Clashes.ForWeek(b.T, Monday).Select(c => c.Kind).ToHashSet();
        Assert.Contains(ClashKind.RoomDoubleBooked, kinds);
        Assert.Contains(ClashKind.FacilitatorDoubleBooked, kinds);
        Assert.Contains(ClashKind.StudentDoubleBooked, kinds);
    }

    [Fact]
    public void PartsThatDoNotOverlapAreNotAFacilitatorClash()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        b.Session(Monday, "13:00", "15:00", null, null, null, In(ada, from: "14:00"));
        b.Session(Monday, "12:00", "14:00", null, null, null, In(ada));
        Assert.DoesNotContain(Clashes.ForWeek(b.T, Monday), c => c.Kind == ClashKind.FacilitatorDoubleBooked);
    }

    [Fact]
    public void ALongGapIsMeasuredFromTheLatestEndSoTwoClassesThatOverlapAreNoGapAtAll()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada") with { MaxGap = TimeSpan.FromHours(3) };
        b.T = b.T with { Facilitators = b.T.Facilitators.Put(ada) };
        b.Session(Monday, "09:00", "11:00", null, null, null, In(ada));
        b.Session(Monday, "10:00", "12:00", null, null, null, In(ada));     // overlaps: a clash, never a gap
        b.Session(Monday, "16:00", "17:00", null, null, null, In(ada));
        var gaps = Clashes.ForWeek(b.T, Monday).Where(c => c.Kind == ClashKind.FacilitatorLongGap).Select(c => c.Description).ToList();
        Assert.Equal(["Ada has a 4h gap Mon 12:00–16:00 (maximum 3h) — between \"Class\" and \"Class\""], gaps);
    }

    [Fact]
    public void OverTheLimitWarnsAndCountsTimeRules()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada", max: 3);
        var creation = b.Class("Creation");
        b.TimeRule(new TimeRule { Kind = TimeRuleKind.Preparation, Percent = 50, ClassIds = [creation.Id] });
        b.Session(Monday, "09:00", "11:30", null, null, creation, In(ada));   // 2h30 teaching under the 3h limit, 1h15 prep over it
        Assert.Contains(Clashes.ForWeek(b.T, Monday), c => c.Kind == ClashKind.FacilitatorOverHours);
    }
}

public class CoverRankingTests
{
    [Fact]
    public void EverybodyNotAwayIsOfferedFreeAndLightFirstGuestsLast()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var busy = b.Facilitator("Busy");
        var light = b.Facilitator("Light");
        var guest = b.Facilitator("Guest");
        b.T = b.T with { Facilitators = b.T.Facilitators.Put(guest with { IsGuest = true }) };
        var away = b.Facilitator("Away");
        var gap = b.Session(Monday, "09:00", "10:00", null, null, null, In(ada));
        b.Session(Monday, "09:00", "10:00", null, null, null, In(busy));
        b.Away(ada, Monday);
        b.Away(away, Monday);
        var ranked = Cover.Ranked(b.T, Occurrences.Resolve(b.T, gap)).Select(c => c.Person.Name).ToList();
        Assert.Equal(["Light", "Busy", "Guest"], ranked);
        _ = light;
    }
}

public class GuestAndEventTests
{
    [Fact]
    public void AGuestClassIsOneWhoseEveryGroupIsAGuestProgrammes()
    {
        var b = new Build();
        var visiting = b.Programme("Visiting", guest: true);
        var degree = b.Programme("Degree");
        var gv = b.Group("Troupe", visiting);
        var gd = b.Group("BA", degree);
        Assert.True(Guests.IsGuestOnly(b.T, b.Session(Monday, "09:00", "10:00", Audience.ForGroup(gv.Id))));
        Assert.False(Guests.IsGuestOnly(b.T, b.Session(Monday, "09:00", "10:00", Audience.ForGroups([gv.Id, gd.Id]))));
        Assert.False(Guests.IsGuestOnly(b.T, b.Session(Monday, "09:00", "10:00", Audience.Everyone)));
    }

    [Fact]
    public void AnAdministratorsOnlyEventIsSeenByAdministratorsAloneAndNeverPrinted()
    {
        var e = new SchoolEvent { Audience = EventAudience.Administrators };
        Assert.True(EventViewer.Administrator.Sees(e));
        Assert.False(EventViewer.Administrator.Prints(e));
        Assert.False(new EventViewer(false, true, false, false, false).Sees(e));
        var staff = new SchoolEvent { Audience = EventAudience.Staff };
        Assert.True(new EventViewer(false, true, false, false, false).Sees(staff));
        Assert.False(new EventViewer(false, false, true, false, false).Sees(staff));
        Assert.True(new EventViewer(false, false, false, false, true).Sees(staff));
    }

    [Fact]
    public void ARepeatAlwaysEnds()
    {
        var e = new SchoolEvent { Date = Monday, Repeat = new Repeat { Kind = RepeatKind.Daily } };
        Assert.Equal(EventSchedule.MaxOccurrences, EventSchedule.RuleDates(e).Count());
        var weekly = new SchoolEvent { Date = Monday, Repeat = new Repeat { Kind = RepeatKind.Weekly, Days = [DayOfWeek.Monday, DayOfWeek.Thursday], Count = 3 } };
        Assert.Equal([Monday, Monday.AddDays(3), Monday.AddDays(7)], EventSchedule.RuleDates(weekly));
    }

    [Fact]
    public void ADateSkippedOrClosedIsOffAndKeepsItsNumberSoTheDatesAfterItKeepTheirs()
    {
        var b = new Build();
        b.Closure(new Closure { Name = "Holiday", From = Monday.AddDays(14), To = Monday.AddDays(20) });
        var e = new SchoolEvent { Date = Monday, Repeat = new Repeat { Kind = RepeatKind.Weekly, Days = [DayOfWeek.Monday], Count = 4 },
                                  Skipped = [Monday.AddDays(7)], SkipClosures = true };
        Assert.Equal([(1, true), (2, false), (3, false), (4, true)], EventSchedule.EveryDate(b.T, e).Select(x => (x.Occurrence.Number, x.On)));
        Assert.Equal([1, 4], EventSchedule.Occurrences(b.T, e).Select(o => o.Number));
        // Not skipping closures, only the date skipped by hand is off.
        Assert.Equal([1, 3, 4], EventSchedule.Occurrences(b.T, e with { SkipClosures = false }).Select(o => o.Number));
    }
}

public class NameOrderTests
{
    [Fact]
    public void DanishLettersComeAfterZAndAaSortsAsÅ()
    {
        var names = new[] { "Åse", "Zara", "Ægir", "Aage", "Anna", "Øyvind" }.OrderBy(n => n, NameOrder.Comparer).ToList();
        Assert.Equal(["Anna", "Zara", "Ægir", "Øyvind", "Aage", "Åse"], names);
    }
}

public class SpacesTests
{
    [Fact]
    public void ASpaceIsInUseOnlyByAClassThatHappensAndOverlaps()
    {
        var b = new Build();
        var hall = b.Room("Hall");
        var gym = b.Room("Gym");
        var loft = b.Room("Loft");
        var guests = b.Programme("Visitors", guest: true);
        var visiting = b.Group("Visiting troupe", guests);
        var morning = b.Session(Monday, "09:00", "11:00", null, hall);
        var cancelled = b.Session(Monday, "10:00", "12:00", null, gym);
        b.Put(cancelled with { Cancelled = true });
        b.Session(Monday, "10:00", "12:00", Audience.ForGroup(visiting.Id), loft);

        var at10 = Spaces.InUse(b.T, Monday, new TimeOnly(10, 0), new TimeOnly(11, 0));
        Assert.Equal(morning.Id, at10[hall.Id].Session.Id);
        Assert.False(at10.ContainsKey(gym.Id), "a cancelled class leaves its space free");
        Assert.True(at10.ContainsKey(loft.Id), "a guest class still fills its space: hiding is drawing, never counting");

        // Ending as another starts is not an overlap, and the class being edited never blocks itself.
        Assert.False(Spaces.InUse(b.T, Monday, new TimeOnly(11, 0), new TimeOnly(12, 0)).ContainsKey(hall.Id));
        Assert.False(Spaces.InUse(b.T, Monday, new TimeOnly(9, 30), new TimeOnly(10, 0), except: morning.Id).ContainsKey(hall.Id));
        Assert.Empty(Spaces.InUse(b.T, Monday.AddDays(1), new TimeOnly(9, 0), new TimeOnly(17, 0)));
    }
}

public class TeachingTests
{
    [Fact]
    public void SomebodyIsTeachingOnlyWhenTheyAreInTheRoomForThatTime()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var cleo = b.Facilitator("Cleo");
        var dag = b.Facilitator("Dag");
        var esme = b.Facilitator("Esme");
        var finn = b.Facilitator("Finn");
        var guests = b.Programme("Visitors", guest: true);
        var visiting = b.Group("Visiting troupe", guests);
        var morning = b.Session(Monday, "09:00", "11:00", null, null, null, In(ada));
        var shared = b.Session(Monday, "09:00", "12:00", null, null, null, In(bo), In(dag, from: "11:00"));
        b.Put(shared with { CoverIds = [esme.Id] });
        b.Away(bo, Monday);
        var cancelled = b.Session(Monday, "09:00", "11:00", null, null, null, In(cleo));
        b.Put(cancelled with { Cancelled = true });
        b.Session(Monday, "10:00", "11:00", Audience.ForGroup(visiting.Id), null, null, In(finn));

        var at10 = Teaching.At(b.T, Monday, new TimeOnly(10, 0), new TimeOnly(10, 30));
        Assert.Equal(morning.Id, at10[ada.Id].Session.Id);
        Assert.False(at10.ContainsKey(bo.Id), "somebody away is not in the room");
        Assert.True(at10.ContainsKey(esme.Id), "cover is in the room");
        Assert.False(at10.ContainsKey(dag.Id), "somebody in only part of a class is free outside their part");
        Assert.False(at10.ContainsKey(cleo.Id), "a cancelled class keeps nobody busy");
        Assert.True(at10.ContainsKey(finn.Id), "a guest class still counts: hiding is drawing, never counting");

        Assert.True(Teaching.At(b.T, Monday, new TimeOnly(11, 0), new TimeOnly(11, 30)).ContainsKey(dag.Id));
        Assert.False(Teaching.At(b.T, Monday, new TimeOnly(10, 0), new TimeOnly(11, 0)).ContainsKey(dag.Id), "a part starting as the time ends does not overlap it");
        // Ending as another starts is not an overlap, and the class being covered or edited never counts.
        Assert.False(Teaching.At(b.T, Monday, new TimeOnly(11, 0), new TimeOnly(12, 0)).ContainsKey(ada.Id));
        Assert.False(Teaching.At(b.T, Monday, new TimeOnly(9, 0), new TimeOnly(10, 0), except: morning.Id).ContainsKey(ada.Id));
        Assert.Empty(Teaching.At(b.T, Monday.AddDays(1), new TimeOnly(9, 0), new TimeOnly(17, 0)));
    }
}

public class WhereaboutsTests
{
    private static TimeOnly At(string hm) => TimeOnly.Parse(hm);

    [Fact]
    public void AStudentIsInTheClassTheyAreOnUntilItEndsAndThenFreeUntilTheNext()
    {
        var b = new Build();
        var g = b.Group("BA");
        var other = b.Group("Prep");
        var ana = b.Student("Ana", g);
        var hall = b.Room("Hall");
        var morning = b.Session(Monday, "09:00", "11:00", Audience.ForGroup(g.Id), hall);
        b.Session(Monday, "12:00", "13:00", Audience.Everyone);                                    // lunch: on for everybody, nobody's class
        var afternoon = b.Session(Monday, "13:00", "14:00", Audience.ForGroup(g.Id));
        b.Session(Monday, "10:00", "11:00", Audience.ForGroup(other.Id));                          // not hers
        var cancelled = b.Session(Monday, "11:00", "12:00", Audience.ForGroup(g.Id));
        b.Put(cancelled with { Cancelled = true });

        var w = Where.Student(b.T, ana.Id, Monday, At("10:30"));
        Assert.Equal(morning.Id, w.Now?.O.Session.Id);
        Assert.Equal(afternoon.Id, w.Next?.O.Session.Id);   // the cancelled class and lunch in between are not hers
        Assert.False(w.Away);
        var gap = Where.Student(b.T, ana.Id, Monday, At("11:00"));   // a class ending at the moment is over
        Assert.Null(gap.Now);
        Assert.Equal(afternoon.Id, gap.Next?.O.Session.Id);
        Assert.Null(Where.Student(b.T, ana.Id, Monday, At("12:15")).Now);   // her week has no lunch on it either
        var after = Where.Student(b.T, ana.Id, Monday, At("14:00"));
        Assert.Null(after.Now);
        Assert.Null(after.Next);
        Assert.Null(Where.Student(b.T, ana.Id, Monday.AddDays(1), At("10:30")).Now);

        b.T = b.T with { Absences = b.T.Absences.Put(new Absence { Person = AbsentPerson.Student, PersonId = ana.Id, From = Monday, To = Monday, StartTime = At("08:00"), EndTime = At("10:30") }) };
        Assert.True(Where.Student(b.T, ana.Id, Monday, At("10:29")).Away);
        Assert.False(Where.Student(b.T, ana.Id, Monday, At("10:30")).Away);   // an absence ending at the moment is over
    }

    [Fact]
    public void AFacilitatorIsWhereTheyAreInTheRoomForTheirPartAndNowhereWhenAway()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var cleo = b.Facilitator("Cleo");
        var shared = b.Session(Monday, "09:00", "12:00", null, null, null, In(ada, from: "10:00", until: "11:00"), In(bo));
        b.Put(shared with { CoverIds = [cleo.Id] });
        b.Away(bo, Monday);

        var early = Where.Facilitator(b.T, ada.Id, Monday, At("09:30"));
        Assert.Null(early.Now);
        Assert.Equal(At("10:00"), early.Next?.From);   // her part, not the class's start
        var during = Where.Facilitator(b.T, ada.Id, Monday, At("10:30"));
        Assert.Equal(shared.Id, during.Now?.O.Session.Id);
        Assert.Equal(At("11:00"), during.Now?.Until);
        Assert.Null(Where.Facilitator(b.T, ada.Id, Monday, At("11:30")).Now);

        var away = Where.Facilitator(b.T, bo.Id, Monday, At("09:30"));
        Assert.True(away.Away);
        Assert.Null(away.Now);
        Assert.Equal(shared.Id, Where.Facilitator(b.T, cleo.Id, Monday, At("09:30")).Now?.O.Session.Id);   // cover stands in for Bo
    }

    [Fact]
    public void AGroupIsInEveryClassOnForItAndASpaceHoldsWhatIsInIt()
    {
        var b = new Build();
        var g = b.Group("BA");
        var hall = b.Room("Hall");
        var gym = b.Room("Gym");
        var lunch = b.Session(Monday, "12:00", "13:00", Audience.Everyone, gym);
        var morning = b.Session(Monday, "09:00", "11:00", Audience.ForGroup(g.Id), hall);

        Assert.Equal(lunch.Id, Where.Group(b.T, g.Id, Monday, At("12:15")).Now?.O.Session.Id);   // a whole-building class is on for every group
        Assert.Equal(morning.Id, Where.Space(b.T, hall.Id, Monday, At("09:00")).Now?.O.Session.Id);
        Assert.Null(Where.Space(b.T, hall.Id, Monday, At("12:15")).Now);
        Assert.Equal(lunch.Id, Where.Space(b.T, gym.Id, Monday, At("09:00")).Next?.O.Session.Id);
    }
}

public class RecentTests
{
    [Fact]
    public void TheWindowOpensOnTheWeekdayBefore()
    {
        Assert.Equal(Monday.AddDays(-3), Recent.Since(Monday));              // a Monday looks back to Friday
        Assert.Equal(Monday, Recent.Since(Monday.AddDays(1)));                // a Tuesday to Monday
        Assert.Equal(Monday.AddDays(4), Recent.Since(Monday.AddDays(6)));     // a Sunday to Friday
    }

    [Fact]
    public void ADaysChangesAreWhatSomebodyWouldNoticeAndAChangePutBackIsNone()
    {
        var b = new Build();
        var g = b.Group("BA");
        var hall = b.Room("Hall");
        var gym = b.Room("Gym");
        var ada = b.Facilitator("Ada");
        var dag = b.Facilitator("Dag");
        var ana = b.Student("Ana", g);
        var respaced = b.Session(Monday, "09:00", "10:00", Audience.ForGroup(g.Id), hall, null, new Assignment(ada.Id));
        var awayFromTheDay = b.Session(Monday, "10:00", "11:00", Audience.ForGroup(g.Id), hall);
        var movedBack = b.Session(Monday, "11:00", "12:00", Audience.ForGroup(g.Id), hall);
        var registerOnly = b.Session(Monday, "13:00", "14:00", Audience.ForGroup(g.Id), hall, null, new Assignment(ada.Id));
        var notesOnly = b.Session(Monday, "14:00", "15:00", Audience.ForGroup(g.Id), hall);
        var anotherDay = b.Session(Monday.AddDays(1), "09:00", "10:00", Audience.ForGroup(g.Id), hall);
        var deleted = b.Session(Monday, "15:00", "16:00", Audience.ForGroup(g.Id), hall);

        var t = b.T;
        var log = new List<Change>();
        var at = new DateTimeOffset(2030, 3, 4, 7, 0, 0, TimeSpan.Zero);
        void Do(Func<Timetable, Batch> build, string by)
        {
            var batch = build(t);
            log.Add(new Change(log.Count + 1, by, at = at.AddMinutes(5), batch));
            t = Apply.Batch(t, batch);
        }
        Do(t => Edits.PutSession(t, t.Sessions[respaced.Id]! with { RoomId = gym.Id }), "admin");
        Do(t => Edits.SetCover(t, respaced.Id, [dag.Id], null), "editor");
        Do(t => Edits.Move(t, awayFromTheDay.Id, Monday.AddDays(2), new TimeOnly(10, 0), new TimeOnly(11, 0)), "admin");
        Do(t => Edits.Move(t, movedBack.Id, Monday, new TimeOnly(12, 0), new TimeOnly(13, 0)), "admin");
        Do(t => Edits.Move(t, movedBack.Id, Monday, new TimeOnly(11, 0), new TimeOnly(12, 0)), "admin");
        Do(t => Edits.FileRegister(t, registerOnly.Id, [ana.Id], [], new RegisterReader(true, null), "admin", at), "admin");
        Do(t => Edits.PutSession(t, t.Sessions[notesOnly.Id]! with { Notes = "Bring mats" }), "admin");
        Do(t => Edits.PutSession(t, t.Sessions[anotherDay.Id]! with { RoomId = gym.Id }), "admin");
        Do(t => Edits.RemoveSessions(t, [deleted.Id]), "admin");
        var added = new Session { Date = Monday, Start = new(16, 0), End = new(17, 0), Title = "Extra", Audience = Audience.ForGroup(g.Id) };
        Do(t => Edits.PutSession(t, added), "editor");
        var addedThenGone = new Session { Date = Monday, Start = new(17, 0), End = new(18, 0), Title = "Mistake" };
        Do(t => Edits.PutSession(t, addedThenGone), "admin");
        Do(t => Edits.RemoveSessions(t, [addedThenGone.Id]), "admin");

        var r = Recent.ForDay(t, log, Monday);

        Assert.Equal([added.Id, deleted.Id, awayFromTheDay.Id, respaced.Id], r.Select(x => x.Id));   // the latest first
        var s = r.Single(x => x.Id == respaced.Id);
        Assert.True(s.SpaceChanged && s.CoverChanged);
        Assert.False(s.TimesChanged || s.DayChanged || s.TeachersChanged || s.CancelledChanged);
        Assert.Equal(["admin", "editor"], s.By);                         // both who changed it, first come first
        Assert.Equal(hall.Id, s.Was!.RoomId);                            // as it was when the window opened
        Assert.True(r.Single(x => x.Id == awayFromTheDay.Id).DayChanged);  // moved away from the day is news on the day
        Assert.True(r.Single(x => x.Id == added.Id).Added);
        Assert.True(r.Single(x => x.Id == deleted.Id).Deleted);
        Assert.Equal([anotherDay.Id], Recent.ForDay(t, log, Monday.AddDays(1)).Select(x => x.Id));
    }
}

public class NoticeableTests
{
    [Fact]
    public void EachThingSomebodyInTheBuildingWouldNoticeCountsOnItsOwnAndNotesDoNot()
    {
        var b = new Build();
        var g = b.Group("BA");
        var h = b.Group("MA");
        var hall = b.Room("Hall");
        var ada = b.Facilitator("Ada");
        var dag = b.Facilitator("Dag");
        var s = b.Session(Monday, "09:00", "10:00", Audience.ForGroup(g.Id), hall, null, new Assignment(ada.Id));
        Assert.True(Recent.Noticeable(s, s with { Date = Monday.AddDays(1) }));
        Assert.True(Recent.Noticeable(s, s with { Start = new(9, 15) }));
        Assert.True(Recent.Noticeable(s, s with { End = new(10, 30) }));
        Assert.True(Recent.Noticeable(s, s with { RoomId = null }));
        Assert.True(Recent.Noticeable(s, s with { Cancelled = true }));
        Assert.True(Recent.Noticeable(s, s with { Title = "Renamed" }));
        Assert.True(Recent.Noticeable(s, s with { Facilitators = [new Assignment(dag.Id)] }));
        Assert.True(Recent.Noticeable(s, s with { Facilitators = [new Assignment(ada.Id, new TimeOnly(9, 30))] }));   // her part of it
        Assert.True(Recent.Noticeable(s, s with { CoverIds = [dag.Id] }));
        Assert.True(Recent.Noticeable(s, s with { Audience = Audience.ForGroups([g.Id, h.Id]) }));
        Assert.True(Recent.Noticeable(null, s));
        Assert.True(Recent.Noticeable(s, null));
        Assert.False(Recent.Noticeable(null, null));
        Assert.False(Recent.Noticeable(s, s with { Notes = "Bring mats", CancelNote = "-", CoverNote = "-", AddedStudentIds = [Guid.NewGuid()] }));
        Assert.False(Recent.Noticeable(s, s with { Facilitators = [new Assignment(ada.Id)], Audience = Audience.ForGroups([g.Id]) with { Kind = AudienceKind.Group } }));   // equal, freshly made
    }
}
