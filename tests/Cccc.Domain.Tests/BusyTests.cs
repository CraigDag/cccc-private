using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>When the people, groups and space a chosen class needs are elsewhere: the stretches the plan shades and Find a time leaves out.</summary>
public class BusyTests
{
    private static readonly EventViewer Editor = new(false, true, false, false, false);
    private static readonly EventViewer Admin = EventViewer.Administrator;

    private static (BusyKind, string, string) At(BusyStretch b) => (b.Kind, Week.Hm(b.From), Week.Hm(b.Until));

    [Fact]
    public void ItsPeopleAreBusyInAnotherRoomForTheirPartOfItCoveringIncludedWhileAwayAndAtAnEventTheReaderMaySee()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var chosen = b.Session(Monday, "10:00", "11:00", null, null, null, In(ada));
        var juggling = b.Session(Monday, "09:00", "10:00", null, null, b.Class("Juggling"), In(ada));
        b.Session(Monday, "11:00", "13:00", null, null, b.Class("Creation lab"), In(bo), In(ada, from: "12:00"));
        var covered = b.Session(Monday, "14:00", "15:00", null, null, b.Class("Tumbling"), In(bo));
        b.Put(covered with { CoverIds = [ada.Id] });
        b.Away(bo, Monday);
        b.Away(ada, Monday, "15:30", "16:00");
        b.Away(ada, Monday.AddDays(1));                  // another day is none of Monday's
        var cancelled = b.Session(Monday, "16:00", "16:30", null, null, null, In(ada));
        b.Put(cancelled with { Cancelled = true });
        var meeting = new SchoolEvent { Title = "Staff meeting", Date = Monday, Start = new(16, 30), End = new(17, 0), Audience = EventAudience.Staff, FacilitatorIds = [ada.Id] };
        var talks = new SchoolEvent { Title = "Salary talks", Date = Monday, Start = new(17, 0), End = new(17, 30), Audience = EventAudience.Administrators, FacilitatorIds = [ada.Id] };
        b.T = b.T with { Events = b.T.Events.Put(meeting).Put(talks) };

        var busy = Busy.For(b.T, chosen, Monday, Editor);
        // Its own time is never in its own way; Ada is in only the second hour of the creation lab; Bo is not on it at all.
        Assert.Equal(
            [
                (BusyKind.Teaching, "09:00", "10:00"),
                (BusyKind.Teaching, "12:00", "13:00"),
                (BusyKind.Teaching, "14:00", "15:00"),
                (BusyKind.Away, "15:30", "16:00"),
                (BusyKind.Event, "16:30", "17:00"),
            ],
            busy.Select(At));
        Assert.All(busy, x => Assert.Equal(ada.Id, x.PersonId));
        Assert.Equal(juggling.Id, busy[0].Class!.Session.Id);
        Assert.Equal(covered.Id, busy[2].Class!.Session.Id);
        Assert.Null(busy[3].Class);
        Assert.Equal(meeting.Id, busy[4].Event!.Id);

