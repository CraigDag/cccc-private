using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// Equipment: what a space has and what a class cannot be taught without. A class in a space without it is in the
/// wrong space, which rings it with a way out to one that has it; Find a time and Make room look only in spaces
/// that have it, and say which they left out.
/// </summary>
public class EquipmentTests
{
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly TimeOnly Early = new(7, 0);
    private static readonly EventViewer Admin = EventViewer.Administrator;

    private static string Json<T>(T? x) => System.Text.Json.JsonSerializer.Serialize(x, ModelJson.Options);

    /// <summary>
    /// Aerial needs rigging points and crash mats: the Aerial Hall has both, the Gym only mats, Studio A neither.
    /// Juggling needs nothing.
    /// </summary>
    private sealed record School(Build B, Equipment Rigging, Equipment Mats, Room Studio, Room Hall, Room Gym, Class Aerial, Class Juggling, Group Ba, Group Prep);

    private static School Built()
    {
        var b = new Build();
        var rigging = b.Equipment("Rigging points");
        var mats = b.Equipment("Crash mats");
        var studio = b.Room("Studio A");
        var hall = b.Room("Aerial Hall", rigging, mats);
        var gym = b.Room("Gym", mats);
        var aerial = b.Class("Aerial", needs: [rigging, mats]);
        var juggling = b.Class("Juggling");
        var (ba, prep) = (b.Group("BA 1"), b.Group("Prep"));
        // Something in the week, so Find a time looks in it at all.
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(prep.Id), studio, juggling);
        return new(b, rigging, mats, studio, hall, gym, aerial, juggling, ba, prep);
    }

    private static Session Proposed(School s, Class c, Room? room, string from = "13:00", string to = "14:30") => new()
    {
        Title = c.Name, ClassId = c.Id, Audience = Audience.ForGroup(s.Ba.Id), RoomId = room?.Id,
        Date = Tuesday, Start = TimeOnly.Parse(from), End = TimeOnly.Parse(to),
    };

    private static List<string> Names(IEnumerable<Equipment> pieces) => pieces.Select(e => e.Name).ToList();

    [Fact]
    public void WhatASpaceLacksIsWhatTheClassNeedsThatItHasNotInTheOrderAPersonReadsNames()
    {
        var s = Built();
        var t = s.B.T;
        Assert.Equal(["Crash mats", "Rigging points"], Names(Spaces.Lacking(t, s.Aerial.Id, s.Studio.Id)));
        Assert.Equal(["Rigging points"], Names(Spaces.Lacking(t, s.Aerial.Id, s.Gym.Id)));
        Assert.Empty(Spaces.Lacking(t, s.Aerial.Id, s.Hall.Id));
        Assert.True(Spaces.Suits(t, s.Aerial.Id, s.Hall.Id));
        Assert.False(Spaces.Suits(t, s.Aerial.Id, s.Gym.Id));
        // A class that needs nothing suits every space; no class, or no space, lacks nothing.
        Assert.Empty(Spaces.Lacking(t, s.Juggling.Id, s.Studio.Id));
        Assert.True(Spaces.Suits(t, s.Juggling.Id, s.Studio.Id));
        Assert.Empty(Spaces.Lacking(t, null, s.Studio.Id));
        Assert.Empty(Spaces.Lacking(t, s.Aerial.Id, null));
        Assert.Equal(["Crash mats", "Rigging points"], Names(Spaces.Needed(t, s.Aerial.Id)));
        Assert.Empty(Spaces.Needed(t, s.Juggling.Id));
        Assert.Empty(Spaces.Needed(t, null));

        // Named twice, it is lacking once; a piece the school has taken off its list is needed by nobody; and
        // Danish order: "Aa" reads as Å, after Z.
        var b = s.B;
        var trapeze = b.Equipment("Aaben trapez");
        var bars = b.Equipment("Barrer");
        var swinging = b.Class("Swinging trapeze", needs: [trapeze, bars, s.Rigging, s.Rigging]);
        var gone = b.T with { Classes = b.T.Classes.Put(swinging with { Needs = swinging.Needs.Add(Guid.NewGuid()) }) };
        Assert.Equal(["Barrer", "Rigging points", "Aaben trapez"], Names(Spaces.Lacking(gone, swinging.Id, s.Studio.Id)));
        Assert.Equal(["Barrer", "Aaben trapez"], Names(Spaces.Lacking(gone, swinging.Id, s.Hall.Id)));
        Assert.Equal(["Barrer", "Rigging points", "Aaben trapez"], Names(Spaces.Needed(gone, swinging.Id)));
    }

    [Fact]
    public void AClassInASpaceWithoutWhatItNeedsIsRingedNamingWhatItLacksThereAndNothingElseIs()
    {
        var s = Built();
        var b = s.B;
        var aerial = b.Session(Tuesday, "13:00", "14:30", Audience.ForGroup(s.Ba.Id), s.Studio, s.Aerial);
        b.Session(Tuesday, "13:00", "14:30", Audience.ForGroup(s.Prep.Id), s.Gym, s.Juggling);

        var clash = Assert.Single(Clashes.ForWeek(b.T, Monday));
        Assert.Equal(ClashKind.SpaceLacksEquipment, clash.Kind);
        Assert.Equal("Space \"Studio A\" lacks what \"Aerial\" needs: Crash mats, Rigging points (Tue 13:00–14:30)", clash.Description);
        Assert.Equal([aerial.Id], clash.SessionIds);
        Assert.True(clash.IsRing);
        Assert.Equal([clash.Description], WaysOut.InClash(b.T, aerial).Select(c => c.Description));

        // In the Gym it still lacks the rigging; in the Hall nothing.
        b.Put(aerial with { RoomId = s.Gym.Id, Start = new(9, 0), End = new(10, 30) });
        Assert.Equal(["Space \"Gym\" lacks what \"Aerial\" needs: Rigging points (Tue 09:00–10:30)"],
            Clashes.ForWeek(b.T, Monday).Select(c => c.Description));
        b.Put(aerial with { RoomId = s.Hall.Id });
        Assert.Empty(Clashes.ForWeek(b.T, Monday));
        // With no space at all it is not in the wrong one, only in none.
        b.Put(aerial with { RoomId = null });
        Assert.Empty(Clashes.ForWeek(b.T, Monday));
    }

    [Fact]
    public void TheAddFormHearsOfTheWrongSpaceBeforeTheClassIsSaved()
    {
        var s = Built();
        var t = s.B.T;
        var studio = Proposed(s, s.Aerial, s.Studio);
        Assert.Equal([(ClashKind.SpaceLacksEquipment, studio.Id)],
            Clashes.CausedBy(t, studio).Select(c => (c.Kind, c.SessionIds.Single())));
        Assert.Empty(Clashes.CausedBy(t, Proposed(s, s.Aerial, s.Hall)));
        Assert.Empty(Clashes.CausedBy(t, Proposed(s, s.Juggling, s.Studio)));
    }

    [Fact]
    public void FindATimeLooksOnlyInSpacesWithWhatTheClassNeedsAndSaysWhichItLeftOut()
    {
        var s = Built();
        var t = s.B.T;
        OpeningAsk Ask(Class c, params Room[] spaces) => new()
        {
            Class = Proposed(s, c, null, "09:00", "10:00"), Spaces = [.. spaces.Select(r => r.Id)],
            From = Tuesday, To = Tuesday, DayStart = new(9, 0), DayEnd = new(12, 0),
        };

        var aerial = Openings.Find(t, Ask(s.Aerial, s.Studio, s.Hall, s.Studio, s.Gym), Today, Early, Admin);
        Assert.NotEmpty(aerial.Openings);
        Assert.All(aerial.Openings, o => Assert.Equal([s.Hall.Id], o.Spaces));
        Assert.Equal([s.Studio.Id, s.Gym.Id], aerial.Unsuited);

        // Asked only where it cannot go, it finds nothing, and says why.
        var nowhere = Openings.Find(t, Ask(s.Aerial, s.Gym), Today, Early, Admin);
        Assert.Empty(nowhere.Openings);
        Assert.Equal([s.Gym.Id], nowhere.Unsuited);

        // A class that needs nothing goes anywhere, and nothing is left out.
        var juggling = Openings.Find(t, Ask(s.Juggling, s.Studio, s.Hall), Today, Early, Admin);
        Assert.Contains(juggling.Openings, o => o.Spaces.Contains(s.Studio.Id));
        Assert.Empty(juggling.Unsuited);
        Assert.Equal([s.Hall.Id], Openings.SpacesFor(t, Ask(s.Aerial, s.Studio, s.Hall, s.Hall)));
    }

    [Fact]
    public void TheWayOutOfTheWrongSpaceIsTheSameTimeInOneThatHasIt()
    {
        var s = Built();
        var b = s.B;
        var aerial = b.Session(Tuesday, "13:00", "14:30", Audience.ForGroup(s.Ba.Id), s.Studio, s.Aerial);

        var ways = WaysOut.For(b.T, aerial.Id, Today, Early, Admin);
        var way = Assert.Single(ways);
        Assert.True(way.SameTime);
        Assert.Equal((s.Hall.Id, aerial.Start), (way.Moved.RoomId!.Value, way.Moved.Start));
        Assert.Empty(WaysOut.InClash(b.T with { Sessions = b.T.Sessions.Put(way.Moved) }, way.Moved));
    }

    [Fact]
    public void WhenTheOnlySpaceWithItIsTakenTheWayOutIsAnotherTimeThereAndNeverTheWrongSpace()
    {
        var s = Built();
        var b = s.B;
        var aerial = b.Session(Tuesday, "13:00", "14:30", Audience.ForGroup(s.Ba.Id), s.Studio, s.Aerial);
        b.Session(Tuesday, "13:00", "14:30", Audience.ForGroup(s.Prep.Id), s.Hall, s.Juggling);

        var ways = WaysOut.For(b.T, aerial.Id, Today, Early, Admin);
        Assert.NotEmpty(ways);
        Assert.All(ways, w =>
        {
            Assert.False(w.SameTime);
            Assert.Equal(s.Hall.Id, w.Moved.RoomId);
            Assert.Empty(WaysOut.InClash(b.T with { Sessions = b.T.Sessions.Put(w.Moved) }, w.Moved));
        });
        // Nearest its own time first, the earlier of two as near: straight before Juggling, then straight after.
        Assert.Equal([new TimeOnly(11, 30), new TimeOnly(14, 30)], ways.Take(2).Select(w => w.Moved.Start));
    }

    [Fact]
    public void MakeRoomLooksOnlyInSpacesThatHaveWhatTheClassNeeds()
    {
        var s = Built();
        var b = s.B;
        // BA's Dance keeps the group from ten until twelve; Studio A is free all morning but has no rigging.
        var dance = b.Class("Dance");
        b.Session(Tuesday, "10:00", "12:00", Audience.ForGroup(s.Ba.Id), s.Gym, dance);
        var ask = new OpeningAsk
        {
            Class = Proposed(s, s.Aerial, null, "09:00", "10:00"), Spaces = [s.Studio.Id, s.Hall.Id],
            From = Tuesday, To = Tuesday, DayStart = new(10, 0), DayEnd = new(12, 0),
        };
        Assert.Empty(Openings.Find(b.T, ask, Today, Early, Admin).Openings);

        var made = MakeRoom.For(b.T, ask, Today, Early, Admin);
        Assert.NotEmpty(made);
        Assert.All(made, m => Assert.Equal(s.Hall.Id, m.Placed.RoomId));
        // The soonest: at ten, as soon as Dance has gone.
        Assert.Equal(new TimeOnly(10, 0), made[0].Placed.Start);
    }

    [Fact]
    public void TakingAPieceOffTheListTakesItOffEverySpaceAndClassInOneStepThatUndoPutsBack()
    {
        var s = Built();
        var b = s.B;
        var aerial = b.Session(Tuesday, "13:00", "14:30", Audience.ForGroup(s.Ba.Id), s.Gym, s.Aerial);
        var t = b.T;
        Assert.Single(Clashes.ForWeek(t, Monday));

        var batch = Edits.RemoveEquipment(t, s.Rigging.Id);
        Assert.Equal("Deleted the equipment Rigging points", batch.Label);
        var after = Apply.Batch(t, batch);
        Assert.Null(after.Equipment[s.Rigging.Id]);
        Assert.Equal([s.Mats.Id], after.Rooms[s.Hall.Id]!.Has);
        Assert.Equal([s.Mats.Id], after.Classes[s.Aerial.Id]!.Needs);
        Assert.Equal(Json(t.Rooms[s.Gym.Id]), Json(after.Rooms[s.Gym.Id]));
        Assert.Equal(Json(t.Equipment[s.Mats.Id]), Json(after.Equipment[s.Mats.Id]));
        // The Gym has the mats, which is all Aerial needs now.
        Assert.Empty(Clashes.ForWeek(after, Monday));
        Assert.Throws<EditRefused>(() => Edits.RemoveEquipment(after, s.Rigging.Id));

        var undone = Apply.Batch(after, batch.Inverse());
        Assert.Equal(Json(t.Equipment[s.Rigging.Id]), Json(undone.Equipment[s.Rigging.Id]));
        Assert.Equal(Json(t.Rooms[s.Hall.Id]), Json(undone.Rooms[s.Hall.Id]));
        Assert.Equal(Json(t.Classes[s.Aerial.Id]), Json(undone.Classes[s.Aerial.Id]));
        Assert.Equal([aerial.Id], Assert.Single(Clashes.ForWeek(undone, Monday)).SessionIds);
    }

    [Fact]
    public void TheSampleSchoolHasEquipmentAndEveryClassInASpaceWithWhatItNeeds()
    {
        var today = new DateOnly(2030, 3, 6);
        var t = SampleSchool.Create(today, today);
        Assert.Equal(["Crash mats", "Mirrors", "Rigging points", "Sprung floor"], t.Equipment.Select(e => e.Name).Order(StringComparer.Ordinal));
        Assert.Contains(t.Classes, c => c.Needs.Count > 0);
        Assert.Contains(t.Sessions, x => Spaces.Needed(t, x.ClassId).Count > 0 && x.RoomId is not null);
        var weeks = t.Sessions.Select(x => Week.MondayOf(x.Date)).Distinct().ToList();
        Assert.NotEmpty(weeks);
        foreach (var monday in weeks)
            Assert.DoesNotContain(Clashes.ForWeek(t, monday), c => c.Kind == ClashKind.SpaceLacksEquipment);
    }
}
