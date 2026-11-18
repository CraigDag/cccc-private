using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// A roll call: everybody the timetable has in the building at one moment, by the space it has them in, the
/// plan's own answer to where each of them is. Whoever is away, or marked absent by a register, is not
/// expected; whoever is between classes is, in no space; what names nobody is counted by eye.
/// </summary>
public class RollCallTests
{
    private static readonly TimeOnly HalfTen = new(10, 30);
    private static readonly EventViewer Admin = EventViewer.Administrator;

    private static (RollWhere Where, string Name, Guid? Room) Line(RollPerson p) => (p.Where, p.Name, p.RoomId);

    [Fact]
    public void EverybodyInAClassIsInItsSpaceFacilitatorsFirstAndSomebodyBetweenClassesIsExpectedInNone()
    {
        var b = new Build();
        var (bigTop, studioA) = (b.Room("Big Top"), b.Room("Studio A"));
        var (ba1, ba2) = (b.Group("BA 1"), b.Group("BA 2"));
        var (alba, bea, cai) = (b.Student("Alba", ba1), b.Student("Bea", ba1), b.Student("Cai", ba2));
        var (ada, bo, cleo, dag) = (b.Facilitator("Ada"), b.Facilitator("Bo"), b.Facilitator("Cleo"), b.Facilitator("Dag"));
        b.T = b.T with { Facilitators = b.T.Facilitators.Put(cleo with { IsGuest = true }) };
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(ba1.Id), studioA, b.Class("Juggling"), In(ada));
        var aerial = b.Session(Monday, "10:00", "11:00", Audience.ForGroup(ba2.Id), bigTop, b.Class("Aerial"), In(bo), In(cleo));
        b.Session(Monday, "11:00", "12:00", Audience.ForGroup(ba1.Id), studioA, b.Class("Acrobatics"), In(ada));
        b.Session(Monday, "13:00", "14:00", Audience.ForGroup(ba2.Id), studioA, b.Class("Dance"), In(dag));
        // Another day's class is nobody's today; neither is a cancelled one.
        b.Session(Monday.AddDays(1), "10:00", "11:00", Audience.ForGroup(ba1.Id), studioA, b.Class("Clown"), In(ada));
        b.Put(b.Session(Monday, "10:00", "11:00", Audience.ForGroup(ba1.Id), studioA, b.Class("Mime"), In(ada)) with { Cancelled = true });

        var roll = RollCall.At(b.T, Monday, HalfTen, Admin);

        var space = Assert.Single(roll.Spaces);
        Assert.Equal(bigTop.Id, space.RoomId);
        Assert.Equal([aerial.Id], space.Classes.Select(o => o.Session.Id));
        Assert.Equal([(RollWhere.InClass, "Bo", bigTop.Id), (RollWhere.InClass, "Cleo", bigTop.Id), (RollWhere.InClass, "Cai Test", bigTop.Id)],
            space.People.Select(Line));
        Assert.True(space.People.Single(p => p.Name == "Cleo").IsGuest);
        Assert.Empty(space.ByEye);
        // BA 1 and Ada are between Juggling and Acrobatics: expected, in no space, saying what came last and what comes next.
        Assert.Equal([(RollWhere.Between, "Ada", null), (RollWhere.Between, "Alba Test", null), (RollWhere.Between, "Bea Test", null)],
            roll.Between.Select(Line));
        var between = roll.Between[0].Day;
        Assert.Equal(("Juggling", "Acrobatics"), (between.Last!.O.Session.Title, between.Next!.O.Session.Title));
        // Dag's day has not begun: listed apart, not expected.
        Assert.Equal([(RollWhere.NotInYet, "Dag", null)], roll.NotExpected.Select(Line));
        Assert.Equal(6, roll.Expected.Count());

