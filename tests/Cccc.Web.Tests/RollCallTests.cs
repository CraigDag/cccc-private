using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Find;
using Cccc.Web.Features.RollCall;

namespace Cccc.Web.Tests;

/// <summary>
/// The roll calls running, held in memory only: one for each timetable, ticked by anybody at once, forgotten
/// when it ends, and ending by itself when somebody forgets to.
/// </summary>
public class RollCallsTests
{
    private static readonly DateOnly Monday = new(2030, 3, 4);
    private static readonly TimeOnly Quarter = new(10, 15);

    private sealed class Clock
    {
        public DateTimeOffset Now = new(2030, 3, 4, 9, 15, 0, TimeSpan.Zero);
    }

    [Fact]
    public void OneRollATimetableTickedByAnybodyAndForgottenWhenItEnds()
    {
        var clock = new Clock();
        var calls = new RollCalls(() => clock.Now);
        var (school, other) = (Guid.NewGuid(), Guid.NewGuid());
        var (alba, bea) = (Guid.NewGuid(), Guid.NewGuid());
        Assert.Null(calls.For(school));

        var run = calls.Start(school, Monday, Quarter, "Preview Admin");
        Assert.Equal((Monday, Quarter, clock.Now, "Preview Admin"), (run.Date, run.At, run.StartedAt, run.StartedBy));
        Assert.Empty(run.Found);
        // Somebody pressing a minute later gets the same roll, not a new one at the later minute.
        clock.Now = clock.Now.AddMinutes(1);
        Assert.Same(run, calls.Start(school, Monday, new(10, 16), "Preview Editor"));
        Assert.Null(calls.For(other));

        calls.Mark(school, alba, true);
        calls.Mark(school, [alba, bea], true);
        Assert.Equal([alba, bea], calls.For(school)!.Found.OrderBy(id => id == bea));
        calls.Mark(school, alba, false);
        Assert.Equal([bea], calls.For(school)!.Found);
        // A tick for a timetable with no roll call running is nowhere.
        calls.Mark(other, alba, true);
        Assert.Null(calls.For(other));

        calls.End(school);
        Assert.Null(calls.For(school));
        // Called again, it starts with nobody found: nothing of the last one is kept.
        Assert.Empty(calls.Start(school, Monday, new(10, 20), "Preview Admin").Found);
    }

    [Fact]
    public void OneLeftRunningEndsByItselfAfterThreeHours()
    {
        var clock = new Clock();
        var calls = new RollCalls(() => clock.Now);
        var school = Guid.NewGuid();
        calls.Start(school, Monday, Quarter, "Preview Admin");
        calls.Mark(school, Guid.NewGuid(), true);
        clock.Now = clock.Now + RollCalls.Lasts - TimeSpan.FromSeconds(1);
        Assert.NotNull(calls.For(school));
        clock.Now = clock.Now.AddSeconds(1);
        Assert.Null(calls.For(school));
        Assert.Equal(TimeSpan.FromHours(3), RollCalls.Lasts);
        // Ended by itself, a new one starts from nothing.
        Assert.Empty(calls.Start(school, Monday, new(13, 15), "Preview Admin").Found);
    }

    [Fact]
    public void EverybodyLookingIsToldOfEachChangeAndNotOfOneThatChangesNothing()
    {
        var calls = new RollCalls(() => DateTimeOffset.UnixEpoch);
        var school = Guid.NewGuid();
        var told = new List<Guid>();
        calls.Changed += told.Add;
        var (alba, bea) = (Guid.NewGuid(), Guid.NewGuid());

        calls.Mark(school, alba, true);
        calls.End(school);
        Assert.Empty(told);

        calls.Start(school, Monday, Quarter, "Preview Admin");
        calls.Start(school, Monday, Quarter, "Preview Editor");
        Assert.Single(told);
        // A whole space at once is one change; ticking who is ticked already, or unticking who is not, is none.
        calls.Mark(school, [alba, bea], true);
        calls.Mark(school, alba, true);
        calls.Mark(school, Guid.NewGuid(), false);
        Assert.Equal(2, told.Count);
        calls.End(school);
        Assert.Equal([school, school, school], told);
    }
}

/// <summary>
/// The roll call's words, on the page and in the band across every page: what is in a space and until when,
/// what to count by eye, and where the timetable has somebody in none of the spaces. Never why somebody is away.
/// </summary>
public class RollCallWordsTests
{
    private static readonly DateOnly Monday = new(2030, 3, 4);
    private static readonly TimeOnly HalfTen = new(10, 30);

    private sealed record School(Timetable T, Room StudioA, Room BigTop, Room Canteen);

