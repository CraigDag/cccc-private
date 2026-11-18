using Cccc.Domain.Model;
using Cccc.Domain.Rules;

namespace Cccc.Domain.Tests;

/// <summary>What a class is usually like: the session of it a new one is filled in from (<see cref="Habits.Like"/>).</summary>
public class HabitsTests
{
    private static readonly DateOnly Fri = Build.Monday.AddDays(4);
    private static readonly DateOnly LastMon = Build.Monday.AddDays(-7), LastFri = Fri.AddDays(-7), FriBefore = Fri.AddDays(-14);

    private sealed record School(Build B, Class Aerial, Group G1, Group G2, Room Hall, Room Studio, Facilitator Ada, Facilitator Bo,
        Session MonG1, Session FriG1, Session FriG2Early, Session FriBeforeG2);

    private static School Make()
    {
        var b = new Build();
        var aerial = b.Class("Aerial");
        var g1 = b.Group("G1");
        var g2 = b.Group("G2");
        var hall = b.Room("Hall");
        var studio = b.Room("Studio");
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var monG1 = b.Session(LastMon, "09:00", "10:30", Audience.ForGroup(g1.Id), hall, aerial, Build.In(ada));
        var friG1 = b.Session(LastFri, "13:00", "14:30", Audience.ForGroup(g1.Id), studio, aerial, Build.In(bo));
        var friG2Early = b.Session(LastFri, "09:00", "10:00", Audience.ForGroup(g2.Id), hall, aerial, Build.In(ada));
        var friBefore = b.Session(FriBefore, "13:00", "14:00", Audience.ForGroup(g2.Id), hall, aerial, Build.In(ada));
        b.Session(LastFri, "13:00", "14:30", Audience.ForGroup(g1.Id), hall, b.Class("Juggling"), Build.In(ada));
        return new School(b, aerial, g1, g2, hall, studio, ada, bo, monG1, friG1, friG2Early, friBefore);
    }

    [Fact]
    public void TheSameWeekdayComesFirstThenTheNearestStartThenTheLatestBefore()
    {
        var s = Make();
        var t = s.B.T;
        Assert.Equal(s.FriG1.Id, Habits.Like(t, s.Aerial.Id, Fri, new TimeOnly(13, 0))?.Id);
        Assert.Equal(s.FriG2Early.Id, Habits.Like(t, s.Aerial.Id, Fri, new TimeOnly(9, 30))?.Id);
        // No start said: the latest Friday before, and of two that day the earlier.
        Assert.Equal(s.FriG2Early.Id, Habits.Like(t, s.Aerial.Id, Fri)?.Id);
        Assert.Equal(s.MonG1.Id, Habits.Like(t, s.Aerial.Id, Build.Monday, new TimeOnly(13, 0))?.Id);
        // No Tuesday: the nearest start, and of two at 09:00 the nearer before.
        Assert.Equal(s.FriG2Early.Id, Habits.Like(t, s.Aerial.Id, Build.Monday.AddDays(1), new TimeOnly(9, 0))?.Id);
        // One before and one after, both at 13:00 and neither on a Tuesday: the one before, though further.
        Assert.Equal(s.FriBeforeG2.Id, Habits.Like(t, s.Aerial.Id, LastFri.AddDays(-3), new TimeOnly(13, 0))?.Id);
        // Another class is never the one, though Juggling last Friday was for G1 with Ada at 13:00.
        Assert.Equal(s.MonG1.Id, Habits.Like(t, s.Aerial.Id, Fri, new TimeOnly(13, 0), groupIds: [s.G1.Id], facilitatorIds: [s.Ada.Id])?.Id);
    }

    [Fact]
    public void TheGroupsOrPeopleNamedComeBeforeTheWeekday()
    {
        var s = Make();
        var t = s.B.T;
        // G1 on a Friday morning: G1's Friday, though G2's is nearer the start.
        Assert.Equal(s.FriG1.Id, Habits.Like(t, s.Aerial.Id, Fri, new TimeOnly(9, 0), groupIds: [s.G1.Id])?.Id);
        // G1 on a Tuesday: G1's Monday morning is nearer 09:00 than its Friday afternoon.
        Assert.Equal(s.MonG1.Id, Habits.Like(t, s.Aerial.Id, Build.Monday.AddDays(1), new TimeOnly(9, 0), groupIds: [s.G1.Id])?.Id);
        Assert.Equal(s.FriG1.Id, Habits.Like(t, s.Aerial.Id, Fri, new TimeOnly(9, 0), facilitatorIds: [s.Bo.Id])?.Id);
        // A group it has never been for changes nothing.
        var g3 = s.B.Group("G3");
        Assert.Equal(s.FriG2Early.Id, Habits.Like(s.B.T, s.Aerial.Id, Fri, new TimeOnly(9, 0), groupIds: [g3.Id])?.Id);
    }

    [Fact]
    public void ACancelledOneNeverRanSoIsNeverTheOneAndTheFutureOnlyWhenThereIsNoPast()
    {
        var s = Make();
        s.B.Put(s.FriG1 with { Cancelled = true });
        Assert.Equal(s.FriBeforeG2.Id, Habits.Like(s.B.T, s.Aerial.Id, Fri, new TimeOnly(13, 0))?.Id);
        // Before every one of them: the soonest after.
        Assert.Equal(s.FriBeforeG2.Id, Habits.Like(s.B.T, s.Aerial.Id, FriBefore.AddDays(-7), new TimeOnly(13, 0))?.Id);
        Assert.Equal(s.MonG1.Id, Habits.Like(s.B.T, s.Aerial.Id, LastMon.AddDays(-7))?.Id);
        Assert.Null(Habits.Like(s.B.T, s.B.Class("Never timetabled").Id, Fri));
        Assert.Null(Habits.Like(s.B.T, Guid.NewGuid(), Fri));
    }

    [Fact]
    public void NothingFormerComesWithIt()
    {
        var s = Make();
        s.B.T = s.B.T with
        {
            Rooms = s.B.T.Rooms.Put(s.Studio with { FormerSince = Build.Monday }),
            Facilitators = s.B.T.Facilitators.Put(s.Bo with { FormerSince = Build.Monday }),
        };
        var like = Habits.Like(s.B.T, s.Aerial.Id, Fri, new TimeOnly(13, 0));
        Assert.Equal(s.FriG1.Id, like?.Id);
        Assert.Null(like!.RoomId);
        Assert.Empty(like.Facilitators);
        Assert.Equal(s.FriG1.Audience, like.Audience);
    }
}
