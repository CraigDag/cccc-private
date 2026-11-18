using System.Collections.Immutable;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Store;
using Cccc.Web.Features.Now;
using Cccc.Web.Features.Sessions;

namespace Cccc.Web.Tests;

/// <summary>
/// What a coach reads in New for you (<see cref="NewsWords"/>), with Ada Brightwater as the coach, and what the Cover
/// page says about whether whoever covers a class has seen it.
/// </summary>
public class NewsWordsTests
{
    private static readonly Facilitator Ada = new() { Name = "Ada Brightwater" };
    private static readonly Facilitator Dag = new() { Name = "Dag Øverlie" };
    private static readonly Facilitator Esme = new() { Name = "Esme Halloran" };
    private static readonly Room Hall = new() { Name = "Hall" };
    private static readonly Room Gym = new() { Name = "Gym" };
    private static readonly Group Ba1 = new() { Name = "BA1" };
    private static readonly DateOnly Thursday = new(2030, 3, 7);

    private static readonly Session Aerial = new()
    {
        Date = Thursday, Start = new(9, 0), End = new(11, 0), Title = "Aerial", RoomId = Hall.Id, Facilitators = [new Assignment(Ada.Id)],
    };

    private static Timetable School(params Facilitator[] away) => Timetable.Empty with
    {
        Rooms = EntitySet<Room>.Of([Hall, Gym]),
        Groups = EntitySet<Group>.Of([Ba1]),
        Facilitators = EntitySet<Facilitator>.Of([Ada, Dag, Esme]),
        Absences = EntitySet<Absence>.Of([.. away.Select(f => new Absence { Person = AbsentPerson.Facilitator, PersonId = f.Id, From = Thursday, To = Thursday })]),
    };

    private static ImmutableList<Assignment> By(params Facilitator[] who) => [.. who.Select(f => new Assignment(f.Id))];

    private static ClassChange Changed(Session? was, Session? now, int sequence = 1) =>
        new((was ?? now)!.Id, was, now, ["admin"], DateTimeOffset.UnixEpoch.AddMinutes(sequence), sequence);

    private static string Said(Timetable t, Session? was, Session? now) => NewsWords.ForYou(t, Changed(was, now), Ada.Id);

    [Fact]
    public void ItSaysFirstWhetherTheClassIsTheirsNowThenWhatElseIsDifferentWithYouForThem()
    {
        var t = School();
        var esmes = Aerial with { Facilitators = By(Esme) };
        var shared = Aerial with { Facilitators = By(Ada, Esme) };

        Assert.Equal("New", Said(t, null, Aerial));
        Assert.Equal("New, and you cover it", Said(t, null, esmes with { CoverIds = [Ada.Id] }));
        Assert.Equal("Deleted", Said(t, Aerial, null));
        Assert.Equal("Moved from Thu 7 Mar · was 09:00–11:00", Said(t, Aerial, Aerial with { Date = Thursday.AddDays(1), Start = new(13, 0), End = new(15, 0) }));
        Assert.Equal("Was in Hall", Said(t, Aerial, Aerial with { RoomId = Gym.Id }));
        Assert.Equal("Cancelled", Said(t, Aerial, Aerial with { Cancelled = true }));
        Assert.Equal("Back on", Said(t, Aerial with { Cancelled = true }, Aerial));
        // Given to them: to teach, beside somebody or instead of them, or to cover for whoever is away.
        Assert.Equal("You teach it now instead of Esme Halloran", Said(t, esmes, Aerial));
        Assert.Equal("You teach it now, with Esme Halloran", Said(t, esmes, shared));
        Assert.Equal("You cover it, for Esme Halloran", Said(School(Esme), esmes, esmes with { CoverIds = [Ada.Id] }));
        Assert.Equal("You cover it", Said(t, esmes, esmes with { CoverIds = [Ada.Id] }));
        // No longer theirs: who has it instead, first, before anything else that changed.
        Assert.Equal("Not yours now: Dag Øverlie teaches it", Said(t, Aerial, Aerial with { Facilitators = By(Dag) }));
        Assert.Equal("Not yours now: Dag Øverlie, Esme Halloran teach it", Said(t, Aerial, Aerial with { Facilitators = By(Dag, Esme) }));
        Assert.Equal("Not yours now: nobody teaches it", Said(t, Aerial, Aerial with { Facilitators = [] }));
        Assert.Equal("Not yours now: Esme Halloran teaches it · moved from Thu 7 Mar", Said(t, shared, esmes with { Date = Thursday.AddDays(1) }));
        Assert.Equal("Not yours now: Dag Øverlie covers it", Said(t, esmes with { CoverIds = [Ada.Id] }, esmes with { CoverIds = [Dag.Id] }));
        Assert.Equal("Not yours now: cover taken off", Said(t, esmes with { CoverIds = [Ada.Id] }, esmes));
        // Still theirs: who covers for them, and their own part, said to them.
        Assert.Equal("Dag Øverlie covering for you", Said(School(Ada), Aerial, Aerial with { CoverIds = [Dag.Id] }));
        Assert.Equal("Dag Øverlie covering", Said(School(Esme), shared, shared with { CoverIds = [Dag.Id] }));
        Assert.Equal("Cover taken off", Said(School(Ada), Aerial with { CoverIds = [Dag.Id] }, Aerial));
        Assert.Equal("Your part is now until 10:00", Said(t, Aerial, Aerial with { Facilitators = [new Assignment(Ada.Id, null, new TimeOnly(10, 0))] }));
        Assert.Equal("A different part for Esme Halloran", Said(t, shared, shared with { Facilitators = [new Assignment(Ada.Id), new Assignment(Esme.Id, new TimeOnly(10, 0))] }));
        Assert.Equal("Your part is now all of it", Said(t, Aerial with { Facilitators = [new Assignment(Ada.Id, null, new TimeOnly(10, 0))] }, Aerial));
        Assert.Equal("Dag Øverlie added", Said(t, Aerial, Aerial with { Facilitators = By(Ada, Dag) }));
        Assert.Equal("Esme Halloran taken off", Said(t, shared, Aerial));
        Assert.Equal("Dag Øverlie instead of Esme Halloran", Said(t, shared, Aerial with { Facilitators = By(Ada, Dag) }));
        // Still covering it: only what else changed.
        Assert.Equal("Was in Hall", Said(t, esmes with { CoverIds = [Ada.Id] }, esmes with { CoverIds = [Ada.Id], RoomId = Gym.Id }));
        // The space taken away or given, who it is for, and its name.
        Assert.Equal("No space now, was in Hall", Said(t, Aerial, Aerial with { RoomId = null }));
        Assert.Equal("A space given", Said(t, Aerial with { RoomId = null }, Aerial));
        Assert.Equal("Now for BA1, was Everyone", Said(t, Aerial, Aerial with { Audience = Audience.ForGroup(Ba1.Id) }));
        Assert.Equal("Was called Aerial", Said(t, Aerial, Aerial with { Title = "Aerial silks" }));
        Assert.Equal("Changed", Said(t, Aerial, Aerial));
    }

