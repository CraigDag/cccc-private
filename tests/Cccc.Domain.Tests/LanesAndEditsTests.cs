using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

public class LaneTests
{
    private static IReadOnlyList<Placed> Place(Build b) =>
        Lanes.Place(b.T.Sessions.Select(LaneItem.Of), Lanes.ForWeek(b.T.Sessions.Select(LaneItem.Of), b.T));

    [Fact]
    public void ADayWithOneGroupIsNotSplitEvenWhenTheWeekHasTwo()
    {
        var b = new Build();
        var one = b.Group("BA 1");
        var two = b.Group("BA 2");
        var mon = b.Session(Monday, "09:00", "10:00", Audience.ForGroup(one.Id));
        b.Session(Monday.AddDays(1), "09:00", "10:00", Audience.ForGroup(one.Id));
        b.Session(Monday.AddDays(1), "09:00", "10:00", Audience.ForGroup(two.Id));
        var p = Place(b).Single(x => x.Id == mon.Id);
        Assert.Equal(0, p.Left, 3);
        Assert.Equal(1, p.Width, 3);
    }

    [Fact]
    public void TwoGroupsAtOnceSplitTheColumn()
    {
        var b = new Build();
        var one = b.Group("BA 1");
        var two = b.Group("BA 2");
        var a = b.Session(Monday, "09:00", "10:00", Audience.ForGroup(one.Id));
        var c = b.Session(Monday, "09:00", "10:00", Audience.ForGroup(two.Id));
        var placed = Place(b);
        Assert.Equal(0.5, placed.Single(x => x.Id == a.Id).Width, 3);
        Assert.Equal(0.5, placed.Single(x => x.Id == c.Id).Left, 3);
    }

    [Fact]
    public void ABlockWidensIntoANeighbouringLaneEmptyForItsWholeTime()
    {
        var b = new Build();
        var one = b.Group("BA 1");
        var two = b.Group("BA 2");
        var three = b.Group("BA 3");
        b.Session(Monday, "09:00", "12:00", Audience.ForGroup(one.Id));      // keeps the morning one cluster
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(two.Id));
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(three.Id));
        var late = b.Session(Monday, "11:00", "12:00", Audience.ForGroup(two.Id));   // BA 3 has nothing then
        var p = Place(b).Single(x => x.Id == late.Id);
        Assert.Equal(2 / 3.0, p.Width, 3);
        Assert.Equal(1 / 3.0, p.Left, 3);
    }

    [Fact]
    public void OnePersonsWeekHasNoLanes() =>
        Assert.Empty(Lanes.ForWeek([LaneItem.Of(new Session { Date = Monday, Start = new(9, 0), End = new(10, 0) })], new Build().T, onePerson: true));
}

public class EditDecisionTests
{
    [Fact]
    public void RenamingAClassRenamesSessionsCarryingItsOldTitleOnlyAndOnlyAhead()
    {
        var b = new Build();
        var acro = b.Class("Acro");
        var ahead = b.Session(Monday.AddDays(1), "09:00", "10:00", cls: acro);
        var own = b.Session(Monday.AddDays(2), "09:00", "10:00", cls: acro);
        b.Put(own with { Title = "Acro showcase" });
        var past = b.Session(Monday.AddDays(-7), "09:00", "10:00", cls: acro);
        var t = Apply.Batch(b.T, Edits.PutClass(b.T, acro with { Name = "Acrobatics" }, Today));
        Assert.Equal("Acrobatics", t.Sessions[ahead.Id]!.Title);
        Assert.Equal("Acro showcase", t.Sessions[own.Id]!.Title);
        Assert.Equal("Acro", t.Sessions[past.Id]!.Title);
    }

    [Fact]
    public void DeletingARoomLeavesItsSessionsRunningWithNoRoom()
    {
        var b = new Build();
        var studio = b.Room("Studio");
        var s = b.Session(Monday.AddDays(1), "09:00", "10:00", room: studio);
        var t = Apply.Batch(b.T, Edits.RemoveRoom(b.T, studio.Id, Today));
        Assert.NotNull(t.Sessions[s.Id]);
        Assert.Null(t.Sessions[s.Id]!.RoomId);
        Assert.Null(t.Rooms[studio.Id]);
    }

    [Fact]
    public void AMovedClassIsCheckedAgainstAbsencesAtItsNewTime()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var s = b.Session(Monday.AddDays(1), "09:00", "10:00", null, null, null, In(ada));
        b.Away(ada, Monday.AddDays(2));
        Assert.Equal(OccurrenceState.Runs, Occurrences.Resolve(b.T, b.T.Sessions[s.Id]!).State);
        var t = Apply.Batch(b.T, Edits.Move(b.T, s.Id, Monday.AddDays(2), new(9, 0), new(10, 0)));
        Assert.Equal(OccurrenceState.NeedsCover, Occurrences.Resolve(t, t.Sessions[s.Id]!).State);
    }
}

public class LateNewsTests
{
    [Fact]
    public void AStudentAbsenceRecordedAfterTheRegisterExcusesTheAbsentAndNeverThePresent()
    {
        var b = new Build();
        var g = b.Group("BA 1");
        var ada = b.Student("Ada", g);
        var bo = b.Student("Bo", g);
        var cleo = b.Student("Cleo", g);
        var s = b.Session(Monday, "09:00", "10:00", Audience.ForGroup(g.Id));
        var other = b.Session(Monday.AddDays(1), "09:00", "10:00", Audience.ForGroup(g.Id));
        b.T = b.T with
        {
            Registers = b.T.Registers.Put(new Register { Id = s.Id, RosterIds = [ada.Id, bo.Id], PresentIds = [bo.Id] })
                                     .Put(new Register { Id = other.Id, RosterIds = [ada.Id, bo.Id], PresentIds = [] }),
        };
        Absence Away(Student who) => new() { Person = AbsentPerson.Student, PersonId = who.Id, From = Monday, To = Monday };
        var t = Apply.Batch(b.T, Edits.PutAbsence(b.T, Away(ada)));
        Assert.Equal(Mark.Excused, t.Registers[s.Id]!.MarkFor(ada.Id));
        Assert.Equal(Mark.Absent, t.Registers[other.Id]!.MarkFor(ada.Id));      // Tuesday: not covered
        t = Apply.Batch(t, Edits.PutAbsence(t, Away(bo)));
        Assert.Equal(Mark.Present, t.Registers[s.Id]!.MarkFor(bo.Id));           // seen in the room
        t = Apply.Batch(t, Edits.PutAbsence(t, Away(cleo)));
        Assert.DoesNotContain(cleo.Id, t.Registers[s.Id]!.ExcusedIds);          // not on the list it was taken against
    }
}
