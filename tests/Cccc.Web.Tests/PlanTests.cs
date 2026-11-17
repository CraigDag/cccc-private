using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Store;
using Cccc.Web.Features.Find;
using Cccc.Web.Features.Plan;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Tests;

/// <summary>The plan's rows and bars come from the domain's own rules; these hold the plan to them.</summary>
public class PlanTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly Timetable T = SampleSchool.Create(Monday, Monday);
    private static readonly Access Admin = new(new Account(Guid.NewGuid(), "admin", "Admin", Role.Administrator, default, null, false, false, "s", DateTimeOffset.UnixEpoch, true));

    private static PlanView View(Lens lens, Scale scale = Scale.Day, Focus? focus = null)
    {
        var v = new PlanView { Lens = lens, Scale = scale };
        v.Start(Monday);
        if (focus is not null) { v.FocusOn(focus); v.Lens = lens; }
        return v;
    }

    [Fact]
    public void NarrowedToOneSpaceTheWeekShowsThatSpacesClassesAndNoOthers()
    {
        var room = T.Rooms.First(r => T.SessionsInWeek(Monday).Count(s => s.RoomId == r.Id) >= 2);
        var m = PlanModel.Build(T, View(Lens.Spaces, focus: new Focus("room", room.Id, room.Name)), Admin, Monday);
        Assert.True(m.RowsAreDays);
        Assert.NotEmpty(m.Bars);
        Assert.All(m.Bars, b => Assert.Equal(room.Id, b.O.Session.RoomId));
        Assert.All(m.Bars, b => Assert.Equal(PlanModel.DayKey(b.Date), b.RowKey));
        var expected = Occurrences.ForWeek(T, Monday).Count(o => o.Session.RoomId == room.Id && !Guests.IsGuestOnly(T, o.Session));
        Assert.Equal(expected, m.Bars.Count);
    }

    [Fact]
    public void AWholeBuildingClassSitsOnEveryGroupsRow()
    {
        var m = PlanModel.Build(T, View(Lens.Groups, Scale.Week), Admin, Monday);
        var everyone = m.Week.First(o => o.Session.Audience.IsEveryone);
        var rowsWithIt = m.Bars.Where(b => b.O.Session.Id == everyone.Session.Id).Select(b => b.RowKey).ToHashSet();
        Assert.True(m.Rows.Count >= 2, $"The sample drew {m.Rows.Count} group rows.");
        Assert.Equal(m.Rows.Select(r => r.Key).ToHashSet(), rowsWithIt);
    }

    [Fact]
    public void RowsAreHeadedOnlyWhenTheyFallInMoreThanOnePart()
    {
        // The sample names its spaces' kinds, so they are headed, and a space without one is "Other spaces".
        var plain = T.Rooms.First();
        var t = T with { Rooms = T.Rooms.Put(plain with { Category = null }) };
        var m = PlanModel.Build(t, View(Lens.Spaces), Admin, Monday);
        Assert.True(m.Headed);
        Assert.Equal("Other spaces", m.Rows.Single(r => r.Id == plain.Id).Section);
        Assert.Contains(m.Rows, r => r.Section == "Hall");
        // And a group in no programme is "No programme" beside groups that are in one.
        var loose = T.Groups.First(g => T.Programmes.Find(g.ProgrammeId) is { IsGuest: false });
        var groups = PlanModel.Build(T with { Groups = T.Groups.Put(loose with { ProgrammeId = null }) }, View(Lens.Groups), Admin, Monday);
        Assert.True(groups.Headed);
        Assert.Equal("No programme", groups.Rows.Single(r => r.Id == loose.Id).Section);

        // A school that names no kinds and no programmes gets no headings at all, not one over everything.
        var none = T with
        {
            Rooms = EntitySet<Room>.Of(T.Rooms.Select(r => r with { Category = null })),
            Programmes = EntitySet<Programme>.Empty,
            Groups = EntitySet<Group>.Of(T.Groups.Select(g => g with { ProgrammeId = null })),
        };
        foreach (var lens in new[] { Lens.Spaces, Lens.Groups })
        {
            var rows = PlanModel.Build(none, View(lens), Admin, Monday);
            Assert.NotEmpty(rows.Rows);
            Assert.False(rows.Headed, lens + " is headed");
        }
        // Nor does one space's week, a row a day, under that space's name.
        var room = T.Rooms.First();
        var week = PlanModel.Build(T, View(Lens.Spaces, focus: new Focus("room", room.Id, room.Name)), Admin, Monday);
        Assert.True(week.RowsAreDays);
        Assert.False(week.Headed);
    }

    [Fact]
    public void ADaySpacesRowsHoldOnlyThatDaysClassesAndOverlapsStack()
    {
        // Two classes in one space at one time is a clash; the plan draws both, one above the other.
        var s = T.SessionsInWeek(Monday).First(x => x.Date == Monday && x.RoomId is not null && !x.Audience.IsEveryone);
        var twin = s with { Id = Guid.NewGuid(), Title = "Twin" };
        var t = T with { Sessions = T.Sessions.Put(twin) };
        var m = PlanModel.Build(t, View(Lens.Spaces), Admin, Monday);
        Assert.All(m.Bars, b => Assert.Equal(Monday, b.Date));
        var pair = m.Bars.Where(b => b.O.Session.Id == s.Id || b.O.Session.Id == twin.Id).ToList();
        Assert.Equal(2, pair.Count);
        Assert.NotEqual(pair[0].Lane, pair[1].Lane);
        Assert.Equal(2, m.LanesOf(s.RoomId!.Value.ToString(), Monday));
        foreach (var row in m.Bars.GroupBy(b => b.RowKey))
            foreach (var a in row)
                foreach (var b in row)
                    if (!ReferenceEquals(a, b) && a.Lane == b.Lane)
                        Assert.False(a.Start < b.End && b.Start < a.End, $"{a.O.Session.Title} and {b.O.Session.Title} overlap on one lane.");
    }

    [Fact]
    public void GuestClassesAreHiddenUntilAskedForAndCountedWhenHidden()
    {
        var guest = Occurrences.ForWeek(T, Monday).FirstOrDefault(o => Guests.IsGuestOnly(T, o.Session));
        Assert.NotNull(guest);
        var hidden = PlanModel.Build(T, View(Lens.Spaces, Scale.Week), Admin, Monday);
        Assert.DoesNotContain(hidden.Bars, b => b.O.Session.Id == guest.Session.Id);
        Assert.True(hidden.HiddenGuests > 0);
        var v = View(Lens.Spaces, Scale.Week);
        v.ShowGuests = true;
        Assert.Contains(PlanModel.Build(T, v, Admin, Monday).Bars, b => b.O.Session.Id == guest.Session.Id);
    }

    [Fact]
    public void FindPutsANameThatStartsWithTheWordsFirstAndOrdersInDanish()
    {
        var b = new Timetable() with
        {
            Facilitators = EntitySet<Facilitator>.Of([
                new Facilitator { Name = "Ida Holm" }, new Facilitator { Name = "Åse Dahl" }, new Facilitator { Name = "Aage Berg" },
                new Facilitator { Name = "Zelda Ada" }, new Facilitator { Name = "Ada Brink" }]),
        };
        var hits = FindIndex.Search(b, "ada", mayEdit: false, admin: false).Select(h => h.Label).ToList();
        Assert.Equal("Ada Brink", hits[0]);
        Assert.Contains("Zelda Ada", hits);
        var a = FindIndex.Search(b, "a", mayEdit: false, admin: false).Where(h => h.Kind == "facilitator").Select(h => h.Label).ToList();
        Assert.True(a.IndexOf("Åse Dahl") > a.IndexOf("Ada Brink"), string.Join(", ", a));
        Assert.DoesNotContain(FindIndex.Search(b, "", false, false), h => h.Kind == "facilitator");
    }

    [Fact]
    public void FindReachesAnActionByWhereItLivesAfterEveryNameAndEveryActionItsOwnWordsFind()
    {
        var b = new Timetable() with { Facilitators = EntitySet<Facilitator>.Of([new Facilitator { Name = "Ida Termansen" }]) };
        // "Term" is the plan's button, not the action's name: found by where it lives, after the person.
        Assert.Equal(["Ida Termansen", "Show the whole semester"], FindIndex.Search(b, "term", mayEdit: false, admin: false).Select(h => h.Label));
        Assert.Equal("go-term", FindIndex.Search(b, "semester", false, false)[0].Kind);
        // An action's own words come first: "week" is the whole week by name before the semester it is only near.
        var week = FindIndex.Search(b, "week", false, false).Select(h => h.Kind).ToList();
        Assert.Equal(["go-week", "go-term"], week);
        Assert.Empty(FindIndex.Search(b, "zebra", false, false));
    }
}