        // At 12:30, BA 1's day is over and Dag's is still to come; at 13:30 Dag is in Studio A with BA 2.
        var lunch = RollCall.At(b.T, Monday, new(12, 30), Admin);
        Assert.Empty(lunch.Spaces);
        Assert.Equal([(RollWhere.Between, "Cai Test", null)], lunch.Between.Select(Line));
        Assert.Equal(
            [(RollWhere.NotInYet, "Dag", null), (RollWhere.Gone, "Ada", null), (RollWhere.Gone, "Bo", null), (RollWhere.Gone, "Cleo", null),
             (RollWhere.Gone, "Alba Test", null), (RollWhere.Gone, "Bea Test", null)],
            lunch.NotExpected.Select(Line));
        var dance = RollCall.At(b.T, Monday, new(13, 30), Admin);
        Assert.Equal([(RollWhere.InClass, "Dag", studioA.Id), (RollWhere.InClass, "Cai Test", studioA.Id)], Assert.Single(dance.Spaces).People.Select(Line));
        // A class that ends as the roll is called is over.
        Assert.DoesNotContain(RollCall.At(b.T, Monday, new(11, 0), Admin).Spaces, s => s.RoomId == bigTop.Id);
    }

    [Fact]
    public void AwayOrMarkedAbsentIsNotExpectedAndAFacilitatorsPartIsOnlyTheirPart()
    {
        var b = new Build();
        var hall = b.Room("Hall");
        var ba1 = b.Group("BA 1");
        var (alba, bea, cai) = (b.Student("Alba", ba1), b.Student("Bea", ba1), b.Student("Cai", ba1));
        var (ada, bo, esme) = (b.Facilitator("Ada"), b.Facilitator("Bo"), b.Facilitator("Esme"));
        var aerial = b.Session(Monday, "10:00", "12:00", Audience.ForGroup(ba1.Id), hall, b.Class("Aerial"), In(ada), In(bo, until: "10:15"), In(esme));
        b.Away(esme, Monday);
        b.T = b.T with { Absences = b.T.Absences.Put(new Absence { Person = AbsentPerson.Student, PersonId = bea.Id, From = Monday, To = Monday }) };

        // Before its register is taken, everybody on the roster not recorded away is expected.
        var roll = RollCall.At(b.T, Monday, HalfTen, Admin);
        Assert.Equal([(RollWhere.InClass, "Ada", hall.Id), (RollWhere.InClass, "Alba Test", hall.Id), (RollWhere.InClass, "Cai Test", hall.Id)],
            Assert.Single(roll.Spaces).People.Select(Line));
        // Bo's part ended at 10:15; Esme and Bea are away.
        Assert.Equal([(RollWhere.Gone, "Bo", null), (RollWhere.Away, "Esme", null), (RollWhere.Away, "Bea Test", null)], roll.NotExpected.Select(Line));

        // Taken, the register says who is not there: Cai, marked absent; Alba, there.
        b.T = b.T with { Registers = b.T.Registers.Put(new Register { Id = aerial.Id, RosterIds = [alba.Id, bea.Id, cai.Id], PresentIds = [alba.Id], ExcusedIds = [bea.Id] }) };
        roll = RollCall.At(b.T, Monday, HalfTen, Admin);
        Assert.Equal([(RollWhere.InClass, "Ada", hall.Id), (RollWhere.InClass, "Alba Test", hall.Id)], Assert.Single(roll.Spaces).People.Select(Line));
        Assert.Equal([(RollWhere.Gone, "Bo", null), (RollWhere.MarkedAbsent, "Cai Test", null), (RollWhere.Away, "Esme", null), (RollWhere.Away, "Bea Test", null)],
            roll.NotExpected.Select(Line));
        // Somebody put on the class after the register was taken is expected: the register says nothing of them.
        var dan = b.Student("Dan", ba1);
        Assert.Contains(RollCall.At(b.T, Monday, HalfTen, Admin).Spaces.Single().People, p => p.Id == dan.Id && p.Where == RollWhere.InClass);
        // Esme away only until 10:00 is in the room at 10:30.
        b.T = b.T with { Absences = b.T.Absences.Remove(b.T.Absences.Single(a => a.PersonId == esme.Id).Id) };
        b.Away(esme, Monday, "08:00", "10:00");
        Assert.Contains(RollCall.At(b.T, Monday, HalfTen, Admin).Spaces.Single().People, p => p.Id == esme.Id && p.Where == RollWhere.InClass);
    }

    [Fact]
    public void WhatNamesNobodyIsCountedByEyeAndAnEventPutsWhoItNamesInItsSpaceOrAwayFromTheBuilding()
    {
        var b = new Build();
        var (gym, canteen, studio) = (b.Room("Gym"), b.Room("Canteen"), b.Room("Studio"));
        var (ba1, empty) = (b.Group("BA 1"), b.Group("Nobody yet"));
        var (alba, bea) = (b.Student("Alba", ba1), b.Student("Bea", ba1));
        var (ada, bo, cleo) = (b.Facilitator("Ada"), b.Facilitator("Bo"), b.Facilitator("Cleo"));
        var open = b.Session(Monday, "10:00", "11:30", Audience.Everyone, gym, b.Class("Open training", Workload.DropIn), In(cleo));
        var none = b.Session(Monday, "10:00", "11:00", Audience.ForGroup(empty.Id), studio, b.Class("Taster"));
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(ba1.Id), studio, b.Class("Juggling"), In(bo));
        var meeting = new SchoolEvent { Title = "Staff meeting", Date = Monday, Start = new(10, 0), End = new(11, 0), Audience = EventAudience.Staff, RoomId = canteen.Id, FacilitatorIds = [ada.Id, bo.Id] };
        var trip = new SchoolEvent { Title = "Museum visit", Date = Monday, Start = new(10, 0), End = new(12, 0), Audience = EventAudience.Students, Place = "Town", StudentIds = [alba.Id] };
        var secret = new SchoolEvent { Title = "Salary talks", Date = Monday, Start = new(10, 0), End = new(11, 0), Audience = EventAudience.Administrators, RoomId = studio.Id };
        var over = new SchoolEvent { Title = "Breakfast", Date = Monday, Start = new(9, 30), End = new(10, 30), Audience = EventAudience.Everyone, RoomId = canteen.Id };
        b.T = b.T with { Events = b.T.Events.Put(meeting).Put(trip).Put(secret).Put(over) };

        var roll = RollCall.At(b.T, Monday, HalfTen, Admin);
        Assert.Equal([canteen.Id, gym.Id, studio.Id], roll.Spaces.Select(s => s.RoomId));
        var (inCanteen, inGym, inStudio) = (roll.Spaces[0], roll.Spaces[1], roll.Spaces[2]);
        // The meeting names Ada and Bo, so they are in the Canteen; it is counted by eye too, for anybody it does not name.
        Assert.Equal([(RollWhere.AtEvent, "Ada", canteen.Id), (RollWhere.AtEvent, "Bo", canteen.Id)], inCanteen.People.Select(Line));
        Assert.Equal(["Staff meeting"], inCanteen.ByEye.Select(x => x.Event!.Event.Title));
        Assert.Equal(meeting.Id, inCanteen.ByEye[0].Id);
        // Open training names nobody on a roster: Cleo is there, and the rest are counted by eye.
        Assert.Equal([(RollWhere.InClass, "Cleo", gym.Id)], inGym.People.Select(Line));
        Assert.Equal([open.Id], inGym.ByEye.Select(x => x.Id));
        // A class for a group with nobody in it, and the talks the administrator sees, are counted by eye in the Studio.
        Assert.Empty(inStudio.People);
        Assert.Equal([none.Id, secret.Id], inStudio.ByEye.Select(x => x.Id));
        // Alba is on the museum visit, away from the building; Bea's day is over.
        Assert.Empty(roll.Between);
        Assert.Equal([(RollWhere.Elsewhere, "Alba Test", null), (RollWhere.Gone, "Bea Test", null)], roll.NotExpected.Select(Line));
        Assert.Equal(trip.Id, roll.NotExpected[0].Event!.Event.Id);

        // A coach does not see the administrators' talks, so nothing of them is on their roll call.
        var coach = new EventViewer(false, false, false, true, true);
        Assert.Equal([none.Id], RollCall.At(b.T, Monday, HalfTen, coach).Spaces.Single(s => s.RoomId == studio.Id).ByEye.Select(x => x.Id));
        // Over, the meeting puts nobody anywhere: Ada has nothing else today, Bo's Juggling is over.
        var later = RollCall.At(b.T, Monday, new(11, 0), Admin);
        Assert.DoesNotContain(later.Spaces, s => s.RoomId == canteen.Id);
        Assert.Equal([(RollWhere.Gone, "Bo", null)], later.NotExpected.Where(p => !p.IsStudent).Select(Line));
    }

    [Fact]
    public void AtTheEdgesTheClassComesFirstAMinuteCountsAndOnlyWhoIsTheirsTodayIsListed()
    {
        var b = new Build();
        var (hall, ring, canteen) = (b.Room("Hall"), b.Room("Ring"), b.Room("Canteen"));
        var visiting = b.Programme("Visiting", guest: true);
        var (ba1, ba2, troupe, nobody) = (b.Group("BA 1"), b.Group("BA 2"), b.Group("Troupe", visiting), b.Group("Nobody yet"));
        var (alba, bea, cara) = (b.Student("Alba", ba1), b.Student("Bea", ba1), b.Student("Cara", ba2));
        var (gus, hal) = (b.Student("Gus", troupe), b.Student("Hal", troupe));
        var (ada, bo, finn, gil, hana, ivo) = (b.Facilitator("Ada"), b.Facilitator("Bo"), b.Facilitator("Finn"), b.Facilitator("Gil"), b.Facilitator("Hana"), b.Facilitator("Ivo"));
        b.Session(Monday, "09:00", "10:00", Audience.ForGroup(ba1.Id), hall, b.Class("Juggling"), In(ada));
        b.Session(Monday, "10:00", "10:30", Audience.ForGroup(ba1.Id), hall, b.Class("Acrobatics"), In(ada));
        b.Session(Monday, "11:00", "12:00", Audience.ForGroup(ba1.Id), hall, b.Class("Dance"), In(ada));
        // A class with nobody on its list, over as the roll is called: nothing to count by eye.
        b.Session(Monday, "10:00", "10:30", Audience.ForGroup(nobody.Id), ring, b.Class("Taster"));
        var warmUp = b.Session(Monday, "10:00", "11:00", Audience.ForGroup(troupe.Id), ring, b.Class("Warm-up"), In(bo));
        b.T = b.T with { Registers = b.T.Registers.Put(new Register { Id = warmUp.Id, RosterIds = [gus.Id, hal.Id], PresentIds = [gus.Id], ExcusedIds = [hal.Id] }) };
        // Hana's class this afternoon is Finn's to cover, and both are away all day; Ivo is away with nothing today.
        b.Put(b.Session(Monday, "13:00", "14:00", Audience.ForGroup(ba1.Id), hall, b.Class("Clown"), In(hana)) with { CoverIds = [finn.Id] });
        b.Away(hana, Monday);
        b.Away(finn, Monday);
        b.Away(ivo, Monday);
        // Gil is former: on a class today, and on no roll call.
        b.Session(Monday, "10:00", "11:00", Audience.ForGroup(nobody.Id), canteen, b.Class("Rigging"), In(gil));
        b.T = b.T with { Facilitators = b.T.Facilitators.Put(gil with { FormerSince = Monday }) };
        // A fitting starting the very minute the roll is called, naming Bea (between classes), Cara (nothing today) and Gus (in the warm-up).
        var fitting = new SchoolEvent { Title = "Fitting", Date = Monday, Start = HalfTen, End = new(11, 0), Audience = EventAudience.Everyone, RoomId = canteen.Id, StudentIds = [bea.Id, cara.Id, gus.Id] };
        b.T = b.T with { Events = b.T.Events.Put(fitting) };

        var roll = RollCall.At(b.T, Monday, HalfTen, Admin);
        Assert.Equal([canteen.Id, ring.Id], roll.Spaces.Select(s => s.RoomId));
        // Cara has nothing on today and is still at the fitting she is named on; Bea is at it rather than between classes.
        Assert.Equal([(RollWhere.AtEvent, "Bea Test", canteen.Id), (RollWhere.AtEvent, "Cara Test", canteen.Id)], roll.Spaces[0].People.Select(Line));
        // In a class, Gus is in its space whatever event names him; a guest programme's student is a guest, nobody else is.
        Assert.Equal([(RollWhere.InClass, "Bo", ring.Id), (RollWhere.InClass, "Gus Test", ring.Id)], roll.Spaces[1].People.Select(Line));
        Assert.Equal([false, true], roll.Spaces[1].People.Select(p => p.IsGuest));
        Assert.Empty(roll.Spaces[1].ByEye);
        // Ada's last class is the one that has just ended, not her first.
        Assert.Equal([(RollWhere.Between, "Ada", null), (RollWhere.Between, "Alba Test", null)], roll.Between.Select(Line));
        Assert.Equal(("Acrobatics", "Dance"), (roll.Between[0].Day.Last!.O.Session.Title, roll.Between[0].Day.Next!.O.Session.Title));
        // Excused is not there; away with a class today, covering it or not, is listed away; away with nothing today, and former, are on no list.
        Assert.Equal([(RollWhere.MarkedAbsent, "Hal Test", null), (RollWhere.Away, "Finn", null), (RollWhere.Away, "Hana", null)], roll.NotExpected.Select(Line));
        Assert.DoesNotContain(roll.Spaces.SelectMany(s => s.People).Concat(roll.Between).Concat(roll.NotExpected), p => p.Id == ivo.Id || p.Id == gil.Id);
        // At 11:00 the warm-up has just ended: Bo's last class is it, so he is listed as gone rather than not at all.
        var eleven = RollCall.At(b.T, Monday, new(11, 0), Admin);
        var bosDay = Assert.Single(eleven.NotExpected, p => p.Id == bo.Id);
        Assert.Equal((RollWhere.Gone, "Warm-up"), (bosDay.Where, bosDay.Day.Last!.O.Session.Title));
    }

    [Fact]
    public void NamesAreInDanishOrderAndAClassWithNoSpaceComesLast()
    {
        var b = new Build();
        var (zed, aerial) = (b.Room("Zed hall"), b.Room("Ærø room"));
        var g = b.Group("BA 1");
        b.Student("Øjvind", g);
        b.Student("Zara", g);
        b.Student("Aase", g);
        b.Session(Monday, "10:00", "11:00", Audience.ForGroup(g.Id), null, b.Class("Theory"), In(b.Facilitator("Åse")), In(b.Facilitator("Zia")));
        b.Session(Monday, "10:00", "11:00", Audience.Everyone, aerial, b.Class("Open"));
        b.Session(Monday, "10:00", "11:00", Audience.Everyone, zed, b.Class("Open too"));

        var roll = RollCall.At(b.T, Monday, HalfTen, Admin);
        Assert.Equal([zed.Id, aerial.Id, null], roll.Spaces.Select(s => s.RoomId));
        // Æ Ø Å after Z, and Aa as Å; facilitators before students.
        Assert.Equal(["Zia", "Åse", "Zara Test", "Øjvind Test", "Aase Test"], roll.Spaces[2].People.Select(p => p.Name));
    }
}
