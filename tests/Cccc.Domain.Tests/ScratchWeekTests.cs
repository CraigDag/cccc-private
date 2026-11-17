using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

public class ScratchWeekTests
{
    private static readonly DateOnly NextWeek = Monday.AddDays(7);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);
    private static readonly DateOnly ScratchWednesday = Week.DateOf(ScratchWeek.Monday, DayOfWeek.Wednesday);

    /// <summary>A class this week for a group, taught by Ada in Studio, with a copy of it in the Scratch Week.</summary>
    private static (Build B, Session Real, Session Copy, Facilitator Ada, Room Studio, Group Ba) Planned()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var studio = b.Room("Studio");
        var ba = b.Group("BA 1");
        b.Student("Alba", ba);
        var real = b.Session(Wednesday, "09:00", "11:00", Audience.ForGroup(ba.Id), studio, null, In(ada));
        b.T = Apply.Batch(b.T, ScratchWeek.CopyIn(b.T, real.Id));
        return (b, real, b.T.Scratch.Single(), ada, studio, ba);
    }

    private static List<(Guid, OccurrenceState, DateOnly, TimeOnly, TimeOnly)> Seen(IEnumerable<Occurrence> week) =>
        [.. week.Select(o => (o.Session.Id, o.State, o.Date, o.Start, o.End)).Order()];

    [Fact]
    public void CopyingAClassInLeavesItWhereItWasAndPutsACopyOnTheSameDayOfTheScratchWeek()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var real = b.Session(Wednesday, "09:00", "11:00", null, b.Room("Studio"), null, In(ada, until: "10:00"));
        b.Put(real with { Cancelled = true, CancelNote = "Show week", CoverIds = [bo.Id] });
        var before = b.T.Sessions[real.Id];

        var t = Apply.Batch(b.T, ScratchWeek.CopyIn(b.T, real.Id));

        Assert.Equal(before, t.Sessions[real.Id]);                   // the class itself is untouched
        Assert.Single(t.Sessions);
        var copy = Assert.Single(t.Scratch);
        Assert.NotEqual(real.Id, copy.Id);
        Assert.Equal(ScratchWednesday, copy.Date);
        Assert.Equal((real.Start, real.End, real.RoomId), (copy.Start, copy.End, copy.RoomId));
        Assert.Equal(new TimeOnly(10, 0), copy.Facilitators.Single().Until);  // each person's part travels
        Assert.False(copy.Cancelled);                                // nothing that happened travels
        Assert.Empty(copy.CoverIds);
        // Copying the same class again puts a second copy beside the first, never over it.
        Assert.Equal(2, Apply.Batch(t, ScratchWeek.CopyIn(t, real.Id)).Scratch.Count);
    }

    [Fact]
    public void NothingInTheScratchWeekCountsForAnybody()
    {
        var (b, real, copy, ada, studio, ba) = Planned();
        // The plan holds a second class at the same time in the same space, with the same person: a clash,
        // an over-booking and more hours, if anything counted it.
        b.T = Apply.Batch(b.T, ScratchWeek.Edit(b.T, v => Edits.PutSession(v, copy with { Id = Guid.NewGuid(), Title = "Another" })));
        var alone = b.T with { Scratch = EntitySet<Session>.Empty };

        Assert.Equal(2, b.T.Scratch.Count);
        Assert.Equal(Seen(Occurrences.ForWeek(alone, Monday)), Seen(Occurrences.ForWeek(b.T, Monday)));
        Assert.Equal(Hours.Teaching(Occurrences.HappeningInWeek(alone, Monday), ada.Id), Hours.Teaching(Occurrences.HappeningInWeek(b.T, Monday), ada.Id));
        Assert.Empty(Clashes.ForWeek(b.T, Monday));
        Assert.Empty(Clashes.ForWeek(b.T, ScratchWeek.Monday));      // not even in its own week, read as real
        Assert.Empty(Occurrences.ForWeek(b.T, ScratchWeek.Monday));
        Assert.Equal(Seen(Registers.MissingInWeek(alone, Monday, Wednesday.AddDays(1))), Seen(Registers.MissingInWeek(b.T, Monday, Wednesday.AddDays(1))));
        Assert.NotEmpty(Registers.MissingInWeek(b.T, Monday, Wednesday.AddDays(1)));   // so the comparison above was about something
        Assert.Equal(Hours.StudentLoad(alone, Occurrences.HappeningInWeek(alone, Monday), b.T.Students.Single().Id),
                     Hours.StudentLoad(b.T, Occurrences.HappeningInWeek(b.T, Monday), b.T.Students.Single().Id));
    }

    [Fact]
    public void TheScratchWeekIsDrawnWithItsOwnClassesClashesAndNothingDated()
    {
        var (b, real, copy, ada, studio, ba) = Planned();
        b.Away(ada, ScratchWednesday);                               // nobody is away in a plan
        b.Closure(new Closure { Name = "Shut", From = ScratchWednesday, To = ScratchWednesday });
        b.T = b.T with { Registers = b.T.Registers.Put(new Register { Id = copy.Id }) };
        b.T = Apply.Batch(b.T, ScratchWeek.Edit(b.T, v => Edits.PutSession(v, copy with { Id = Guid.NewGuid(), Title = "Another" })));

        var view = ScratchWeek.View(b.T);
        var week = Occurrences.ForWeek(view, ScratchWeek.Monday);
        Assert.Equal(2, week.Count);
        Assert.All(week, o => Assert.True(o.Happens));               // not closed, nobody away
        Assert.All(week, o => Assert.False(o.NeedsCover));
        Assert.Empty(view.Registers);
        Assert.Empty(Occurrences.ForWeek(view, Monday));             // the real week is not in the plan
        var kinds = Clashes.ForWeek(view, ScratchWeek.Monday).Select(c => c.Kind).ToList();
        Assert.Contains(ClashKind.RoomDoubleBooked, kinds);          // its own classes clash with each other
        Assert.Contains(ClashKind.FacilitatorDoubleBooked, kinds);
        Assert.All(Clashes.ForWeek(view, ScratchWeek.Monday), c => Assert.DoesNotContain(real.Id, c.SessionIds));
    }

    [Fact]
    public void AnEditMadeOnTheScratchWeekGoesBackToItAndChangesNothingElse()
    {
        var (b, real, copy, _, _, _) = Planned();
        var friday = Week.DateOf(ScratchWeek.Monday, DayOfWeek.Friday);
        var batch = ScratchWeek.Edit(b.T, v => Edits.Move(v, copy.Id, friday, new TimeOnly(13, 0), new TimeOnly(15, 0)));
        Assert.All(batch.Ops, o => Assert.Equal("scratch", o.Collection));
        Assert.EndsWith("in the Scratch Week", batch.Label);
        Edits.CheckLocks(b.T, batch, Today);
        var t = Apply.Batch(b.T, batch);
        Assert.Equal((friday, new TimeOnly(13, 0)), (t.Scratch[copy.Id]!.Date, t.Scratch[copy.Id]!.Start));
        Assert.Equal(b.T.Sessions[real.Id], t.Sessions[real.Id]);

        var gone = Apply.Batch(t, ScratchWeek.Edit(t, v => Edits.RemoveSessions(v, [copy.Id])));
        Assert.Empty(gone.Scratch);
        Assert.Single(gone.Sessions);

        // Anything but its classes is refused: a plan has no absences, registers or events of its own.
        Assert.Throws<EditRefused>(() => ScratchWeek.Edit(t, v => Edits.PutAbsence(v, new Absence { PersonId = Guid.NewGuid(), From = friday, To = friday })));
    }

    [Fact]
    public void NothingRealIsDatedIntoTheScratchWeekAndNothingOfThePlanLeavesItExceptByPlacing()
    {
        var (b, real, copy, _, _, _) = Planned();
        var intoPlan = Edits.PutSession(b.T, real with { Date = ScratchWednesday });
        Assert.Throws<EditRefused>(() => Edits.CheckLocks(b.T, intoPlan, Today));
        var outOfPlan = ScratchWeek.Edit(b.T, v => Edits.Move(v, copy.Id, NextWeek, copy.Start, copy.End));
        Assert.Throws<EditRefused>(() => Edits.CheckLocks(b.T, outOfPlan, Today));
        // Copy this week to… never takes the Scratch Week as a destination either.
        var copied = Apply.Batch(b.T, Edits.CopyWeek(b.T, Monday, [ScratchWeek.Monday], Today));
        Assert.Equal(b.T.Scratch.Single(), copied.Scratch.Single());
        Assert.Empty(copied.SessionsInWeek(ScratchWeek.Monday));
    }

    [Fact]
    public void CopyingAWeekInReplacesThePlanAndCopyingItAgainChangesNothing()
    {
        var (b, real, copy, _, _, _) = Planned();
        var monday = b.Session(Monday, "10:00", "11:00");
        var t = Apply.Batch(b.T, ScratchWeek.CopyWeekIn(b.T, Monday));
        Assert.Equal(2, t.Scratch.Count);
        Assert.DoesNotContain(t.Scratch, s => s.Id == copy.Id);       // what the plan held is replaced
        Assert.Equal([ScratchWeek.Monday, ScratchWednesday], t.Scratch.Select(s => s.Date).Order());
        Assert.Equal(b.T.Sessions, t.Sessions);
        Assert.True(ScratchWeek.CopyWeekIn(t, Monday).IsEmpty);
        Assert.Throws<EditRefused>(() => ScratchWeek.CopyWeekIn(t, ScratchWeek.Monday));
        _ = monday;
    }

    [Fact]
    public void PlacingThePlanReplacesTheWeekCountsFromThenAndKeepsThePlan()
    {
        var (b, real, copy, ada, _, _) = Planned();
        var friday = Week.DateOf(ScratchWeek.Monday, DayOfWeek.Friday);
        b.T = Apply.Batch(b.T, ScratchWeek.Edit(b.T, v => Edits.Move(v, copy.Id, friday, new TimeOnly(13, 0), new TimeOnly(15, 0))));
        var replaced = b.Session(NextWeek.AddDays(1), "09:00", "10:00");

        var batch = ScratchWeek.PlaceOnto(b.T, NextWeek, Today);
        Edits.CheckLocks(b.T, batch, Today);
        var t = Apply.Batch(b.T, batch);

        var placed = Assert.Single(t.SessionsInWeek(NextWeek));
        Assert.Equal((NextWeek.AddDays(4), new TimeOnly(13, 0)), (placed.Date, placed.Start));
        Assert.Null(t.Sessions[replaced.Id]);
        Assert.Equal(TimeSpan.FromHours(2), Hours.Teaching(Occurrences.HappeningInWeek(t, NextWeek), ada.Id));
        Assert.Equal(b.T.Scratch, t.Scratch);
        Assert.Equal(b.T.Sessions[real.Id], t.Sessions[real.Id]);
        // Placing it again onto the same week changes nothing.
        Assert.True(ScratchWeek.PlaceOnto(t, NextWeek, Today).IsEmpty);
    }

    [Fact]
    public void APlanIsNeverPlacedOntoAWeekThatHasEndedOrHoldsARegister()
    {
        var (b, real, _, _, _, _) = Planned();
        var lastWeek = Monday.AddDays(-7);
        b.Session(lastWeek, "09:00", "10:00");
        b.T = b.T with { Registers = b.T.Registers.Put(new Register { Id = real.Id }) };

        Assert.Contains("ended", ScratchWeek.WhyNot(b.T, lastWeek, Today));
        Assert.Contains("registers", ScratchWeek.WhyNot(b.T, Monday, Today));
        Assert.Null(ScratchWeek.WhyNot(b.T, NextWeek, Today));
        Assert.Throws<EditRefused>(() => ScratchWeek.PlaceOnto(b.T, lastWeek, Today));
        Assert.Throws<EditRefused>(() => ScratchWeek.PlaceOnto(b.T, Monday, Today));
        Assert.Throws<EditRefused>(() => ScratchWeek.PlaceOnto(b.T, ScratchWeek.Monday, Today));
    }

    [Fact]
    public void EmptyingTheScratchWeekTakesOnlyThePlan()
    {
        var (b, real, _, _, _, _) = Planned();
        var t = Apply.Batch(b.T, ScratchWeek.Empty(b.T));
        Assert.Empty(t.Scratch);
        Assert.Equal(b.T.Sessions, t.Sessions);
    }

    [Fact]
    public void EveryDeleteAndRenameReachesThePlanToo()
    {
        var (b, _, copy, ada, studio, ba) = Planned();
        var acro = b.Class("Acro");
        var bo = b.Facilitator("Bo");
        var alba = b.T.Students.Single();
        var p = b.Programme("BA");
        b.T = b.T with
        {
            Scratch = b.T.Scratch.Put(copy with
            {
                ClassId = acro.Id, Title = "Acro", Facilitators = [In(ada), In(bo)], AddedStudentIds = [alba.Id],
                Audience = new Audience { Kind = AudienceKind.Programme, ProgrammeId = p.Id, GroupIds = [ba.Id] },
            }),
        };
        var t = b.T;
        Assert.Equal("Acrobatics", Apply.Batch(t, Edits.PutClass(t, acro with { Name = "Acrobatics" }, Today)).Scratch[copy.Id]!.Title);
        Assert.Null(Apply.Batch(t, Edits.RemoveClass(t, acro.Id, Today)).Scratch[copy.Id]!.ClassId);
        Assert.Null(Apply.Batch(t, Edits.RemoveRoom(t, studio.Id, Today)).Scratch[copy.Id]!.RoomId);
        Assert.Equal([bo.Id], Apply.Batch(t, Edits.RemoveFacilitator(t, ada.Id, Today)).Scratch[copy.Id]!.FacilitatorIds);
        Assert.Empty(Apply.Batch(t, Edits.RemoveStudent(t, alba.Id, Today)).Scratch[copy.Id]!.AddedStudentIds);
        Assert.Empty(Apply.Batch(t, Edits.RemoveGroup(t, ba.Id, Today)).Scratch[copy.Id]!.Audience.GroupIds);
        Assert.Null(Apply.Batch(t, Edits.RemoveProgramme(t, p.Id, Today)).Scratch[copy.Id]!.Audience.ProgrammeId);
    }

    [Fact]
    public void TheScratchWeekIsSaidByNameAndItsDaysWithoutDates()
    {
        Assert.Equal("Wed", Week.ShortDate(ScratchWednesday));
        Assert.Equal("the Scratch Week", Week.ForDocuments(ScratchWednesday));
        Assert.Equal("the Scratch Week", Week.Range(ScratchWednesday));
        Assert.Equal("Wed 6 Mar", Week.ShortDate(Wednesday));
        Assert.Equal(DayOfWeek.Monday, ScratchWeek.Monday.DayOfWeek);
        Assert.False(Week.HasEnded(ScratchWeek.Monday, new DateOnly(2199, 12, 31)));
    }

    [Fact]
    public void ThePlanIsKeptWithTheTimetableAndAnOlderCopyWithoutOneReadsAsEmpty()
    {
        var (b, _, copy, _, _, _) = Planned();
        var json = ModelJson.Serialise(b.T);
        Assert.Equal(json, ModelJson.Serialise(ModelJson.Deserialise(json)));
        Assert.Contains(copy.Id.ToString(), json);
        var older = System.Text.Json.Nodes.JsonNode.Parse(json)!.AsObject();
        Assert.True(older.Remove("scratch"));
        Assert.Empty(ModelJson.Deserialise(older.ToJsonString()).Scratch);
        Assert.Contains(Collections.All, c => c.Name == "scratch");   // following, putting back and history carry it
    }
}
