using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>Finding a time, on the made-up school (a fixed week, so nothing here moves with the calendar) and on small hand-made ones.</summary>
public class OpeningsTests
{
    private static readonly DateOnly SampleMonday = new(2026, 11, 16);
    private static readonly EventViewer Editor = new(false, true, false, false, false);
    private static readonly EventViewer Admin = EventViewer.Administrator;
    private static readonly TimeOnly Early = new(7, 0);

    private static Guid Id<T>(EntitySet<T> set, Func<T, bool> which) where T : class, IEntity => set.Single(which).Id;

    private static (DateOnly, string, string, string) Row(Timetable t, Opening o) =>
        (o.Date, Week.Hm(o.Start), Week.Hm(o.Latest), string.Join(", ", o.Spaces.Select(r => t.Rooms[r]!.Name)));

    private static (DateOnly, string, string) When(Opening o) => (o.Date, Week.Hm(o.Start), Week.Hm(o.Latest));

    private static Session NewClass(string length, Audience? audience = null, params Assignment[] who)
    {
        var minutes = (int)TimeSpan.Parse(length).TotalMinutes;
        return new Session { Title = "Extra acrobatics", Start = new(9, 0), End = new TimeOnly(9, 0).AddMinutes(minutes), Audience = audience ?? Audience.ForGroups([]), Facilitators = [.. who] };
    }

    [Fact]
    public void AdaAndBa1InAHallForAnHourAndAHalfGetTheTimesEverybodyAndAHallAreFree()
    {
        var t = SampleSchool.Create(SampleMonday, SampleMonday);
        var ada = Id(t.Facilitators, f => f.Name == "Ada Brightwater");
        var ba1 = Id(t.Groups, g => g.Name == "BA 1");
        var halls = new[] { Id(t.Rooms, r => r.Name == "Big Top"), Id(t.Rooms, r => r.Name == "Aerial Hall") };
        var ask = new OpeningAsk
        {
            Class = NewClass("01:30", Audience.ForGroup(ba1), new Assignment(ada)),
            Spaces = halls, From = SampleMonday, To = SampleMonday.AddDays(4),
        };
        var found = Openings.Find(t, ask, SampleMonday, Early, Editor);

        // Monday after Circus disciplines; Tuesday after Creation lab; Wednesday after lunch, when open training
        // (nobody is required at it) leaves BA 1 free but has the Big Top; Friday after Aerial, both halls free at
        // 14:30 though the showing books the Big Top from 16:00. Thursday Ada is away.
        Assert.Equal(
            [
                (SampleMonday, "15:00", "16:30", "Big Top, Aerial Hall"),
                (SampleMonday.AddDays(1), "16:00", "16:30", "Big Top, Aerial Hall"),
                (SampleMonday.AddDays(2), "12:45", "16:30", "Aerial Hall"),
                (SampleMonday.AddDays(4), "14:30", "16:30", "Big Top, Aerial Hall"),
            ],
            found.Openings.Select(o => Row(t, o)));
        Assert.Equal([new DayNote(SampleMonday.AddDays(3), SampleMonday.AddDays(3), "Ada Brightwater is away")], found.NoTime);
        Assert.Empty(found.NotSearched);
        Assert.Equal(0, found.More);

        Assert.Equal(
            [
                new OpeningNote("Staff meeting then, 15:30–16:30", false),
                new OpeningNote("Ada Brightwater comes straight from Circus disciplines", true),
                new OpeningNote("BA 1 comes straight from Circus disciplines", true),
            ],
            found.Openings[0].Notes);
        Assert.Equal(
            [new OpeningNote("Ada Brightwater has nothing else that day", false), new OpeningNote("BA 1 comes straight from Creation lab", true)],
            found.Openings[1].Notes);
        // What the clash check will say about it once it is there is said beside it, never a reason to leave it out.
        Assert.Contains(found.Openings[2].Notes, n => !n.Good && n.Text.StartsWith("BA 1 double-booked: \"Open training\""));
        // A meeting for staff is no reason against a time for students alone.
        var studentsOnly = Openings.Find(t, ask with { Class = NewClass("01:30", Audience.ForGroup(ba1)) }, SampleMonday, Early, Editor).Openings[0];
        Assert.Equal((SampleMonday, "15:00", "16:30"), When(studentsOnly));
        Assert.DoesNotContain(studentsOnly.Notes, n => n.Text.StartsWith("Staff meeting"));
        // A visit all day for everybody is no reason against a time; the showing at 16:00 starts as it ends.
        Assert.Equal(
            [new OpeningNote("Ada Brightwater has nothing else that day", false), new OpeningNote("BA 1 comes straight from Aerial", true)],
            found.Openings[3].Notes);
    }

