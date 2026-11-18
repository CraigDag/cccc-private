using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// Ways out of a clash: the same time in a space like it when only the space clashes, then another time the same
/// day in its own space, nearest first, each put to the clash check itself, and one press to take it.
/// </summary>
public class WaysOutTests
{
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly TimeOnly Early = new(7, 0);

    private static IReadOnlyList<WayOut> Ways(Timetable t, Session s, DateOnly? today = null, TimeOnly? now = null) =>
        WaysOut.For(t, s.Id, today ?? Today, now ?? Early, EventViewer.Administrator);

    /// <summary>Each way out, short: where, when, and whether only the space moved.</summary>
    private static List<string> Said(Timetable t, IEnumerable<WayOut> ways) =>
        ways.Select(w => $"{(w.Moved.RoomId is { } r ? t.Rooms[r]!.Name : "no space")} {w.Moved.Start:HH:mm}–{w.Moved.End:HH:mm}{(w.SameTime ? " same time" : "")}").ToList();

    private static string Json(Session? s) => System.Text.Json.JsonSerializer.Serialize(s, ModelJson.Options);

    private static Room Space(Build b, string name, string? kind)
    {
        var r = new Room { Name = name, Category = kind };
        b.T = b.T with { Rooms = b.T.Rooms.Put(r) };
        return r;
    }

    private static void NoClashLeft(Timetable t, IEnumerable<WayOut> ways)
    {
        foreach (var w in ways)
        {
            var after = t with { Sessions = t.Sessions.Put(w.Moved) };
            Assert.Empty(WaysOut.InClash(after, w.Moved));
        }
    }

