using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// Equipment out of use: a piece a space has, out of use from a day until a day or until somebody says it is back.
/// On those days the space lacks it, as if it did not have it: a class needing it there is ringed, saying so and when
/// it is back, with its ways out; Find a time, Make room and the ways out look in that space only on the other days.
/// </summary>
public class OutOfUseTests
{
    private static readonly DateOnly Tuesday = Monday.AddDays(1), Wednesday = Monday.AddDays(2), Thursday = Monday.AddDays(3);
    private static readonly TimeOnly Early = new(7, 0);
    private static readonly EventViewer Admin = EventViewer.Administrator;

    /// <summary>
    /// Aerial needs rigging points and crash mats: the Aerial Hall and the Big Top have both, the Gym only mats,
    /// Studio A neither. Juggling needs nothing.
    /// </summary>
    private sealed record School(Build B, Equipment Rigging, Equipment Mats, Room Studio, Room Hall, Room Top, Room Gym, Class Aerial, Class Juggling, Group Ba, Group Prep);

    private static School Built()
    {
        var b = new Build();
        var rigging = b.Equipment("Rigging points");
        var mats = b.Equipment("Crash mats");
        var studio = b.Room("Studio A");
        var hall = b.Room("Aerial Hall", rigging, mats);
        var top = b.Room("Big Top", rigging, mats);
        var gym = b.Room("Gym", mats);
        var aerial = b.Class("Aerial", needs: [rigging, mats]);
        var juggling = b.Class("Juggling");
        var (ba, prep) = (b.Group("BA 1"), b.Group("Prep"));
        // Something in the week, so Find a time looks in it at all.
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(prep.Id), studio, juggling);
        return new(b, rigging, mats, studio, hall, top, gym, aerial, juggling, ba, prep);
    }

    private static OutOfUse Out(Room r, Equipment e, DateOnly from, DateOnly? until) => new() { RoomId = r.Id, EquipmentId = e.Id, From = from, Until = until };

    private static Timetable With(Timetable t, params OutOfUse[] spells) => t with { OutOfUse = EntitySet<OutOfUse>.Of(spells) };

    private static List<string> Names(IEnumerable<Equipment> pieces) => pieces.Select(e => e.Name).ToList();

    private static string Json(Timetable t) => System.Text.Json.JsonSerializer.Serialize(t, ModelJson.Options);

    private static Session Proposed(School s, Class c, Room? room, DateOnly day, string from = "13:00", string to = "14:30") => new()
    {
        Title = c.Name, ClassId = c.Id, Audience = Audience.ForGroup(s.Ba.Id), RoomId = room?.Id,
        Date = day, Start = TimeOnly.Parse(from), End = TimeOnly.Parse(to),
    };

    [Fact]
    public void APieceOutOfUseIsLackedOnTheDaysItIsOutAndAPieceNotThereIsNeverOutOfUse()
    {
        var s = Built();
        var mats = Out(s.Hall, s.Mats, Tuesday, Wednesday);
        var t = With(s.B.T, mats);
        Assert.Empty(Spaces.Lacking(t, s.Aerial.Id, s.Hall.Id, Monday));
        Assert.Equal(["Crash mats"], Names(Spaces.Lacking(t, s.Aerial.Id, s.Hall.Id, Tuesday)));
        Assert.Equal(["Crash mats"], Names(Spaces.Lacking(t, s.Aerial.Id, s.Hall.Id, Wednesday)));
        Assert.Empty(Spaces.Lacking(t, s.Aerial.Id, s.Hall.Id, Thursday));
        Assert.Equal(mats, Assert.Single(Spaces.Lacks(t, s.Aerial.Id, s.Hall.Id, Tuesday)).Out);
        Assert.False(Spaces.Suits(t, s.Aerial.Id, s.Hall.Id, Tuesday));
        Assert.True(Spaces.Suits(t, s.Aerial.Id, s.Hall.Id, Thursday));
        // It still has it: what it has not at all is another question, asked over many days.
        Assert.True(Spaces.HasAll(t, s.Aerial.Id, s.Hall.Id));
        Assert.Empty(Spaces.Missing(t, s.Aerial.Id, s.Hall.Id));
        // A class that needs nothing lacks nothing, whatever is out of use.
        Assert.Empty(Spaces.Lacking(t, s.Juggling.Id, s.Hall.Id, Tuesday));

        // In the Gym, which has no rigging: the rigging is lacked as not there, even said out of use, and the mats as out of use.
        var gymMats = Out(s.Gym, s.Mats, Tuesday, Tuesday);
        var gymRigging = Out(s.Gym, s.Rigging, Tuesday, null);
        var gym = With(s.B.T, gymMats, gymRigging);
        Assert.Equal([("Crash mats", gymMats), ("Rigging points", (OutOfUse?)null)],
            Spaces.Lacks(gym, s.Aerial.Id, s.Gym.Id, Tuesday).Select(l => (l.Piece.Name, l.Out)));
        Assert.Equal(["Rigging points"], Names(Spaces.Missing(gym, s.Aerial.Id, s.Gym.Id)));
        Assert.Equal(["Rigging points"], Names(Spaces.Lacking(gym, s.Aerial.Id, s.Gym.Id, Wednesday)));

        // Until somebody says it is back: out from its first day on, however far; never in the Scratch Week, which has no dates.
        var open = Out(s.Hall, s.Rigging, Thursday, null);
        var t2 = With(s.B.T, open);
        Assert.Empty(Spaces.Lacking(t2, s.Aerial.Id, s.Hall.Id, Wednesday));
        Assert.Equal(["Rigging points"], Names(Spaces.Lacking(t2, s.Aerial.Id, s.Hall.Id, Thursday)));
        Assert.Equal(["Rigging points"], Names(Spaces.Lacking(t2, s.Aerial.Id, s.Hall.Id, Thursday.AddDays(400))));
        Assert.Empty(Spaces.Lacking(t2, s.Aerial.Id, s.Hall.Id, ScratchWeek.Monday.AddDays(3)));
        Assert.Null(Spaces.OutOn(t2, s.Hall.Id, s.Rigging.Id, ScratchWeek.Monday.AddDays(3)));

        // Two spells saying so: the one lasting longest, until somebody says, then the latest end; another space's, never.
        var shortSpell = Out(s.Hall, s.Rigging, Tuesday, Wednesday);
        var longer = Out(s.Hall, s.Rigging, Monday, Thursday);
        var elsewhere = Out(s.Top, s.Rigging, Monday, null);
        Assert.Equal(longer, Spaces.OutOn(With(s.B.T, shortSpell, longer, elsewhere), s.Hall.Id, s.Rigging.Id, Tuesday));
        Assert.Equal(open, Spaces.OutOn(With(s.B.T, shortSpell, open, longer), s.Hall.Id, s.Rigging.Id, Thursday));
        Assert.Null(Spaces.OutOn(With(s.B.T, elsewhere), s.Hall.Id, s.Rigging.Id, Tuesday));
        Assert.Null(Spaces.OutOn(With(s.B.T, shortSpell), s.Hall.Id, s.Mats.Id, Tuesday));
    }

    [Fact]
    public void WhatIsOutOfUseOnADayIsOneSpellAPieceAndItLeavesWithoutItTheClassesThatNeedItThereThen()
    {
        var s = Built();
        var b = s.B;
        var ada = b.Facilitator("Ada");
        var monday = b.Session(Monday, "13:00", "14:30", Audience.ForGroup(s.Ba.Id), s.Hall, s.Aerial);
        var tuesday = b.Session(Tuesday, "09:00", "10:30", Audience.ForGroup(s.Ba.Id), s.Hall, s.Aerial, In(ada));
        b.Session(Tuesday, "11:00", "12:00", Audience.ForGroup(s.Ba.Id), s.Hall, s.Juggling);              // needs nothing
        b.Session(Tuesday, "13:00", "14:30", Audience.ForGroup(s.Prep.Id), s.Top, s.Aerial);               // another space
        b.Put(b.Session(Wednesday, "13:00", "14:30", Audience.ForGroup(s.Ba.Id), s.Hall, s.Aerial) with { Cancelled = true });
        var thursday = b.Session(Thursday, "13:00", "14:30", Audience.ForGroup(s.Ba.Id), s.Hall, s.Aerial);
        b.Away(ada, Tuesday);                                                                               // needs cover, runs all the same
        var spell = Out(s.Hall, s.Mats, Tuesday, null);
        var t = With(b.T, spell);

        // What it leaves without it: on its days only, in its space, a class that needs it and happens, soonest first.
        Assert.Equal([tuesday.Id, thursday.Id], Spaces.Affected(t, spell, Monday, Thursday).Select(o => o.Session.Id));
        Assert.Equal([tuesday.Id], Spaces.Affected(t, spell with { Until = Wednesday }, Monday, Thursday).Select(o => o.Session.Id));
        Assert.Equal([thursday.Id], Spaces.Affected(t, spell, Wednesday, Thursday).Select(o => o.Session.Id));
        Assert.Equal([monday.Id, tuesday.Id], Spaces.Affected(t, spell with { From = Monday }, Monday, Tuesday).Select(o => o.Session.Id));
        Assert.Empty(Spaces.Affected(t, spell with { Until = Monday.AddDays(-1), From = Monday.AddDays(-7) }, Monday, Thursday));
        Assert.Empty(Spaces.Affected(t, spell with { EquipmentId = s.Rigging.Id, RoomId = s.Gym.Id }, Monday, Thursday));
        // From a time on its first day, what has ended by then needs nothing any more.
        Assert.Equal([thursday.Id], Spaces.Affected(t, spell, Tuesday, Thursday, new TimeOnly(10, 30)).Select(o => o.Session.Id));
        Assert.Equal([tuesday.Id, thursday.Id], Spaces.Affected(t, spell, Tuesday, Thursday, new TimeOnly(10, 29)).Select(o => o.Session.Id));

        // What is out of use on a day: one spell for each piece in each space, by space and then piece as a person reads them.
        var topRigging = Out(s.Top, s.Rigging, Monday, Wednesday);
        var topMats = Out(s.Top, s.Mats, Monday, Tuesday);
        var hallRigging = Out(s.Hall, s.Rigging, Monday, Monday);
        var shorter = Out(s.Hall, s.Mats, Tuesday, Wednesday);
        var day = With(b.T, topRigging, shorter, spell, topMats, hallRigging);
        Assert.Equal([spell, topMats, topRigging], Spaces.OutOfUseOn(day, Tuesday));
        Assert.Equal([hallRigging, topMats, topRigging], Spaces.OutOfUseOn(day, Monday));
        Assert.Equal([spell], Spaces.OutOfUseOn(day, Thursday));
        Assert.Empty(Spaces.OutOfUseOn(day, ScratchWeek.Monday.AddDays(1)));
        // Not in a space no longer the school's, nor for a piece a space no longer has or the school no longer lists.
        var formerTop = day with { Rooms = day.Rooms.Put(s.Top with { FormerSince = Monday }) };
        Assert.Equal([spell], Spaces.OutOfUseOn(formerTop, Tuesday));
        var noMats = day with { Rooms = day.Rooms.Put(s.Top with { Has = [s.Rigging.Id] }) };
        Assert.Equal([spell, topRigging], Spaces.OutOfUseOn(noMats, Tuesday));
        var unlisted = day with { Equipment = day.Equipment.Remove(s.Rigging.Id) };
        Assert.Equal([spell, topMats], Spaces.OutOfUseOn(unlisted, Tuesday));
    }

    [Fact]
    public void AClassNeedingItThereIsRingedSayingItIsOutOfUseAndWhenItIsBack()
    {
        var s = Built();
        var b = s.B;
        var tuesday = b.Session(Tuesday, "13:00", "14:30", Audience.ForGroup(s.Ba.Id), s.Hall, s.Aerial);
        b.Session(Thursday, "13:00", "14:30", Audience.ForGroup(s.Ba.Id), s.Hall, s.Aerial);
        var t = With(b.T, Out(s.Hall, s.Mats, Tuesday, Wednesday));

        var clash = Assert.Single(Clashes.ForWeek(t, Monday));
        Assert.Equal(ClashKind.SpaceLacksEquipment, clash.Kind);
        Assert.Equal("Space \"Aerial Hall\" lacks what \"Aerial\" needs: Crash mats out of use, back Thu 7 Mar (Tue 13:00–14:30)", clash.Description);
        Assert.Equal([tuesday.Id], clash.SessionIds);
        Assert.True(clash.IsRing);

        // Until somebody says it is back, it says only that it is out of use.
        Assert.Equal(["Space \"Aerial Hall\" lacks what \"Aerial\" needs: Crash mats out of use (Tue 13:00–14:30)",
                      "Space \"Aerial Hall\" lacks what \"Aerial\" needs: Crash mats out of use (Thu 13:00–14:30)"],
            Clashes.ForWeek(With(b.T, Out(s.Hall, s.Mats, Tuesday, null)), Monday).Select(c => c.Description));

        // In the Gym: what it has not first, then what it has out of use.
        b.Put(tuesday with { RoomId = s.Gym.Id });
        Assert.Equal("Space \"Gym\" lacks what \"Aerial\" needs: Rigging points; Crash mats out of use, back Wed 6 Mar (Tue 13:00–14:30)",
            Assert.Single(Clashes.ForWeek(With(b.T, Out(s.Gym, s.Mats, Tuesday, Tuesday)), Monday)).Description);

        // The add form hears of it before the class is saved, on the days it is out only.
        Assert.Equal([ClashKind.SpaceLacksEquipment], Clashes.CausedBy(t, Proposed(s, s.Aerial, s.Hall, Wednesday)).Select(c => c.Kind));
        Assert.Empty(Clashes.CausedBy(t, Proposed(s, s.Aerial, s.Hall, Thursday, "10:00", "11:00")));
    }

    [Fact]
    public void FindATimeAndMakeRoomLookInASpaceOnlyOnTheDaysWhatTheClassNeedsIsInUseThere()
    {
        var s = Built();
        var t = With(s.B.T, Out(s.Hall, s.Mats, Tuesday, Wednesday));
        OpeningAsk Ask(params Room[] spaces) => new()
        {
            Class = Proposed(s, s.Aerial, null, Tuesday, "09:00", "10:00"), Spaces = [.. spaces.Select(r => r.Id)],
            From = Tuesday, To = Thursday, DayStart = new(9, 0), DayEnd = new(12, 0),
        };

        // Only the Aerial Hall asked: nothing on the two days its mats are out, each said why, and Thursday found.
        var hall = Openings.Find(t, Ask(s.Hall), Today, Early, Admin);
        Assert.NotEmpty(hall.Openings);
        Assert.All(hall.Openings, o => Assert.Equal((Thursday, s.Hall.Id), (o.Date, Assert.Single(o.Spaces))));
        Assert.Equal([(Tuesday, Wednesday, "Aerial Hall: Crash mats out of use, back Thu 7 Mar")], hall.NoTime.Select(n => (n.From, n.To, n.Why)));
        // It has what Aerial needs, so it is not left out for lacking it.
        Assert.Empty(hall.Unsuited);
        Assert.Equal([s.Hall.Id], Openings.SpacesFor(t, Ask(s.Hall)));

        // With the Big Top asked too, Tuesday is the Big Top's alone.
        var both = Openings.Find(t, Ask(s.Hall, s.Top), Today, Early, Admin);
        Assert.Equal([s.Top.Id], both.Openings.First(o => o.Date == Tuesday).Spaces);
        Assert.Equal([s.Hall.Id, s.Top.Id], both.Openings.First(o => o.Date == Thursday).Spaces);
        Assert.Empty(both.NoTime);

        // Make room: BA's Dance keeps the group from ten until twelve on Tuesday. With the Hall's mats in use, the room
        // made is in the Hall as soon as Dance has gone; with them out that day, there is none to make there.
        var b = s.B;
        b.Session(Tuesday, "10:00", "12:00", Audience.ForGroup(s.Ba.Id), s.Gym, b.Class("Dance"));
        var morning = Ask(s.Hall) with { To = Tuesday, DayStart = new(10, 0), DayEnd = new(12, 0) };
        Assert.Empty(Openings.Find(b.T, morning, Today, Early, Admin).Openings);
        Assert.Equal((s.Hall.Id, new TimeOnly(10, 0)), MakeRoom.For(b.T, morning, Today, Early, Admin).Select(m => (m.Placed.RoomId!.Value, m.Placed.Start)).First());
        Assert.Empty(MakeRoom.For(With(b.T, Out(s.Hall, s.Mats, Tuesday, Wednesday)), morning, Today, Early, Admin));
        // With the Big Top asked too, the room is made there from ten, as soon as Dance has gone: the Hall is no
        // place to try it that day, so it never stands first among the places to try.
        var hallOrTop = morning with { Spaces = [s.Hall.Id, s.Top.Id] };
        Assert.Equal((s.Top.Id, new TimeOnly(10, 0)), MakeRoom.For(With(b.T, Out(s.Hall, s.Mats, Tuesday, Wednesday)), hallOrTop, Today, Early, Admin)
            .Select(m => (m.Placed.RoomId!.Value, m.Placed.Start)).First());
    }

    [Fact]
    public void TheWayOutIsTheSameTimeInASpaceWithItInUseAndNeverAnotherTimeWhereItIsOut()
    {
        var s = Built();
        var b = s.B;
        var aerial = b.Session(Tuesday, "13:00", "14:30", Audience.ForGroup(s.Ba.Id), s.Hall, s.Aerial);
        var t = With(b.T, Out(s.Hall, s.Mats, Tuesday, Wednesday));

        var way = WaysOut.For(t, aerial.Id, Today, Early, Admin).First();
        Assert.True(way.SameTime);
        Assert.Equal((s.Top.Id, aerial.Start), (way.Moved.RoomId!.Value, way.Moved.Start));

        // With the Big Top taken then, another time in the Big Top: never the Aerial Hall that day, whatever time.
        b.Session(Tuesday, "13:00", "14:30", Audience.ForGroup(s.Prep.Id), s.Top, s.Juggling);
        var busy = With(b.T, Out(s.Hall, s.Mats, Tuesday, Wednesday));
        var ways = WaysOut.For(busy, aerial.Id, Today, Early, Admin);
        Assert.NotEmpty(ways);
        Assert.All(ways, w =>
        {
            Assert.Equal(s.Top.Id, w.Moved.RoomId);
            Assert.Empty(WaysOut.InClash(busy with { Sessions = busy.Sessions.Put(w.Moved) }, w.Moved));
        });

        // An administrator answering a coach's ask for another space is offered first the free ones with it in use that day.
        var asked = new SpaceAsk { FacilitatorId = Guid.NewGuid(), Date = Tuesday, Start = new(9, 0), End = new(10, 0), RoomId = s.Studio.Id, ClassId = s.Aerial.Id };
        Assert.Equal([s.Top.Id, s.Hall.Id, s.Gym.Id], Asking.OtherSpacesFree(t, asked).Select(r => r.Id));
        Assert.Equal([s.Hall.Id, s.Top.Id, s.Gym.Id], Asking.OtherSpacesFree(t, asked with { Date = Thursday }).Select(r => r.Id));
    }

    [Fact]
    public void PuttingAPieceOutOfUseAndBackIsEachOneUndoAndRefusedWhereItCannotBe()
    {
        var s = Built();
        var t = s.B.T;
        var spell = Out(s.Hall, s.Mats, Tuesday, null);
        var put = Edits.PutOutOfUse(t, spell);
        Assert.Equal("Put the Crash mats in Aerial Hall out of use", put.Label);
        var after = Apply.Batch(t, put);
        Assert.Equal(spell, after.OutOfUse.Single());
        Assert.Equal(Json(t), Json(Apply.Batch(after, put.Inverse())));
        var changed = Edits.PutOutOfUse(after, spell with { Until = Thursday });
        Assert.Equal("Changed the days out of use for the Crash mats in Aerial Hall", changed.Label);
        Assert.Equal(Thursday, Apply.Batch(after, changed).OutOfUse.Single().Until);
        // Back in use the day it went out: as if it never was, back to the same day.
        Assert.Equal(spell with { Until = Tuesday }, Apply.Batch(after, Edits.PutOutOfUse(after, spell with { Until = Tuesday })).OutOfUse.Single());

        Assert.Throws<EditRefused>(() => Edits.PutOutOfUse(t, Out(s.Studio, s.Mats, Tuesday, null)));          // Studio A has no mats
        Assert.Throws<EditRefused>(() => Edits.PutOutOfUse(t, spell with { Until = Monday }));                 // back before it went
        Assert.Throws<EditRefused>(() => Edits.PutOutOfUse(t, spell with { EquipmentId = Guid.NewGuid() }));   // not on the school's list
        Assert.Throws<EditRefused>(() => Edits.PutOutOfUse(t, spell with { RoomId = Guid.NewGuid() }));        // no such space
        Assert.Throws<EditRefused>(() => Edits.PutOutOfUse(t with { Rooms = t.Rooms.Put(s.Hall with { FormerSince = Monday }) }, spell));
        Assert.Throws<EditRefused>(() => Edits.PutOutOfUse(t, spell with { From = ScratchWeek.Monday }));

        // Back in use from today: out until yesterday when it went out before today; otherwise as if it never was.
        var since = Apply.Batch(t, Edits.PutOutOfUse(t, spell with { From = Monday.AddDays(-7) }));
        var back = Edits.BackInUse(since, spell.Id, Tuesday);
        Assert.Equal("Back in use: the Crash mats in Aerial Hall", back.Label);
        var backed = Apply.Batch(since, back);
        Assert.Equal(spell with { From = Monday.AddDays(-7), Until = Monday }, backed.OutOfUse.Single());
        Assert.Empty(Spaces.Lacking(backed, s.Aerial.Id, s.Hall.Id, Tuesday));
        Assert.Equal(["Crash mats"], Names(Spaces.Lacking(backed, s.Aerial.Id, s.Hall.Id, Monday)));
        Assert.Equal(Json(since), Json(Apply.Batch(backed, back.Inverse())));
        Assert.Throws<EditRefused>(() => Edits.BackInUse(backed, spell.Id, Tuesday));
        var today = Edits.BackInUse(after, spell.Id, Tuesday);
        Assert.Equal("Back in use: the Crash mats in Aerial Hall", today.Label);
        Assert.Empty(Apply.Batch(after, today).OutOfUse);
        var ahead = Edits.BackInUse(after, spell.Id, Monday);
        Assert.Equal("No longer putting the Crash mats in Aerial Hall out of use from Tue 5 Mar", ahead.Label);
        Assert.Empty(Apply.Batch(after, ahead).OutOfUse);
        Assert.Throws<EditRefused>(() => Edits.BackInUse(t, spell.Id, Tuesday));

        // Taking the piece off the list, or deleting a space never used, takes its spells with it, in the same Undo.
        var gone = Edits.RemoveEquipment(after, s.Mats.Id);
        Assert.Empty(Apply.Batch(after, gone).OutOfUse);
        Assert.Equal(Json(after), Json(Apply.Batch(Apply.Batch(after, gone), gone.Inverse())));
        Assert.Empty(Apply.Batch(after, Edits.RemoveRoom(after, s.Hall.Id, Tuesday)).OutOfUse);
        Assert.Single(Apply.Batch(after, Edits.RemoveRoom(after, s.Top.Id, Tuesday)).OutOfUse);
        // A space used before today is kept as former, and keeps what was out of use in it, as the days it was used do.
        s.B.Session(Monday, "11:00", "12:00", Audience.ForGroup(s.Ba.Id), s.Hall, s.Juggling);
        var used = Apply.Batch(s.B.T, Edits.PutOutOfUse(s.B.T, spell));
        Assert.Single(Apply.Batch(used, Edits.RemoveRoom(used, s.Hall.Id, Tuesday)).OutOfUse);
    }

    [Fact]
    public void NextYearKeepsWhatIsStillOutOfUseWhenItStartsAndNothingInAFormerSpace()
    {
        var s = Built();
        var t = s.B.T;
        var starts = NextYear.For(t)!.Starts;
        var over = Out(s.Hall, s.Mats, Monday, Thursday);
        var open = Out(s.Hall, s.Rigging, Monday, null);
        var into = Out(s.Top, s.Mats, starts.AddDays(-3), starts);
        var former = Out(s.Gym, s.Mats, Monday, null);
        var year = With(t with { Rooms = t.Rooms.Put(s.Gym with { FormerSince = Tuesday }) }, over, open, into, former);
        Assert.Equal(new[] { open, into }.OrderBy(o => o.Id), NextYear.Make(year, "Next", keepClosures: false).OutOfUse.OrderBy(o => o.Id));
    }
}
