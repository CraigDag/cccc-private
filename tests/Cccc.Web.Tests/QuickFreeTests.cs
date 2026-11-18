using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Web.Features.Find;

namespace Cccc.Web.Tests;

/// <summary>
/// A question typed into Find about who or what is free, or where somebody is (<see cref="QuickFree"/>), asked of
/// the made-up school on Wednesday 18 November 2026 at 10:15.
/// </summary>
public class QuickFreeTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Today = Monday.AddDays(2);
    private static readonly TimeOnly Now = new(10, 15);
    private static readonly Timetable T = SampleSchool.Create(Monday, Today);
    private static readonly EventViewer Editor = new(false, true, false, false, false);

    private static QuickFree.Reading? TryRead(string line, DateOnly? shown = null, Timetable? t = null) =>
        QuickFree.Read(t ?? T, line, Today, shown ?? Today, Now);

    private static QuickFree.Reading Read(string line, DateOnly? shown = null, Timetable? t = null) =>
        TryRead(line, shown, t) ?? throw new Xunit.Sdk.XunitException($"\"{line}\" was not read as a question about who is free");

    /// <summary>
    /// The day, the stretch, how much of the day, what it lists and who it names, and any words not understood, as one
    /// line a failure can show whole.
    /// </summary>
    private static string Said(QuickFree.Reading r) =>
        $"{r.Date:yyyy-MM-dd} {Week.Hm(r.Start)}-{Week.Hm(r.End)} {r.Span}{(r.Now ? " now" : "")} {r.Only} [{string.Join(", ", r.Things.Select(x => x.Name))}]"
        + (r.NotUnderstood.Count > 0 ? " not understood: " + string.Join(", ", r.NotUnderstood) : "");

    private static IReadOnlyList<FindHit> Hits(string line, EventViewer? viewer = null, Timetable? t = null, int max = 12) =>
        QuickFree.Hits(t ?? T, Read(line, t: t), viewer ?? Editor, max);

    private static string Detail(string line, EventViewer? viewer = null, Timetable? t = null) => Hits(line, viewer, t).Single().Detail!;

    private static IEnumerable<string> Lines(IEnumerable<FindHit> hits) => hits.Select(h => h.Label + " — " + h.Detail);

    /// <summary>A fixed id for something a test adds, so the order it checks never depends on ids drawn at random.</summary>
    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    [Theory]
    [InlineData("free", "2026-11-18 10:15-10:16 Moment now Both []")]
    [InlineData("free now", "2026-11-18 10:15-10:16 Moment now Both []")]
    [InlineData("what's free right now", "2026-11-18 10:15-10:16 Moment now Both []")]
    [InlineData("free fri 13-15", "2026-11-20 13:00-15:00 Stretch Both []")]
    [InlineData("free fri 13 to 15", "2026-11-20 13:00-15:00 Stretch Both []")]
    [InlineData("free fri 13", "2026-11-20 13:00-13:01 Moment Both []")]
    [InlineData("available at 13 fri", "2026-11-20 13:00-13:01 Moment Both []")]
    [InlineData("empty 1pm fri", "2026-11-20 13:00-13:01 Moment Both []")]
    [InlineData("free fri noon", "2026-11-20 12:00-12:01 Moment Both []")]
    [InlineData("free fri", "2026-11-20 00:00-23:59 Day Both []")]
    [InlineData("free mon 23/11 13-15", "2026-11-23 13:00-15:00 Stretch Both []")]
    [InlineData("who's free fri 13-15", "2026-11-20 13:00-15:00 Stretch People []")]
    [InlineData("anyone free fri 13", "2026-11-20 13:00-13:01 Moment People []")]
    [InlineData("rooms free fri 13", "2026-11-20 13:00-13:01 Moment Spaces []")]
    [InlineData("free rigging fri 13", "2026-11-20 13:00-13:01 Moment Spaces []")]
    [InlineData("which groups are free fri 13", "2026-11-20 13:00-13:01 Moment Groups []")]
    [InlineData("bo fri 10", "2026-11-20 10:00-10:01 Moment Both [Bo Lindqvist]")]
    [InlineData("bo 10", "2026-11-18 10:00-10:01 Moment Both [Bo Lindqvist]")]
    [InlineData("where is bo", "2026-11-18 10:15-10:16 Moment now Both [Bo Lindqvist]")]
    [InlineData("where's alba", "2026-11-18 10:15-10:16 Moment now Both [Alba Ashcombe]")]
    [InlineData("bo now", "2026-11-18 10:15-10:16 Moment now Both [Bo Lindqvist]")]
    [InlineData("is studio b free thu 14", "2026-11-19 14:00-14:01 Moment Both [Studio B]")]
    [InlineData("ada thu", "2026-11-19 00:00-23:59 Day Both [Ada Brightwater]")]
    [InlineData("is ba 1 free fri 13", "2026-11-20 13:00-13:01 Moment Both [BA 1]")]
    [InlineData("stud fri 13", "2026-11-20 13:00-13:01 Moment Both [Studio A, Studio B]")]
    [InlineData("bo or ada fri 13", "2026-11-20 13:00-13:01 Moment Both [Bo Lindqvist, Ada Brightwater]")]
    public void AQuestionSaysFreeOrWhereOrNamesSomebodyWithADayATimeOrNow(string line, string said) =>
        Assert.Equal(said, Said(Read(line)));

    [Theory]
    [InlineData("free tomorrow morning", "2026-11-19 00:00-12:00", "morning")]
    [InlineData("free fri afternoon", "2026-11-20 12:00-23:45", "afternoon")]
    [InlineData("free fri evening", "2026-11-20 17:00-23:45", "evening")]
    [InlineData("free fri after 14", "2026-11-20 14:00-23:45", "from 14:00")]
    [InlineData("free fri from 14", "2026-11-20 14:00-23:45", "from 14:00")]
    [InlineData("free fri before 11", "2026-11-20 00:00-11:00", "until 11:00")]
    [InlineData("free fri 13-15", "2026-11-20 13:00-15:00", null)]
    public void APartOfTheDayIsAStretchSaidAsItWasSaid(string line, string stretch, string? part)
    {
        var r = Read(line);
        Assert.Equal(stretch, $"{r.Date:yyyy-MM-dd} {Week.Hm(r.Start)}-{Week.Hm(r.End)}");
        Assert.Equal(part, r.Part);
        Assert.Equal($"{Week.ShortDate(r.Date)} {part ?? "13:00–15:00"}", QuickFree.When(r));
    }

    [Theory]
    [InlineData("bo")]
    [InlineData("studio b")]
    [InlineData("fri 13")]
    [InlineData("where")]
    [InlineData("where fri 13")]
    [InlineData("now")]
    [InlineData("aerial fri 13")]
    [InlineData("free aerial fri")]
    [InlineData("degree free fri 13")]
    [InlineData("staff meeting fri 13")]
    [InlineData("who teaches aerial")]
    [InlineData("")]
    public void ALineThatAsksNothingAboutWhoIsFreeIsNotOne(string line) => Assert.Null(TryRead(line));

    [Fact]
    public void AClassNamedByTwoClassesIsStillAClassNamed()
    {
        var aerial = T.Classes.Single(c => c.Name == "Aerial");
        var t = T with { Classes = T.Classes.Put(aerial with { Id = Guid.NewGuid() }) };
        Assert.Null(TryRead("free aerial fri", t: t));
    }

    [Fact]
    public void WordsNotUnderstoodAreSaidAndTheRestAnswered()
    {
        Assert.Equal(["xyz"], Read("free fri 13 xyz").NotUnderstood);
        Assert.EndsWith(" · not understood: xyz", Hits("free fri 13 xyz")[0].Detail);
        Assert.EndsWith(" · not understood: please", Detail("bo fri 10 please"));
        Assert.Empty(Read("is there anything free fri 13").NotUnderstood);
        // A day said twice, by its name and its date, is one day.
        Assert.Empty(Read("free mon 23/11 13-15").NotUnderstood);
        Assert.Empty(Read("free fri 20 nov 13").NotUnderstood);
        Assert.Equal(new DateOnly(2026, 11, 20), Read("free fri 20 nov 13").Date);
    }

    [Fact]
    public void AWordSaidTwiceOrATimeThatCannotBeIsLeftNotUnderstood()
    {
        var twice = Read("bo fri thu 10");
        Assert.Equal((new DateOnly(2026, 11, 20), "thu"), (twice.Date, twice.NotUnderstood.Single()));
        Assert.Equal(["14"], Read("free fri 13:00 14").NotUnderstood);
        Assert.Equal(["16"], Read("free fri 13-15 16").NotUnderstood);
        Assert.Equal(["16:00"], Read("free fri 13-15 16:00").NotUnderstood);
        // From the end of the day, or until its start, is no stretch: the time alone is read.
        foreach (var line in new[] { "free fri from 23:50", "free fri until 00:00" })
        {
            var r = Read(line);
            Assert.True(r.Start < r.End, $"\"{line}\" was read as {Week.Hm(r.Start)}-{Week.Hm(r.End)}");
        }
        Assert.True(Read("free today now").Now);
    }

    [Fact]
    public void ATimeWithNoDayIsOnTheDayThePlanShowsAndNowIsAlwaysToday()
    {
        var friday = new DateOnly(2026, 11, 20);
        Assert.Equal(friday, Read("free 13-15", shown: friday).Date);
        Assert.Equal(friday, Read("bo 10", shown: friday).Date);
        Assert.Equal(Today, Read("free", shown: friday).Date);
        // Nobody is free or busy in the Scratch Week: a day named while it is on screen is this week's.
        Assert.Equal(friday, Read("free fri 13", shown: Week.ScratchMonday).Date);
        Assert.Equal(Today, Read("free 13", shown: Week.ScratchMonday).Date);
    }

    [Fact]
    public void SomebodyNamedIsToldWhatTheyAreInThenOrHowLongTheyAreFree()
    {
        var bo = Hits("bo fri 10").Single();
        Assert.Equal(("facilitator", "Bo Lindqvist", "Fri 20 Nov 10:00 · Guest workshop in Big Top until 11:30"), (bo.Kind, bo.Label, bo.Detail));
        Assert.Equal((T.Facilitators[bo.Id!.Value]!.Colour, (DateOnly?)new DateOnly(2026, 11, 20)), (bo.Colour, bo.On));
        Assert.Equal("Wed 18 Nov 10:00 · free until 13:00, then Open training in Big Top", Detail("bo 10"));
        Assert.Equal("Now · free until 13:00, then Open training in Big Top", Detail("where is bo"));
        // A student by the classes they are on; a group by the classes on for it.
        var alba = Hits("where is alba").Single();
        Assert.Equal(("student", "Now · Strength and conditioning in Gym until 10:30"), (alba.Kind, alba.Detail));
        Assert.Equal("Fri 20 Nov 13:00 · Aerial in Aerial Hall until 14:30", Detail("is ba 1 free fri 13"));
        // A space by who its class is for. Friday's prep juggling is off for the study day, so Studio B has nothing.
        Assert.Equal("Fri 20 Nov 12:15 · Lunch for Everyone until 12:45", Detail("canteen fri 12:15"));
        Assert.Equal("Fri 20 Nov 13:00 · free all day", Detail("studio b fri 13"));
        Assert.Equal("Fri 20 Nov 13:00 · free for the rest of the day", Detail("studio a fri 13"));
        Assert.Equal("Fri 20 Nov 16:30 · Work-in-progress showing until 17:30", Detail("big top fri 16:30"));
        // Away, covering, and away for part of the day, last week.
        Assert.Equal("Thu 19 Nov 10:00 · away all day", Detail("is ada free thu 10"));
        Assert.Equal("Thu 19 Nov · away all day", Detail("ada thu"));
        Assert.Equal("Mon 9 Nov 11:00 · away until 12:00", Detail("bo 9/11 11"));
        Assert.Equal("Mon 9 Nov 11:00 · covering Acrobatics in Big Top until 12:00", Detail("dag 9/11 11"));
        // A stretch: free around it, or what is in its way; a whole day: when they are free in it.
        Assert.Equal("Fri 20 Nov 09:00–12:00 · free all day", Detail("ada fri 9-12"));
        Assert.Equal("Fri 20 Nov 13:00–15:00 · not free: Aerial in Aerial Hall 13:00–14:30", Detail("dag fri 13-15"));
        Assert.Equal("Mon 23 Nov 10:00–14:00 · not free: Acrobatics in Big Top 10:45–12:00 and 1 more", Detail("ada mon 23/11 10-14"));
        Assert.Equal("Fri 20 Nov 12:00–13:00 · free from 11:30", Detail("bo fri 12-13"));
        Assert.Equal("Fri 20 Nov · free until 09:00 and from 11:00", Detail("finn fri"));
        Assert.Equal("Mon 23 Nov · free until 10:45, 12:00–13:00 and from 14:30", Detail("bo mon 23/11"));
        Assert.Equal("Fri 20 Nov · free all day", Detail("ada fri"));
        // Each answer in the colour of what it is about; a student has none.
        Assert.Equal(T.Rooms.Single(r => r.Name == "Studio B").Colour, Hits("studio b fri 13").Single().Colour);
        Assert.Equal(T.Groups.Single(g => g.Name == "BA 1").Colour, Hits("is ba 1 free fri 13").Single().Colour);
        Assert.Null(Hits("where is alba").Single().Colour);
    }

    [Fact]
    public void ADayIsSaidByItsFreeStretchesOrByWhatTakesItWhole()
    {
        var friday = new DateOnly(2026, 11, 20);
        var ada = T.Facilitators.Single(f => f.Name == "Ada Brightwater");
        var cleo = T.Facilitators.Single(f => f.Name == "Cleo Marchetti");
        var esme = T.Facilitators.Single(f => f.Name == "Esme Halloran");
        var alba = T.Students.Single(s => s.Name == "Alba Ashcombe");
        var benno = T.Students.Single(s => s.Name == "Benno Birkeland");
        var gym = T.Rooms.Single(r => r.Name == "Gym");
        // Away from one o'clock is away until the last time an absence can be given, as the absence form gives it.
        Absence Away(AbsentPerson who, Guid id, int from, int fromMinute, int until, int untilMinute) => new()
        {
            Person = who, PersonId = id, From = friday, To = friday, StartTime = new(from, fromMinute), EndTime = new(until, untilMinute),
        };
        var t = T with
        {
            Absences = T.Absences
                .Put(Away(AbsentPerson.Facilitator, ada.Id, 13, 0, 23, 45))
                .Put(Away(AbsentPerson.Student, alba.Id, 0, 0, 12, 0))
                .Put(Away(AbsentPerson.Student, alba.Id, 12, 0, 23, 45))
                .Put(Away(AbsentPerson.Student, benno.Id, 0, 0, 23, 45)),
            Events = T.Events
                .Put(new SchoolEvent { Title = "Festival jury", Date = friday, AllDay = true, FacilitatorIds = [cleo.Id] })
                .Put(new SchoolEvent { Title = "Festival jury", Date = Today.AddDays(1), AllDay = true, FacilitatorIds = [ada.Id] }),
            Sessions = Enumerable.Range(0, 4).Aggregate(T.Sessions, (all, i) => all.Put(new Session
            {
                Date = friday, Start = new(9 + i, 0), End = new(9 + i, 30), Title = "Tumbling", RoomId = gym.Id, Facilitators = [new Assignment(esme.Id)],
            })),
        };
        Assert.Equal("Fri 20 Nov · free until 09:00, 09:30–10:00, 10:30–11:00, 11:30–12:00 and 1 more", Detail("esme fri", t: t));
        Assert.Equal("Fri 20 Nov · free until 13:00", Detail("ada fri", t: t));
        Assert.Equal("Fri 20 Nov 14:00 · away for the rest of the day", Detail("ada fri 14", t: t));
        Assert.Equal("Fri 20 Nov 12:00–14:00 · not free: away from 13:00", Detail("ada fri 12-14", t: t));
        Assert.Equal("Fri 20 Nov · Festival jury all day", Detail("cleo fri", t: t));
        Assert.Equal("Fri 20 Nov 10:00 · Festival jury all day", Detail("cleo fri 10", t: t));
        Assert.Equal("Fri 20 Nov 09:00–11:00 · not free: Festival jury all day", Detail("cleo fri 9-11", t: t));
        // Away all day and at an event all day is away; a day taken by two things is said by the first.
        Assert.Equal("Thu 19 Nov · away all day", Detail("ada thu", t: t));
        Assert.Equal("Fri 20 Nov · away all day", Detail("benno fri", t: t));
        Assert.StartsWith("Fri 20 Nov · not free: away until 12:00 and ", Detail("alba fri", t: t));
    }

    [Fact]
    public void OfTwoNamedAlikeBothAreAnsweredAndSoIsEverybodyNamed()
    {
        Assert.Equal(["Studio A — Fri 20 Nov 13:00 · free for the rest of the day", "Studio B — Fri 20 Nov 13:00 · free all day"], Lines(Hits("stud fri 13")));
        Assert.Equal(["Bo Lindqvist", "Ada Brightwater"], Hits("bo or ada fri 13").Select(h => h.Label));
        Assert.Single(Hits("bo or ada fri 13", max: 1));
        // A student and a facilitator sharing a first name are both answered, in the order of their names.
        var ba2 = T.Groups.Single(g => g.Name == "BA 2");
        var bo = T with { Students = T.Students.Put(new Student { Id = Id(1), FirstName = "Bo", Surname = "Berg", GroupId = ba2.Id }) };
        Assert.Equal(["Bo Berg", "Bo Lindqvist"], Hits("bo fri 10", t: bo).Select(h => h.Label));
        // Somebody named twice over, by their first name and the start of their surname, is answered once.
        Assert.Single(Hits("bo lindq fri 10"));
        var studios = T with { Rooms = T.Rooms.Put(new Room { Id = Id(2), Name = "Studio 0" }).Put(new Room { Id = Id(3), Name = "Studio C" }) };
        Assert.Equal(["Studio 0", "Studio A", "Studio B", "Studio C"], Hits("stud fri 13", t: studios).Select(h => h.Label));
    }

    [Fact]
    public void ADayNothingCanBeSaidAboutSaysWhy()
    {
        // Prep's Friday is a study day, for Prep and its students; the week after next has nothing in it yet.
        Assert.Equal("Fri 20 Nov 13:00 · Prep study day: closed for Prep", Detail("quinn fri 13"));
        Assert.Equal("Fri 20 Nov 13:00 · Prep study day: closed for Prep", Detail("prep free fri 13"));
        Assert.Equal("Mon 30 Nov 10:00 · " + Openings.NothingYet, Detail("bo 30/11 10"));
        var blank = Hits("free 30/11 13").Single();
        Assert.Equal(("free", "Free Mon 30 Nov 13:00", Openings.NothingYet), (blank.Kind, blank.Label, blank.Detail));
    }

    [Fact]
    public void NamingNobodyListsWhatIsFreeThenAfterALineSayingHowMany()
    {
        var hits = Hits("free fri 13-15");
        Assert.Equal(("free", "Free Fri 20 Nov 13:00–15:00", "5 spaces and 5 facilitators"), (hits[0].Kind, hits[0].Label, hits[0].Detail));
        Assert.Equal(
            [
                "Big Top — free from 11:30 until 16:00", "Canteen — free from 12:45", "Gym — free all day", "Studio A — free from 11:00",
                "Studio B — free all day", "Ada Brightwater — free all day", "Bo Lindqvist — free from 11:30", "Cleo Marchetti — free all day",
                "Esme Halloran — free all day", "Finn Aalborg-Reyes — free from 11:00",
            ],
            Lines(hits.Skip(1)));
        Assert.Equal(["room", "room", "room", "room", "room", "facilitator", "facilitator", "facilitator", "facilitator", "facilitator"], hits.Skip(1).Select(h => h.Kind));
        Assert.All(hits, h => Assert.Equal(new DateOnly(2026, 11, 20), h.On));
        // A visiting facilitator is not one of the school's to give the time to.
        Assert.DoesNotContain(hits, h => h.Label == "Gwen Stroud");
        // Now, and a whole day: only what has nothing in it all day.
        var now = Hits("free");
        Assert.Equal(("Free now", "5 spaces and 5 facilitators"), (now[0].Label, now[0].Detail));
        Assert.Contains("Studio A — free until 10:45", Lines(now));
        Assert.DoesNotContain(now, h => h.Label is "Gym" or "Esme Halloran");      // Wednesday's strength is on
        Assert.Equal(["Free Fri 20 Nov — 2 spaces and 3 facilitators", "Gym — free all day", "Studio B — free all day",
                      "Ada Brightwater — free all day", "Cleo Marchetti — free all day", "Esme Halloran — free all day"], Lines(Hits("free fri")));
        Assert.Equal(3, Hits("free fri 13-15", max: 3).Count);
        Assert.Empty(Hits("free fri 13", max: 0));
        Assert.Empty(Hits("bo fri 10", max: 0));
    }

    [Fact]
    public void ASpaceOrAFacilitatorNoLongerUsedIsNotListedAndANewOneIsInItsPlace()
    {
        var gym = T.Rooms.Single(r => r.Name == "Gym");
        var cleo = T.Facilitators.Single(f => f.Name == "Cleo Marchetti");
        var t = T with
        {
            Rooms = T.Rooms.Put(gym with { FormerSince = Today }),
            Facilitators = T.Facilitators.Put(cleo with { FormerSince = Today }).Put(new Facilitator { Id = Id(4), Name = "Anna Berg" }),
        };
        Assert.Equal(["Free Fri 20 Nov — 1 space and 3 facilitators", "Studio B — free all day", "Ada Brightwater — free all day",
                      "Anna Berg — free all day", "Esme Halloran — free all day"], Lines(Hits("free fri", t: t)));
    }

    [Fact]
    public void AQuestionCanAskOnlyForSpacesForWhatTheyHaveForPeopleOrForGroups()
    {
        Assert.Equal(["Free Fri 20 Nov 13:00 — 1 space with Rigging points", "Big Top — free from 11:30 until 16:00"], Lines(Hits("free rigging fri 13")));
        var people = Hits("who's free fri 13-15");
        Assert.Equal(("free-people", "5 facilitators"), (people[0].Kind, people[0].Detail));
        Assert.All(people.Skip(1), h => Assert.Equal("facilitator", h.Kind));
        Assert.Equal(6, Hits("rooms free fri 13").Count);
        Assert.All(Hits("rooms free fri 13").Skip(1), h => Assert.Equal("room", h.Kind));
        // Prep's day is closed and the visiting company's group is not the school's: only BA 2 is free at one.
        var groups = Hits("groups free fri 13");
        Assert.Equal(("free-groups", "1 group"), (groups[0].Kind, groups[0].Detail));
        Assert.Equal(["BA 2 — free from 12:45"], Lines(groups.Skip(1)));
        Assert.Equal(["Free Mon 23 Nov 10:00 — no group"], Lines(Hits("groups free mon 23/11 10")));
        var more = T with { Groups = T.Groups.Put(new Group { Id = Id(6), Name = "Zeta" }).Put(new Group { Id = Id(7), Name = "Alumni" }).Put(new Group { Id = Id(8), Name = "Mu" }) };
        var late = Hits("groups free fri 17", t: more);
        // Lunch is on for every group, so a group with nothing of its own is free from after it.
        Assert.Equal(["Free Fri 20 Nov 17:00 — 5 groups", "Alumni — free from 12:45", "BA 1 — free from 14:30", "BA 2 — free from 12:45",
                      "Mu — free from 12:45", "Zeta — free from 12:45"], Lines(late));
        Assert.All(late, h => Assert.Equal(new DateOnly(2026, 11, 20), h.On));
        // The start of two things a space has is both of them.
        var pads = T with { Equipment = T.Equipment.Put(new Equipment { Id = Id(5), Name = "Crash pads" }) };
        Assert.Equal(["Free Fri 20 Nov 13:00 — no space with Crash mats and Crash pads"], Lines(Hits("free crash fri 13", t: pads)));
    }

    [Fact]
    public void SomebodyOutsideTheHoursTheyGaveIsStillFreeSaidSoAndLast()
    {
        var esme = T.Facilitators.Single(f => f.Name == "Esme Halloran");
        var t = T with { Facilitators = T.Facilitators.Put(esme with { Availability = [new AvailabilityWindow(DayOfWeek.Friday, new TimeOnly(8, 0), new TimeOnly(12, 0))] }) };
        var people = Hits("who's free fri 13-15", t: t).Skip(1).ToList();
        Assert.Equal("Esme Halloran — free all day · outside their hours", Lines(people).Last());
        Assert.Equal("Esme Halloran — free all day", Lines(Hits("who's free fri 9-11", t: t)).Single(l => l.StartsWith("Esme", StringComparison.Ordinal)));
        Assert.Contains("Esme Halloran — free until 13:00 · outside their hours", Lines(Hits("who's free thu 9", t: t)));
        Assert.Contains("Esme Halloran — free all day · outside their hours", Lines(Hits("who's free fri 7-9", t: t)));
        // A whole day is in their hours when they gave some for that day.
        Assert.Contains("Esme Halloran — free all day", Lines(Hits("who's free fri", t: t)));
        var thursdays = T with { Facilitators = T.Facilitators.Put(esme with { Availability = [new AvailabilityWindow(DayOfWeek.Thursday, new TimeOnly(8, 0), new TimeOnly(12, 0))] }) };
        Assert.Contains("Esme Halloran — free all day · outside their hours", Lines(Hits("who's free fri", t: thursdays)));
    }

    [Fact]
    public void AnEventIsNamedOnlyToAReaderWhoMaySeeItAndOtherwiseItsSpaceIsBooked()
    {
        var talks = new SchoolEvent { Title = "Salary talks", Date = Today, Start = new(14, 0), End = new(15, 0), Audience = EventAudience.Administrators, RoomId = T.Rooms.Single(r => r.Name == "Studio B").Id };
        var t = T with { Events = T.Events.Put(talks) };
        Assert.Equal("Wed 18 Nov 14:30 · booked until 15:00", Detail("studio b 14:30", Editor, t));
        Assert.Equal("Wed 18 Nov 14:30 · Salary talks until 15:00", Detail("studio b 14:30", EventViewer.Administrator, t));
        Assert.DoesNotContain(Hits("rooms free 14:30", Editor, t), h => h.Label == "Studio B");
        var untitled = T with { Events = T.Events.Put(talks with { Title = " " }) };
        Assert.Equal("Wed 18 Nov 14:30 · An event until 15:00", Detail("studio b 14:30", EventViewer.Administrator, untitled));
    }

    [Fact]
    public void AGroupSomeOfWhoseStudentsAreElsewhereIsToldHowMany()
    {
        var ba2 = T.Groups.Single(g => g.Name == "BA 2");
        var alba = T.Students.Single(s => s.Name == "Alba Ashcombe");
        var benno = T.Students.Single(s => s.Name == "Benno Birkeland");
        var thursday = T.Sessions.Single(s => s.Date == Today.AddDays(1) && s.Title == "Creation lab" && s.Audience.IsFor(ba2.Id));
        var t = T with { Sessions = T.Sessions.Put(thursday with { AddedStudentIds = [alba.Id, benno.Id] }) };
        Assert.Equal("Thu 19 Nov 14:00 · 2 of its students in Creation lab until 16:00", Detail("ba 1 thu 14", t: t));
    }

    [Fact]
    public void FindAnswersItForAnybodyAfterACommandAndNeverAsAClassTitledWithItsWords()
    {
        IReadOnlyList<FindHit> Search(string q, bool mayEdit = true, bool admin = true, int max = 12, bool records = false) =>
            FindIndex.Search(T, q, mayEdit, admin, max, today: Today, shown: Today, recordsAway: records, now: Now, viewer: Editor);
        Assert.Equal("free", Search("free fri 13")[0].Kind);
        Assert.DoesNotContain(Search("free fri 13"), h => h.Kind == "quick-add");
        Assert.DoesNotContain(Search("where is bo fri 10"), h => h.Kind == "quick-add");
        Assert.Equal(("facilitator", "Bo Lindqvist"), (Search("bo fri 10", mayEdit: false, admin: false)[0].Kind, Search("bo fri 10", mayEdit: false, admin: false)[0].Label));
        // A class named is still a class to add, and a line about somebody away is that.
        Assert.Equal("quick-add", Search("staff meeting fri 13")[0].Kind);
        // Naming a space with a time asks about it, but words it leaves over may be the title of a class to add:
        // that comes first, to somebody who may add one, and the answer after it.
        Assert.Equal(["quick-add", "room"], Search("staff meeting fri 15:30 canteen").Take(2).Select(h => h.Kind));
        Assert.Equal("Add Staff meeting · Fri 20 Nov 15:30–16:30", Search("staff meeting fri 15:30 canteen")[0].Label);
        Assert.Equal("room", Search("staff meeting fri 15:30 canteen", mayEdit: false, admin: false)[0].Kind);
        Assert.DoesNotContain(Search("staff canteen fri 15:30"), h => h.Kind == "quick-add");
        Assert.DoesNotContain(Search("free canteen fri 15:30 meeting"), h => h.Kind == "quick-add");
        Assert.DoesNotContain(Search("where is bo fri 10 meeting"), h => h.Kind == "quick-add");
        Assert.Equal("quick-add", Search("aerial fri 13")[0].Kind);
        Assert.Equal(["quick-away"], Search("bo off fri 9-12", records: true).Select(h => h.Kind));
        Assert.DoesNotContain(Search("bo off fri 9-12", records: false), h => h.Kind == "facilitator");
        // Within the hits Find shows, and nothing at all when the time now is not known.
        Assert.Equal(4, Search("free fri 13", max: 4).Count);
        Assert.Equal(["free", "room", "room"], Search("free", max: 3).Select(h => h.Kind));
        Assert.DoesNotContain(FindIndex.Search(T, "free fri 13", true, true, today: Today, shown: Today), h => h.Kind == "free");
        // A class named by its start is still one to add, before the answer, and both within the hits Find shows.
        Assert.Equal(["quick-add", "free"], Search("acro free fri 13").Take(2).Select(h => h.Kind));
        Assert.Equal(4, Search("acro free fri 13", max: 4).Count);
        // An event is named to the reader Find is asked for.
        var talks = new SchoolEvent { Title = "Salary talks", Date = Today, Start = new(14, 0), End = new(15, 0), Audience = EventAudience.Administrators, RoomId = T.Rooms.Single(r => r.Name == "Studio B").Id };
        var withTalks = T with { Events = T.Events.Put(talks) };
        Assert.Equal("Wed 18 Nov 14:30 · Salary talks until 15:00",
            FindIndex.Search(withTalks, "studio b 14:30", true, true, today: Today, shown: Today, now: Now, viewer: EventViewer.Administrator)[0].Detail);
        // The action that finds a time still comes after the answer, by its words.
        Assert.Equal("find-time", Search("free")[^1].Kind);
    }
}
