using System.Collections.Immutable;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// What is new to a facilitator about their own classes (<see cref="News"/>), asked on Wednesday 6 March 2030 at
/// 10:00, when their two weeks run to Tuesday 19 March.
/// </summary>
public class NewsTests
{
    private static readonly DateOnly Wednesday = Monday.AddDays(2);
    private static readonly TimeOnly Ten = new(10, 0);
    private static readonly ImmutableDictionary<Guid, long> NothingSeen = ImmutableDictionary<Guid, long>.Empty;

    /// <summary>A timetable and the history of what was done to it, a change every five minutes.</summary>
    private sealed class Log(Timetable t)
    {
        public Timetable T = t;
        public readonly List<Change> Changes = [];
        private DateTimeOffset _at = new(2030, 3, 5, 7, 0, 0, TimeSpan.Zero);

        public void Do(Func<Timetable, Batch> build, string by = "admin")
        {
            var batch = build(T);
            Changes.Add(new Change(Changes.Count + 1, by, _at = _at.AddMinutes(5), batch));
            T = Apply.Batch(T, batch);
        }

        public void Respace(Session s, Room r, string by = "admin") => Do(t => Edits.PutSession(t, t.Sessions[s.Id]! with { RoomId = r.Id }), by);
    }

    private static IReadOnlyList<ClassChange> Ada(Log log, Facilitator ada, IReadOnlyDictionary<Guid, long>? seen = null) =>
        News.For(log.T, log.Changes, ada.Id, Wednesday, Ten, ["ada"], seen ?? NothingSeen);

    [Fact]
    public void ItIsTheirClassesStillToComeInTheNextTwoWeeksThatSomebodyElseChangedSoonestFirst()
    {
        var b = new Build();
        var hall = b.Room("Hall");
        var gym = b.Room("Gym");
        var ada = b.Facilitator("Ada");
        var dag = b.Facilitator("Dag");
        var esme = b.Facilitator("Esme");
        var laterToday = b.Session(Wednesday, "09:30", "11:00", null, hall, null, In(ada));          // still on at ten
        var respaced = b.Session(Monday.AddDays(3), "09:00", "10:00", null, hall, null, In(ada));
        var givenAway = b.Session(Monday.AddDays(4), "09:00", "10:00", null, hall, null, In(ada));
        var deleted = b.Session(Monday.AddDays(4), "11:00", "12:00", null, hall, null, In(ada));
        var covering = b.Session(Monday.AddDays(7), "13:00", "14:00", null, hall, null, In(esme));    // Ada covers it for Esme
        var movedIn = b.Session(Monday.AddDays(21), "09:00", "10:00", null, hall, null, In(ada));     // moved into the two weeks
        var lastDay = b.Session(Monday.AddDays(15), "09:00", "10:00", null, hall, null, In(ada));     // Tuesday 19th, the last of them
        var earlierToday = b.Session(Wednesday, "08:00", "09:30", null, hall, null, In(ada));        // over by ten
        var past = b.Session(Monday, "09:00", "10:00", null, hall, null, In(ada));
        var farOff = b.Session(Monday.AddDays(16), "09:00", "10:00", null, hall, null, In(ada));      // Wednesday 20th, after them
        var dags = b.Session(Monday.AddDays(3), "11:00", "12:00", null, hall, null, In(dag));
        var hers = b.Session(Monday.AddDays(3), "14:00", "15:00", null, hall, null, In(ada));         // changed by Ada herself
        b.Away(esme, Monday.AddDays(7));
        var log = new Log(b.T);
        log.Respace(laterToday, gym);
        log.Respace(respaced, gym);
        log.Do(t => Edits.PutSession(t, t.Sessions[givenAway.Id]! with { Facilitators = [In(dag)] }));
        log.Do(t => Edits.RemoveSessions(t, [deleted.Id]));
        log.Do(t => Edits.SetCover(t, covering.Id, [ada.Id], null), "editor");
        log.Do(t => Edits.Move(t, movedIn.Id, Monday.AddDays(8), new TimeOnly(9, 0), new TimeOnly(10, 0)));
        log.Respace(lastDay, gym);
        var added = new Session { Date = Monday.AddDays(9), Start = new(9, 0), End = new(10, 0), Title = "Extra", Facilitators = [In(ada)] };
        log.Do(t => Edits.PutSession(t, added));
        foreach (var s in new[] { earlierToday, past, farOff, dags }) log.Respace(s, gym);
        log.Respace(hers, gym, by: "ADA");
        log.Do(t => Edits.Move(t, respaced.Id, respaced.Date, new TimeOnly(9, 15), new TimeOnly(10, 15)));

        var news = Ada(log, ada);

        Assert.Equal([laterToday.Id, respaced.Id, givenAway.Id, deleted.Id, covering.Id, movedIn.Id, added.Id, lastDay.Id], news.Select(x => x.Id));
        var given = news.Single(x => x.Id == givenAway.Id);
        Assert.Equal([ada.Id], given.Was!.FacilitatorIds);                // theirs as it was, given to somebody else now
        Assert.Equal(["editor"], news.Single(x => x.Id == covering.Id).By);
        Assert.Equal(5, news.Single(x => x.Id == covering.Id).Sequence);  // its last change, which Got it remembers
        Assert.Equal(log.Changes.Count, news.Single(x => x.Id == respaced.Id).Sequence);
        // Dag's own news is his own class, and the one given to him.
        Assert.Equal([dags.Id, givenAway.Id], News.For(log.T, log.Changes, dag.Id, Wednesday, Ten, [], NothingSeen).Select(x => x.Id));
    }

