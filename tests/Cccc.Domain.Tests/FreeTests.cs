using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>Who and what is free (<see cref="Free"/>): <see cref="Busy"/> asked about a time, the answer a question typed into Find gives.</summary>
public class FreeTests
{
    private static readonly EventViewer Editor = new(false, true, false, false, false);

    private static (BusyKind, string, string) At(BusyStretch b) => (b.Kind, Week.Hm(b.From), Week.Hm(b.Until));

    private static TimeOnly T(string hm) => TimeOnly.Parse(hm);

    private static string Said((TimeOnly? From, TimeOnly? Until) s) =>
        $"{(s.From is { } f ? Week.Hm(f) : "start")}–{(s.Until is { } u ? Week.Hm(u) : "end")}";

    [Fact]
    public void ASpaceIsBusyInAnyClassInItAndAtAnyEventInItWhoeverMayReadTheEvent()
    {
        var b = new Build();
        var studio = b.Room("Studio B");
        var gym = b.Room("Gym");
        var ba1 = b.Group("BA 1");
        var acro = b.Session(Monday, "09:00", "10:30", Audience.ForGroup(ba1.Id), studio, b.Class("Acrobatics"));
        b.Session(Monday, "13:00", "17:00", Audience.Everyone, studio, b.Class("Open training", Workload.DropIn));
        b.Session(Monday, "10:30", "12:00", null, gym, b.Class("Strength"));                              // another space
        var off = b.Session(Monday, "11:00", "12:00", null, studio, b.Class("Juggling"));
        b.Put(off with { Cancelled = true });                                                             // leaves it free
        var talks = new SchoolEvent { Title = "Salary talks", Date = Monday, Start = new(17, 0), End = new(18, 0), Audience = EventAudience.Administrators, RoomId = studio.Id };
        b.T = b.T with { Events = b.T.Events.Put(talks) };

        var day = Free.Day(b.T, Whose.Space, studio.Id, Monday, Editor);
        // An open session fills a space as much as any class; an event an editor may not read still books it.
        Assert.Equal([(BusyKind.Space, "09:00", "10:30"), (BusyKind.Space, "13:00", "17:00"), (BusyKind.Space, "17:00", "18:00")], day.Select(At));
        Assert.Equal(acro.Id, day[0].Class!.Session.Id);
        Assert.Null(day[2].Event);                                                                        // only booked, to an editor
        Assert.Equal(talks.Id, Free.Day(b.T, Whose.Space, studio.Id, Monday, EventViewer.Administrator)[2].Event!.Id);

        var lateMorning = Free.Over(day, T("11:00"), T("12:00"));
        Assert.True(lateMorning.IsFree);
        Assert.Equal((T("10:30"), T("13:00")), (lateMorning.Since!.Value, lateMorning.Until!.Value));
        Assert.Equal(day[1], lateMorning.Next);
        // A class ending as the time starts is not in its way, nor one starting as it ends.
        var between = Free.Over(day, T("10:30"), T("13:00"));
        Assert.True(between.IsFree);
        Assert.Equal((T("10:30"), T("13:00")), (between.Since!.Value, between.Until!.Value));
        Assert.Equal([day[0]], Free.Over(day, T("10:15"), T("13:00")).InTheWay);
        Assert.Equal([day[1], day[2]], Free.Over(day, T("16:00"), T("17:30")).InTheWay);
        // Before anything that day it is free since the day began; after everything, until it ends.
        var early = Free.Over(day, T("08:00"), T("09:00"));
        Assert.Equal((null, T("09:00")), (early.Since, early.Until));
        var late = Free.Over(day, T("19:00"), T("20:00"));
        Assert.Equal((T("18:00"), null, null), (late.Since, late.Until, late.Next));
        // Nothing in Gym's way on Monday afternoon; nothing at all in it on Tuesday.
        Assert.True(Free.Over(Free.Day(b.T, Whose.Space, gym.Id, Monday, Editor), T("13:00"), T("17:00")).IsFree);
        Assert.Empty(Free.Day(b.T, Whose.Space, studio.Id, Monday.AddDays(1), Editor));
    }

