using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Web.Features.Data;

namespace Cccc.Web.Tests;

/// <summary>What a person reads about equipment: where a piece is and what needs it, and why Find a time left a space out.</summary>
public class EquipmentWordsTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly Timetable T = SampleSchool.Create(Monday, Monday);

    private static Equipment Piece(string name) => T.Equipment.Single(e => e.Name == name);
    private static Room Space(string name) => T.Rooms.Single(r => r.Name == name);
    private static Class Class(string name) => T.Classes.Single(c => c.Name == name);

    private static Session Of(string className) => new()
    {
        Title = "", ClassId = Class(className).Id, Date = Monday, Start = new(9, 0), End = new(10, 30),
    };

    /// <summary>Find a time's line for a class asked these spaces, with what the rule left out of them.</summary>
    private static string? LeftOut(string className, params string[] spaces)
    {
        var c = Of(className);
        var ask = new OpeningAsk { Class = c, Spaces = [.. spaces.Select(s => Space(s).Id)], From = Monday, To = Monday };
        return EquipmentWords.LeftOut(T, c, ask.Spaces, Openings.Unsuited(T, ask));
    }

    [Fact]
    public void APieceSaysWhichSpacesHaveItAndWhichClassesNeedIt()
    {
        Assert.Equal("In Aerial Hall and Big Top · needed by Aerial", EquipmentWords.Uses(T, Piece("Rigging points").Id));
        Assert.Equal("In Studio A and Studio B · needed by no class", EquipmentWords.Uses(T, Piece("Mirrors").Id));
        Assert.Equal("In no space · needed by no class", EquipmentWords.Uses(T, Guid.NewGuid()));
        // A former space has it no longer, for anything ahead.
        var bigTop = Space("Big Top");
        var t = T with { Rooms = T.Rooms.Put(bigTop with { FormerSince = Monday }) };
        Assert.Equal("In Aerial Hall · needed by Aerial", EquipmentWords.Uses(t, Piece("Rigging points").Id));
    }

    [Fact]
    public void ListsReadInTheOrderAPersonReadsNames()
    {
        Assert.Equal("Crash mats, Rigging points", EquipmentWords.Names(T, Space("Aerial Hall").Has));
        Assert.Equal("Crash mats, Rigging points", EquipmentWords.Names(T, [Piece("Rigging points").Id, Guid.NewGuid(), Piece("Crash mats").Id, Piece("Crash mats").Id]));
        Assert.Equal("", EquipmentWords.Names(T, Space("Canteen").Has));
        Assert.Equal("without Crash mats and Rigging points", EquipmentWords.Without(Spaces.Lacking(T, Class("Aerial").Id, Space("Canteen").Id)));
        Assert.Equal("", EquipmentWords.And([]));
        Assert.Equal("Mirrors", EquipmentWords.And(["Mirrors"]));
        Assert.Equal("Crash mats, Mirrors and Sprung floor", EquipmentWords.And(["Crash mats", "Mirrors", "Sprung floor"]));

        // Danish order: "Aa" reads as Å, after Z, for the pieces and for the spaces that have one.
        var aa = new Equipment { Name = "Aaben trapez" };
        var barre = new Equipment { Name = "Barre" };
        var t = T with
        {
            Equipment = T.Equipment.Put(aa).Put(barre),
            Rooms = T.Rooms.Put(new Room { Name = "Aalborg Hall", Has = [aa.Id] }).Put(new Room { Name = "Bakery", Has = [aa.Id] }),
        };
        Assert.Equal("Barre, Aaben trapez", EquipmentWords.Names(t, [aa.Id, barre.Id]));
        Assert.Equal("In Bakery and Aalborg Hall · needed by no class", EquipmentWords.Uses(t, aa.Id));
    }

    [Fact]
    public void FindATimeSaysWhichSpacesItLookedInWhenItLeftSomeOutAndNothingWhenItLeftNoneOut()
    {
        Assert.Equal("Aerial needs Crash mats and Rigging points, so only Aerial Hall and Big Top are looked in.",
            LeftOut("Aerial", "Studio A", "Aerial Hall", "Gym", "Big Top"));
        Assert.Equal("Aerial needs Crash mats and Rigging points, so only Big Top is looked in.", LeftOut("Aerial", "Big Top", "Gym"));
        Assert.Equal("Aerial needs Crash mats and Rigging points, and Gym lacks Rigging points.", LeftOut("Aerial", "Gym"));
        Assert.Equal("Aerial needs Crash mats and Rigging points, and none of these spaces has both.", LeftOut("Aerial", "Gym", "Studio A"));
        Assert.Equal("Contemporary dance needs Sprung floor, and none of these spaces has it.", LeftOut("Contemporary dance", "Gym", "Canteen"));
        Assert.Null(LeftOut("Aerial", "Aerial Hall", "Big Top"));
        Assert.Null(LeftOut("Juggling", "Gym", "Canteen"));
    }
}
