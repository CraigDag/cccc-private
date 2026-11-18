using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// Making room: when no time is free, the times only one other class is in the way of, with that class moved to one
/// of its ways out so neither is in any clash, and one press for both.
/// </summary>
public class MakeRoomTests
{
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly TimeOnly Early = new(7, 0);
    private static readonly EventViewer Admin = EventViewer.Administrator;

    private static Room Space(Build b, string name, string? kind = "Studio")
    {
        var r = new Room { Name = name, Category = kind };
        b.T = b.T with { Rooms = b.T.Rooms.Put(r) };
        return r;
    }

    private static string Json(Session? s) => System.Text.Json.JsonSerializer.Serialize(s, ModelJson.Options);

    private static string Where(Timetable t, Session s) =>
        $"{(s.RoomId is { } r ? t.Rooms[r]!.Name : "no space")} {s.Date:ddd} {s.Start:HH:mm}–{s.End:HH:mm}";

    /// <summary>Each, short: where the class goes, then where the other class goes.</summary>
    private static List<string> Said(Timetable t, IEnumerable<RoomMade> made) =>
        made.Select(m => $"{Where(t, m.Placed)}, moving {m.Other.Moved.DisplayTitle} to {Where(t, m.Other.Moved)}{(m.Other.SameTime ? " same time" : "")}").ToList();

    /// <summary>Rigging for MA, taught by Bo, for an hour: a class nobody has added yet.</summary>
    private static Session Rigging(Group ma, Facilitator bo) => new()
    {
        Title = "Rigging", Audience = Audience.ForGroup(ma.Id), Facilitators = [new Assignment(bo.Id)],
        Date = Tuesday, Start = new(9, 0), End = new(10, 0),
    };

    private static OpeningAsk Ask(Session c, IEnumerable<Room> spaces, string from = "10:00", string until = "11:00", DateOnly? to = null) => new()
    {
        Class = c, Spaces = [.. spaces.Select(r => r.Id)], From = c.Date, To = to ?? c.Date,
        DayStart = TimeOnly.Parse(from), DayEnd = TimeOnly.Parse(until),
    };

    /// <summary>
    /// Studio A at ten on Tuesday is all Rigging may have, and Dance is in it; Dance has been in Studio B before, and
    /// Studio B is free then.
    /// </summary>
    private sealed record School(Build B, Facilitator Ada, Facilitator Bo, Group Ba, Group Ma, Room StudioA, Room StudioB, Session Dance, Session Rigging);