        // An event only administrators may read keeps Ada busy for an administrator, and for nobody else.
        Assert.Contains(Busy.For(b.T, chosen, Monday, Admin), x => x.Event?.Id == talks.Id);
        // Away all day is the whole of it.
        Assert.Equal([(BusyKind.Away, "00:00", "23:59")], Busy.For(b.T, chosen, Monday.AddDays(1), Editor).Select(At));
        Assert.Equal(TimeOnly.MaxValue, Busy.For(b.T, chosen, Monday.AddDays(1), Editor).Single().Until);
    }

    [Fact]
    public void ItsGroupsAreBusyInAnyOtherClassForThemLunchIncludedAndItsStudentsInAnyTheyAreOnButNobodyAtAnOpenSession()
    {
        var b = new Build();
        var ba = b.Group("BA");
        var ma = b.Group("MA");
        var other = b.Group("Prep");
        var sam = b.Student("Sam", ba);
        var kim = b.Student("Kim", ma);
        var lea = b.Student("Lea", other);
        var chosen = b.Session(Monday, "10:00", "11:00", Audience.ForGroups([ba.Id, ma.Id]));
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(ma.Id), null, b.Class("Dance"));
        var extra = b.Session(Monday, "11:00", "12:00", Audience.ForGroup(other.Id), null, b.Class("Hand to hand"));
        b.Put(extra with { AddedStudentIds = [sam.Id, kim.Id] });
        b.Session(Monday, "12:00", "12:45", Audience.Everyone, null, b.Class("Lunch", Workload.NotCounted));
        b.Session(Monday, "13:00", "17:00", Audience.Everyone, null, b.Class("Open training", Workload.DropIn));
        b.Session(Monday, "15:00", "16:00", Audience.ForGroup(other.Id), null, b.Class("Prep only"));
        var physio = new SchoolEvent { Title = "Physio", Date = Monday, Start = new(17, 0), End = new(17, 30), Audience = EventAudience.Students, StudentIds = [sam.Id] };
        var hers = new SchoolEvent { Title = "Physio", Date = Monday, Start = new(16, 0), End = new(16, 30), Audience = EventAudience.Students, StudentIds = [lea.Id] };
        b.T = b.T with { Events = b.T.Events.Put(physio).Put(hers) };

        var busy = Busy.For(b.T, chosen, Monday, Editor);
        Assert.Equal(
            [
                (BusyKind.Group, "09:00", "10:00"),
                (BusyKind.Students, "11:00", "12:00"),
                (BusyKind.Group, "12:00", "12:45"),
                (BusyKind.Event, "17:00", "17:30"),
            ],
            busy.Select(At));
        Assert.Equal([ma.Id], busy[0].GroupIds);
        Assert.Equal(2, busy[1].Students);
        // Lunch is on for the whole building, so for both its groups, by name.
        Assert.Equal([ba.Id, ma.Id], busy[2].GroupIds);
        Assert.Equal(1, busy[3].Students);
        Assert.Null(busy[3].PersonId);

        // A whole-building class needs every group, so Prep's own class keeps it too.
        var everyone = chosen with { Audience = Audience.Everyone };
        Assert.Contains(Busy.For(b.T, everyone, Monday, Editor), x => x.Kind == BusyKind.Group && Week.Hm(x.From) == "15:00");
    }

    [Fact]
    public void ItsSpaceIsBusyInAnotherClassAndAtAnyEventInItWhoeverMayReadItAndOnlyAReaderWhoMaySeeTheEventIsToldWhatItIs()
    {
        var b = new Build();
        var studio = b.Room("Studio");
        var hall = b.Room("Hall");
        var chosen = b.Session(Monday, "10:00", "11:00", null, studio);
        b.Session(Monday, "09:00", "10:00", null, studio, b.Class("Aerial"));
        b.Session(Monday, "11:00", "12:00", null, hall, b.Class("Elsewhere"));
        var talks = new SchoolEvent { Title = "Salary talks", Date = Monday, Start = new(14, 0), End = new(15, 0), Audience = EventAudience.Administrators, RoomId = studio.Id };
        b.T = b.T with { Events = b.T.Events.Put(talks) };

        var busy = Busy.For(b.T, chosen, Monday, Editor);
        Assert.Equal([(BusyKind.Space, "09:00", "10:00"), (BusyKind.Space, "14:00", "15:00")], busy.Select(At));
        Assert.All(busy, x => Assert.Equal(studio.Id, x.RoomId));
        Assert.Null(busy[1].Event);
        Assert.Equal(talks.Id, Busy.For(b.T, chosen, Monday, Admin)[1].Event!.Id);
        // A class with no space keeps no space busy.
        Assert.Empty(Busy.For(b.T, chosen with { RoomId = null }, Monday, Admin));

        // Another time somewhere else is not kept from the hours its own space is in use.
        var hallOnly = new OpeningAsk { Class = chosen, Spaces = [hall.Id], From = Monday, To = Monday, DayStart = new(9, 0), DayEnd = new(12, 0) };
        Assert.Equal([("09:00", "09:45")], Openings.Find(b.T, hallOnly, Today, new TimeOnly(7, 0), Editor).Openings.Select(o => (Week.Hm(o.Start), Week.Hm(o.Latest))));
    }

    [Fact]
    public void FindATimeOffersAQuarterHourExactlyWhenNothingTheClassNeedsIsBusyThen()
    {
        // The made-up school's week, for a class of each of its groups' with two of its facilitators.
        var monday = new DateOnly(2026, 11, 16);
        var t = SampleSchool.Create(monday, monday);
        var ada = t.Facilitators.Single(f => f.Name == "Ada Brightwater").Id;
        var asked = 0;
        foreach (var g in t.Groups.Where(g => t.Programmes.Find(g.ProgrammeId) is not { IsGuest: true }))
        {
            var other = t.Sessions.Where(s => s.WeekMonday == monday && s.Audience.IsFor(g.Id)).SelectMany(s => s.FacilitatorIds).FirstOrDefault(id => id != ada);
            var c = new Session { Title = "Extra", Start = new(9, 0), End = new(10, 30), Audience = Audience.ForGroup(g.Id), Facilitators = [new Assignment(ada), new Assignment(other, From: new(9, 45))] };
            var ask = new OpeningAsk { Class = c, From = monday, To = monday.AddDays(4), DayStart = new(8, 0), DayEnd = new(18, 0) };
            var offered = Openings.Find(t, ask, monday, new TimeOnly(7, 0), Editor).Openings
                .SelectMany(o => Enumerable.Range(0, (int)(o.Latest - o.Start).TotalMinutes / 15 + 1).Select(i => (o.Date, o.Start.AddMinutes(i * 15))))
                .ToHashSet();
            for (var day = monday; day <= monday.AddDays(4); day = day.AddDays(1))
            {
                var busy = Busy.For(t, c, day, Editor);
                bool nothingIn = Occurrences.ClosureFor(t, day, c) is not null || !t.SessionsInWeek(day).Any();
                for (var at = new TimeOnly(8, 0); at.AddMinutes(90) <= new TimeOnly(18, 0); at = at.AddMinutes(15))
                {
                    var placed = Openings.Placed(c, day, at, null);
                    bool free = !nothingIn && busy.Where(x => x.Kind != BusyKind.Space).All(x =>
                    {
                        var (a, z) = x.PersonId is { } id ? TimeWindows.Assigned(placed, id) : (placed.Start, placed.End);
                        return z <= a || !x.Overlaps(a, z);
                    });
                    Assert.True(free == offered.Contains((day, at)), $"{g.Name}, {day:ddd} {Week.Hm(at)}: busy says {(free ? "free" : "busy")}.");
                    asked++;
                }
            }
        }
        Assert.True(asked > 500, $"Only {asked} times were asked about.");
    }
}
