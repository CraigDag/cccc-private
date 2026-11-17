using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Store;
using Cccc.Web.Features.Print;
using Cccc.Web.Features.Week;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Tests;

/// <summary>What the printed building's day draws: the sheet is paper somebody pins up, so it must not lie about a space.</summary>
public class PrintTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly Timetable T = SampleSchool.Create(Monday, Monday);
    private static readonly Access Admin = new(new Account(Guid.NewGuid(), "admin", "Admin", Role.Administrator, default, null, false, false, "s", DateTimeOffset.UnixEpoch, true));

    private static WeekModel Model(Timetable t, Action<WeekView>? narrow = null)
    {
        var v = new WeekView();
        v.Start(Monday, null);
        narrow?.Invoke(v);
        return WeekModel.Build(t, v, Admin, Monday);
    }

    [Fact]
    public void NarrowedToAGroupASpaceBusyWithAnotherGroupIsStillDrawnInUse()
    {
        var ba1 = T.Groups.First(g => g.Name == "BA 1");
        var m = Model(T, v => v.Groups.Add(ba1.Id));
        var rows = SpacesDay.Rows(T, m, Monday);
        // Monday's Juggling is BA 2's, in Studio A at 10:45; narrowed to BA 1 it is not on the sheet, and its space is not free.
        var juggling = Occurrences.ForWeek(T, Monday).First(o => o.Date == Monday && o.Session.Title == "Juggling");
        var row = rows.Single(r => r.Key == juggling.Session.RoomId.ToString());
        var bar = Assert.Single(row.Bars, b => b.O.Session.Id == juggling.Session.Id);
        Assert.False(bar.Shown, "another group's class is drawn as in use, without its words");
        Assert.All(rows.SelectMany(r => r.Bars).Where(b => b.Shown), b => Assert.True(b.O.Session.Audience.IsFor(ba1.Id), $"{b.O.Session.Title} is on the sheet but not for BA 1"));
        Assert.Contains(rows.SelectMany(r => r.Bars), b => b.Shown);

        // A class that does not happen (Friday's, closed for the Prep study day) fills nothing.
        var friday = Monday.AddDays(4);
        var closed = Occurrences.ForWeek(T, Monday).First(o => o.Date == friday && o.State == OccurrenceState.Closed && o.Session.RoomId is not null);
        Assert.DoesNotContain(SpacesDay.Rows(T, m, friday).SelectMany(r => r.Bars), b => b.O.Session.Id == closed.Session.Id);
    }

    [Fact]
    public void AClassWithNoSpaceLeadsTheSheetAndOverlapsStack()
    {
        var s = T.SessionsInWeek(Monday).First(x => x.Date == Monday && x.RoomId is not null && !x.Audience.IsEveryone);
        var twin = s with { Id = Guid.NewGuid(), Title = "Twin" };
        var roomless = s with { Id = Guid.NewGuid(), Title = "Nowhere yet", RoomId = null };
        var t = T with { Sessions = T.Sessions.Put(twin).Put(roomless) };
        var rows = SpacesDay.Rows(t, Model(t), Monday);
        Assert.Equal(SpacesDay.NoSpace, rows[0].Key);
        Assert.Contains(rows[0].Bars, b => b.O.Session.Id == roomless.Id && b.Shown);
        var row = rows.Single(r => r.Key == s.RoomId.ToString());
        Assert.Equal(2, row.Lanes);
        var lanes = row.Bars.Where(b => b.O.Session.Id == s.Id || b.O.Session.Id == twin.Id).Select(b => b.Lane).OrderBy(x => x).ToList();
        Assert.Equal([0, 1], lanes);
        // Without one, there is no such row at all.
        Assert.DoesNotContain(SpacesDay.Rows(T, Model(T), Monday), r => r.Key == SpacesDay.NoSpace);
    }

    [Fact]
    public void ACrowdedDayGoesOverMoreSheetsAndNeverLosesASpace()
    {
        // Forty spaces, each with three classes on top of each other on Monday, and one class with no space.
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var rooms = Enumerable.Range(1, 40).Select(i => new Room { Name = $"Space {i:00}", Category = i % 3 == 0 ? null : $"Floor {i % 3}" }).ToList();
        var sessions = rooms.SelectMany(r => Enumerable.Range(0, 3).Select(k => new Session
        {
            Date = Monday, Start = new TimeOnly(9 + k, 0), End = new TimeOnly(13, 15), Title = $"A class with a long name {k}", RoomId = r.Id,
            Audience = Audience.Everyone,
        })).Append(new Session { Date = Monday, Start = new TimeOnly(9, 0), End = new TimeOnly(10, 0), Title = "Nowhere yet", Audience = Audience.Everyone }).ToList();
        var t = T with { Rooms = EntitySet<Room>.Of(rooms), Sessions = EntitySet<Session>.Of(sessions) };
        var m = Model(t);

        var sheets = SpacesDay.Pages(SpacesDay.Rows(t, m, Monday), 0);
        Assert.True(sheets.Count > 1, $"a hundred and twenty lanes fitted on {sheets.Count} sheet");
        Assert.All(sheets, p =>
        {
            Assert.True(p.Bottom + (p.Legend ? SpacesDay.Legend : 0) <= SpacesDay.PageHeight + 0.01f, $"a sheet's rows end at {p.Bottom}, past {SpacesDay.PageHeight}");
            Assert.True(p.Lane >= SpacesDay.MinLane, $"lanes {p.Lane} high are too thin for a line of words");
            Assert.Equal(SpacesDay.NoSpace, p.Rows[0].Key);
        });
        var printed = sheets.SelectMany(p => p.Rows).Where(r => r.Key != SpacesDay.NoSpace).Select(r => r.Key).ToList();
        Assert.Equal(rooms.Select(r => r.Id.ToString()).Order(), printed.Order());

        // The sheets the document draws are those sheets, a day after another.
        int expected = m.Days.Sum(d => SpacesDay.Pages(SpacesDay.Rows(t, m, d), SpacesDay.Events(m, d, EventViewer.Administrator).Select(e => e.Lane + 1).DefaultIfEmpty(0).Max()).Count);
        var pdf = new PdfGate().Generate(Sheets.RoomGrid(t, m, "Whole school", DateTimeOffset.UnixEpoch, EventViewer.Administrator));
        Assert.Equal(expected, System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(pdf), @"/Type\s*/Page(?!s)").Count);

        // A school of the sample's size is one sheet a day, with tall lanes.
        var sample = Model(T);
        Assert.All(sample.Days, d => Assert.Single(SpacesDay.Pages(SpacesDay.Rows(T, sample, d), 1)));
    }

    [Fact]
    public void NarrowedToASpaceOnlyThatSpaceHasARowAndHiddenGuestsStillFillTheirs()
    {
        var thursday = Monday.AddDays(3);
        var guest = Occurrences.ForWeek(T, Monday).First(o => o.Date == thursday && Guests.IsGuestOnly(T, o.Session) && o.Session.RoomId is not null);
        var rows = SpacesDay.Rows(T, Model(T), thursday);
        var bar = Assert.Single(rows.Single(r => r.Key == guest.Session.RoomId.ToString()).Bars, b => b.O.Session.Id == guest.Session.Id);
        Assert.False(bar.Shown, "a hidden guest class is not on the sheet, and its space is not free either");

        var room = guest.Session.RoomId!.Value;
        var only = SpacesDay.Rows(T, Model(T, v => v.Rooms.Add(room)), thursday, new HashSet<Guid> { room });
        Assert.Equal([room.ToString()], only.Select(r => r.Key));
    }
}
