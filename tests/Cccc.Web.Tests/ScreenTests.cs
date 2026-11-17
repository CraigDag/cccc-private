using System.Net;
using System.Text.RegularExpressions;
using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Store;
using Cccc.Web.Features.Screens;
using Cccc.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Cccc.Web.Tests;

/// <summary>
/// Pairing a screen: it shows a code, an administrator types it, and only the browser that asked for the
/// code collects the screen's token, once.
/// </summary>
public sealed class ScreenPairingTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 9, 0, 0, TimeSpan.Zero);
    private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "stores", Guid.NewGuid().ToString("N"));
    private readonly ScreenDirectory _screens;
    private readonly ScreenPairing _pairing;

    public ScreenPairingTests()
    {
        _screens = new ScreenDirectory(ProjectStore.Open(_folder, create: true));
        _pairing = new ScreenPairing(_screens);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void TheCodeIsSixCharactersNobodyMisreadsAndOnlyTheClaimCollectsTheTokenOnce()
    {
        var (code, claim) = _pairing.Start(Now)!.Value;
        Assert.Equal(ScreenPairing.CodeLength, code.Length);
        Assert.All(code, ch => Assert.Contains(ch, ScreenPairing.Alphabet));
        Assert.Equal((ScreenPairing.Outcome.Pending, (string?)null), _pairing.Collect(claim, Now));

        // Typed as an administrator would: in lower case, with a space or a dash in it.
        var screen = _pairing.Approve(ScreenPairing.Show(code).ToLowerInvariant().Replace(" ", " - "), "Corridor", null, Now.AddMinutes(1));
        // Typed again before the screen has collected: refused, rather than a second screen nobody holds.
        Assert.Throws<ScreenRefused>(() => _pairing.Approve(code, "Again", null, Now.AddMinutes(1)));
        Assert.Single(_screens.All());
        Assert.Equal(ScreenPairing.Outcome.Unknown, _pairing.Collect("not the claim", Now.AddMinutes(1)).Outcome);
        // What the screen shows to the corridor collects nothing.
        Assert.Equal(ScreenPairing.Outcome.Unknown, _pairing.Collect(code, Now.AddMinutes(1)).Outcome);
        var (outcome, token) = _pairing.Collect(claim, Now.AddMinutes(1));
        Assert.Equal(ScreenPairing.Outcome.Paired, outcome);
        Assert.Equal(screen.Id, _screens.FindByToken(token)?.Id);
        Assert.Equal(ScreenPairing.Outcome.Unknown, _pairing.Collect(claim, Now.AddMinutes(1)).Outcome);
        Assert.Throws<ScreenRefused>(() => _pairing.Approve(code, "Twice", null, Now.AddMinutes(1)));
        Assert.Single(_screens.All());
    }

    [Fact]
    public void ACodeLastsTenMinutesAndAPairedScreenHasTwoMoreToCollectItsToken()
    {
        var (late, _) = _pairing.Start(Now)!.Value;
        Assert.Throws<ScreenRefused>(() => _pairing.Approve(late, "Late", null, Now + ScreenPairing.Lifetime));

        // Typed in the last second of its ten minutes, it can still be collected a minute later.
        var (code, claim) = _pairing.Start(Now)!.Value;
        _pairing.Approve(code, "Corridor", null, Now + ScreenPairing.Lifetime - TimeSpan.FromSeconds(1));
        Assert.Equal(ScreenPairing.Outcome.Paired, _pairing.Collect(claim, Now + ScreenPairing.Lifetime + TimeSpan.FromMinutes(1)).Outcome);

        // But not after two: its token is gone, and the screen shows a new code.
        var (slow, slowClaim) = _pairing.Start(Now)!.Value;
        _pairing.Approve(slow, "Slow", null, Now);
        Assert.Equal(ScreenPairing.Outcome.Unknown, _pairing.Collect(slowClaim, Now + ScreenPairing.CollectWithin).Outcome);
    }

    [Fact]
    public void NoMoreThanSixtyFourScreensWaitAtOnceEachWithItsOwnCode()
    {
        var codes = new HashSet<string>();
        for (int i = 0; i < ScreenPairing.MaxWaiting; i++) codes.Add(_pairing.Start(Now)!.Value.Code);
        Assert.Equal(ScreenPairing.MaxWaiting, codes.Count);
        Assert.All(codes, code => Assert.All(code, ch => Assert.Contains(ch, ScreenPairing.Alphabet)));
        Assert.Null(_pairing.Start(Now));
        // Ten minutes on, those have run out and there is room again.
        Assert.NotNull(_pairing.Start(Now + ScreenPairing.Lifetime));
    }

    [Fact]
    public void ACodeNoScreenIsShowingIsRefusedInWords()
    {
        var e = Assert.Throws<ScreenRefused>(() => _pairing.Approve("ACD EFH", "Corridor", null, Now));
        Assert.Contains("No screen is showing that code", e.Message);
        Assert.Empty(_screens.All());
    }
}

