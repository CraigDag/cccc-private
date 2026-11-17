using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

public class CopyWeekTests
{
    private static readonly DateOnly NextWeek = Monday.AddDays(7);
    private static readonly DateOnly WeekAfter = Monday.AddDays(14);

    [Fact]
    public void ACopyReplacesTheDestinationWeekRatherThanAddingToIt()
    {
        var b = new Build();
        b.Session(Monday, "09:00", "10:00");
        b.Session(Monday.AddDays(2), "11:00", "12:00");
        b.Session(NextWeek.AddDays(4), "13:00", "14:00");          // will be replaced
        var t = Apply.Batch(b.T, Edits.CopyWeek(b.T, Monday, [NextWeek], Today));
        var copied = t.SessionsInWeek(NextWeek).OrderBy(s => s.Date).ToList();
        Assert.Equal(2, copied.Count);
        Assert.Equal([NextWeek, NextWeek.AddDays(2)], copied.Select(s => s.Date));
        Assert.Equal(2, t.SessionsInWeek(Monday).Count());           // the source is untouched
    }

    [Fact]
    public void AWeekHoldingARegisterIsNeverReplaced()
    {
        var b = new Build();
        b.Session(Monday, "09:00", "10:00");
        var recorded = b.Session(NextWeek, "13:00", "14:00");
        b.T = b.T with { Registers = b.T.Registers.Put(new Register { Id = recorded.Id }) };
        var t = Apply.Batch(b.T, Edits.CopyWeek(b.T, Monday, [NextWeek, WeekAfter], Today));
        Assert.Equal([recorded.Id], t.SessionsInWeek(NextWeek).Select(s => s.Id));
        Assert.True(t.Registers.Contains(recorded.Id));
        Assert.Single(t.SessionsInWeek(WeekAfter));                   // the other week was still filled
    }

    [Fact]
    public void AWeekThatHasEndedIsNeverAnyCopysDestination()
    {
        var b = new Build();
        b.Session(Monday, "09:00", "10:00");
        var lastWeek = Monday.AddDays(-7);
        var kept = b.Session(lastWeek, "15:00", "16:00");
        var t = Apply.Batch(b.T, Edits.CopyWeek(b.T, Monday, [lastWeek], Today));
        Assert.Equal([kept.Id], t.SessionsInWeek(lastWeek).Select(s => s.Id));
    }

    [Fact]
    public void CopyingTheSameWeekTwiceGivesTheSameIdsSoNothingDoubles()
    {
        var b = new Build();
        b.Session(Monday, "09:00", "10:00");
        var once = Apply.Batch(b.T, Edits.CopyWeek(b.T, Monday, [NextWeek], Today));
        var twice = Apply.Batch(once, Edits.CopyWeek(once, Monday, [NextWeek], Today));
        Assert.Single(twice.SessionsInWeek(NextWeek));
    }
}

public class RemovingTests
{
    [Fact]
    public void RemovingAGroupLeavesItsOnlySessionForNoGroupNeverForEverybody()
    {
        var b = new Build();
        var g = b.Group("BA 1");
        var s = b.Session(Monday.AddDays(1), "09:00", "10:00", Audience.ForGroup(g.Id));
        var t = Apply.Batch(b.T, Edits.RemoveGroup(b.T, g.Id, Today));
        var after = t.Sessions[s.Id]!;
        Assert.NotEqual(AudienceKind.Everyone, after.Audience.Kind);
        Assert.Empty(after.Audience.GroupIds);
    }

    [Fact]
    public void RemovingAGroupLeavesAWeekThatHasEndedAsItWas()
    {
        var b = new Build();
        var g = b.Group("BA 1");
        var past = b.Session(Monday.AddDays(-7), "09:00", "10:00", Audience.ForGroup(g.Id));
        var t = Apply.Batch(b.T, Edits.RemoveGroup(b.T, g.Id, Today));
        Assert.Equal([g.Id], t.Sessions[past.Id]!.Audience.GroupIds);
    }

    [Fact]
    public void RemovingAStudentSweepsSessionsAheadEventsAndAbsencesButNotEndedWeeks()
    {
        var b = new Build();
        var ada = b.Student("Ada");
        var ahead = b.Session(Monday.AddDays(1), "09:00", "10:00");
        var past = b.Session(Monday.AddDays(-7), "09:00", "10:00");
        b.Put(ahead with { AddedStudentIds = [ada.Id] });
        b.Put(past with { AddedStudentIds = [ada.Id] });
        b.T = b.T with
        {
            Absences = b.T.Absences.Put(new Absence { Person = AbsentPerson.Student, PersonId = ada.Id, From = Monday, To = Monday }),
        };
        var t = Apply.Batch(b.T, Edits.RemoveStudent(b.T, ada.Id, Today));
        Assert.Null(t.Students[ada.Id]);
        Assert.Empty(t.Sessions[ahead.Id]!.AddedStudentIds);
        Assert.Equal([ada.Id], t.Sessions[past.Id]!.AddedStudentIds);
        Assert.Empty(t.Absences);
    }
}

public class ClashWordingTests
{
    [Fact]
    public void AGroupInTwoPlacesIsOneStudentWarningNotOnePerStudent()
    {
        var b = new Build();
        var g = b.Group("BA 1");
        foreach (var n in new[] { "Ada", "Bo", "Cleo", "Dag" }) b.Student(n, g);
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(g.Id));
        b.Session(Monday, "09:30", "10:30", Audience.ForGroup(g.Id));
        var students = Clashes.ForWeek(b.T, Monday).Where(c => c.Kind == ClashKind.StudentDoubleBooked).ToList();
        var one = Assert.Single(students);
        Assert.StartsWith("4 students (Ada Test, Bo Test and 2 more)", one.Description);
    }

    [Fact]
    public void TheSameGroupTwiceAtOnceIsDoubleBookedEvenWithNobodyEnrolled()
    {
        var b = new Build();
        var g = b.Group("BA 1");
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(g.Id));
        b.Session(Monday, "09:30", "10:30", Audience.ForGroup(g.Id));
        Assert.Contains(Clashes.ForWeek(b.T, Monday), c => c.Kind == ClashKind.GroupDoubleBooked);
    }

    [Fact]
    public void TwoWholeBuildingSessionsAtOnceAreNotAGroupClash()
    {
        var b = new Build();
        b.Group("BA 1");
        b.Session(Monday, "12:00", "13:00");
        b.Session(Monday, "12:30", "13:30");
        Assert.DoesNotContain(Clashes.ForWeek(b.T, Monday), c => c.Kind == ClashKind.GroupDoubleBooked);
    }
}