    [Fact]
    public void GotItMakesItSeenUntilItChangesAgainAndThenItIsToldFromAsTheySawIt()
    {
        var b = new Build();
        var hall = b.Room("Hall");
        var gym = b.Room("Gym");
        var studio = b.Room("Studio");
        var ada = b.Facilitator("Ada");
        var thursday = b.Session(Monday.AddDays(3), "09:00", "10:00", null, hall, null, In(ada));
        var friday = b.Session(Monday.AddDays(4), "09:00", "10:00", null, hall, null, In(ada));
        var log = new Log(b.T);
        log.Respace(thursday, gym);
        log.Respace(friday, gym);
        var news = Ada(log, ada);
        Assert.Equal([thursday.Id, friday.Id], news.Select(x => x.Id));

        var seen = News.GotIt(NothingSeen, [news[0]], oldest: 1);
        Assert.Equal([friday.Id], Ada(log, ada, seen).Select(x => x.Id));

        log.Respace(thursday, studio, by: "editor");
        var again = Ada(log, ada, seen);
        Assert.Equal([thursday.Id, friday.Id], again.Select(x => x.Id));
        Assert.Equal(gym.Id, again[0].Was!.RoomId);                       // as they saw it, not as the window found it
        Assert.Equal(["editor"], again[0].By);
        // Put back where they saw it, it is no change to them, though it is still one since the window opened.
        log.Respace(thursday, gym);
        Assert.Equal([friday.Id], Ada(log, ada, seen).Select(x => x.Id));
        Assert.Contains(Ada(log, ada), x => x.Id == thursday.Id);
    }