/// <summary>
/// What a screen in the corridor says. Everybody walking past reads it, visitors too, so it carries what a
/// printed timetable on the wall would: the class, who it is for, the first names of who teaches it, and
/// where. Never a student, a note, why a class is off, who is away or covering, or an event the students
/// may not see.
/// </summary>
public class ScreenBoardTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly DateOnly Tuesday = Monday.AddDays(1), Wednesday = Monday.AddDays(2), Thursday = Monday.AddDays(3), Friday = Monday.AddDays(4);
    private static readonly Timetable T = SampleSchool.Create(Monday, Tuesday);

    private static int At(int hour, int minute = 0) => hour * 60 + minute;

    private static ScreenBoardModel Board(Timetable t, DateOnly day, int minute, IReadOnlyList<ClassChange>? changed = null) =>
        ScreenBoard.For(t, day, minute, changed ?? []);

    private static SpaceLine Line(ScreenBoardModel b, string space) => b.Lines.Single(l => l.Room.Name == space);

    private static Session Class(Timetable t, DateOnly day, string space, int hour) =>
        t.Sessions.Single(s => s.Date == day && s.RoomId is { } r && t.Rooms[r]?.Name == space && s.Start.Hour == hour);

    private static Guid RoomId(string name) => T.Rooms.Single(r => r.Name == name).Id;

    /// <summary>Every word the board puts on a screen, in one string.</summary>
    private static string Words(ScreenBoardModel b) => string.Join("\n", new IEnumerable<string>[]
    {
        b.Lines.SelectMany(l => new[] { l.Room.Name, l.Until }.Concat(l.Day.SelectMany(x => new[] { x.Title, x.Detail }))),
        b.Off.SelectMany(o => new[] { o.Title, o.Why }),
        b.ClosedFor,
        b.Changes.SelectMany(c => new[] { c.Title, c.When, c.What }),
        b.Events.SelectMany(e => new[] { e.Title, e.Detail }),
        b.Elsewhere.SelectMany(e => new[] { e.Title, e.Detail }),
        b.Coming.SelectMany(u => new[] { u.Booking.Title, u.Booking.Detail, u.Space }),
        b.Closed is { } c ? [c.Name] : [],
    }.SelectMany(x => x));

    [Fact]
    public void AClassCarriesWhoItIsForAndTheFirstNamesOfWhoIsInTheRoomAtThatMinute()
    {
        // Monday's circus disciplines in the Big Top: Ada all afternoon, Dag from two o'clock.
        var before = Line(Board(T, Monday, At(13, 30)), "Big Top").Now!;
        Assert.Equal(("Circus disciplines", "Degree · Ada"), (before.Title, before.Detail));
        var after = Board(T, Monday, At(14, 30));
        Assert.Equal("Degree · Ada and Dag", Line(after, "Big Top").Now!.Detail);
        // Before it starts, everybody who will teach it.
        Assert.Equal("Degree · Ada and Dag", Line(Board(T, Monday, At(12, 30)), "Big Top").Next!.Detail);
        Assert.DoesNotContain("Brightwater", Words(after));
        Assert.DoesNotContain("Øverlie", Words(after));
    }

    [Fact]
    public void AVisitingCompanysClassKeepsItsSpaceBusy()
    {
        var b = Board(T, Thursday, At(10, 30));
        Assert.Equal("Guest workshop", Line(b, "Aerial Hall").Now?.Title);
        Assert.DoesNotContain(b.FreeNow, l => l.Room.Name == "Aerial Hall");
    }

    [Fact]
    public void AnEventTheStudentsMayNotSeeIsOnlyBookedInItsSpaceAndOtherwiseNotThereAtAll()
    {
        // The staff meeting in Studio A on Mondays: the space is busy, and nothing says with what.
        var monday = Board(T, Monday, At(15, 45));
        Assert.Equal(("Booked", ""), (Line(monday, "Studio A").Now?.Title, Line(monday, "Studio A").Now?.Detail));
        Assert.DoesNotContain("Staff meeting", Words(monday));
        // The administrators' budget review has no space: it is nowhere.
        Assert.DoesNotContain("Budget review", Words(Board(T, Wednesday, At(8, 15))));
        // What every student may see is named, in its space and among the day's events.
        var friday = Board(T, Friday, At(16, 15));
        Assert.Equal("Work-in-progress showing", Line(friday, "Big Top").Now?.Title);
        Assert.Contains(friday.Events, e => e.Title == "Work-in-progress showing" && e.Detail == "Big Top");
        Assert.Contains(Board(T, Thursday, At(9)).Events, e => e.Title == "Nordlys Troupe visiting" && e.AllDay);
    }

    [Fact]
    public void ACancelledClassIsListedWithoutItsReasonAndNoNoteOrStudentReachesTheScreen()
    {
        var dance = Class(T, Tuesday, "Studio A", 9);
        var aerial = Class(T, Tuesday, "Aerial Hall", 9);
        var t = T with
        {
            Sessions = T.Sessions
                .Put(dance with { Cancelled = true, CancelNote = "Finn has the flu" })
                .Put(aerial with { Notes = "Alba Ashcombe twisted an ankle" }),
        };
        var b = Board(t, Tuesday, At(10));
        Assert.Equal([("Contemporary dance", "cancelled")], b.Off.Select(o => (o.Title, o.Why)));
        Assert.Null(Line(b, "Studio A").Now);
        Assert.Contains(b.FreeNow, l => l.Room.Name == "Studio A");
        foreach (var never in new[] { "flu", "Alba", "Ashcombe", "ankle" })
            Assert.DoesNotContain(never, Words(b));

        // No student is named on any day of the week, morning or afternoon.
        Assert.NotEmpty(T.Students);
        foreach (var day in Enumerable.Range(0, 5).Select(Monday.AddDays))
            foreach (var minute in new[] { At(10), At(14) })
            {
                var words = Words(Board(T, day, minute));
                foreach (var s in T.Students) Assert.DoesNotContain(s.Surname, words);
            }
    }

    [Fact]
    public void AClassAwaitingCoverNamesNobodyAndNothingSaysWhoIsAway()
    {
        // Ada is away on Thursday and her acrobatics is not covered yet: it is still on, with nobody named.
        var b = Board(T, Thursday, At(9, 30));
        var acro = Line(b, "Big Top").Now!;
        Assert.Equal(("Acrobatics", "BA 2"), (acro.Title, acro.Detail));
        Assert.DoesNotContain("Ada", Words(b));
        // A week before, Bo was away that Monday morning and Dag covered: Dag is named teaching it, Bo is not,
        // and the cover's note is nowhere.
        var covered = Board(T, Monday.AddDays(-7), At(11));
        Assert.Equal("BA 1 · Ada and Dag", Line(covered, "Big Top").Now?.Detail);
        Assert.DoesNotContain("took the morning", Words(covered));
    }

    [Fact]
    public void WhatChangedIsSaidAsAStudentNeedsItAndNeverWhoTeachesOrCovers()
    {
        var dance = Class(T, Tuesday, "Studio A", 9);
        var aerial = Class(T, Tuesday, "Aerial Hall", 9);
        var strength = Class(T, Tuesday, "Gym", 13);
        var creation = Class(T, Tuesday, "Studio B", 13);
        var lunch = Class(T, Tuesday, "Canteen", 12);
        var juggling = Class(T, Monday, "Studio A", 10);
        var dag = T.Facilitators.Single(f => f.Name == "Dag Øverlie").Id;
        var gym = RoomId("Gym");
        var added = new Session { Date = Tuesday, Start = new(15, 0), End = new(16, 0), Title = "Extra rigging", RoomId = gym };
        var away = new Session { Date = Tuesday, Start = new(14, 0), End = new(15, 0), Title = "Rigging check", RoomId = gym };
        var handstands = new Session { Date = Tuesday, Start = new(16, 0), End = new(17, 0), Title = "Handstands", RoomId = gym };
        var movedDance = dance with { Start = new(10, 0), End = new(12, 0), RoomId = RoomId("Studio B") };
        static ClassChange C(Session? was, Session? now) => new((was ?? now)!.Id, was, now, ["admin"], DateTimeOffset.UnixEpoch);

        var changes = new[]
        {
            C(dance, movedDance),
            C(aerial, aerial with { Facilitators = [new Assignment(dag)] }),   // a new teacher, nothing else
            C(strength, strength with { CoverIds = [dag] }),                                     // cover, nothing else
            C(creation, creation with { Cancelled = true }),                                      // what is off says it already
            C(null, added),
            C(juggling, juggling with { Date = Tuesday }),
            C(lunch, null),
            C(away, away with { Date = Wednesday }),
            C(handstands with { Cancelled = true }, handstands),
        };
        var b = Board(T, Tuesday, At(8), changes);
        Assert.Equal(
        [
            ("Contemporary dance", "10:00–12:00", "Was 09:00–11:00 · now in Studio B"),
            ("Extra rigging", "15:00–16:00", "New today, in Gym"),
            ("Juggling", "10:45–12:00", "Moved here from Mon 16 Nov"),
            ("Lunch", "12:00–12:45", "No longer on today"),
            ("Rigging check", "14:00–15:00", "Moved to Wed 18 Nov"),
            ("Handstands", "16:00–17:00", "Back on"),
        ], b.Changes.Select(c => (c.Title, c.When, c.What)));
    }

    [Fact]
    public void AClosureForSomeGroupsIsNamedAndOneForTheWholeBuildingIsTheBanner()
    {
        var ba1 = T.Groups.Single(g => g.Name == "BA 1").Id;
        var trip = new Closure { Name = "Study trip", From = Tuesday, To = Tuesday, Scope = ClosureScope.Groups, GroupIds = [ba1] };
        var some = Board(T with { Closures = T.Closures.Put(trip) }, Tuesday, At(10));
        Assert.Equal(["BA 1: Study trip"], some.ClosedFor);
        Assert.Null(some.Closed);
        // BA 1's aerial is off, so the hall is free; BA 2's dance carries on.
        Assert.Null(Line(some, "Aerial Hall").Now);
        Assert.NotNull(Line(some, "Studio A").Now);

        var snow = new Closure { Name = "Snow day", From = Tuesday, To = Tuesday, Scope = ClosureScope.WholeBuilding };
        var all = Board(T with { Closures = T.Closures.Put(trip).Put(snow) }, Tuesday, At(10));
        Assert.Equal("Snow day", all.Closed?.Name);
        Assert.Empty(all.ClosedFor);
        Assert.NotEmpty(all.Lines);
        Assert.All(all.Lines, l => Assert.Null(l.Now));
    }

    [Fact]
    public void ComingUpIsWhatStartsNextAnywhereInTimeOrderWithItsSpace()
    {
        Assert.Equal(
        [
            ("12:00", "Lunch", "Canteen"),
            ("13:00", "Strength and conditioning", "Gym"),
            ("13:00", "Creation lab", "Studio B"),
        ], Board(T, Tuesday, At(10, 15)).Coming.Select(u => (Week.Hm(u.Booking.Start), u.Booking.Title, u.Space)));
        // A space that is only "Booked" says nothing to anybody passing, so it is not on the list.
        var monday = Board(T, Monday, At(15));
        Assert.Contains(Line(monday, "Studio A").Day, x => x.Title == "Booked");
        Assert.DoesNotContain(monday.Coming, u => u.Booking.Title == "Booked");
    }

    [Fact]
    public void ADoorSignLooksAheadToTheNextDaysItsSpaceIsUsedWithinAWeek()
    {
        var studio = RoomId("Studio A");
        var ahead = ScreenBoard.DaysAheadIn(T, studio, Tuesday);
        Assert.Equal([Wednesday, Thursday, Friday], ahead.Select(d => d.Day));
        Assert.Equal(["Juggling"], ahead[0].Bookings.Select(b => b.Title));
        // From Friday it passes over the weekend.
        Assert.Equal([Monday.AddDays(7), Monday.AddDays(8), Monday.AddDays(9)], ScreenBoard.DaysAheadIn(T, studio, Friday).Select(d => d.Day));
        // After the last week built only the staff meeting is ahead, booked, and nothing at all for the gym.
        var lastFriday = Monday.AddDays(11);
        Assert.Equal([(Monday.AddDays(14), "Booked")], ScreenBoard.DaysAheadIn(T, studio, lastFriday).Select(d => (d.Day, d.Bookings.Single().Title)));
        Assert.Empty(ScreenBoard.DaysAheadIn(T, RoomId("Gym"), lastFriday));
    }

    [Fact]
    public void AFormerSpaceIsOnTheBoardOnlyOnADaySomethingIsInIt()
    {
        var canteen = T.Rooms[RoomId("Canteen")]!;
        var t = T with { Rooms = T.Rooms.Put(canteen with { FormerSince = Monday }) };
        Assert.Contains(Board(t, Tuesday, At(10)).Lines, l => l.Room.Name == "Canteen");   // lunch is still in it
        var noLunch = EntitySet<Session>.Of(T.Sessions.Where(s => !(s.Date == Tuesday && s.RoomId == canteen.Id)));
        Assert.DoesNotContain(Board(t with { Sessions = noLunch }, Tuesday, At(10)).Lines, l => l.Room.Name == "Canteen");
        // A current space with nothing in it is listed, free all day.
        Assert.Equal("all day", Line(Board(T with { Sessions = noLunch }, Tuesday, At(10)), "Canteen").Until);
    }
}