    [Fact]
    public void ClosedDaysAndWeeksWithNothingInThemYetAreSaidAndNotLookedIn()
    {
        var t = SampleSchool.Create(SampleMonday, SampleMonday);
        var prep = Id(t.Groups, g => g.Name == "Prep");
        var ask = new OpeningAsk { Class = NewClass("01:00", Audience.ForGroup(prep)), From = SampleMonday, To = SampleMonday.AddDays(25) };
        var found = Openings.Find(t, ask, SampleMonday, Early, Editor);
        Assert.Equal(
            [
                new DayNote(SampleMonday.AddDays(4), SampleMonday.AddDays(4), "Prep study day: closed for Prep"),
                new DayNote(SampleMonday.AddDays(14), SampleMonday.AddDays(20), Openings.NothingYet),
                new DayNote(SampleMonday.AddDays(21), SampleMonday.AddDays(25), "Autumn break: closed"),
            ],
            found.NotSearched);
        Assert.NotEmpty(found.Openings);
        Assert.All(found.Openings, o => Assert.True(o.Date != SampleMonday.AddDays(4) && o.Date < SampleMonday.AddDays(14)));

        // A whole-building class is closed only when the building is: Prep's study day does not stop it.
        var everyone = Openings.Find(t, ask with { Class = NewClass("00:15", Audience.Everyone) }, SampleMonday, Early, Editor);
        Assert.DoesNotContain(everyone.NotSearched, n => n.Why.StartsWith("Prep study day"));
    }

    [Fact]
    public void TodayStartsAfterNowAndTheDaysBeforeAreNotLookedIn()
    {
        var t = SampleSchool.Create(SampleMonday, SampleMonday);
        var ba2 = Id(t.Groups, g => g.Name == "BA 2");
        var wednesday = SampleMonday.AddDays(2);
        var ask = new OpeningAsk { Class = NewClass("00:30", Audience.ForGroup(ba2)), From = SampleMonday, To = wednesday };
        var found = Openings.Find(t, ask, wednesday, new TimeOnly(13, 10), Editor);
        Assert.Equal([(wednesday, "13:15", "17:30")], found.Openings.Select(When));
        var onTheQuarter = Openings.Find(t, ask, wednesday, new TimeOnly(13, 15), Editor);
        Assert.Equal([(wednesday, "13:30", "17:30")], onTheQuarter.Openings.Select(When));
    }