    [Fact]
    public void AChangeTheyMadeThemselvesCountsAsSeenAndWhatSomebodyElseDoesAfterIsToldFromIt()
    {
        var b = new Build();
        var hall = b.Room("Hall");
        var gym = b.Room("Gym");
        var ada = b.Facilitator("Ada");
        var thursday = b.Session(Monday.AddDays(3), "09:00", "10:00", null, hall, null, In(ada));
        var log = new Log(b.T);
        log.Respace(thursday, gym, by: "Ada");
        Assert.Empty(Ada(log, ada));
        log.Do(t => Edits.Move(t, thursday.Id, thursday.Date, new TimeOnly(10, 0), new TimeOnly(11, 0)));
        var news = Assert.Single(Ada(log, ada));
        Assert.True(news.TimesChanged);
        Assert.False(news.SpaceChanged);                                    // the gym was her own doing
        Assert.Equal(["admin"], news.By);
        // And a change somebody else made before hers is seen too: she saw the class as she left it.
        log.Do(t => Edits.Move(t, thursday.Id, thursday.Date, new TimeOnly(11, 0), new TimeOnly(12, 0)), "ada");
        Assert.Empty(Ada(log, ada));
        // Seen after a change of her own, it stays seen: what she did earlier does not take back what she saw later.
        log.Do(t => Edits.Move(t, thursday.Id, thursday.Date, new TimeOnly(12, 0), new TimeOnly(13, 0)));
        var seen = News.GotIt(NothingSeen, Ada(log, ada), oldest: 1);
        Assert.Empty(Ada(log, ada, seen));
    }

    [Fact]
    public void SeenSaysWhetherTheLastChangeIsSeenAndLeavesOutWhatNothingChangedLately()
    {
        var b = new Build();
        var hall = b.Room("Hall");
        var gym = b.Room("Gym");
        var ada = b.Facilitator("Ada");
        var thursday = b.Session(Monday.AddDays(3), "09:00", "10:00", null, hall, null, In(ada));
        var friday = b.Session(Monday.AddDays(4), "09:00", "10:00", null, hall, null, In(ada));
        var monday = b.Session(Monday.AddDays(7), "09:00", "10:00", null, hall, null, In(ada));
        var tuesday = b.Session(Monday.AddDays(8), "09:00", "10:00", null, hall, null, In(ada));
        var log = new Log(b.T);
        log.Respace(thursday, gym);
        log.Respace(friday, gym);
        log.Respace(tuesday, gym);
        var seen = News.GotIt(NothingSeen, Ada(log, ada).Where(x => x.Id != friday.Id), oldest: 1);
        log.Respace(tuesday, hall);                                         // seen in the gym, then put back

        var said = News.Seen(log.T, log.Changes, ada.Id, Wednesday, Ten, ["ada"], seen);

        Assert.True(said[thursday.Id]);
        Assert.False(said[friday.Id]);
        Assert.False(said[tuesday.Id]);                                     // news to her, though no change since the window opened
        Assert.False(said.ContainsKey(monday.Id));                          // nothing changed it: nothing to have seen
        log.Respace(monday, gym, by: "ada");
        Assert.True(News.Seen(log.T, log.Changes, ada.Id, Wednesday, Ten, ["ada"], seen)[monday.Id]);
    }

    [Fact]
    public void WhatIsKeptIsEachClassUpToItsLastChangeAndNothingOlderThanWhatIsStillRead()
    {
        Guid gone = Guid.NewGuid(), edge = Guid.NewGuid(), later = Guid.NewGuid(), fresh = Guid.NewGuid();
        var seen = NothingSeen.Add(gone, 4).Add(edge, 5).Add(later, 10);
        ClassChange Item(Guid id, long sequence) => new(id, null, null, [], DateTimeOffset.UnixEpoch, sequence);
        // Got it on an older reading of a class leaves what they saw later; the first change still read is 5.
        var kept = News.GotIt(seen, [Item(fresh, 12), Item(later, 7)], oldest: 5);
        Assert.Equal(new[] { (edge, 5L), (later, 10L), (fresh, 12L) }.OrderBy(x => x.Item1), kept.Select(kv => (kv.Key, kv.Value)).OrderBy(x => x.Item1));
    }