    private static School Tuesdays()
    {
        var b = new Build();
        var (ada, bo) = (b.Facilitator("Ada"), b.Facilitator("Bo"));
        var (ba, ma) = (b.Group("BA 1"), b.Group("MA"));
        var studioA = Space(b, "Studio A");
        var studioB = Space(b, "Studio B");
        var dance = b.Class("Dance");
        b.Session(Monday, "10:00", "11:00", Audience.ForGroup(ba.Id), studioB, dance, In(ada));
        var inTheWay = b.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(ba.Id), studioA, dance, In(ada));
        return new(b, ada, bo, ba, ma, studioA, studioB, inTheWay, Rigging(ma, bo));
    }

    [Fact]
    public void WhenNothingIsFreeTheOneClassInTheWayMovesToItsWayOutAndTheClassFits()
    {
        var s = Tuesdays();
        var t = s.B.T;
        var ask = Ask(s.Rigging, [s.StudioA]);
        Assert.Empty(Openings.Find(t, ask, Today, Early, Admin).Openings);

        var made = MakeRoom.For(t, ask, Today, Early, Admin);
        Assert.Equal(["Studio A Tue 10:00–11:00, moving Dance to Studio B Tue 10:00–11:00 same time"], Said(t, made));
        Assert.Equal(s.Dance.Id, made[0].Other.Moved.Id);
        Assert.Equal([("Dance has been here before", true)], made[0].Other.Notes.Select(n => (n.Text, n.Good)));
        // What Find a time would say beside the time, as it would be once Dance has gone.
        Assert.Equal(["Bo has nothing else that day", "MA has nothing else that day"], made[0].Notes.Select(n => n.Text));

        // Both in no clash afterwards.
        var after = t with { Sessions = t.Sessions.Put(made[0].Other.Moved).Put(made[0].Placed) };
        Assert.Empty(WaysOut.InClash(after, made[0].Placed));
        Assert.Empty(WaysOut.InClash(after, made[0].Other.Moved));

        // For BA 1 with no space asked, it is Dance's group in the way rather than its space: Dance moves in time,
        // the nearer of an hour either side, the earlier when both are as near.
        var anywhere = Ask(s.Rigging with { Audience = Audience.ForGroup(s.Ba.Id) }, []);
        Assert.Equal(["no space Tue 10:00–11:00, moving Dance to Studio A Tue 09:00–10:00"], Said(t, MakeRoom.For(t, anywhere, Today, Early, Admin)));
    }

    [Fact]
    public void NeverRoundSomebodyAwayAnEventABookedSpaceOrTwoClasses()
    {
        var s = Tuesdays();
        var ask = Ask(s.Rigging, [s.StudioA]);
        Assert.NotEmpty(MakeRoom.For(s.B.T, ask, Today, Early, Admin));

        var away = new Build { T = s.B.T };
        away.Away(s.Bo, Tuesday, "10:00", "10:30");
        Assert.Empty(MakeRoom.For(away.T, ask, Today, Early, Admin));

        var meeting = s.B.T with { Events = s.B.T.Events.Put(new SchoolEvent { Title = "Meeting", Date = Tuesday, Start = new(10, 0), End = new(10, 30), FacilitatorIds = [s.Bo.Id] }) };
        Assert.Empty(MakeRoom.For(meeting, ask, Today, Early, Admin));

        var booked = s.B.T with { Events = s.B.T.Events.Put(new SchoolEvent { Title = "Visit", Date = Tuesday, Start = new(10, 0), End = new(10, 30), RoomId = s.StudioA.Id }) };
        Assert.Empty(MakeRoom.For(booked, ask, Today, Early, Admin));

        // MA's day off: a closure for its groups leaves the day out, as Find a time does.
        var closed = new Build { T = s.B.T };
        closed.Closure(new Closure { Name = "MA showing", From = Tuesday, To = Tuesday, Scope = ClosureScope.Groups, GroupIds = [s.Ma.Id] });
        Assert.Empty(MakeRoom.For(closed.T, ask, Today, Early, Admin));

        // MA in Juggling in the Gym then as well: two classes in the way.
        var two = new Build { T = s.B.T };
        two.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(s.Ma.Id), Space(two, "Gym", "Gym"), two.Class("Juggling"), In(two.Facilitator("Cleo")));
        Assert.Empty(MakeRoom.For(two.T, ask, Today, Early, Admin));
    }

    [Fact]
    public void NotAClassThatHasStartedNorOneWithNoWayOutNorWhereTheClassWouldStillClash()
    {
        var s = Tuesdays();
        var t = s.B.T;
        // Ten past ten on Tuesday: Dance has started, so it stays; and so does any time before now.
        Assert.Empty(MakeRoom.For(t, Ask(s.Rigging, [s.StudioA], "10:15", "11:15"), Tuesday, new(10, 10), Admin));

        // Studio B taken then, and Ada teaching all the rest of the day: Dance has nowhere to go.
        var stuck = new Build { T = t };
        stuck.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(stuck.Group("Prep").Id), s.StudioB, stuck.Class("Aerial"), In(stuck.Facilitator("Cleo")));
        var gym = Space(stuck, "Gym", "Gym");
        stuck.Session(Tuesday, "08:00", "10:00", Audience.ForGroup(s.Ba.Id), gym, stuck.Class("Acrobatics"), In(s.Ada));
        stuck.Session(Tuesday, "11:00", "18:00", Audience.ForGroup(s.Ba.Id), gym, stuck.Class("Acrobatics"), In(s.Ada));
        Assert.Empty(WaysOut.For(stuck.T with { Sessions = stuck.T.Sessions.Put(s.Rigging with { Start = new(10, 0), End = new(11, 0), RoomId = s.StudioA.Id }) }, s.Dance.Id, Today, Early, Admin));
        Assert.Empty(MakeRoom.For(stuck.T, Ask(s.Rigging, [s.StudioA]), Today, Early, Admin));
        // Either studio will do: Dance cannot move, but Aerial can, to nine in its own studio.
        Assert.Equal(["Studio B Tue 10:00–11:00, moving Aerial to Studio B Tue 09:00–10:00"],
            Said(stuck.T, MakeRoom.For(stuck.T, Ask(s.Rigging, [s.StudioA, s.StudioB]), Today, Early, Admin)));

        // Bo only works from half past ten on Tuesdays: Rigging at ten would still be outside his hours.
        var bos = t.Facilitators[s.Bo.Id]! with { Availability = [new AvailabilityWindow(DayOfWeek.Tuesday, new(10, 30), new(18, 0))] };
        Assert.Empty(MakeRoom.For(t with { Facilitators = t.Facilitators.Put(bos) }, Ask(s.Rigging, [s.StudioA]), Today, Early, Admin));
    }

    [Fact]
    public void EachOtherClassIsLookedAtOnceAtTheFirstTimeItAloneIsInTheWay()
    {
        // From nine to noon Dance is in the way of every time from 09:15 to 10:45: one way, not seven.
        var s = Tuesdays();
        var wide = MakeRoom.For(s.B.T, Ask(s.Rigging, [s.StudioA], "09:00", "12:00"), Today, Early, Admin);
        Assert.Equal(["Studio A Tue 09:15–10:15, moving Dance to Studio B Tue 10:00–11:00 same time"], Said(s.B.T, wide));

        // Bo teaching Clown until half past ten as well: before then two classes are in the way, so Dance is
        // looked at first at half past ten, where it alone is, and not given up on before.
        var b = new Build { T = s.B.T };
        b.Session(Tuesday, "09:00", "10:30", Audience.ForGroup(b.Group("Prep").Id), Space(b, "Gym", "Gym"), b.Class("Clown"), In(s.Bo));
        var made = MakeRoom.For(b.T, Ask(s.Rigging, [s.StudioA], "09:30", "11:30"), Today, Early, Admin);
        Assert.Equal(["Studio A Tue 10:30–11:30, moving Dance to Studio B Tue 10:00–11:00 same time"], Said(b.T, made));
        // Looking until a quarter to twelve, Dance alone is in the way at a quarter to eleven too, the last time; it is
        // still half past ten it is looked at first, never earlier when Clown is in the way as well.
        Assert.Equal(["Studio A Tue 10:30–11:30, moving Dance to Studio B Tue 10:00–11:00 same time"],
            Said(b.T, MakeRoom.For(b.T, Ask(s.Rigging, [s.StudioA], "09:30", "11:45"), Today, Early, Admin)));
        // The same with both in the way of its people and groups (Rigging for BA 1, no space asked): Dance goes to
        // the nearest time it can, ending as Rigging begins.
        var forBa = Ask(s.Rigging with { Audience = Audience.ForGroup(s.Ba.Id) }, [], "09:30", "11:30");
        Assert.Equal(["no space Tue 10:30–11:30, moving Dance to Studio A Tue 09:30–10:30"], Said(b.T, MakeRoom.For(b.T, forBa, Today, Early, Admin)));
        Assert.Equal(["no space Tue 10:30–11:30, moving Dance to Studio A Tue 09:30–10:30"],
            Said(b.T, MakeRoom.For(b.T, forBa with { DayEnd = new(11, 45) }, Today, Early, Admin)));

        // Bo in only the second half of Rigging: Clown keeps him from his part of it only, so at ten Dance alone is in
        // the way. (Rigging is at nine as asked about, so his part from half past nine is from half past ten at ten.)
        var half = s.Rigging with { Facilitators = [new Assignment(s.Bo.Id, new(9, 30), null)] };
        Assert.Equal((new TimeOnly(10, 30), new TimeOnly(11, 0)), TimeWindows.Assigned(Openings.Placed(half, Tuesday, new(10, 0), null), s.Bo.Id));
        Assert.Equal(["Studio A Tue 10:00–11:00, moving Dance to Studio B Tue 10:00–11:00 same time"],
            Said(b.T, MakeRoom.For(b.T, Ask(half, [s.StudioA], "10:00", "11:00"), Today, Early, Admin)));
    }

    [Fact]
    public void AClassWithNoWayOutAtTheFirstTimeIsLookedAtAgainAtTheLastAndTheSoonestComesFirst()
    {
        // Ada teaches Dance from nine to eleven and Acrobatics from noon: Rigging with her for two hours before noon
        // has Dance in its way at every time. At nine Dance has nowhere to go; at ten, the last time, it can go an
        // hour earlier, ending as Rigging begins.
        var b = new Build();
        var (ada, bo) = (b.Facilitator("Ada"), b.Facilitator("Bo"));
        var (ba, ma) = (b.Group("BA 1"), b.Group("MA"));
        var (studioA, studioB, gym) = (Space(b, "Studio A"), Space(b, "Studio B"), Space(b, "Gym", "Gym"));
        b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(ba.Id), studioA, b.Class("Dance"), In(ada));
        b.Session(Tuesday, "12:00", "18:00", Audience.ForGroup(ba.Id), gym, b.Class("Acrobatics"), In(ada));
        var ask = Ask(Rigging(ma, bo) with { End = new(11, 0), Facilitators = [new Assignment(ada.Id)] }, [studioB], "09:00", "12:00");
        Assert.Empty(Openings.Find(b.T, ask, Today, Early, Admin).Openings);
        Assert.Equal(["Studio B Tue 10:00–12:00, moving Dance to Studio A Tue 08:00–10:00"], Said(b.T, MakeRoom.For(b.T, ask, Today, Early, Admin)));
        // Studio C asked first, and Mime in it from eleven: Dance is alone in the way there only at nine; at ten it is
        // alone in the way in Studio B, and that is where the class goes.
        var mime = new Build { T = b.T };
        var studioC = Space(mime, "Studio C");
        mime.Session(Tuesday, "11:00", "12:00", Audience.ForGroup(mime.Group("Prep").Id), studioC, mime.Class("Mime"), In(mime.Facilitator("Cleo")));
        var either = ask with { Spaces = [studioC.Id, studioB.Id] };
        Assert.Empty(Openings.Find(mime.T, either, Today, Early, Admin).Openings);
        Assert.Equal(["Studio B Tue 10:00–12:00, moving Dance to Studio A Tue 08:00–10:00"], Said(mime.T, MakeRoom.For(mime.T, either, Today, Early, Admin)));

        // Either studio, Mime in Studio B all morning, and Ada teaching from eleven: Dance is met first, at nine, and
        // makes room only at a quarter to eleven; Mime, met next, makes room at nine, so it is offered first.
        var o = new Build();
        var (ada2, bo2, cleo) = (o.Facilitator("Ada"), o.Facilitator("Bo"), o.Facilitator("Cleo"));
        var (ba2, ma2, prep) = (o.Group("BA 1"), o.Group("MA"), o.Group("Prep"));
        var (a2, b2, gym2) = (Space(o, "Studio A"), Space(o, "Studio B"), Space(o, "Gym", "Gym"));
        o.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(ba2.Id), a2, o.Class("Dance"), In(ada2));
        o.Session(Tuesday, "11:00", "18:00", Audience.ForGroup(ba2.Id), gym2, o.Class("Acrobatics"), In(ada2));
        o.Session(Tuesday, "09:00", "12:00", Audience.ForGroup(prep.Id), b2, o.Class("Mime"), In(cleo));
        var both = Ask(Rigging(ma2, bo2), [a2, b2], "09:00", "11:45");
        Assert.Empty(Openings.Find(o.T, both, Today, Early, Admin).Openings);
        Assert.Equal(["Studio B Tue 09:00–10:00, moving Mime to Studio B Tue 10:00–13:00", "Studio A Tue 10:45–11:45, moving Dance to Studio A Tue 08:45–10:45"],
            Said(o.T, MakeRoom.For(o.T, both, Today, Early, Admin)));

        // Three such Dances in three studios, met first and making room only at a quarter to eleven, and Mime in a
        // fourth studio, met last and making room at nine: three offered, the soonest first, Mime among them.
        var four = new Build();
        var spaces = new List<Room>();
        foreach (var i in new[] { 1, 2, 3 })
        {
            var (teacher, group, studio) = (four.Facilitator("Coach " + i), four.Group("BA " + i), Space(four, "Studio " + i));
            four.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(group.Id), studio, four.Class("Dance " + i), In(teacher));
            four.Session(Tuesday, "11:00", "18:00", Audience.ForGroup(group.Id), null, four.Class("Acrobatics " + i), In(teacher));
            spaces.Add(studio);
        }
        var fourth = Space(four, "Studio 4");
        four.Session(Tuesday, "09:15", "12:00", Audience.ForGroup(four.Group("Prep").Id), fourth, four.Class("Mime"), In(four.Facilitator("Cleo")));
        var any = Ask(Rigging(four.Group("MA"), four.Facilitator("Bo")), [.. spaces, fourth], "09:00", "11:45");
        Assert.Empty(Openings.Find(four.T, any, Today, Early, Admin).Openings);
        Assert.Equal(["Studio 4 Tue 09:00–10:00, moving Mime to Studio 4 Tue 10:00–12:45", "Studio 1 Tue 10:45–11:45, moving Dance 1 to Studio 1 Tue 08:45–10:45",
                "Studio 2 Tue 10:45–11:45, moving Dance 2 to Studio 2 Tue 08:45–10:45"],
            Said(four.T, MakeRoom.For(four.T, any, Today, Early, Admin)));
    }

    [Fact]
    public void NeverBeforeNowNorAtTheTimeAClassAlreadyHas()
    {
        // Ten past ten, with Dance a quarter past: a time from half past nine would already have begun.
        var s = Tuesdays();
        var b = new Build { T = s.B.T };
        b.Put(s.Dance with { Start = new(10, 15), End = new(11, 15) });
        var made = MakeRoom.For(b.T, Ask(s.Rigging, [s.StudioA], "09:30", "11:30"), Tuesday, new(10, 10), Admin);
        Assert.Equal(["Studio A Tue 10:15–11:15, moving Dance to Studio B Tue 10:15–11:15 same time"], Said(b.T, made));

        // Rigging already in Studio A at ten, clashing with Dance: room made there would be Dance's way out, not
        // another time for Rigging.
        var clashing = new Build { T = s.B.T };
        var rigging = clashing.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(s.Ma.Id), s.StudioA, clashing.Class("Rigging"), In(s.Bo));
        Assert.Empty(MakeRoom.For(clashing.T, Ask(rigging, [s.StudioA]), Today, Early, Admin));
    }

    [Fact]
    public void ItLooksAtTwelveOtherClassesAtMost()
    {
        // Three weeks of Studio A taken from ten to eleven, and only the thirteenth class in it able to move.
        var b = new Build();
        var (ada, bo, cleo) = (b.Facilitator("Ada"), b.Facilitator("Bo"), b.Facilitator("Cleo"));
        var (ba, ma, prep) = (b.Group("BA 1"), b.Group("MA"), b.Group("Prep"));
        var studioA = Space(b, "Studio A");
        var studioB = Space(b, "Studio B");
        var gym = Space(b, "Gym", "Gym");
        var days = Enumerable.Range(0, 21).Select(i => Monday.AddDays(i)).Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();
        for (int i = 0; i < days.Count; i++)
        {
            b.Session(days[i], "10:00", "11:00", Audience.ForGroup(ba.Id), studioA, b.Class("Class " + (i + 1)), In(ada));
            if (i == MakeRoom.MostTried) continue;
            // The others are stuck: Studio B taken then, and Ada teaching the rest of the day.
            b.Session(days[i], "10:00", "11:00", Audience.ForGroup(prep.Id), studioB, b.Class("Aerial"), In(cleo));
            b.Session(days[i], "08:00", "10:00", Audience.ForGroup(ba.Id), gym, b.Class("Acrobatics"), In(ada));
            b.Session(days[i], "11:00", "18:00", Audience.ForGroup(ba.Id), gym, b.Class("Acrobatics"), In(ada));
        }
        var ask = Ask(Rigging(ma, bo) with { Date = Monday }, [studioA], to: days[^1]);
        Assert.Empty(MakeRoom.For(b.T, ask, Today, Early, Admin));
        // From the day after the first, the movable class is among the first twelve looked at.
        Assert.Equal(["Class 13"], MakeRoom.For(b.T, ask with { From = days[1] }, Today, Early, Admin).Select(m => m.Other.Moved.DisplayTitle));

        // A day closed for its groups is not looked in, so it spends none of the twelve: with every class able to
        // move and MA away for the first twelve days, the next three are offered.
        var open = new Build();
        var (ada2, bo2) = (open.Facilitator("Ada"), open.Facilitator("Bo"));
        var (ba2, ma2) = (open.Group("BA 1"), open.Group("MA"));
        var a2 = Space(open, "Studio A");
        Space(open, "Studio B");
        for (int i = 0; i < days.Count; i++)
            open.Session(days[i], "10:00", "11:00", Audience.ForGroup(ba2.Id), a2, open.Class("Class " + (i + 1)), In(ada2));
        open.Closure(new Closure { Name = "MA on tour", From = days[0], To = days[MakeRoom.MostTried - 1], Scope = ClosureScope.Groups, GroupIds = [ma2.Id] });
        var away = Ask(Rigging(ma2, bo2) with { Date = Monday }, [a2], to: days[^1]);
        Assert.Equal(["Class 13", "Class 14", "Class 15"], MakeRoom.For(open.T, away, Today, Early, Admin).Select(m => m.Other.Moved.DisplayTitle));

        // Twelve on one day as well: thirteen studios at ten, each with a class whose teacher has the rest of the day
        // taken, but the thirteenth's.
        var one = new Build();
        var studios = new List<Room>();
        for (int i = 1; i <= MakeRoom.MostTried + 1; i++)
        {
            var (teacher, group, studio) = (one.Facilitator("Coach " + i), one.Group("Group " + i), Space(one, "Studio " + i));
            one.Session(Monday, "10:00", "11:00", Audience.ForGroup(group.Id), studio, one.Class("Class " + i), In(teacher));
            if (i <= MakeRoom.MostTried)
            {
                one.Session(Monday, "08:00", "10:00", Audience.ForGroup(group.Id), null, one.Class("Warm-up " + i), In(teacher));
                one.Session(Monday, "11:00", "18:00", Audience.ForGroup(group.Id), null, one.Class("Training " + i), In(teacher));
            }
            studios.Add(studio);
        }
        var oneDay = Ask(Rigging(one.Group("MA"), one.Facilitator("Bo")) with { Date = Monday }, studios);
        Assert.Empty(MakeRoom.For(one.T, oneDay, Today, Early, Admin));
        Assert.Equal(["Class 13"], MakeRoom.For(one.T, oneDay with { Spaces = [.. oneDay.Spaces.Skip(1)] }, Today, Early, Admin).Select(m => m.Other.Moved.DisplayTitle));
    }

    [Fact]
    public void TheSoonestFirstEachOtherClassOnceAndThreeAtMost()
    {
        var b = new Build();
        var (ada, bo) = (b.Facilitator("Ada"), b.Facilitator("Bo"));
        var (ba, ma) = (b.Group("BA 1"), b.Group("MA"));
        var studioA = Space(b, "Studio A");
        Space(b, "Studio B");
        // Studio A is taken from ten to eleven every day of the week, each day by a class of its own.
        var names = new[] { "Dance", "Mime", "Clown", "Voice", "Stretch" };
        for (int i = 0; i < 5; i++)
            b.Session(Monday.AddDays(i), "10:00", "11:00", Audience.ForGroup(ba.Id), studioA, b.Class(names[i]), In(ada));
        var t = b.T;
        var ask = Ask(Rigging(ma, bo) with { Date = Monday }, [studioA], to: Monday.AddDays(4));
        Assert.Empty(Openings.Find(t, ask, Today, Early, Admin).Openings);
        var made = MakeRoom.For(t, ask, Today, Early, Admin);
        Assert.Equal(MakeRoom.Most, made.Count);
        Assert.Equal(["Dance", "Mime", "Clown"], made.Select(m => m.Other.Moved.DisplayTitle));
        Assert.Equal([Monday, Monday.AddDays(1), Monday.AddDays(2)], made.Select(m => m.Placed.Date));
        Assert.All(made, m => Assert.Equal(new TimeOnly(10, 0), m.Placed.Start));
    }

    [Fact]
    public void MakingRoomMovesBothInOneStepThatOneUndoTakesBackAndIsRefusedOnceItNoLongerMakesRoom()
    {
        var s = Tuesdays();
        var t = s.B.T;
        var made = MakeRoom.For(t, Ask(s.Rigging, [s.StudioA]), Today, Early, Admin).Single();
        var w = made.Other.Moved;

        var batch = Edits.MakeRoom(t, made.Placed, w.Id, w.Date, w.Start, w.RoomId, Today, Early, Admin);
        Assert.Equal("Made room for Rigging, Tue 5 Mar 10:00, by moving Dance to Studio B", batch.Label);
        var after = Apply.Batch(t, batch);
        Assert.Equal(s.StudioB.Id, after.Sessions[s.Dance.Id]!.RoomId);
        Assert.Equal((s.StudioA.Id, new TimeOnly(10, 0)), (after.Sessions[made.Placed.Id]!.RoomId, after.Sessions[made.Placed.Id]!.Start));
        Assert.Empty(WaysOut.InClash(after, after.Sessions[made.Placed.Id]!));
        var undone = Apply.Batch(after, batch.Inverse());
        Assert.Equal(Json(t.Sessions[s.Dance.Id]), Json(undone.Sessions[s.Dance.Id]));
        Assert.False(undone.Sessions.Contains(made.Placed.Id));

        // Somebody puts a class in Studio B meanwhile: moving Dance there no longer makes room, even with Studio C,
        // a studio as well, free at the same time.
        var taken = new Build { T = t };
        taken.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(taken.Group("Prep").Id), s.StudioB, taken.Class("Aerial"), In(taken.Facilitator("Cleo")));
        var studioC = Space(taken, "Studio C");
        Assert.Contains(WaysOut.For(taken.T with { Sessions = taken.T.Sessions.Put(made.Placed) }, w.Id, Today, Early, Admin), x => x.Moved.RoomId == studioC.Id && x.Moved.Start == w.Start);
        Assert.Contains("no longer makes room", Assert.Throws<EditRefused>(() =>
            Edits.MakeRoom(taken.T, made.Placed, w.Id, w.Date, w.Start, w.RoomId, Today, Early, Admin)).Message);
        // The add form asks the same before it offers the move: yes for the class as it was found; no once Studio B
        // is taken, and no once the class is somewhere Dance is not in its way.
        Assert.Equal(s.StudioB.Id, MakeRoom.Still(t, made.Placed, w.Id, w.Date, w.Start, w.RoomId, Today, Early, Admin)?.Moved.RoomId);
        Assert.Null(MakeRoom.Still(taken.T, made.Placed, w.Id, w.Date, w.Start, w.RoomId, Today, Early, Admin));
        Assert.Null(MakeRoom.Still(t, made.Placed with { Start = new(12, 0), End = new(13, 0) }, w.Id, w.Date, w.Start, w.RoomId, Today, Early, Admin));
        // And a move that never made room for this class is no way of making it.
        Assert.Throws<EditRefused>(() => Edits.MakeRoom(t, made.Placed with { Start = new(12, 0), End = new(13, 0) }, w.Id, w.Date, w.Start, w.RoomId, Today, Early, Admin));
        Assert.Throws<EditRefused>(() => Edits.MakeRoom(t, made.Placed, Guid.NewGuid(), w.Date, w.Start, w.RoomId, Today, Early, Admin));
        Assert.Equal("A class must end after it starts.", Assert.Throws<EditRefused>(() =>
            Edits.MakeRoom(t, made.Placed with { End = made.Placed.Start }, w.Id, w.Date, w.Start, w.RoomId, Today, Early, Admin)).Message);

        // What is saved is tidied as any class is: one entry for somebody named twice.
        var twice = made.Placed with { Facilitators = [new Assignment(s.Bo.Id), new Assignment(s.Bo.Id)] };
        Assert.Single(Apply.Batch(t, Edits.MakeRoom(t, twice, w.Id, w.Date, w.Start, w.RoomId, Today, Early, Admin)).Sessions[twice.Id]!.Facilitators);

        // Moved in time this week, the other class says where it was, as a way out taken does; next week it would not.
        var forBa = s.Rigging with { Audience = Audience.ForGroup(s.Ba.Id), Start = new(10, 0), End = new(11, 0) };
        var inTime = MakeRoom.For(t, Ask(forBa, []), Today, Early, Admin).Single().Other.Moved;
        var moved = Apply.Batch(t, Edits.MakeRoom(t, forBa, inTime.Id, inTime.Date, inTime.Start, inTime.RoomId, Today, Early, Admin));
        Assert.Equal(new MovedFrom(Tuesday, new(10, 0), new(11, 0)), moved.Sessions[s.Dance.Id]!.MovedFrom);
        Assert.Null(Apply.Batch(t, Edits.MakeRoom(t, forBa, inTime.Id, inTime.Date, inTime.Start, inTime.RoomId, Today.AddDays(-7), Early, Admin)).Sessions[s.Dance.Id]!.MovedFrom);
    }

    [Fact]
    public void TheOtherClassIsInTheWayOnlyByAClashNeverByAHardDayBesideIt()
    {
        var b = new Build();
        var ba = b.Group("BA 1");
        var acrobatics = b.Class("Acrobatics", effort: Effort.Hard);
        var studio = Space(b, "Studio A");
        var other = b.Session(Tuesday, "09:00", "11:00", Audience.ForGroup(ba.Id), studio, acrobatics);
        var t = b.T with { Settings = b.T.Settings with { MostHardInARow = TimeSpan.FromHours(2) } };
        var placed = new Session
        {
            Title = "Acrobatics", ClassId = acrobatics.Id, Audience = Audience.ForGroup(ba.Id), RoomId = studio.Id,
            Date = Tuesday, Start = new(10, 0), End = new(11, 0),
        };
        Assert.True(MakeRoom.InTheWay(t, placed, other.Id));      // the same group in the same space at once
        // Straight after it, three hours of hard training in a row names it; it is not in the way of anything.
        var after = placed with { Start = new(11, 0), End = new(12, 0) };
        Assert.Contains(Clashes.CausedBy(t, after), c => c.IsHardTraining && c.SessionIds.Contains(other.Id));
        Assert.False(MakeRoom.InTheWay(t, after, other.Id));
    }

    [Fact]
    public void AClassTheTimetableHasMovesThereAndSaysWhereItWasOnceItsWeekHasBegun()
    {
        var s = Tuesdays();
        var b = s.B;
        var rigging = b.Session(Tuesday, "15:00", "16:00", Audience.ForGroup(s.Ma.Id), s.StudioA, b.Class("Rigging"), In(s.Bo));
        var t = b.T;
        var made = MakeRoom.For(t, Ask(rigging, [s.StudioA]), Today, Early, Admin).Single();
        Assert.Equal(rigging.Id, made.Placed.Id);
        var w = made.Other.Moved;
        var batch = Edits.MakeRoom(t, Edits.Moved(rigging, made.Placed.Date, made.Placed.Start, made.Placed.RoomId, sayMoved: true), w.Id, w.Date, w.Start, w.RoomId, Today, Early, Admin);
        var after = Apply.Batch(t, batch);
        Assert.Equal((new TimeOnly(10, 0), new MovedFrom(Tuesday, new(15, 0), new(16, 0))), (after.Sessions[rigging.Id]!.Start, after.Sessions[rigging.Id]!.MovedFrom));
        // Dance moved only its space, so it has not moved in time.
        Assert.Null(after.Sessions[s.Dance.Id]!.MovedFrom);
        Assert.Equal(Json(t.Sessions[rigging.Id]), Json(Apply.Batch(after, batch.Inverse()).Sessions[rigging.Id]));

        // Asked for Studio B only, with Aerial in it then: Dance in its own Studio A is not in the way there.
        var other = new Build { T = t };
        other.Session(Tuesday, "10:00", "11:00", Audience.ForGroup(other.Group("Prep").Id), s.StudioB, other.Class("Aerial"), In(other.Facilitator("Cleo")));
        Assert.Equal(["Studio B Tue 10:00–11:00, moving Aerial to Studio B Tue 09:00–10:00"],
            Said(other.T, MakeRoom.For(other.T, Ask(rigging, [s.StudioB]), Today, Early, Admin)));
    }
}