    [Fact]
    public void AClassLookedForAsItselfIsNotInItsOwnWayItsOwnTimeIsNotOfferedAndEachPartMovesWithIt()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var dag = b.Facilitator("Dag");
        var s = b.Session(Monday, "13:00", "15:00", null, null, null, In(ada), In(dag, from: "14:00"));
        b.Session(Monday, "09:00", "10:00", null, null, null, In(dag));
        var morning = new OpeningAsk { Class = s, From = Monday, To = Monday, DayStart = new(8, 0), DayEnd = new(12, 0) };
        // Dag is in its second hour only: 09:00 puts that hour at 10:00, after his other class.
        Assert.Equal([(Monday, "09:00", "10:00")], Openings.Find(b.T, morning, Today, Early, Editor).Openings.Select(When));
        var afternoon = morning with { DayStart = new(12, 0), DayEnd = new(17, 0) };
        Assert.Equal([(Monday, "12:00", "12:45"), (Monday, "13:15", "15:00")], Openings.Find(b.T, afternoon, Today, Early, Editor).Openings.Select(When));
    }

    [Fact]
    public void AnEventNamingSomebodyKeepsThemBusyWhenTheReaderMaySeeItAndAnyEventBooksItsSpace()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var studio = b.Room("Studio B");
        b.Session(Monday, "07:00", "07:15");     // the week has something in it
        var panel = new SchoolEvent { Title = "Audition panel", Date = Monday, Start = new(10, 0), End = new(11, 0), Audience = EventAudience.Staff, FacilitatorIds = [ada.Id] };
        b.T = b.T with { Events = b.T.Events.Put(panel) };
        var ask = new OpeningAsk { Class = NewClass("01:00", null, In(ada)), From = Monday, To = Monday, DayStart = new(9, 0), DayEnd = new(12, 0) };
        Assert.Equal([(Monday, "09:00", "09:00"), (Monday, "11:00", "11:00")], Openings.Find(b.T, ask, Today, Early, Editor).Openings.Select(When));

        // One only administrators may read neither keeps Ada busy for an editor nor says why; it does for an administrator.
        b.T = b.T with { Events = b.T.Events.Put(panel with { Audience = EventAudience.Administrators }) };
        Assert.Equal([(Monday, "09:00", "11:00")], Openings.Find(b.T, ask, Today, Early, Editor).Openings.Select(When));
        Assert.Equal([(Monday, "09:00", "09:00"), (Monday, "11:00", "11:00")], Openings.Find(b.T, ask, Today, Early, Admin).Openings.Select(When));

        // A space with an event in it is booked whoever may read the event, and nothing says what it is.
        var talks = new SchoolEvent { Title = "Salary talks", Date = Monday, Start = new(10, 0), End = new(11, 0), Audience = EventAudience.Administrators, RoomId = studio.Id };
        b.T = b.T with { Events = b.T.Events.Remove(panel.Id).Put(talks) };
        var room = new OpeningAsk { Class = NewClass("01:00"), Spaces = [studio.Id], From = Monday, To = Monday, DayStart = new(9, 0), DayEnd = new(12, 0) };
        var found = Openings.Find(b.T, room, Today, Early, Editor);
        Assert.Equal([(Monday, "09:00", "09:00"), (Monday, "11:00", "11:00")], found.Openings.Select(When));
        Assert.DoesNotContain(found.Openings.SelectMany(o => o.Notes), n => n.Text.Contains("Salary"));
        var allDay = talks with { AllDay = true };
        b.T = b.T with { Events = b.T.Events.Put(allDay) };
        Assert.Empty(Openings.Find(b.T, room, Today, Early, Editor).Openings);

        // An event naming a student keeps their group from that time.
        var ba = b.Group("BA");
        var sam = b.Student("Sam", ba);
        var trip = new SchoolEvent { Title = "Physio", Date = Monday, Start = new(10, 0), End = new(11, 0), Audience = EventAudience.Students, StudentIds = [sam.Id] };
        b.T = b.T with { Events = b.T.Events.Remove(talks.Id).Put(trip) };
        var group = new OpeningAsk { Class = NewClass("01:00", Audience.ForGroup(ba.Id)), From = Monday, To = Monday, DayStart = new(9, 0), DayEnd = new(12, 0) };
        Assert.Equal([(Monday, "09:00", "09:00"), (Monday, "11:00", "11:00")], Openings.Find(b.T, group, Today, Early, Editor).Openings.Select(When));
    }

    [Fact]
    public void ACancelledOrClosedClassKeepsNobodyBusy()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var ba = b.Group("BA");
        var cancelled = b.Session(Monday, "10:00", "11:00", Audience.ForGroup(ba.Id), null, null, In(ada));
        b.Put(cancelled with { Cancelled = true });
        var ask = new OpeningAsk { Class = NewClass("01:00", Audience.ForGroup(ba.Id), In(ada)), From = Monday, To = Monday, DayStart = new(9, 0), DayEnd = new(12, 0) };
        Assert.Equal([(Monday, "09:00", "11:00")], Openings.Find(b.T, ask, Today, Early, Editor).Openings.Select(When));
    }

    [Fact]
    public void BesideATimeIsHowItSitsInEachPersonsAndGroupsDay()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var ba = b.Group("BA");
        var ma = b.Group("MA");
        var lunch = b.Class("Lunch", Workload.NotCounted);
        b.Session(Monday, "11:00", "12:00", Audience.ForGroup(ba.Id), null, null, In(ada));
        b.Session(Monday, "12:00", "12:45", Audience.Everyone, null, lunch);
        var ask = new OpeningAsk { Class = NewClass("01:00", Audience.ForGroups([ba.Id, ma.Id]), In(ada), In(bo)), From = Monday, To = Monday, DayStart = new(10, 0), DayEnd = new(11, 0) };
        var only = Openings.Find(b.T, ask, Today, Early, Editor).Openings.Single();
        // Lunch is not "something else" for a group: nobody counts it as their day.
        Assert.Equal(
            [
                new OpeningNote("Bo has nothing else that day", false),
                new OpeningNote("MA has nothing else that day", false),
                new OpeningNote("Ada goes straight on to Class", true),
                new OpeningNote("BA goes straight on to Class", true),
            ],
            only.Notes);
    }

    [Fact]
    public void AnOpenSessionLeavesItsGroupFreeButKeepsItsSpaceAndLunchKeepsEverybody()
    {
        var b = new Build();
        var ba = b.Group("BA");
        var top = b.Room("Big Top");
        var studio = b.Room("Studio");
        var open = b.Class("Open training", Workload.DropIn);
        var lunch = b.Class("Lunch", Workload.NotCounted);
        b.Session(Monday, "13:00", "17:00", Audience.Everyone, top, open);
        b.Session(Monday, "12:00", "12:45", Audience.Everyone, null, lunch);
        var ask = new OpeningAsk { Class = NewClass("01:00", Audience.ForGroup(ba.Id)), Spaces = [top.Id, studio.Id], From = Monday, To = Monday, DayStart = new(11, 0), DayEnd = new(14, 0) };
        var found = Openings.Find(b.T, ask, Today, Early, Editor);
        Assert.Equal([(Monday, "11:00", "11:00", "Big Top, Studio"), (Monday, "12:45", "13:00", "Studio")], found.Openings.Select(o => Row(b.T, o)));
    }

    [Fact]
    public void AStudentOnAnotherGroupsClassKeepsTheirGroupFromThatTime()
    {
        var b = new Build();
        var a = b.Group("A");
        var bGroup = b.Group("B");
        var sam = b.Student("Sam", bGroup);
        var other = b.Session(Monday, "10:00", "11:00", Audience.ForGroup(a.Id));
        b.Put(other with { AddedStudentIds = [sam.Id] });
        var ask = new OpeningAsk { Class = NewClass("01:00", Audience.ForGroup(bGroup.Id)), From = Monday, To = Monday, DayStart = new(9, 0), DayEnd = new(12, 0) };
        Assert.Equal([(Monday, "09:00", "09:00"), (Monday, "11:00", "11:00")], Openings.Find(b.T, ask, Today, Early, Editor).Openings.Select(When));
    }

    [Fact]
    public void AWeekendIsLookedInOnlyWhenTheSchoolHasAClassThatDay()
    {
        var b = new Build();
        b.Session(Monday, "07:00", "07:15");
        b.Session(Monday.AddDays(5), "07:00", "07:15");     // a Saturday class
        var ask = new OpeningAsk { Class = NewClass("01:00"), From = Monday, To = Monday.AddDays(6), DayStart = new(9, 0), DayEnd = new(10, 0) };
        Assert.Equal(Enumerable.Range(0, 6).Select(i => Monday.AddDays(i)), Openings.Find(b.T, ask, Today, Early, Editor).Openings.Select(o => o.Date));
    }

    [Fact]
    public void ADayWithNoTimeSaysWhy()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var ba = b.Group("BA");
        var room = b.Room("Studio");
        b.Away(ada, Monday);
        b.Session(Monday.AddDays(1), "09:00", "17:00", Audience.ForGroup(ba.Id));
        b.Session(Monday.AddDays(2), "09:45", "17:00", Audience.ForGroup(ba.Id));
        b.Session(Monday.AddDays(3), "09:00", "17:00", Audience.Everyone, room);
        var ask = new OpeningAsk { Class = NewClass("01:00", Audience.ForGroup(ba.Id), In(ada), In(bo)), From = Monday, To = Monday, DayStart = new(9, 0), DayEnd = new(17, 0) };
        Assert.Equal([new DayNote(Monday, Monday, "Ada is away")], Openings.Find(b.T, ask, Today, Early, Editor).NoTime);
        var tuesday = ask with { Class = NewClass("01:00", Audience.ForGroup(ba.Id)), From = Monday.AddDays(1), To = Monday.AddDays(1) };
        Assert.Equal("Never all free at once", Openings.Find(b.T, tuesday, Today, Early, Editor).NoTime.Single().Why);
        var wednesday = tuesday with { From = Monday.AddDays(2), To = Monday.AddDays(2) };
        Assert.Equal("Free together for 45m at most", Openings.Find(b.T, wednesday, Today, Early, Editor).NoTime.Single().Why);
        var thursday = new OpeningAsk { Class = NewClass("01:00"), Spaces = [room.Id], From = Monday.AddDays(3), To = Monday.AddDays(3), DayStart = new(9, 0), DayEnd = new(17, 0) };
        var found = Openings.Find(b.T, thursday, Today, Early, Editor);
        Assert.Empty(found.Openings);
        Assert.Equal("No space is free when everybody is", found.NoTime.Single().Why);

        // Away the morning, and the group busy all afternoon: nobody is away all day, they are never free together.
        b.Away(ada, Monday.AddDays(4), "09:00", "13:00");
        b.Session(Monday.AddDays(4), "13:00", "17:00", Audience.ForGroup(ba.Id));
        var friday = ask with { From = Monday.AddDays(4), To = Monday.AddDays(4) };
        Assert.Equal("Never all free at once", Openings.Find(b.T, friday, Today, Early, Editor).NoTime.Single().Why);

        // The same reason on a Friday and the Monday after is one note, over the weekend.
        b.Away(ada, Monday.AddDays(7));
        b.Session(Monday.AddDays(8), "07:00", "07:15");
        b.T = b.T with { Absences = b.T.Absences.Put(new Absence { Person = AbsentPerson.Facilitator, PersonId = ada.Id, From = Monday.AddDays(4), To = Monday.AddDays(4) }) };
        var over = ask with { From = Monday.AddDays(4), To = Monday.AddDays(7) };
        Assert.Equal([new DayNote(Monday.AddDays(4), Monday.AddDays(7), "Ada is away")], Openings.Find(b.T, over, Today, Early, Editor).NoTime);
    }

    [Fact]
    public void TheListStopsAtThirtyAndSaysHowManyMore()
    {
        var b = new Build();
        for (int w = 0; w < 8; w++) b.Session(Monday.AddDays(7 * w), "07:00", "07:15");
        var ask = new OpeningAsk { Class = NewClass("01:00"), From = Monday, To = Monday.AddDays(7 * 8 - 3), DayStart = new(9, 0), DayEnd = new(10, 0) };
        var found = Openings.Find(b.T, ask, Today, Early, Editor);
        Assert.Equal(Openings.Most, found.Openings.Count);
        Assert.Equal(40 - Openings.Most, found.More);
        var far = Openings.Find(b.T, ask with { To = Monday.AddDays(1000) }, Today, Early, Editor);
        Assert.Equal(Monday.AddDays(Openings.MostDays), far.NotSearched[^1].To);
    }
}