    [Fact]
    public void AWeeksNewClassesAreOneLineAndSoAreItsDeletedOnesSoonestFirst()
    {
        var t = School();
        var juggling = Aerial with { Id = Guid.NewGuid(), Title = "Juggling" };
        Session On(Session s, int days) => s with { Id = Guid.NewGuid(), Date = Thursday.AddDays(days) };
        var moved = Changed(Aerial, Aerial with { RoomId = Gym.Id });
        var news = new[]
        {
            moved,
            Changed(null, On(Aerial, 4)), Changed(null, On(juggling, 5)), Changed(On(Aerial, 6), null),   // next week: Mon, Tue, Wed
            Changed(On(juggling, 8), null), Changed(null, On(Aerial, 11)),                                  // next Friday; the Monday after
        };

        var lines = NewsWords.Lines(news);

        Assert.Equal([NewsKind.One, NewsKind.Added, NewsKind.Deleted, NewsKind.One], lines.Select(l => l.Kind));
        Assert.Equal(["Was in Hall", "2 new classes", "2 classes deleted", "New"], lines.Select(l => NewsWords.What(t, l, Ada.Id)));
        Assert.Equal(["Aerial", "Aerial and Juggling", "Aerial and Juggling", "Aerial"], lines.Select(l => NewsWords.Title(t, l)));
        Assert.Equal("Thu 7 Mar, 09:00–11:00 · Gym", NewsWords.When(t, lines[0]));
        Assert.Equal("Mon 11 Mar to Tue 12 Mar", NewsWords.When(t, lines[1]));
        Assert.Equal("Wed 13 Mar to Fri 15 Mar", NewsWords.When(t, lines[2]));
        Assert.Equal([false, false, true, false], lines.Select(l => NewsWords.Off(l, Ada.Id)));
        Assert.True(NewsWords.Off(new NewsLine([Changed(Aerial, Aerial with { Facilitators = By(Dag) })], NewsKind.One), Ada.Id));
        Assert.True(NewsWords.Off(new NewsLine([Changed(Aerial, Aerial with { Cancelled = true })], NewsKind.One), Ada.Id));
        // Several on one day, the same name twice: each name once, the first three, and how many more.
        var five = new NewsLine([.. new[] { "Aerial", "Juggling", "Aerial", "Trapeze", "Rope" }.Select(n => Changed(null, Aerial with { Id = Guid.NewGuid(), Title = n }))], NewsKind.Added);
        Assert.Equal("Aerial, Juggling, Trapeze and 1 more", NewsWords.Title(t, five));
        Assert.Equal("Thu 7 Mar", NewsWords.When(t, five));
        Assert.Equal("5 new classes", NewsWords.What(t, five, Ada.Id));
    }

