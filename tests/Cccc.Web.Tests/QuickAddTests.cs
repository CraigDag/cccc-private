using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Web.Features.Find;

namespace Cccc.Web.Tests;

/// <summary>A class typed into Find in one line (<see cref="QuickAdd"/>), read against the made-up school on Wednesday 18 November 2026.</summary>
public class QuickAddTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Today = Monday.AddDays(2);
    private static readonly Timetable T = SampleSchool.Create(Monday, Today);

    private static Guid Class(string name) => T.Classes.Single(c => c.Name == name).Id;
    private static Guid Group(string name) => T.Groups.Single(g => g.Name == name).Id;
    private static Guid Space(string name) => T.Rooms.Single(r => r.Name == name).Id;
    private static Guid Person(string name) => T.Facilitators.Single(f => f.Name == name).Id;

    private static QuickAdd.Reading Read(string line, DateOnly? shown = null, Timetable? t = null) =>
        QuickAdd.Read(t ?? T, line, Today, shown ?? Today) ?? throw new Xunit.Sdk.XunitException($"\"{line}\" was not read as a class to add");

    private static string When(QuickAdd.Reading r) => $"{r.Draft.Date:yyyy-MM-dd} {Week.Hm(r.Draft.Start)}–{Week.Hm(r.Draft.End)}";

    [Fact]
    public void AClassAndATimeTakeTheRestFromItsMostLikeSession()
    {
        var r = Read("aerial 4/12 13");
        Assert.Equal("2026-12-04 13:00–14:30", When(r));
        Assert.Equal(Class("Aerial"), r.Draft.ClassId);
        Assert.Equal("Aerial", r.Draft.Title);
        Assert.Equal(Audience.ForGroup(Group("BA 1")), r.Draft.Audience, AudienceIs);
        Assert.Equal(Space("Aerial Hall"), r.Draft.RoomId);
        Assert.Equal(new[] { Person("Dag Øverlie") }, r.Draft.FacilitatorIds);
        Assert.Equal(new DateOnly(2026, 11, 27), r.Like?.Date);
        Assert.Empty(r.NotUnderstood);
        var hit = QuickAdd.Hit(T, r);
        Assert.Equal("Add Aerial · Fri 4 Dec 13:00–14:30", hit.Label);
        Assert.Equal("BA 1 · Aerial Hall · Dag Øverlie · the rest as on Fri 27 Nov", hit.Detail);
        Assert.Equal("quick-add", hit.Kind);
        Assert.Same(r.Draft, hit.Draft);
        Assert.Equal(T.Classes[Class("Aerial")]!.Colour, hit.Colour);
        // A start only keeps the length; a different start drops nothing else.
        Assert.Equal("2026-12-04 09:00–10:30", When(Read("aerial 4/12 9")));
    }

    [Fact]
    public void WhatTheLineSaysBeatsWhatTheClassUsuallyIs()
    {
        var r = Read("Juggling ba2 with cleo and bo tomorrow 9.30-11 studio b");
        Assert.Equal("2026-11-19 09:30–11:00", When(r));
        Assert.Equal(Audience.ForGroup(Group("BA 2")), r.Draft.Audience, AudienceIs);
        Assert.Equal(Space("Studio B"), r.Draft.RoomId);
        Assert.Equal(new[] { Person("Cleo Marchetti"), Person("Bo Lindqvist") }, r.Draft.FacilitatorIds);
        Assert.Null(r.Like);
        Assert.Equal("BA 2 · Studio B · Bo Lindqvist, Cleo Marchetti", QuickAdd.Hit(T, r).Detail);
        // The group named picks which of its sessions the rest is like: BA 2's Wednesday, with Dag, 1h 15m.
        var ba2 = Read("aerial ba 2 4/12 13");
        Assert.Equal("2026-12-04 13:00–14:15", When(ba2));
        Assert.Equal(new[] { Person("Dag Øverlie") }, ba2.Draft.FacilitatorIds);
        Assert.Equal(DayOfWeek.Wednesday, ba2.Like?.Date.DayOfWeek);
        // A programme is the whole of it; "everyone" is the whole building.
        Assert.Equal(AudienceKind.Programme, Read("aerial degree 4/12 13").Draft.Audience.Kind);
        Assert.Equal(T.Programmes.Single(p => p.Name == "Degree").Id, Read("aerial degree 4/12 13").Draft.Audience.ProgrammeId);
        Assert.True(Read("lunch 4/12 12 everyone").Draft.Audience.IsEveryone);
        Assert.True(Read("aerial 4/12 13 whole school").Draft.Audience.IsEveryone);
        // "Prep" is a group and a programme: the group, the nearer of the two.
        Assert.Equal(Audience.ForGroup(Group("Prep")), Read("aerial prep 4/12 13").Draft.Audience, AudienceIs);
    }

    [Fact]
    public void ADayNameIsTheNextOneOrInTheWeekThePlanShows()
    {
        Assert.Equal(new DateOnly(2026, 11, 20), Read("aerial fri 9").Draft.Date);
        Assert.Equal(Today, Read("aerial wed 9").Draft.Date);
        Assert.Equal(new DateOnly(2026, 11, 23), Read("aerial mon 9").Draft.Date);
        Assert.Equal(new DateOnly(2026, 11, 25), Read("aerial next wed 9").Draft.Date);
        Assert.Equal(new DateOnly(2026, 11, 23), Read("aerial next monday 9").Draft.Date);
        Assert.Equal(new DateOnly(2026, 11, 19), Read("aerial tomorrow 9").Draft.Date);
        Assert.Equal(Today, Read("aerial today 9").Draft.Date);
        // The plan on a week ahead: its Friday, and the one after.
        var ahead = new DateOnly(2026, 12, 1);
        Assert.Equal(new DateOnly(2026, 12, 4), Read("aerial fri 9", ahead).Draft.Date);
        Assert.Equal(new DateOnly(2026, 12, 11), Read("aerial next fri 9", ahead).Draft.Date);
        // The plan on a week that has gone: the coming Friday, not that week's.
        Assert.Equal(new DateOnly(2026, 11, 20), Read("aerial fri 9", new DateOnly(2026, 11, 3)).Draft.Date);
        // No day said: the day the plan is on.
        Assert.Equal(ahead, Read("aerial 9", ahead).Draft.Date);
        // In the Scratch Week a day name stays in it.
        var scratch = Week.ScratchMonday.AddDays(2);
        Assert.Equal(Week.ScratchMonday.AddDays(4), Read("aerial fri 9", scratch).Draft.Date);
        Assert.Equal("Add Aerial · Scratch Week Fri 09:00–10:30", QuickAdd.Hit(T, Read("aerial fri 9", scratch)).Label);
        Assert.Equal(Week.ScratchMonday.AddDays(4), Read("aerial next fri 9", scratch).Draft.Date);
    }

    [Theory]
    [InlineData("aerial 27 nov 9", "2026-11-27")]
    [InlineData("aerial nov 27th 9", "2026-11-27")]
    [InlineData("aerial 27. november 9", "2026-11-27")]
    [InlineData("aerial 2026-11-27 9", "2026-11-27")]
    [InlineData("aerial 27/11 9", "2026-11-27")]
    [InlineData("aerial 27/11/2026 9", "2026-11-27")]
    [InlineData("aerial 27.11. 9", "2026-11-27")]
    [InlineData("aerial 4.12 9", "2026-12-04")]
    [InlineData("aerial 3/1 9", "2027-01-03")]
    [InlineData("aerial 1/11 9", "2026-11-01")]
    public void ADateIsReadTheWaysPeopleWriteOne(string line, string date) =>
        Assert.Equal(DateOnly.Parse(date, System.Globalization.CultureInfo.InvariantCulture), Read(line).Draft.Date);

    [Theory]
    [InlineData("aerial 4/12 13:00", "13:00–14:30")]
    [InlineData("aerial 4/12 1pm", "13:00–14:30")]
    [InlineData("aerial 4/12 1", "13:00–14:30")]
    [InlineData("aerial 4/12 6", "18:00–19:30")]
    [InlineData("aerial 4/12 7", "07:00–08:30")]
    [InlineData("aerial 4/12 9.30", "09:30–11:00")]
    [InlineData("aerial 4/12 10.05", "10:00–11:30")]
    [InlineData("aerial 4/12 at 2", "14:00–15:30")]
    [InlineData("aerial 4/12 kl. 14", "14:00–15:30")]
    [InlineData("aerial 4/12 noon", "12:00–13:30")]
    [InlineData("aerial 4/12 11-1", "11:00–13:00")]
    [InlineData("aerial 4/12 1-2.30pm", "13:00–14:30")]
    [InlineData("aerial 4/12 10-11pm", "22:00–23:00")]
    [InlineData("aerial 4/12 9-11am", "09:00–11:00")]
    [InlineData("aerial 4/12 6-8", "18:00–20:00")]
    [InlineData("aerial 4/12 13 to 14:30", "13:00–14:30")]
    [InlineData("aerial 4/12 13:00–14:30", "13:00–14:30")]
    [InlineData("aerial 4/12 13:00 15:00", "13:00–15:00")]
    [InlineData("aerial 4/12 9 for 90m", "09:00–10:30")]
    [InlineData("aerial 4/12 9 2h", "09:00–11:00")]
    [InlineData("aerial 4/12 9 1h15", "09:00–10:15")]
    [InlineData("aerial 4/12 9 2 hours", "09:00–11:00")]
    [InlineData("aerial 4/12 9 1.5h", "09:00–10:30")]
    [InlineData("aerial 4/12 23", "23:00–23:45")]
    public void ATimeIsReadTheWaysPeopleSayOne(string line, string when)
    {
        var r = Read(line);
        Assert.Equal(when, $"{Week.Hm(r.Draft.Start)}–{Week.Hm(r.Draft.End)}");
        Assert.Empty(r.NotUnderstood);
    }

    [Fact]
    public void NamesAreFoundWholeLongestFirstWithoutTheirSpacesOrByTheStartOfOne()
    {
        var both = Read("aerial in aerial hall 4/12 13");
        Assert.Equal(Class("Aerial"), both.Draft.ClassId);
        Assert.Equal(Space("Aerial Hall"), both.Draft.RoomId);
        Assert.Empty(both.NotUnderstood);
        Assert.Equal(Space("Big Top"), Read("aerial bigtop 4/12 13").Draft.RoomId);
        Assert.Equal(Space("Big Top"), Read("AERIAL BIG TOP 4/12 13").Draft.RoomId);
        Assert.Equal(Class("Acrobatics"), Read("acro 4/12 9").Draft.ClassId);
        Assert.Equal(Class("Strength and conditioning"), Read("strength 4/12 9").Draft.ClassId);
        Assert.Equal(new[] { Person("Bo Lindqvist") }, Read("aerial 4/12 13 lindq").Draft.FacilitatorIds);
        Assert.Equal(new[] { Person("Dag Øverlie") }, Read("aerial 4/12 13 with dag øverlie").Draft.FacilitatorIds);
        Assert.Equal(new[] { Person("Finn Aalborg-Reyes") }, Read("aerial 4/12 13 finn").Draft.FacilitatorIds);
        // The start of two names is neither, and says which it could be.
        var stu = Read("aerial 4/12 13 stu");
        Assert.Equal(Space("Aerial Hall"), stu.Draft.RoomId);
        Assert.Equal(new[] { "stu (Studio A or Studio B?)" }, stu.NotUnderstood);
        // Two letters are too few to be the start of a name: "gy" is not the Gym.
        var gy = Read("aerial 4/12 13 gy");
        Assert.Equal(Space("Aerial Hall"), gy.Draft.RoomId);
        Assert.Equal(new[] { "gy" }, gy.NotUnderstood);
        // Two people of one first name: neither, and the line says so.
        var twoAdas = T with { Facilitators = T.Facilitators.Put(new Facilitator { Name = "Ada Lund" }) };
        var ada = Read("aerial 4/12 13 ada", t: twoAdas);
        Assert.Equal(new[] { Person("Dag Øverlie") }, ada.Draft.FacilitatorIds);
        Assert.Equal(new[] { "ada (Ada Brightwater or Ada Lund?)" }, ada.NotUnderstood);
        // A day name is a day before it is the start of a name.
        var t = T with { Facilitators = T.Facilitators.Put(new Facilitator { Name = "Frida Holm" }) };
        var fri = Read("aerial fri 13", t: t);
        Assert.Equal(new DateOnly(2026, 11, 20), fri.Draft.Date);
        Assert.Equal(new[] { Person("Dag Øverlie") }, fri.Draft.FacilitatorIds);
    }

    [Fact]
    public void WithNoClassNamedWhatIsLeftIsTheTitle()
    {
        var r = Read("staff meeting fri 15:30 canteen");
        Assert.Null(r.Draft.ClassId);
        Assert.Equal("Staff meeting", r.Draft.Title);
        Assert.Equal("2026-11-20 15:30–16:30", When(r));
        Assert.Equal(Space("Canteen"), r.Draft.RoomId);
        Assert.Equal(AudienceKind.Group, r.Draft.Audience.Kind);
        Assert.Empty(r.Draft.Audience.GroupIds);
        Assert.Empty(r.Draft.Facilitators);
        Assert.Null(r.Like);
        Assert.Equal("Add Staff meeting · Fri 20 Nov 15:30–16:30", QuickAdd.Hit(T, r).Label);
        Assert.Equal("no group yet · Canteen · nobody teaching yet", QuickAdd.Hit(T, r).Detail);
        // A small word inside the title stays in it.
        Assert.Equal("Meet the parents", Read("meet the parents fri 18 everyone").Draft.Title);
        // A former class is not offered: its name is only a title.
        var juggling = T.Classes[Class("Juggling")]!;
        var former = T with { Classes = T.Classes.Put(juggling with { FormerSince = Monday }) };
        var old = Read("juggling fri 13", t: former);
        Assert.Null(old.Draft.ClassId);
        Assert.Equal("Juggling", old.Draft.Title);
    }

    [Fact]
    public void WordsNotUnderstoodAreSaidAndNeverQuietlyUsed()
    {
        var r = Read("aerial juggling 4/12 13 blah 30/2");
        Assert.Equal(Class("Aerial"), r.Draft.ClassId);
        Assert.Equal(new[] { "Juggling", "blah", "30/2" }, r.NotUnderstood);
        Assert.EndsWith(" · not understood: Juggling, blah, 30/2", QuickAdd.Hit(T, r).Detail);
        // Longer than a school day is not a length.
        Assert.Equal(new[] { "13h" }, Read("aerial 4/12 9 13h").NotUnderstood);
    }

    [Fact]
    public void WhoWasInOnlyPartOfItKeepsThatPartOnlyAtTheSameTimes()
    {
        var same = Read("circus 7/12");
        Assert.Equal("2026-12-07 13:00–15:00", When(same));
        Assert.Contains(new Assignment(Person("Dag Øverlie"), new TimeOnly(14, 0)), same.Draft.Facilitators);
        var other = Read("circus 7/12 9");
        Assert.Equal("2026-12-07 09:00–11:00", When(other));
        Assert.All(other.Draft.Facilitators, a => Assert.Null(a.From));
        Assert.Equal(2, other.Draft.Facilitators.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("aerial")]
    [InlineData("dag 13")]
    [InlineData("fri 13")]
    [InlineData("big top fri 13")]
    [InlineData("at 13")]
    [InlineData("staff meeting")]
    [InlineData("staff meeting fri")]
    public void NothingIsOfferedWithoutAClassOrTitleAndATimeToAddItAt(string line) =>
        Assert.Null(QuickAdd.Read(T, line, Today, Today));

    [Fact]
    public void AClassWithADayAndNoTimeTakesItsUsualTime()
    {
        var r = Read("juggling 4/12");
        Assert.Equal("2026-12-04 11:00–12:00", When(r));
        Assert.Equal(Audience.ForGroup(Group("Prep")), r.Draft.Audience, AudienceIs);
        Assert.Equal(Space("Studio B"), r.Draft.RoomId);
    }

    [Fact]
    public void FindOffersItFirstToSomebodyWhoMayEditAndNeverToAReader()
    {
        var editor = FindIndex.Search(T, "aerial 4/12 13", mayEdit: true, admin: false, today: Today, shown: Today);
        Assert.Equal("quick-add", editor[0].Kind);
        Assert.Equal("Add Aerial · Fri 4 Dec 13:00–14:30", editor[0].Label);
        Assert.DoesNotContain(FindIndex.Search(T, "aerial 4/12 13", mayEdit: false, admin: false, today: Today, shown: Today), h => h.Kind == "quick-add");
        // Lunch is a class; the rest of what Find has for it comes after, within the same number of hits.
        var lunch = FindIndex.Search(T, "lunch 12", mayEdit: true, admin: true, max: 3, today: Today, shown: Today);
        Assert.Equal("quick-add", lunch[0].Kind);
        Assert.True(lunch.Count <= 3);
        // The line comes first and the rest fill what is left of the hits Find shows.
        var rooms = T with { Rooms = T.Rooms.Put(new Room { Name = "Lunch 12 a" }).Put(new Room { Name = "Lunch 12 b" }).Put(new Room { Name = "Lunch 12 c" }) };
        var three = FindIndex.Search(rooms, "lunch 12", mayEdit: true, admin: true, max: 3, today: Today, shown: Today);
        Assert.Equal(new[] { "quick-add", "room", "room" }, three.Select(h => h.Kind));
        // A name alone is found, not added.
        Assert.DoesNotContain(FindIndex.Search(T, "aerial", mayEdit: true, admin: true, today: Today, shown: Today), h => h.Kind == "quick-add");
        Assert.Equal("Aerial Hall", FindIndex.Search(T, "aerial", mayEdit: true, admin: true, today: Today, shown: Today)[0].Label);
    }

    private static readonly IEqualityComparer<Audience> AudienceIs = EqualityComparer<Audience>.Create((a, b) => a!.Equivalent(b!), a => a.Kind.GetHashCode());
}