    [Fact]
    public void TheEveningEmailIsEachFacilitatorsUnseenNewsAboutTheirNextDayAndNobodyWithoutAnAddress()
    {
        var b = new Build();
        var hall = b.Room("Hall");
        var gym = b.Room("Gym");
        Facilitator Mailed(string name, bool guest = false, bool former = false)
        {
            var f = b.Facilitator(name) with { Email = name.ToLowerInvariant() + "@example.invalid", IsGuest = guest, FormerSince = former ? Monday : null };
            b.T = b.T with { Facilitators = b.T.Facilitators.Put(f) };
            return f;
        }
        var ada = Mailed("Ada");
        var esme = Mailed("Esme");
        var cleo = Mailed("Cleo");                                                                      // nothing new for her
        var gus = Mailed("Gus", guest: true);
        var dag = Mailed("Dag", former: true);
        var bo = b.Facilitator("Bo");                                                                   // no address
        var thursday = Wednesday.AddDays(1);
        var moved = b.Session(thursday, "09:00", "10:00", null, hall, null, In(ada));
        var gone = b.Session(thursday, "11:00", "12:00", null, hall, null, In(ada));                   // moved off tomorrow
        var movedIn = b.Session(Monday.AddDays(7), "13:00", "14:00", null, hall, null, In(ada));       // moved onto tomorrow
        var friday = b.Session(thursday.AddDays(1), "09:00", "10:00", null, hall, null, In(ada));
        var seenOne = b.Session(thursday, "15:00", "16:00", null, hall, null, In(ada));
        var hers = b.Session(thursday, "16:00", "17:00", null, hall, null, In(ada));
        var esmes = b.Session(thursday, "17:00", "18:00", null, hall, null, In(esme));                // Esme's, moved to Friday
        var given = b.Session(Monday.AddDays(7), "15:00", "16:00", null, hall, null, In(ada));         // Ada's, given to Esme on tomorrow
        b.Session(thursday, "10:00", "11:00", null, gym, null, In(cleo));
        var others = new[] { b.Session(thursday, "09:00", "10:00", null, gym, null, In(gus)), b.Session(thursday, "11:00", "12:00", null, gym, null, In(dag)),
                             b.Session(thursday, "13:00", "14:00", null, gym, null, In(bo)) };
        var log = new Log(b.T);
        log.Respace(moved, gym);
        log.Do(t => Edits.Move(t, gone.Id, Monday.AddDays(7), new TimeOnly(11, 0), new TimeOnly(12, 0)));
        log.Do(t => Edits.Move(t, movedIn.Id, thursday, new TimeOnly(13, 0), new TimeOnly(14, 0)));
        log.Respace(friday, gym);
        log.Respace(seenOne, gym);
        log.Respace(hers, gym, by: "ada");
        log.Do(t => Edits.Move(t, esmes.Id, thursday.AddDays(1), new TimeOnly(17, 0), new TimeOnly(18, 0)));
        log.Do(t => Edits.PutSession(t, t.Sessions[given.Id]! with { Date = thursday, Facilitators = [In(esme)] }));
        foreach (var s in others) log.Respace(s, hall);
        var seen = News.GotIt(NothingSeen, News.For(log.T, log.Changes, ada.Id, Wednesday, Ten, ["ada"], NothingSeen).Where(x => x.Id == seenOne.Id), oldest: 1);

        var notes = EveningNotes.For(log.T, log.Changes, Wednesday, new TimeOnly(18, 0),
            id => id == ada.Id ? ["ada"] : [], id => id == ada.Id ? seen : NothingSeen);

        Assert.Equal(["Ada", "Esme"], notes.Select(n => n.Facilitator.Name));
        Assert.All(notes, n => Assert.Equal(thursday, n.Day));
        // Not the one given away on another day: it was hers on Monday, and on Thursday it is Esme's.
        Assert.Equal([moved.Id, movedIn.Id, gone.Id], notes[0].Changes.Select(x => x.Id));
        Assert.Equal([given.Id, esmes.Id], notes[1].Changes.Select(x => x.Id));
        // Asked the evening before Friday instead, it is Friday's: Esme's class moved onto it is hers there.
        Assert.Equal([(ada.Id, friday.Id), (esme.Id, esmes.Id)], EveningNotes.For(log.T, log.Changes, thursday, new TimeOnly(18, 0), _ => [], _ => NothingSeen)
            .SelectMany(n => n.Changes.Select(x => (n.Facilitator.Id, x.Id))));
    }
}