/// <summary>A screen over HTTP, with nobody signed in: pairing, its cookie, and what its page carries.</summary>
public class ScreenHostTests : IClassFixture<Host>
{
    private readonly Host _host;
    public ScreenHostTests(Host host) => _host = host;

    private static readonly Regex CodeIn = new("data-testid=\"screen-code\">([^<]+)<");
    private static readonly Regex ClaimIn = new("data-claim=\"([0-9A-F]+)\"");

    private sealed record Paired(HttpClient Screen, string Claim, string Token, string Cookie, Guid Id);

    /// <summary>
    /// The timetable open for everybody: the sample around today, with a class in Studio A all day that has a
    /// note, one in Studio B called off with a reason, an administrators' event in Studio B and a staff one.
    /// </summary>
    private Timetable School()
    {
        var choice = _host.Services.GetRequiredService<ProjectChoice>();
        if (choice.Open() is { } open) return open.State;
        var today = _host.Services.GetRequiredService<SchoolClock>().Today;
        var t = SampleSchool.Create(today, today);
        var studioA = t.Rooms.Single(r => r.Name == "Studio A").Id;
        var studioB = t.Rooms.Single(r => r.Name == "Studio B").Id;
        t = t with
        {
            Sessions = t.Sessions
                .Put(new Session { Date = today, Start = new(0, 0), End = new(23, 59), Title = "Corridor test class", RoomId = studioA, Notes = "Alba Ashcombe twisted an ankle" })
                .Put(new Session { Date = today, Start = new(0, 0), End = new(23, 59), Title = "Called-off class", RoomId = studioB, Cancelled = true, CancelNote = "Finn has the flu" }),
            Events = t.Events
                .Put(new SchoolEvent { Title = "Salary talks", Date = today, Start = new(0, 0), End = new(23, 59), Audience = EventAudience.Administrators, RoomId = studioB })
                .Put(new SchoolEvent { Title = "Staff away day", Date = today, AllDay = true, Audience = EventAudience.Staff }),
        };
        choice.Choose(_host.Services.GetRequiredService<ProjectStore>().Create("School", t, "test", DateTimeOffset.UtcNow));
        return t;
    }