    // At 10:30: Acrobatics in Studio A (Bea excused on its register), Open training in Big Top for the whole
    // building, a staff meeting in the canteen naming nobody, Cai between Juggling and Dance, Dag's Dance still
    // to come, Esme's Juggling over, Finn away, and Dina at the museum.
    private static School Build()
    {
        Room Room(string name) => new() { Name = name };
        var (studioA, studioB, bigTop, canteen) = (Room("Studio A"), Room("Studio B"), Room("Big Top"), Room("Canteen"));
        var (ba1, ba2) = (new Group { Name = "BA 1" }, new Group { Name = "BA 2" });
        Student Student(string first, Group g) => new() { FirstName = first, Surname = "Test", GroupId = g.Id };
        var (alba, bea, cai, dina) = (Student("Alba", ba1), Student("Bea", ba1), Student("Cai", ba2), Student("Dina", ba2));
        Facilitator Facilitator(string name) => new() { Name = name };
        var (ada, bo, dag, esme, finn) = (Facilitator("Ada"), Facilitator("Bo"), Facilitator("Dag"), Facilitator("Esme"), Facilitator("Finn"));
        Session Session(string title, string from, string to, Audience audience, Room room, params Facilitator[] who) => new()
        {
            Date = Monday, Start = TimeOnly.Parse(from), End = TimeOnly.Parse(to), Title = title, Audience = audience, RoomId = room.Id,
            Facilitators = [.. who.Select(f => new Assignment(f.Id, null, null))],
        };
        var acro = Session("Acrobatics", "10:00", "11:00", Audience.ForGroup(ba1.Id), studioA, ada);
        var t = new Timetable() with
        {
            Rooms = EntitySet<Room>.Of([studioA, studioB, bigTop, canteen]),
            Groups = EntitySet<Group>.Of([ba1, ba2]),
            Students = EntitySet<Student>.Of([alba, bea, cai, dina]),
            Facilitators = EntitySet<Facilitator>.Of([ada, bo, dag, esme, finn]),
            Sessions = EntitySet<Session>.Of([
                acro,
                Session("Open training", "10:00", "12:00", Audience.Everyone, bigTop, bo),
                Session("Juggling", "09:00", "10:00", Audience.ForGroup(ba2.Id), studioB, esme),
                Session("Dance", "11:00", "12:00", Audience.ForGroup(ba2.Id), studioB, dag, finn)]),
            Registers = EntitySet<Register>.Of([new Register { Id = acro.Id, PresentIds = [alba.Id], ExcusedIds = [bea.Id], RosterIds = [alba.Id, bea.Id] }]),
            Absences = EntitySet<Absence>.Of([new Absence { Person = AbsentPerson.Facilitator, PersonId = finn.Id, From = Monday, To = Monday }]),
            Events = EntitySet<SchoolEvent>.Of([
                new SchoolEvent { Title = "Staff meeting", Date = Monday, Start = new(10, 15), End = new(10, 45), RoomId = canteen.Id },
                new SchoolEvent { Title = "Museum visit", Date = Monday, Start = new(10, 0), End = new(14, 0), Place = " City Museum ", StudentIds = [dina.Id], Audience = EventAudience.Students }]),
        };
        return new(t, studioA, bigTop, canteen);
    }

    [Fact]
    public void ASpaceSaysWhatIsInItUntilWhenAndWhatToCountByEye()
    {
        var (t, studioA, bigTop, canteen) = Build();
        var roll = RollCall.At(t, Monday, HalfTen, EventViewer.Administrator);
        Assert.Equal(["Big Top", "Canteen", "Studio A"], roll.Spaces.Select(s => RollCallWords.Space(t, s.RoomId)));
        var (top, meeting, studio) = (roll.Spaces[0], roll.Spaces[1], roll.Spaces[2]);
        Assert.Equal("Open training until 12:00", RollCallWords.InIt(t, top));
        Assert.Equal("Open training, for the whole building", RollCallWords.ByEye(t, Assert.Single(top.ByEye)));
        Assert.Equal("Staff meeting until 10:45", RollCallWords.InIt(t, meeting));
        Assert.Equal("Staff meeting, an event", RollCallWords.ByEye(t, Assert.Single(meeting.ByEye)));
        Assert.Equal("Acrobatics until 11:00", RollCallWords.InIt(t, studio));
        Assert.Equal(["Ada", "Alba Test"], studio.People.Select(p => p.Name));

        // A class of a group with nobody in it names nobody either; two at once are both said, classes first.
        var empty = new Group { Name = "BA 3" };
        var t2 = t with
        {
            Groups = t.Groups.Put(empty),
            Sessions = t.Sessions.Put(new Session { Date = Monday, Start = new(10, 0), End = new(11, 30), Title = "Clown", Audience = Audience.ForGroup(empty.Id), RoomId = canteen.Id }),
            Events = t.Events.Put(new SchoolEvent { Title = "Open day", Date = Monday, AllDay = true, RoomId = canteen.Id }),
        };
        var busy = RollCall.At(t2, Monday, HalfTen, EventViewer.Administrator).Spaces.Single(s => s.RoomId == canteen.Id);
        Assert.Equal("Clown until 11:30 · Open day, all day · Staff meeting until 10:45", RollCallWords.InIt(t2, busy));
        Assert.Equal("Clown, with nobody on its list", RollCallWords.ByEye(t2, busy.ByEye[0]));
        // A class given no space, and one whose space has gone, are said as such.
        Assert.Equal("No space given", RollCallWords.Space(t, null));
        Assert.Equal("A space that has gone", RollCallWords.Space(t, Guid.NewGuid()));
    }