    [Fact]
    public void TheCoverPageSaysWhetherWhoeverCoversHasSeenItAndNothingWhenThereIsNothingToHaveSeen()
    {
        var t = School();
        var s = Aerial with { CoverIds = [Dag.Id] };
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, bool>> seenBy = new Dictionary<Guid, IReadOnlyDictionary<Guid, bool>>
        {
            [Dag.Id] = new Dictionary<Guid, bool> { [s.Id] = true },
            [Ada.Id] = new Dictionary<Guid, bool> { [s.Id] = false },
        };
        Assert.Equal(new SeenWords("Seen", true), CoverWords.Seen(t, s, seenBy));
        Assert.Equal(new SeenWords("Not seen yet", false), CoverWords.Seen(t, s with { CoverIds = [Ada.Id] }, seenBy));
        Assert.Equal(new SeenWords("Seen by Dag Øverlie; not seen yet by Ada Brightwater", false), CoverWords.Seen(t, s with { CoverIds = [Ada.Id, Dag.Id] }, seenBy));
        Assert.Equal(new SeenWords("Seen by Dag Øverlie", true), CoverWords.Seen(t, s with { CoverIds = [Dag.Id, Esme.Id] }, seenBy));
        IReadOnlyDictionary<Guid, IReadOnlyDictionary<Guid, bool>> neither = new Dictionary<Guid, IReadOnlyDictionary<Guid, bool>>
        {
            [Dag.Id] = new Dictionary<Guid, bool> { [s.Id] = false },
            [Ada.Id] = new Dictionary<Guid, bool> { [s.Id] = false },
        };
        Assert.Equal(new SeenWords("Not seen yet by Ada Brightwater, Dag Øverlie", false), CoverWords.Seen(t, s with { CoverIds = [Dag.Id, Ada.Id] }, neither));
        Assert.Null(CoverWords.Seen(t, s with { CoverIds = [Esme.Id] }, seenBy));        // does not sign in
        Assert.Null(CoverWords.Seen(t, s with { Id = Guid.NewGuid() }, seenBy));         // nothing changed it lately
    }

    [Fact]
    public void WhatTheyHaveSeenIsKeptPerTimetableAndPersonAndARecordNobodyCanReadIsNone()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "stores", Guid.NewGuid().ToString("N"));
        var store = ProjectStore.Open(folder, create: true);
        try
        {
            var project = store.Create("School", School(), "test", DateTimeOffset.UnixEpoch);
            var accounts = new AccountDirectory(store, () => DateTimeOffset.UnixEpoch);
            var seen = new SeenNews(store, accounts);
            // Whose doing is theirs: every sign-in linked to them, a suspended one too, for those who can sign in at all.
            var admin = accounts.CreateFirstAdministrator(accounts.SetupToken()!, "admin", "Admin", "a long passphrase");
            void Linked(string login, Facilitator f, bool suspended = false) =>
                accounts.Update(admin.Id, accounts.Add(admin.Id, login, login, Role.Viewer, "a long passphrase").Id, login, Role.Viewer, AccountTags.None, f.Id, suspended);
            Linked("ada", Ada);
            Linked("ada-phone", Ada);
            Linked("dag-old", Dag, suspended: true);
            Linked("dag", Dag);
            Linked("esme", Esme, suspended: true);
            var logins = seen.LoginsOf([Ada.Id, Dag.Id, Esme.Id, Guid.NewGuid()]);
            Assert.Equal(new[] { Ada.Id, Dag.Id }.Order(), logins.Keys.Order());
            Assert.Equal(["ada", "ada-phone"], logins[Ada.Id].Order());
            Assert.Equal(["dag", "dag-old"], logins[Dag.Id].Order());
            Assert.Equal([Ada.Id], seen.LoginsOf([Ada.Id]).Keys);

            Change At(long sequence) => new(sequence, "admin", DateTimeOffset.UnixEpoch, new Batch("x", []));
            seen.GotIt(project, Ada.Id, [Changed(Aerial, Aerial with { RoomId = Gym.Id }, 7)], [At(3), At(7)]);
            Assert.Equal([(Aerial.Id, 7L)], seen.Read(project, Ada.Id).Select(kv => (kv.Key, kv.Value)));
            Assert.Empty(seen.Read(project, Dag.Id));
            // Kept while the changes read still reach back to it; read from changes that start after it, it decides
            // nothing any more, and goes when anything else is seen.
            var other = Aerial with { Id = Guid.NewGuid() };
            var third = Aerial with { Id = Guid.NewGuid() };
            seen.GotIt(project, Ada.Id, [Changed(other, other with { RoomId = Gym.Id }, 9)], [At(5), At(9)]);
            Assert.Equal([(Aerial.Id, 7L), (other.Id, 9L)], seen.Read(project, Ada.Id).Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Value));
            seen.GotIt(project, Ada.Id, [Changed(third, third with { RoomId = Gym.Id }, 10)], [At(8), At(10)]);
            Assert.Equal([(other.Id, 9L), (third.Id, 10L)], seen.Read(project, Ada.Id).Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Value));
            store.SetNewsSeen(project, Ada.Id, "{not a map");
            Assert.Empty(seen.Read(project, Ada.Id));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}