    [Fact]
    public void OnlyItsSpaceClashingIsTheSameTimeInASpaceLikeItFirstThenTheNearestTimeInItsOwn()
    {
        var b = new Build();
        var (ada, bo, cleo) = (b.Facilitator("Ada"), b.Facilitator("Bo"), b.Facilitator("Cleo"));
        var (ba, prep, ma) = (b.Group("BA 1"), b.Group("Prep"), b.Group("MA"));
        var studioA = Space(b, "Studio A", "Studio");
        var studioB = Space(b, "Studio B", "Studio");
        var studioC = Space(b, "Studio C", "Studio");
        Space(b, "Studio D", "Studio");
        var tent = Space(b, "Tent", "Hall");
        var gym = Space(b, "Gym", "Gym");
        var annex = Space(b, "Studio Annex", "Studio");
        b.T = b.T with { Rooms = b.T.Rooms.Put(annex with { FormerSince = Monday.AddDays(-30) }) };
        var juggling = b.Class("Juggling");
        // Juggling has been in the Tent twice and Studio C once, never in the Gym or Studio B.
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(prep.Id), tent, juggling, In(bo));
        b.Session(Monday.AddDays(2), "09:00", "10:00", Audience.ForGroup(prep.Id), tent, juggling, In(bo));
        b.Session(Monday.AddDays(3), "09:00", "10:00", Audience.ForGroup(prep.Id), studioC, juggling, In(bo));
        b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(ba.Id), studioA, b.Class("Dance"), In(ada));
        var c = b.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(prep.Id), studioA, juggling, In(bo));
        b.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(ma.Id), studioC, b.Class("Aerial"), In(cleo));
        var t = b.T;
        Assert.Equal([ClashKind.RoomDoubleBooked], WaysOut.InClash(t, c).Select(x => x.Kind));

        // The Tent first (it has been there most), Studio C busy then, then the studios by name, two at most;
        // the Gym is neither, and the Studio Annex is a former space. Then Studio A after the dance, which is
        // nearer than before it.
        var ways = Ways(t, c);
        Assert.Equal(["Tent 10:00–11:00 same time", "Studio B 10:00–11:00 same time", "Studio A 11:00–12:00"], Said(t, ways));
        Assert.Equal([("Juggling has been here before", true), ("Juggling has not been here before", false)],
            ways.Take(2).Select(w => (w.Notes.Single().Text, w.Notes.Single().Good)));
        NoClashLeft(t, ways);
        Assert.All(ways, w => Assert.Equal(c.Id, w.Moved.Id));
        Assert.DoesNotContain(ways, w => w.Moved.RoomId == gym.Id);

        // An event in Studio B books it: then the next studio.
        var booked = t with { Events = t.Events.Put(new SchoolEvent { Title = "Visit", Date = Tuesday, Start = new(10, 30), End = new(11, 30), RoomId = studioB.Id }) };
        Assert.Equal(["Tent 10:00–11:00 same time", "Studio D 10:00–11:00 same time", "Studio A 11:00–12:00"], Said(booked, Ways(booked, c)));
    }

    [Fact]
    public void APersonOrAGroupInTwoPlacesIsAnotherTimeOnlyWhenTheyAreFreeAndInsideTheirHours()
    {
        var b = new Build();
        var (ada, bo) = (b.Facilitator("Ada"), b.Facilitator("Bo"));
        var (ba, prep) = (b.Group("BA 1"), b.Group("Prep"));
        var studioA = Space(b, "Studio A", "Studio");
        var studioB = Space(b, "Studio B", "Studio");
        var bigTop = Space(b, "Big Top", "Hall");
        var c = b.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(prep.Id), studioA, b.Class("Juggling"), In(bo));
        // Bo teaches in the Big Top at the same time: a space alone is no way out of that.
        b.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(ba.Id), bigTop, b.Class("Acrobatics"), In(bo));
        var t = b.T;
        Assert.Equal([ClashKind.FacilitatorDoubleBooked], WaysOut.InClash(t, c).Select(x => x.Kind));
        var ways = Ways(t, c);
        Assert.Equal(["Studio A 09:00–10:00", "Studio A 11:00–12:00"], Said(t, ways));
        Assert.DoesNotContain(ways, w => w.SameTime);
        NoClashLeft(t, ways);

        // Bo's hours end at 11:30 on Tuesdays: 11:00–12:00 would put him outside them, so it is no way out.
        var bos = t.Facilitators[bo.Id]! with { Availability = [new AvailabilityWindow(DayOfWeek.Tuesday, new(8, 0), new(11, 30))] };
        var hours = t with { Facilitators = t.Facilitators.Put(bos) };
        Assert.Equal(["Studio A 09:00–10:00"], Said(hours, Ways(hours, c)));

        // The same group twice at once is another time too, never another space.
        var g = new Build();
        var group = g.Group("BA 1");
        var (x, y) = (g.Facilitator("X"), g.Facilitator("Y"));
        var one = Space(g, "One", null);
        var two = Space(g, "Two", null);
        var first = g.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(group.Id), one, null, In(x));
        g.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(group.Id), two, null, In(y));
        Assert.Equal([ClashKind.GroupDoubleBooked], WaysOut.InClash(g.T, first).Select(k => k.Kind));
        Assert.Equal(["One 09:00–10:00", "One 11:00–12:00"], Said(g.T, Ways(g.T, first)));
    }

    [Fact]
    public void ItsOwnSpaceFullAllDayMovesBothAndNoSpaceAtAllNeedsNone()
    {
        var b = new Build();
        var (ada, bo) = (b.Facilitator("Ada"), b.Facilitator("Bo"));
        var (ba, prep) = (b.Group("BA 1"), b.Group("Prep"));
        var studioA = Space(b, "Studio A", "Studio");
        var studioB = Space(b, "Studio B", "Studio");
        var bigTop = Space(b, "Big Top", "Hall");
        // Studio A has a class all day; Bo is in the Big Top at 10:00 as well as in Studio A.
        b.Session(Tuesday, "08:00", "18:00", Audience.ForGroup(ba.Id), studioA, b.Class("Creation"), In(ada));
        var c = b.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(prep.Id), studioA, b.Class("Juggling"), In(bo));
        b.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(prep.Id), bigTop, b.Class("Acrobatics"), In(bo));
        var t = b.T;
        // Not only its space clashes, and its own space has no other time: another time in a space like it.
        Assert.Equal(["Studio B 09:00–10:00", "Studio B 11:00–12:00"], Said(t, Ways(t, c)));
        NoClashLeft(t, Ways(t, c));

        Assert.Equal("Moved Juggling to 09:00 in Studio B", Edits.TakeWayOut(t, c.Id, Tuesday, new(9, 0), studioB.Id, Today, Early, EventViewer.Administrator).Label);

        // A class with no space is moved in time only, and needs none.
        var nowhere = c with { RoomId = null };
        var t2 = t with { Sessions = t.Sessions.Put(nowhere) };
        Assert.Equal(["no space 09:00–10:00", "no space 11:00–12:00"], Said(t2, Ways(t2, nowhere)));

        // An event in its own space all day is never a way out either: another time in a space like it.
        var open = t with
        {
            Sessions = t.Sessions.Remove(t.Sessions.Single(x => x.Title == "Creation").Id),
            Events = t.Events.Put(new SchoolEvent { Title = "Open day", Date = Tuesday, Start = new(8, 0), End = new(18, 0), RoomId = studioA.Id }),
        };
        Assert.Equal(["Studio B 09:00–10:00", "Studio B 11:00–12:00"], Said(open, Ways(open, c)));
    }

    [Fact]
    public void NoClashACancelledOneOneStartedOrAWeekEndedHasNoneAndTodayLooksOnlyAheadOfNow()
    {
        var b = new Build();
        var (ada, bo) = (b.Facilitator("Ada"), b.Facilitator("Bo"));
        var (ba, prep) = (b.Group("BA 1"), b.Group("Prep"));
        var studioA = Space(b, "Studio A", "Studio");
        var studioB = Space(b, "Studio B", "Studio");
        var other = b.Session(Monday, "09:00", "11:00", Audience.ForGroup(ba.Id), studioA, null, In(ada));
        var c = b.Session(Monday, "10:00", "11:00", Audience.ForGroup(prep.Id), studioA, null, In(bo));
        var t = b.T;

        // Today, at 09:30: the same time elsewhere, and times from 09:45 on only, never before now.
        var today = Ways(t, c, Monday, new(9, 30));
        Assert.Equal(["Studio B 10:00–11:00 same time", "Studio A 11:00–12:00"], Said(t, today));
        Assert.All(today, w => Assert.True(w.Moved.Start > new TimeOnly(9, 30)));
        Assert.Empty(Ways(t, c, Monday, new(10, 0)));                       // started
        Assert.Empty(Ways(t, c, Monday.AddDays(7)));                         // its week has ended
        var cancelled = t with { Sessions = t.Sessions.Put(other with { Cancelled = true }) };
        Assert.Empty(WaysOut.InClash(cancelled, c));
        Assert.Empty(Ways(cancelled, c));                                    // nothing clashes with it now
        Assert.Empty(Ways(t with { Sessions = t.Sessions.Put(c with { Cancelled = true }) }, c));
        Assert.Empty(WaysOut.For(t, Guid.NewGuid(), Today, Early, EventViewer.Administrator));

        // A class that only needs cover has none: that is Cover's, and moving it would not find its teacher.
        var b2 = new Build();
        var dag = b2.Facilitator("Dag");
        var alone = b2.Session(Monday.AddDays(1), "10:00", "11:00", Audience.ForGroup(b2.Group("BA 2").Id), Space(b2, "Gym", null), null, In(dag));
        b2.Away(dag, Monday.AddDays(1), "09:00", "12:00");
        Assert.Contains(Clashes.ForWeek(b2.T, Monday), x => x.Kind == ClashKind.NeedsCover && x.SessionIds.Contains(alone.Id));
        Assert.Empty(WaysOut.InClash(b2.T, alone));
        Assert.Empty(Ways(b2.T, alone));

        // A clash in the Scratch Week, drawn from its own view, has none: nothing there is on a real day.
        var plan = Week.DateOf(Week.ScratchMonday, DayOfWeek.Monday);
        var copy = c with { Id = Guid.NewGuid(), Date = plan };
        var scratch = ScratchWeek.View(t with { Scratch = EntitySet<Session>.Of([other with { Id = Guid.NewGuid(), Date = plan }, copy]) });
        Assert.NotEmpty(WaysOut.InClash(scratch, copy));
        Assert.Empty(WaysOut.For(scratch, copy.Id, Today, Early, EventViewer.Administrator));
    }

    [Fact]
    public void OutsideTheirHoursIsTheNearestTimeInsideThemSaidWithWhatIsNewAboutIt()
    {
        var b = new Build();
        var bo = b.Facilitator("Bo", max: 1.5);
        var (ba, prep) = (b.Group("BA 1"), b.Group("Prep"));
        var studioA = Space(b, "Studio A", "Studio");
        var bigTop = Space(b, "Big Top", "Hall");
        var c = b.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(prep.Id), studioA, b.Class("Juggling"), In(bo));
        b.Session(Tuesday, "14:00", "15:00", Audience.ForGroup(ba.Id), bigTop, b.Class("Acrobatics"), In(bo));
        var bos = b.T.Facilitators[bo.Id]! with { Availability = [new AvailabilityWindow(DayOfWeek.Tuesday, new(13, 0), new(16, 0))] };
        var t = b.T with { Facilitators = b.T.Facilitators.Put(bos) };
        Assert.Equal([ClashKind.FacilitatorUnavailable], WaysOut.InClash(t, c).Select(x => x.Kind));

        // The stretch after it runs from 10:15 to 13:00, and only its end is inside Bo's hours; the stretch after
        // Acrobatics is inside them from its start. Nothing before it is.
        var ways = Ways(t, c);
        Assert.Equal(["Studio A 13:00–14:00", "Studio A 15:00–16:00"], Said(t, ways));
        NoClashLeft(t, ways);
        // Bo is over the limit wherever in the day it goes, and Prep has nothing else that day wherever it goes:
        // neither is said, since neither is a reason for one time over another.
        Assert.Equal([[("Bo goes straight on to Acrobatics", true)], [("Bo comes straight from Acrobatics", true)]],
            ways.Select(w => w.Notes.Select(n => (n.Text, n.Good)).ToList()));
        Assert.Contains(Clashes.ForWeek(t, Tuesday), x => x.Kind == ClashKind.FacilitatorOverHours);

        // A staff meeting at one of the times is said beside it, as Find a time says it.
        var meeting = t with { Events = t.Events.Put(new SchoolEvent { Title = "Staff meeting", Audience = EventAudience.Facilitators, Date = Tuesday, Start = new(15, 0), End = new(15, 30) }) };
        Assert.Equal([("Staff meeting then, 15:00–15:30", false), ("Bo comes straight from Acrobatics", true)],
            Ways(meeting, c)[1].Notes.Select(n => (n.Text, n.Good)));

        // A long gap a time would make is new, so it is said: in at eight and then nothing until Acrobatics.
        var gappy = t with
        {
            Facilitators = t.Facilitators.Put(bos with
            {
                MaxGap = TimeSpan.FromMinutes(30), MaxWeeklyHours = null,
                Availability = [new AvailabilityWindow(DayOfWeek.Tuesday, new(8, 0), new(9, 0)), new AvailabilityWindow(DayOfWeek.Tuesday, new(14, 0), new(15, 0))],
            }),
        };
        var early = Assert.Single(Ways(gappy, c));
        Assert.Equal("Studio A 08:00–09:00", Said(gappy, [early]).Single());
        Assert.Equal(["Bo has a 5h gap Tue 09:00–14:00 (maximum 30m) — between \"Juggling\" and \"Acrobatics\""],
            early.Notes.Where(n => !n.Good).Select(n => n.Text));
    }

    [Fact]
    public void ASpaceWithNoKindIsLikeNoOtherUnlessItsClassHasBeenThereAndTheDayStretchesToTakeInTheClass()
    {
        var b = new Build();
        var (ada, bo) = (b.Facilitator("Ada"), b.Facilitator("Bo"));
        var (ba, prep) = (b.Group("BA 1"), b.Group("Prep"));
        var studio = Space(b, "Studio", null);
        Space(b, "Office", null);
        var hall = Space(b, "Hall", null);
        var juggling = b.Class("Juggling");
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(prep.Id), hall, juggling, In(bo));
        b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(ba.Id), studio, b.Class("Dance"), In(ada));
        var c = b.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(prep.Id), studio, juggling, In(bo));
        Assert.Equal(["Hall 10:00–11:00 same time", "Studio 11:00–12:00", "Studio 08:00–09:00"], Said(b.T, Ways(b.T, c)));

        // An early class can go earlier than eight, and a late one later than six, as far as it already is.
        var e = new Build();
        var (x, y) = (e.Facilitator("X"), e.Facilitator("Y"));
        var (one, two) = (e.Group("One"), e.Group("Two"));
        var room = Space(e, "Room", null);
        e.Session(Tuesday, "06:30", "07:30", Audience.ForGroup(one.Id), room, null, In(x));
        var early = e.Session(Tuesday, "07:00", "08:00", Audience.ForGroup(two.Id), room, null, In(y));
        e.Session(Tuesday, "08:30", "18:00", Audience.ForGroup(one.Id), room, null, In(x));
        Assert.Equal(["Room 07:30–08:30"], Said(e.T, Ways(e.T, early)));
        // Y teaching two at once in the evening: free until then, and the day goes no later than it already does.
        var late = e.Session(Tuesday, "18:30", "19:30", Audience.ForGroup(two.Id), Space(e, "Other", null), null, In(y));
        e.Session(Tuesday, "18:30", "19:30", Audience.ForGroup(one.Id), room, null, In(y));
        Assert.Equal([ClashKind.FacilitatorDoubleBooked], WaysOut.InClash(e.T, late).Select(k => k.Kind));
        Assert.Equal(["Other 17:30–18:30"], Said(e.T, Ways(e.T, late)));
    }

    [Fact]
    public void TakingOneMovesItAndSaysWhereAndOneNoLongerFreeIsRefused()
    {
        var b = new Build();
        var (ada, bo, cleo) = (b.Facilitator("Ada"), b.Facilitator("Bo"), b.Facilitator("Cleo"));
        var (ba, prep, ma) = (b.Group("BA 1"), b.Group("Prep"), b.Group("MA"));
        var studioA = Space(b, "Studio A", "Studio");
        var studioB = Space(b, "Studio B", "Studio");
        b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(ba.Id), studioA, b.Class("Dance"), In(ada));
        var c = b.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(prep.Id), studioA, b.Class("Juggling"), In(bo));
        var t = b.T;
        var ways = Ways(t, c);
        Assert.Equal(["Studio B 10:00–11:00 same time", "Studio A 11:00–12:00", "Studio A 08:00–09:00"], Said(t, ways));
        // At 11:00 Bo and Prep would come straight from nothing: the class where it is now is not another class.
        Assert.Empty(ways[1].Notes);

        var there = Edits.TakeWayOut(t, c.Id, ways[0].Moved.Date, ways[0].Moved.Start, ways[0].Moved.RoomId, Today, Early, EventViewer.Administrator);
        Assert.Equal("Moved Juggling to Studio B", there.Label);
        var moved = Apply.Batch(t, there);
        Assert.Equal((studioB.Id, new TimeOnly(10, 0)), (moved.Sessions[c.Id]!.RoomId, moved.Sessions[c.Id]!.Start));
        Assert.Empty(WaysOut.InClash(moved, moved.Sessions[c.Id]!));
        Assert.Equal(Json(t.Sessions[c.Id]), Json(Apply.Batch(moved, there.Inverse()).Sessions[c.Id]));

        Assert.Null(moved.Sessions[c.Id]!.MovedFrom);

        // Another time says where it was, as Find a time does once its week has begun; a week ahead does not.
        var later = Edits.TakeWayOut(t, c.Id, Tuesday, new(11, 0), studioA.Id, Today, Early, EventViewer.Administrator);
        Assert.Equal("Moved Juggling to 11:00", later.Label);
        var at11 = Apply.Batch(t, later).Sessions[c.Id]!;
        Assert.Equal((new TimeOnly(11, 0), new TimeOnly(12, 0), new MovedFrom(Tuesday, new(10, 0), new(11, 0))), (at11.Start, at11.End, at11.MovedFrom));
        Assert.Null(Apply.Batch(t, Edits.TakeWayOut(t, c.Id, Tuesday, new(11, 0), studioA.Id, Today.AddDays(-7), Early, EventViewer.Administrator)).Sessions[c.Id]!.MovedFrom);

        // Only a way it was offered can be taken.
        Assert.Throws<EditRefused>(() => Edits.TakeWayOut(t, c.Id, Tuesday, new(11, 0), studioB.Id, Today, Early, EventViewer.Administrator));
        Assert.Throws<EditRefused>(() => Edits.TakeWayOut(t, c.Id, Tuesday, new(12, 0), studioA.Id, Today, Early, EventViewer.Administrator));
        Assert.Throws<EditRefused>(() => Edits.TakeWayOut(t, c.Id, Tuesday.AddDays(1), new(10, 0), studioB.Id, Today, Early, EventViewer.Administrator));

        // Somebody puts a class in Studio B meanwhile: that is no longer a way out.
        var taken = t with { Sessions = t.Sessions.Put(new Session { Date = Tuesday, Start = new(10, 0), End = new(11, 0), Title = "Aerial", RoomId = studioB.Id, Audience = Audience.ForGroup(ma.Id), Facilitators = [new Assignment(cleo.Id)] }) };
        Assert.Contains("no longer a way out", Assert.Throws<EditRefused>(() =>
            Edits.TakeWayOut(taken, c.Id, Tuesday, new(10, 0), studioB.Id, Today, Early, EventViewer.Administrator)).Message);
    }
}