    [Fact]
    public void OutsideTheSpacesEachKindSaysWhereTheTimetableHasThemAndNeverWhyTheyAreAway()
    {
        var (t, _, _, _) = Build();
        var roll = RollCall.At(t, Monday, HalfTen, EventViewer.Administrator);
        var cai = Assert.Single(roll.Between);
        Assert.Equal(("Between classes", "Juggling in Studio B until 10:00, then Dance in Studio B at 11:00"),
            (RollCallWords.Heading(cai.Where), RollCallWords.Detail(t, cai)));
        Assert.Equal(
            [("At an event in none of the spaces", "Dina Test", "Museum visit until 14:00 (City Museum)"),
             ("First class still to come", "Dag", "Dance in Studio B at 11:00"),
             ("Classes over for today", "Esme", "Juggling in Studio B, until 10:00"),
             ("Not there, says the register", "Bea Test", "Acrobatics in Studio A: excused"),
             ("Recorded away", "Finn", "")],
            roll.NotExpected.Select(p => (RollCallWords.Heading(p.Where), p.Name, RollCallWords.Detail(t, p))));
        // Absent rather than excused says so; an absence with a reason still says only that they are away.
        var acro = t.Sessions.Single(s => s.Title == "Acrobatics");
        var absent = t with { Registers = t.Registers.Put(t.Registers[acro.Id]! with { ExcusedIds = [] }) };
        Assert.Equal("Acrobatics in Studio A: absent",
            RollCallWords.Detail(absent, RollCall.At(absent, Monday, HalfTen, EventViewer.Administrator).NotExpected.Single(p => p.Name == "Bea Test")));
        var finn = t.Absences.Single();
        var why = t with { Absences = t.Absences.Put(finn with { Reason = "Knee operation" }) };
        var away = RollCall.At(why, Monday, HalfTen, EventViewer.Administrator).NotExpected.Single(p => p.Where == RollWhere.Away);
        Assert.Equal("", RollCallWords.Detail(why, away));
        Assert.DoesNotContain(roll.NotExpected.Concat(roll.Between), p => RollCallWords.Detail(why, p).Contains("Knee", StringComparison.Ordinal));
    }

    [Fact]
    public void BeforeTheCallItSaysWhomItWouldList()
    {
        var (t, _, _, _) = Build();
        // Ada and Alba in Acrobatics, Bo in Open training, Cai between classes; Bea is excused, Dina at the museum.
        Assert.Equal("2 facilitators and 2 students expected, and 2 classes or events to count by eye.",
            RollCallWords.Expected(RollCall.At(t, Monday, HalfTen, EventViewer.Administrator)));
        // Somebody who may not see the museum visit has Dina between classes, as everywhere.
        Assert.Equal("2 facilitators and 3 students expected, and 2 classes or events to count by eye.",
            RollCallWords.Expected(RollCall.At(t, Monday, HalfTen, new EventViewer(false, false, false, true, false))));
        // A kind with nobody is not said, and nor is counting by eye when there is nothing to count.
        var dance = t.Sessions.Single(s => s.Title == "Dance");
        var open = t.Sessions.Single(s => s.Title == "Open training");
        var noStaff = t with { Sessions = t.Sessions.Put(dance with { Facilitators = [] }).Remove(open.Id) };
        Assert.Equal("2 students expected.", RollCallWords.Expected(RollCall.At(noStaff, Monday, new(11, 30), EventViewer.Administrator)));
        Assert.Equal("The timetable expects nobody in the building now.", RollCallWords.Expected(RollCall.At(t, Monday, new(18, 0), EventViewer.Administrator)));
        var onlyEvents = t with { Sessions = EntitySet<Session>.Of([]) };
        Assert.Equal("Nobody expected by name, and 1 class or event to count by eye.",
            RollCallWords.Expected(RollCall.At(onlyEvents, Monday, new(10, 20), EventViewer.Administrator)));
    }
}

/// <summary>The roll call is found in Ctrl+K by what it is for, by whoever may call it, and by nobody else.</summary>
public class RollCallFindTests
{
    [Fact]
    public void FireAndEvacuationFindTheRollCallForWhoeverMayCallIt()
    {
        var t = new Timetable();
        foreach (var q in new[] { "roll", "fire", "evacuation", "drill" })
            Assert.Equal("roll-call", FindIndex.Search(t, q, mayEdit: false, admin: false, rollCall: true)[0].Kind);
        Assert.DoesNotContain(FindIndex.Search(t, "fire", mayEdit: true, admin: true), h => h.Kind == "roll-call");
        Assert.Contains(FindIndex.Search(t, "", mayEdit: false, admin: false, rollCall: true), h => h.Kind == "roll-call");
    }
}
