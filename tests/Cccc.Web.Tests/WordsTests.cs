using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Data;
using Cccc.Web.Features.Now;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Tests;

/// <summary>The words a count and a noun are written in, on lists, buttons, forms and reports.</summary>
public class WordsTests
{
    [Fact]
    public void ACountIsSingularForExactlyOne()
    {
        Assert.Equal("0 groups", Looks.Count(0, "group"));
        Assert.Equal("1 group", Looks.Count(1, "group"));
        Assert.Equal("2 groups", Looks.Count(2, "group"));
        Assert.Equal("1 class ahead", Looks.Count(1, "class ahead", "classes ahead"));
        Assert.Equal("3 classes ahead", Looks.Count(3, "class ahead", "classes ahead"));
    }

    [Fact]
    public void EveryKindOfDataIsAddedWithTheRightArticle()
    {
        var added = Kinds.All.ToDictionary(k => k.Slug, k => "Add " + Looks.A(k.One));
        Assert.Equal("Add a space", added["rooms"]);
        Assert.Equal("Add an area", added["areas"]);
        Assert.Equal("Add an absence", added["absences"]);
        Assert.Equal("Add a time rule", added["time-rules"]);
        Assert.All(Kinds.All, k => Assert.DoesNotMatch("^Add a [aeiouAEIOU]", "Add " + Looks.A(k.One)));
    }

    [Fact]
    public void AChangedClassSaysOnlyWhatIsDifferentTheMostTellingFirst()
    {
        var hall = new Room { Name = "Hall" };
        var gym = new Room { Name = "Gym" };
        var ada = new Facilitator { Name = "Ada Brightwater" };
        var dag = new Facilitator { Name = "Dag Øverlie" };
        var ba = new Group { Name = "BA 1" };
        var ma = new Group { Name = "MA" };
        var t = Timetable.Empty with
        {
            Rooms = EntitySet<Room>.Of([hall, gym]),
            Facilitators = EntitySet<Facilitator>.Of([ada, dag]),
            Groups = EntitySet<Group>.Of([ba, ma]),
        };
        var day = new DateOnly(2030, 3, 4);
        var was = new Session
        {
            Date = day, Start = new(9, 0), End = new(10, 0), Title = "Aerial", RoomId = hall.Id,
            Audience = Audience.ForGroup(ba.Id), Facilitators = [new Assignment(ada.Id)],
        };
        string What(Session? before, Session? after, DateOnly? on = null) =>
            ChangeWords.What(t, new ClassChange(was.Id, before, after, ["admin"], DateTimeOffset.UnixEpoch), on ?? day);

        Assert.Equal("New", What(null, was));
        Assert.Equal("Deleted", What(was, null));
        Assert.Equal("Cancelled", What(was, was with { Cancelled = true }));
        Assert.Equal("Back on", What(was with { Cancelled = true }, was));
        Assert.Equal("Moved to Tue 5 Mar", What(was, was with { Date = day.AddDays(1) }));
        Assert.Equal("Moved here from Mon 4 Mar · was 09:00–10:00", What(was, was with { Date = day.AddDays(1), Start = new(11, 0), End = new(12, 0) }, day.AddDays(1)));
        Assert.Equal("Was 09:00–10:00 · was in Hall", What(was, was with { Start = new(10, 0), End = new(11, 0), RoomId = gym.Id }));
        Assert.Equal("No space now, was in Hall", What(was, was with { RoomId = null }));
        Assert.Equal("A space given", What(was with { RoomId = null }, was));
        Assert.Equal("Dag Øverlie instead of Ada Brightwater", What(was, was with { Facilitators = [new Assignment(dag.Id)] }));
        Assert.Equal("Dag Øverlie added", What(was, was with { Facilitators = [new Assignment(ada.Id), new Assignment(dag.Id)] }));
        Assert.Equal("Ada Brightwater taken off", What(was, was with { Facilitators = [] }));
        Assert.Equal("A different part for Ada Brightwater", What(was, was with { Facilitators = [new Assignment(ada.Id, new TimeOnly(9, 30))] }));
        Assert.Equal("Dag Øverlie covering", What(was, was with { CoverIds = [dag.Id] }));
        Assert.Equal("Cover taken off", What(was with { CoverIds = [dag.Id] }, was));
        Assert.Equal("Now for BA 1, MA, was BA 1", What(was, was with { Audience = Audience.ForGroups([ba.Id, ma.Id]) }));
        Assert.Equal("Was called Aerial", What(was, was with { Title = "Aerial duo" }));
    }

    [Fact]
    public void AFacilitatorsDayListsTheClassesTheyNoLongerHaveToday()
    {
        var ada = new Facilitator { Name = "Ada Brightwater" };
        var dag = new Facilitator { Name = "Dag Øverlie" };
        var t = Timetable.Empty with { Facilitators = EntitySet<Facilitator>.Of([ada, dag]) };
        var day = new DateOnly(2030, 3, 4);
        var hers = new Session { Date = day, Start = new(9, 0), End = new(10, 0), Title = "Aerial", Facilitators = [new Assignment(ada.Id)] };
        ClassChange C(Session? before, Session? after) => new(Guid.NewGuid(), before, after, ["admin"], DateTimeOffset.UnixEpoch);
        var moved = C(hers, hers with { Date = day.AddDays(2) });
        var deleted = C(hers, null);
        var givenAway = C(hers, hers with { Facilitators = [new Assignment(dag.Id)] });
        var coverOff = C(hers with { Facilitators = [new Assignment(dag.Id)], CoverIds = [ada.Id] }, hers with { Facilitators = [new Assignment(dag.Id)] });
        var stillHers = C(hers, hers with { Start = new(11, 0), End = new(12, 0) });
        var givenHer = C(hers with { Facilitators = [new Assignment(dag.Id)] }, hers);
        var anotherDay = C(hers with { Date = day.AddDays(1) }, null);
        var notHers = C(hers with { Facilitators = [new Assignment(dag.Id)] }, null);

        var gone = ChangeWords.NoLongerTheirs([moved, deleted, givenAway, coverOff, stillHers, givenHer, anotherDay, notHers], ada.Id, day);

        Assert.Equal([moved, deleted, givenAway, coverOff], gone);
        Assert.Equal("Moved to Wed 6 Mar", ChangeWords.NoLongerTheirsWhat(t, moved, day));
        Assert.Equal("Deleted", ChangeWords.NoLongerTheirsWhat(t, deleted, day));
        Assert.Equal("Not yours now · Dag Øverlie instead of Ada Brightwater", ChangeWords.NoLongerTheirsWhat(t, givenAway, day));
        Assert.Equal("Not yours now · Cover taken off", ChangeWords.NoLongerTheirsWhat(t, coverOff, day));
    }
}