    [Fact]
    public void AFacilitatorIsBusyInARoomForTheirPartOfItCoveringIncludedWhileAwayAndAtAnEventTheReaderMaySee()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        b.Session(Monday, "09:00", "10:00", null, null, b.Class("Juggling"), In(ada));
        b.Session(Monday, "10:00", "12:00", null, null, b.Class("Creation lab"), In(bo), In(ada, from: "11:00"));
        var covered = b.Session(Monday, "13:00", "14:00", null, null, b.Class("Tumbling"), In(bo));
        b.Put(covered with { CoverIds = [ada.Id] });
        b.Away(bo, Monday);
        b.Away(ada, Monday, "15:00", "15:30");
        var meeting = new SchoolEvent { Title = "Staff meeting", Date = Monday, Start = new(16, 0), End = new(16, 30), Audience = EventAudience.Staff, FacilitatorIds = [ada.Id] };
        var talks = new SchoolEvent { Title = "Salary talks", Date = Monday, Start = new(17, 0), End = new(17, 30), Audience = EventAudience.Administrators, FacilitatorIds = [ada.Id] };
        b.T = b.T with { Events = b.T.Events.Put(meeting).Put(talks) };

        var day = Free.Day(b.T, Whose.Facilitator, ada.Id, Monday, Editor);
        Assert.Equal(
            [
                (BusyKind.Teaching, "09:00", "10:00"),
                (BusyKind.Teaching, "11:00", "12:00"),     // only her part of the creation lab
                (BusyKind.Teaching, "13:00", "14:00"),     // covering
                (BusyKind.Away, "15:00", "15:30"),
                (BusyKind.Event, "16:00", "16:30"),        // not the administrators' event an editor may not see
            ],
            day.Select(At));
        Assert.True(Free.Over(day, T("10:00"), T("11:00")).IsFree);
        // Bo is away all day, so his own classes keep him from nothing more than that.
        var bos = Free.Day(b.T, Whose.Facilitator, bo.Id, Monday, Editor);
        Assert.Equal([(BusyKind.Away, "00:00", "23:59")], bos.Select(x => (x.Kind, Week.Hm(x.From), Week.Hm(x.Until))));
        Assert.Empty(Free.Stretches(bos));
        Assert.False(Free.Over(bos, TimeOnly.MinValue, TimeOnly.MaxValue).IsFree);
    }

    [Fact]
    public void AGroupIsBusyInItsClassesAnOpenSessionAsideAndWhileSomeOfItsStudentsAreInAnother()
    {
        var b = new Build();
        var ba1 = b.Group("BA 1");
        var ba2 = b.Group("BA 2");
        var alba = b.Student("Alba", ba1);
        b.Student("Benno", ba1);
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(ba1.Id), null, b.Class("Aerial"));
        b.Session(Monday, "10:00", "11:00", Audience.ForGroup(ba2.Id), null, b.Class("Juggling"));       // another group's
        var lab = b.Session(Monday, "11:00", "12:00", Audience.ForGroup(ba2.Id), null, b.Class("Creation lab"));
        b.Put(lab with { AddedStudentIds = [alba.Id] });
        b.Session(Monday, "12:00", "12:45", Audience.Everyone, null, b.Class("Lunch", Workload.NotCounted));
        b.Session(Monday, "13:00", "17:00", Audience.Everyone, null, b.Class("Open training", Workload.DropIn));

        var day = Free.Day(b.T, Whose.Group, ba1.Id, Monday, Editor);
        Assert.Equal([(BusyKind.Group, "09:00", "10:00"), (BusyKind.Students, "11:00", "12:00"), (BusyKind.Group, "12:00", "12:45")], day.Select(At));
        Assert.Equal(1, day[1].Students);
        Assert.True(Free.Over(day, T("13:00"), T("17:00")).IsFree);
    }

    [Fact]
    public void AStudentIsBusyInTheClassesTheyAreOnAnOpenSessionAsideAndWhileRecordedAway()
    {
        var b = new Build();
        var ba1 = b.Group("BA 1");
        var ba2 = b.Group("BA 2");
        var alba = b.Student("Alba", ba1);
        var benno = b.Student("Benno", ba1);
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(ba1.Id), null, b.Class("Aerial"));
        var lab = b.Session(Monday, "11:00", "12:00", Audience.ForGroup(ba2.Id), null, b.Class("Creation lab"));
        b.Put(lab with { AddedStudentIds = [alba.Id] });
        var strength = b.Session(Monday, "14:00", "15:00", Audience.ForGroup(ba1.Id), null, b.Class("Strength"));
        b.Put(strength with { LeftOffStudentIds = [alba.Id] });
        b.Session(Monday, "15:00", "17:00", Audience.Everyone, null, b.Class("Open training", Workload.DropIn));
        b.T = b.T with { Absences = b.T.Absences.Put(new Absence { Person = AbsentPerson.Student, PersonId = alba.Id, From = Monday, To = Monday, StartTime = new(10, 15), EndTime = new(10, 45) }) };

        var day = Free.Day(b.T, Whose.Student, alba.Id, Monday, Editor);
        Assert.Equal([(BusyKind.Students, "09:00", "10:00"), (BusyKind.Away, "10:15", "10:45"), (BusyKind.Students, "11:00", "12:00")], day.Select(At));
        Assert.Equal(alba.Id, day[1].PersonId);
        // Benno is on the strength class she was left off, and on nothing he was not put on.
        Assert.Equal(["09:00", "14:00"], Free.Day(b.T, Whose.Student, benno.Id, Monday, Editor).Select(x => Week.Hm(x.From)));
    }

    [Fact]
    public void TheFreeStretchesOfADayAreWhatIsLeftBetweenTheBusyOnesRunTogether()
    {
        var b = new Build();
        var studio = b.Room("Studio B");
        b.Session(Monday, "09:00", "11:00", null, studio, b.Class("Acrobatics"));
        b.Session(Monday, "10:00", "12:00", null, studio, b.Class("Juggling"));     // overlaps the first: one busy stretch
        b.Session(Monday, "12:00", "13:00", null, studio, b.Class("Lunch"));        // ends as it begins: still one
        b.Session(Monday, "15:00", "17:00", null, studio, b.Class("Aerial"));
        b.Session(Monday, "15:30", "16:00", null, studio, b.Class("Tumbling"));     // inside the one before
        var day = Free.Day(b.T, Whose.Space, studio.Id, Monday, Editor);
        Assert.Equal(["start–09:00", "13:00–15:00", "17:00–end"], Free.Stretches(day).Select(Said));
        Assert.Equal(["start–end"], Free.Stretches([]).Select(Said));
        var allMorning = Free.Stretches([new BusyStretch(BusyKind.Space, TimeOnly.MinValue, T("12:00"))]);
        Assert.Equal(["12:00–end"], allMorning.Select(Said));
    }

    [Fact]
    public void NothingIsSaidAboutWhoIsFreeOnADayTheSchoolIsClosedOrInAWeekWithNothingInItYet()
    {
        var b = new Build();
        var ba1 = b.Group("BA 1");
        var ba2 = b.Group("BA 2");
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(ba1.Id), null, b.Class("Aerial"));
        b.Closure(new Closure { Name = "Study day", From = Monday.AddDays(2), To = Monday.AddDays(2), Scope = ClosureScope.WholeBuilding });
        b.Closure(new Closure { Name = "Trip", From = Monday.AddDays(3), To = Monday.AddDays(3), Scope = ClosureScope.Groups, GroupIds = [ba2.Id] });

        Assert.Null(Free.Unknowable(b.T, Monday, []));
        Assert.Equal("Study day: closed", Free.Unknowable(b.T, Monday.AddDays(2), []));
        // A closure for one group closes nothing about anybody else.
        Assert.Null(Free.Unknowable(b.T, Monday.AddDays(3), []));
        Assert.Null(Free.Unknowable(b.T, Monday.AddDays(3), [ba1.Id]));
        Assert.Equal("Trip: closed for BA 2", Free.Unknowable(b.T, Monday.AddDays(3), [ba2.Id]));
        Assert.Equal(Openings.NothingYet, Free.Unknowable(b.T, Monday.AddDays(7), []));
    }

    [Fact]
    public void AMomentIsTheMinuteStartingThenSoAClassEndingThenIsOver()
    {
        Assert.Equal((T("13:00"), T("13:01")), Free.Moment(T("13:00")));
        Assert.Equal((T("23:59"), TimeOnly.MaxValue), Free.Moment(T("23:59")));
        var b = new Build();
        var studio = b.Room("Studio B");
        b.Session(Monday, "09:00", "13:00", null, studio, b.Class("Acrobatics"));
        var day = Free.Day(b.T, Whose.Space, studio.Id, Monday, Editor);
        var (start, end) = Free.Moment(T("13:00"));
        Assert.True(Free.Over(day, start, end).IsFree);
        var (s2, e2) = Free.Moment(T("12:59"));
        Assert.False(Free.Over(day, s2, e2).IsFree);
    }
}