    private static Task<HttpResponseMessage> Collect(HttpClient c, string claim) => c.PostAsync("/screen/collect", new StringContent(claim));

    /// <summary>A screen asks to be paired, an administrator types its code, and it collects its cookie.</summary>
    private async Task<Paired> Pair(Guid? roomId)
    {
        School();
        var tv = _host.Client();
        var page = await tv.GetAsync("/screen");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.True(page.Headers.CacheControl?.NoStore, "The waiting page may be kept by a cache.");
        var html = await page.Content.ReadAsStringAsync();
        var code = CodeIn.Match(html).Groups[1].Value;
        var claim = ClaimIn.Match(html).Groups[1].Value;
        Assert.True(code.Length > 0 && claim.Length > 0, "The waiting page had no code or claim in it:\n" + html);
        Assert.Equal(HttpStatusCode.Accepted, (await Collect(tv, claim)).StatusCode);
        var screen = _host.Services.GetRequiredService<ScreenPairing>().Approve(code, "Corridor", roomId, DateTimeOffset.UtcNow);
        var paired = await Collect(tv, claim);
        Assert.Equal(HttpStatusCode.OK, paired.StatusCode);
        var cookie = paired.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(ScreenEndpoints.CookieName + "="));
        var token = cookie[(ScreenEndpoints.CookieName.Length + 1)..cookie.IndexOf(';')];
        return new Paired(tv, claim, token, cookie, screen.Id);
    }

    [Fact]
    public async Task APairedScreenShowsTheBuildingWithNobodySignedInAndNothingPrivate()
    {
        var p = await Pair(null);
        var parts = p.Cookie.Split(';').Select(x => x.Trim().ToLowerInvariant()).ToList();
        Assert.Contains("path=/screen", parts);
        Assert.Contains("httponly", parts);
        Assert.Contains("samesite=strict", parts);
        Assert.Contains(parts, x => x.StartsWith("expires="));
        // The claim collected the cookie once; asking again finds nothing.
        Assert.Equal(HttpStatusCode.Gone, (await Collect(p.Screen, p.Claim)).StatusCode);

        var r = await p.Screen.GetAsync("/screen");
        Assert.True(r.Headers.CacheControl?.NoStore, "The board may be kept by a cache.");
        var html = await r.Content.ReadAsStringAsync();
        Assert.Contains("data-testid=\"screen\"", html);
        // Each look starts the cookie's 400 days again, and is noted for the administrators' list.
        Assert.Contains(r.Headers.GetValues("Set-Cookie"), h => h.StartsWith($"{ScreenEndpoints.CookieName}={p.Token};"));
        Assert.NotNull(_host.Services.GetRequiredService<ScreenDirectory>().Find(p.Id)?.Seen);
        var t = School();
        foreach (var room in t.Rooms.Current()) Assert.Contains(room.Name, html);
        Assert.Contains("Called-off class", html);
        foreach (var never in new[] { "twisted", "Ashcombe", "flu", "Salary talks", "Staff away day", "Budget review", "Staff meeting" })
            Assert.DoesNotContain(never, html);
        foreach (var s in t.Students) Assert.DoesNotContain(s.Surname, html);
    }

    [Fact]
    public async Task TheScreensCookieOpensNoOtherPage()
    {
        var p = await Pair(null);
        var bare = _host.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = false });
        HttpRequestMessage Ask(string path)
        {
            var ask = new HttpRequestMessage(HttpMethod.Get, path);
            ask.Headers.Add("Cookie", $"{ScreenEndpoints.CookieName}={p.Token}");
            return ask;
        }
        foreach (var path in new[] { "/", "/plan", "/admin/screens", "/data" })
        {
            var r = await bare.SendAsync(Ask(path));
            Assert.True(r.StatusCode == HttpStatusCode.Redirect, $"{path} answered {(int)r.StatusCode} to a screen");
            Assert.StartsWith("/signin", r.Headers.Location!.OriginalString.Replace("http://localhost", ""));
        }
        // The same cookie at the screen's own address is the board: it is a real one, refused everywhere else.
        Assert.Contains("data-testid=\"screen\"", await (await bare.SendAsync(Ask("/screen"))).Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnpairingTurnsTheScreenBackIntoACodeAndTakesItsCookieBack()
    {
        var p = await Pair(null);
        Assert.True(_host.Services.GetRequiredService<ScreenDirectory>().Unpair(p.Id));
        var r = await p.Screen.GetAsync("/screen");
        var html = await r.Content.ReadAsStringAsync();
        Assert.Matches(CodeIn, html);
        Assert.DoesNotContain("data-testid=\"screen\"", html);
        var gone = r.Headers.GetValues("Set-Cookie").Single(h => h.StartsWith(ScreenEndpoints.CookieName + "=")).ToLowerInvariant();
        Assert.Contains("expires=thu, 01 jan 1970", gone);
        Assert.Contains("path=/screen", gone);
    }

    [Fact]
    public async Task ADoorSignShowsItsSpacesWholeDay()
    {
        var studio = School().Rooms.Single(r => r.Name == "Studio A");
        var p = await Pair(studio.Id);
        var html = await p.Screen.GetStringAsync("/screen");
        Assert.Contains("data-testid=\"screen-space\">Studio A<", html);
        Assert.Contains("Corridor test class", html);
        Assert.DoesNotContain("twisted", html);
    }

    [Fact]
    public async Task AClaimNobodyWasGivenCollectsNothing()
    {
        var c = _host.Client();
        foreach (var claim in new[] { "", "made up", new string('A', 64), new string('A', 300) })
            Assert.Equal(HttpStatusCode.Gone, (await Collect(c, claim)).StatusCode);
    }
}

/// <summary>A page reloaded in a loop cannot fill the list of waiting screens. Its own host, as it spends the budget.</summary>
public class ScreenAskLimitTests : IClassFixture<Host>
{
    private readonly Host _host;
    public ScreenAskLimitTests(Host host) => _host = host;

    [Fact]
    public async Task ScreensAskingTooOftenFromOnePlaceAreToldToWait()
    {
        var c = _host.Client();
        for (int i = 0; i < SignInLimiter.AttemptsPerWindow; i++)
            Assert.Contains("data-testid=\"screen-code\"", await c.GetStringAsync("/screen"));
        var html = await c.GetStringAsync("/screen");
        Assert.DoesNotContain("data-testid=\"screen-code\"", html);
        Assert.Contains("ask again in a minute", html);
    }
}
