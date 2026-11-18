using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Web.Features.Plan;

namespace Cccc.Web.Tests;

/// <summary>A way out of a clash in a few words, on the button that takes it.</summary>
public class WayOutWordsTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);

    [Fact]
    public void ItSaysTheSpaceAtTheSameTimeOrTheTimeAndWhetherTheSpaceMovesToo()
    {
        var a = new Room { Name = "Studio A" };
        var b = new Room { Name = "Studio B" };
        var t = Timetable.Empty with { Rooms = EntitySet<Room>.Of([a, b]) };
        var s = new Session { Date = Monday, Start = new(10, 0), End = new(11, 0), Title = "Juggling", RoomId = a.Id };
        string Said(Session moved, bool same) => WayOutWords.Where(t, s, new WayOut(moved, same, []));

        Assert.Equal("Studio B, same time", Said(s with { RoomId = b.Id }, true));
        Assert.Equal("11:00–12:00, same space", Said(s with { Start = new(11, 0), End = new(12, 0) }, false));
        Assert.Equal("09:00–10:00 in Studio B", Said(s with { Start = new(9, 0), End = new(10, 0), RoomId = b.Id }, false));
        var nowhere = s with { RoomId = null };
        Assert.Equal("09:00–10:00", WayOutWords.Where(t, nowhere, new WayOut(nowhere with { Start = new(9, 0), End = new(10, 0) }, false, [])));
    }

    [Fact]
    public void JugglingPutWhereCreationIsOnTheSamplesThursdayHasTheEmptyStudioThenTheTimesCleoIsFreeEitherSide()
    {
        var t = SampleSchool.Create(Monday, Monday);
        var thursday = Monday.AddDays(3);
        var studioB = t.Rooms.Single(r => r.Name == "Studio B");
        var creation = t.Sessions.Single(s => s.Date == thursday && s.RoomId == studioB.Id && s.Start == new TimeOnly(9, 0));
        var juggling = t.Classes.Single(c => c.Name == "Juggling");
        var cleo = t.Facilitators.Single(f => f.Name.StartsWith("Cleo", StringComparison.Ordinal));
        var ba1 = t.Groups.Single(g => g.Name == "BA 1");
        var s = new Session
        {
            Date = thursday, Start = new(10, 0), End = new(11, 0), Title = juggling.Name, ClassId = juggling.Id,
            Audience = Audience.ForGroup(ba1.Id), RoomId = studioB.Id, Facilitators = [new Assignment(cleo.Id)],
        };
        t = t with { Sessions = t.Sessions.Put(s) };
        Assert.Equal([ClashKind.RoomDoubleBooked], WaysOut.InClash(t, s).Select(c => c.Kind));
        Assert.Contains(creation.Id, WaysOut.InClash(t, s).Single().SessionIds);

        var ways = WaysOut.For(t, s.Id, Monday, new TimeOnly(8, 0), EventViewer.Administrator);
        // Lunch is for everybody until 12:45, and Cleo teaches Creation from one until four.
        Assert.Equal(["Studio A, same time", "08:00–09:00, same space", "16:00–17:00, same space"], ways.Select(w => WayOutWords.Where(t, s, w)));
        Assert.Equal("Juggling has been here before", ways[0].Notes.Single().Text);
    }
}
