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

public class PeopleImportTests
{
    [Fact]
    public void PastedRowsFromExcelBecomeStudentsInTheirGroupsAndOneUndoableChange()
    {
        var b = new Build();
        var g = b.Group("BA 1");
        b.Student("Ada", g);                                             // "Ada Test" is already on the list
        const string pasted = "First name\tSurname\tEmail\tGroup\nBo\tLind\tbo@example.invalid\tba 1\nAda\tTest\t\t\nCleo\tMarr\t\tBA 9\nDag\n";
        var lines = PeopleImport.Students(b.T, pasted);
        Assert.Equal(4, lines.Count);                                    // heading left out
        Assert.Equal(g.Id, lines[0].Student!.GroupId);                   // group matched regardless of case
        Assert.True(lines[1].Skipped);                                   // already there
        Assert.Null(lines[2].Student!.GroupId);
        Assert.Contains("No group called", lines[2].Note);
        Assert.True(lines[3].Skipped);                                   // no surname
        var batch = PeopleImport.Apply(b.T, lines);
        Assert.Equal("Imported 2 people", batch.Label);
        Assert.Equal(3, Apply.Batch(b.T, batch).Students.Count);
    }

    [Fact]
    public void AStudentCalledFirstIsNotTakenForAHeadingAndQuotedCommasStayInOneCell()
    {
        var lines = PeopleImport.Students(new Build().T, "First,Last\n\"Ng, Jr\",Kim");
        Assert.Equal(["First", "Ng, Jr"], lines.Select(l => l.Student!.FirstName));
    }
}

public class ReminderTests
{
    [Fact]
    public void TheEveningReminderNamesWhoWasInTheRoomForClassesThatHaveEndedAndNobodyElse()
    {
        var b = new Build();
        var g = b.Group("BA 1");
        b.Student("Ada", g);
        var cleo = b.Facilitator("Cleo");
        var dag = b.Facilitator("Dag");
        var guest = b.Facilitator("Gwen");
        b.T = b.T with
        {
            Facilitators = b.T.Facilitators.Put(b.T.Facilitators[cleo.Id]! with { Email = "cleo@example.invalid" })
                                           .Put(b.T.Facilitators[dag.Id]! with { Email = "dag@example.invalid" })
                                           .Put(b.T.Facilitators[guest.Id]! with { Email = "gwen@example.invalid", IsGuest = true }),
        };
        var morning = b.Session(Monday, "09:00", "10:00", Audience.ForGroup(g.Id), null, null, In(cleo), In(guest));
        b.Session(Monday, "17:00", "19:00", Audience.ForGroup(g.Id), null, null, In(dag));              // still running at 18:00
        var filed = b.Session(Monday, "11:00", "12:00", Audience.ForGroup(g.Id), null, null, In(dag));
        b.T = b.T with { Registers = b.T.Registers.Put(new Register { Id = filed.Id }) };
        var r = Assert.Single(RegisterReminders.For(b.T, Monday, new TimeOnly(18, 0)));
        Assert.Equal(cleo.Id, r.Facilitator.Id);
        Assert.Equal([morning.Id], r.Classes.Select(o => o.Session.Id));
    }
}
