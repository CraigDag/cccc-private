using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// A facilitator asking for a space at a time. Nothing on the timetable changes until an administrator says yes,
/// which adds the class asked for, taught by whoever asked, as the add form would: the space in use then is said
/// beside the answer and never refuses it.
/// </summary>
public class SpaceAskTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 4, 7, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly TimeOnly Eight = new(8, 0);

    private static Timetable Do(Timetable t, Batch b) => Apply.Batch(t, b);

    private static void Refused(Func<Batch> edit, string words) =>
        Assert.Contains(words, Assert.Throws<EditRefused>(() => edit()).Message);

    private static SpaceAsk Ask(Room room, DateOnly date, string from, string until, Audience? who = null, Class? cls = null, string title = "") => new()
    {
        RoomId = room.Id, Date = date, Start = TimeOnly.Parse(from), End = TimeOnly.Parse(until),
        Audience = who ?? Audience.Everyone, ClassId = cls?.Id, Title = title,
    };

    [Fact]
    public void AskingForASpaceChangesNothingUntilAnAdministratorSaysYesWhichAddsTheClassTaughtByWhoeverAsked()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var studioA = b.Room("Studio A");
        var studioB = b.Room("Studio B");
        var ba1 = b.Group("BA 1");
        var juggling = b.Session(Tuesday, "10:00", "11:00", null, studioA, b.Class("Juggling"), In(bo));
        var before = b.T;

        var asked = Do(before, Edits.AskForSpace(before, Ask(studioA, Tuesday, "10:00", "11:30", Audience.ForGroup(ba1.Id), title: "  Duo act rehearsal "),
            AskingReader.Facilitator(ada.Id), Today, Eight, At));
        var a = Assert.Single(asked.SpaceAsks);
        Assert.Equal((ada.Id, Tuesday, new TimeOnly(10, 0), new TimeOnly(11, 30), studioA.Id, "Duo act rehearsal", At),
            (a.FacilitatorId, a.Date, a.Start, a.End, a.RoomId, a.Title, a.AskedAt));
        Assert.True(a.Audience.Equivalent(Audience.ForGroup(ba1.Id)));
        Assert.Equal((null, null, null), (a.Answer, a.AnsweredAt, a.SessionId));
        Assert.Equal(SpaceAskState.Waiting, Asking.StateOf(a, Today, Eight));
        Assert.Equal([a.Id], Asking.SpaceAsksWaiting(asked, Today, Eight).Select(x => x.Id));
        // Nothing that counts classes can see it: the week, its clashes and Ada's hours are as they were.
        Assert.Equal(before with { SpaceAsks = asked.SpaceAsks }, asked);
        Assert.Empty(Clashes.ForWeek(asked, Monday));
        Assert.Equal(TimeSpan.Zero, Hours.For(asked, Occurrences.HappeningInWeek(asked, Monday), ada.Id).Total);

        // What a yes would run into, said before it is: Studio A has Bo's juggling then; Studio B is free.
        var clash = Assert.Single(Asking.WhatAYesClashesWith(asked, a));
        Assert.Equal(ClashKind.RoomDoubleBooked, clash.Kind);
        Assert.Contains(juggling.Id, clash.SessionIds);
        Assert.Contains("Duo act rehearsal", clash.Description);
        Assert.Empty(Asking.WhatAYesClashesWith(asked, a, studioB.Id));
        Assert.Equal([studioB.Id], Asking.OtherSpacesFree(asked, a).Select(r => r.Id));

        // Yes, in the space asked for, whatever is there: the class as the add form would add it.
        var yes = Edits.AnswerSpaceAsk(asked, a.Id, Answer.Yes, AskingReader.Administrator, Today, Eight, At.AddHours(1));
        var booked = Do(asked, yes);
        var answered = booked.SpaceAsks[a.Id]!;
        var s = booked.Sessions[answered.SessionId!.Value]!;
        Assert.Equal((Tuesday, new TimeOnly(10, 0), new TimeOnly(11, 30), (Guid?)studioA.Id, (Guid?)null, "Duo act rehearsal"),
            (s.Date, s.Start, s.End, s.RoomId, s.ClassId, s.Title));
        Assert.Equal([ada.Id], s.FacilitatorIds);
        Assert.Empty(s.CoverIds);
        Assert.True(s.Audience.Equivalent(Audience.ForGroup(ba1.Id)));
        Assert.Equal((Answer.Yes, At.AddHours(1), SpaceAskState.Booked), (answered.Answer!.Value, answered.AnsweredAt!.Value, Asking.StateOf(answered, Today, Eight)));
        Assert.Equal(before.Sessions.Count + 1, booked.Sessions.Count);
        Assert.Empty(Asking.SpaceAsksWaiting(booked, Today, Eight));
        Assert.Contains(Clashes.ForWeek(booked, Monday), c => c.Kind == ClashKind.RoomDoubleBooked && c.SessionIds.Contains(s.Id));
        Assert.Equal(TimeSpan.FromMinutes(90), Hours.For(booked, Occurrences.HappeningInWeek(booked, Monday), ada.Id).Total);
        Assert.Contains("Duo act rehearsal", yes.Label);
        Assert.Contains("Studio A", yes.Label);
        Refused(() => Edits.AnswerSpaceAsk(booked, a.Id, Answer.No, AskingReader.Administrator, Today, Eight, At), "answered already");
        Refused(() => Edits.TakeBackSpaceAsk(booked, a.Id, AskingReader.Facilitator(ada.Id)), "answered it already");

        // Undone, it waits again and the class has gone.
        var undone = Do(booked, yes.Inverse());
        Assert.Equal(before.Sessions.Count, undone.Sessions.Count);
        Assert.Equal(SpaceAskState.Waiting, Asking.StateOf(undone.SpaceAsks[a.Id]!, Today, Eight));

        // Yes somewhere else: in Studio B, nothing to run into.
        var elsewhere = Do(asked, Edits.AnswerSpaceAsk(asked, a.Id, Answer.Yes, AskingReader.Administrator, Today, Eight, At, studioB.Id));
        var there = elsewhere.Sessions[elsewhere.SpaceAsks[a.Id]!.SessionId!.Value]!;
        Assert.Equal(studioB.Id, there.RoomId);
        Assert.Equal(studioA.Id, elsewhere.SpaceAsks[a.Id]!.RoomId);
        Assert.Empty(Clashes.ForWeek(elsewhere, Monday));
        // As asked is the space asked for, whichever it is: Studio B asked for is Studio B.
        var inB = Do(before, Edits.AskForSpace(before, Ask(studioB, Tuesday, "13:00", "14:00", title: "Duo act rehearsal"), AskingReader.Facilitator(ada.Id), Today, Eight, At));
        var askedB = inB.SpaceAsks.Single();
        var yesB = Do(inB, Edits.AnswerSpaceAsk(inB, askedB.Id, Answer.Yes, AskingReader.Administrator, Today, Eight, At));
        Assert.Equal(studioB.Id, yesB.Sessions[yesB.SpaceAsks[askedB.Id]!.SessionId!.Value]!.RoomId);
        // The other spaces free then are the others with nothing in them: at 13:00 Studio A, never Studio B itself;
        // at 10:00, while Studio A has Bo's juggling, none.
        Assert.Equal([studioA.Id], Asking.OtherSpacesFree(inB, askedB).Select(r => r.Id));
        Assert.Empty(Asking.OtherSpacesFree(inB, askedB with { Start = new(10, 0), End = new(11, 30) }));

        // No changes nothing on the timetable; the ask says so.
        var no = Do(asked, Edits.AnswerSpaceAsk(asked, a.Id, Answer.No, AskingReader.Administrator, Today, Eight, At));
        Assert.Equal(asked with { SpaceAsks = no.SpaceAsks }, no);
        Assert.Equal((Answer.No, (Guid?)null, SpaceAskState.NotThisTime), (no.SpaceAsks[a.Id]!.Answer!.Value, no.SpaceAsks[a.Id]!.SessionId, Asking.StateOf(no.SpaceAsks[a.Id]!, Today, Eight)));
        Assert.Empty(Asking.SpaceAsksWaiting(no, Today, Eight));
    }

    [Fact]
    public void AClassFromTheCatalogueIsCalledByItsNameAsItIsWhenTheAnswerComesAndTheTimesSnapAsTheAddFormSnapsThem()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var hall = b.Room("Hall");
        var aerial = b.Class("Aerial");
        var degree = b.Programme("Degree");
        var ba1 = b.Group("BA 1", degree);
        var ba2 = b.Group("BA 2", degree);
        b.Session(Tuesday, "10:00", "11:00", null, hall, b.Class("Juggling"), In(b.Facilitator("Bo")));
        var t = b.T;
        var me = AskingReader.Facilitator(ada.Id);

        // 10:08 and 11:52 snap to 10:15 and 11:45; a programme is asked for as its groups.
        var asked = Do(t, Edits.AskForSpace(t, Ask(hall, Tuesday, "10:08", "11:52", Edits.ForProgramme(t, degree.Id), aerial), me, Today, Eight, At));
        var a = Assert.Single(asked.SpaceAsks);
        Assert.Equal((new TimeOnly(10, 15), new TimeOnly(11, 45), "", (Guid?)aerial.Id), (a.Start, a.End, a.Title, a.ClassId));
        Assert.Equal((AudienceKind.Groups, (Guid?)null), (a.Audience.Kind, a.Audience.ProgrammeId));
        Assert.Equal(new[] { ba1.Id, ba2.Id }.Order(), a.Audience.GroupIds.Order());
        // One group is a group; the whole building is the whole building, whatever groups came with it.
        Assert.Equal(AudienceKind.Group, Do(t, Edits.AskForSpace(t, Ask(hall, Tuesday, "13:00", "14:00", Audience.ForGroups([ba2.Id, Guid.NewGuid()]), aerial), me, Today, Eight, At)).SpaceAsks.Single().Audience.Kind);
        var everyone = Do(t, Edits.AskForSpace(t, Ask(hall, Tuesday, "13:00", "14:00", Audience.Everyone with { GroupIds = [ba1.Id] }, aerial), me, Today, Eight, At)).SpaceAsks.Single();
        Assert.True(everyone.Audience.Equivalent(Audience.Everyone));

        // Renamed before the answer, the class added is called what it is called now, and counts as it.
        var renamed = Do(asked, Edits.PutClass(asked, aerial with { Name = "Aerial skills" }, Today));
        var booked = Do(renamed, Edits.AnswerSpaceAsk(renamed, a.Id, Answer.Yes, AskingReader.Administrator, Today, Eight, At));
        var s = booked.Sessions[booked.SpaceAsks[a.Id]!.SessionId!.Value]!;
        Assert.Equal(("Aerial skills", (Guid?)aerial.Id), (s.Title, s.ClassId));
        Assert.Contains("\"Aerial skills\"", Assert.Single(Asking.WhatAYesClashesWith(renamed, a)).Description);
        // A name of its own stays its own beside the class.
        var own = Do(t, Edits.AskForSpace(t, Ask(hall, Tuesday, "13:00", "14:00", null, aerial, "Aerial, extra"), me, Today, Eight, At));
        var o = own.SpaceAsks.Single();
        var ownBooked = Do(own, Edits.AnswerSpaceAsk(own, o.Id, Answer.Yes, AskingReader.Administrator, Today, Eight, At));
        Assert.Equal(("Aerial, extra", (Guid?)aerial.Id), (ownBooked.Sessions[ownBooked.SpaceAsks[o.Id]!.SessionId!.Value]!.Title, ownBooked.Sessions[ownBooked.SpaceAsks[o.Id]!.SessionId!.Value]!.ClassId));
    }

    [Fact]
    public void OnlyTheFacilitatorThemselvesAsksForATimeStillToComeInASpaceForSomebodyAndSaysWhatItIs()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var gone = b.Facilitator("Former");
        var hall = b.Room("Hall");
        var shut = b.Room("Old hall");
        var old = b.Class("Old class");
        var ba1 = b.Group("BA 1");
        b.T = b.T with
        {
            Facilitators = b.T.Facilitators.Put(gone with { FormerSince = Today }),
            Rooms = b.T.Rooms.Put(shut with { FormerSince = Today }),
            Classes = b.T.Classes.Put(old with { FormerSince = Today }),
        };
        var t = b.T;
        var me = AskingReader.Facilitator(ada.Id);
        var fine = Ask(hall, Tuesday, "10:00", "11:00", Audience.ForGroup(ba1.Id), title: "Rehearsal");
        Batch AskFor(SpaceAsk x, AskingReader? who = null, string now = "08:00") => Edits.AskForSpace(t, x, who ?? me, Today, TimeOnly.Parse(now), At);

        Refused(() => AskFor(fine, AskingReader.Nobody), "not linked");
        Refused(() => AskFor(fine, AskingReader.Administrator), "not linked");
        Refused(() => AskFor(fine, AskingReader.Facilitator(Guid.NewGuid())), "not linked");
        Refused(() => AskFor(fine, AskingReader.Facilitator(gone.Id)), "not linked");
        Refused(() => AskFor(fine with { End = new(10, 0) }), "finish after it starts");
        Refused(() => AskFor(fine with { End = new(10, 5) }), "finish after it starts");   // 10:05 snaps to 10:00
        Refused(() => AskFor(fine with { Date = Today.AddDays(-1) }), "time has gone");
        Refused(() => AskFor(fine with { Date = Today }, now: "10:00"), "time has gone");
        Refused(() => AskFor(fine with { Date = Today }, now: "10:30"), "time has gone");
        Refused(() => AskFor(fine with { Date = Week.ScratchMonday }), "time has gone");
        Refused(() => AskFor(fine with { RoomId = Guid.NewGuid() }), "Choose a space");
        Refused(() => AskFor(fine with { RoomId = shut.Id }), "Choose a space");
        Refused(() => AskFor(fine with { Audience = new Audience { Kind = AudienceKind.Group } }), "Say who it is for");
        Refused(() => AskFor(fine with { Audience = Audience.ForGroup(Guid.NewGuid()) }), "Say who it is for");
        Refused(() => AskFor(fine with { ClassId = old.Id }), "no longer the school's");
        Refused(() => AskFor(fine with { ClassId = Guid.NewGuid() }), "no longer the school's");
        Refused(() => AskFor(fine with { Title = "   " }), "Say what it is");

        // Today, at a time still to come, is fine; and whoever it says asked, it is Ada's own.
        var asked = Do(t, AskFor(fine with { Date = Today, FacilitatorId = gone.Id, Answer = Answer.Yes, SessionId = Guid.NewGuid() }, now: "09:45"));
        var a = Assert.Single(asked.SpaceAsks);
        Assert.Equal((ada.Id, (Answer?)null, (Guid?)null), (a.FacilitatorId, a.Answer, a.SessionId));
        Assert.Equal("Asked for Hall on " + Week.ShortDate(Tuesday), AskFor(fine).Label);
    }

    [Fact]
    public void WhoeverAskedTakesItBackWhileItWaitsAndOnlyAnAdministratorAnswersForATimeStillToCome()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var hall = b.Room("Hall");
        var cls = b.Class("Acrobatics");
        var ba1 = b.Group("BA 1");
        var t = b.T;
        var me = AskingReader.Facilitator(ada.Id);
        var asked = Do(t, Edits.AskForSpace(t, Ask(hall, Today, "10:00", "11:00", Audience.ForGroup(ba1.Id), cls), me, Today, Eight, At));
        var a = asked.SpaceAsks.Single();
        var admin = AskingReader.Administrator;
        Batch Yes(Timetable x, string now = "08:00", DateOnly? today = null, Guid? room = null) =>
            Edits.AnswerSpaceAsk(x, a.Id, Answer.Yes, admin, today ?? Today, TimeOnly.Parse(now), At, room);

        Refused(() => Edits.TakeBackSpaceAsk(asked, a.Id, AskingReader.Facilitator(bo.Id)), "Only whoever asked");
        Refused(() => Edits.TakeBackSpaceAsk(asked, a.Id, admin), "Only whoever asked");
        Refused(() => Edits.AnswerSpaceAsk(asked, a.Id, Answer.Yes, me, Today, Eight, At), "Only an administrator");
        Refused(() => Edits.AnswerSpaceAsk(asked, a.Id, Answer.No, AskingReader.Facilitator(bo.Id), Today, Eight, At), "Only an administrator");
        var taken = Do(asked, Edits.TakeBackSpaceAsk(asked, a.Id, me));
        Assert.Empty(taken.SpaceAsks);
        Refused(() => Edits.TakeBackSpaceAsk(taken, a.Id, me), "taken back already");
        Refused(() => Yes(taken), "taken back");

        // Once its time has started it has passed, waiting for nobody, and a yes is too late; a no is still a no.
        Assert.Equal([SpaceAskState.Waiting, SpaceAskState.Passed, SpaceAskState.Passed],
            new[] { "09:59", "10:00", "10:30" }.Select(n => Asking.StateOf(a, Today, TimeOnly.Parse(n))));
        Assert.Equal(SpaceAskState.Passed, Asking.StateOf(a, Today.AddDays(1), new(7, 0)));
        Assert.Empty(Asking.SpaceAsksWaiting(asked, Today, new(10, 0)));
        Refused(() => Yes(asked, "10:00"), "time has passed");
        Refused(() => Yes(asked, today: Today.AddDays(1)), "time has passed");
        Assert.Equal(SpaceAskState.NotThisTime, Asking.StateOf(Do(asked, Edits.AnswerSpaceAsk(asked, a.Id, Answer.No, admin, Today, new(10, 30), At)).SpaceAsks[a.Id]!, Today, new(10, 30)));
        Assert.Single(Do(asked, Yes(asked, "09:59")).Sessions);

        // What the yes needs has gone: whoever asked, the space (unless another is named), the class, the group.
        var formerAda = asked with { Facilitators = asked.Facilitators.Put(ada with { FormerSince = Today }) };
        Refused(() => Yes(formerAda), "no longer on this timetable");
        Assert.Empty(Asking.SpaceAsksWaiting(formerAda, Today, Eight));
        var shut = asked with { Rooms = asked.Rooms.Put(hall with { FormerSince = Today }) };
        Refused(() => Yes(shut), "no longer the school's");
        Refused(() => Yes(asked, room: Guid.NewGuid()), "no longer the school's");
        var studio = b.Room("Studio");
        var withStudio = shut with { Rooms = shut.Rooms.Put(studio) };
        Assert.Equal(studio.Id, Do(withStudio, Yes(withStudio, room: studio.Id)).Sessions.Single().RoomId);
        Refused(() => Yes(asked with { Classes = asked.Classes.Put(cls with { FormerSince = Today }) }), "Say no, and they can ask again");
        Refused(() => Yes(asked with { Groups = asked.Groups.Remove(ba1.Id) }), "The group it was for has gone");
    }

    [Fact]
    public void WaitingAsksComeSoonestFirstFromWhoeverIsStillOnTheTimetable()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var hall = b.Room("Hall");
        var t = b.T;
        var asks = new[]
        {
            (ada, Tuesday, "14:00", At.AddMinutes(1)),
            (bo, Tuesday, "09:00", At.AddMinutes(3)),
            (ada, Today, "16:00", At.AddMinutes(5)),
            (bo, Tuesday, "09:00", At.AddMinutes(2)),
        };
        foreach (var (who, day, from, at) in asks)
            t = Do(t, Edits.AskForSpace(t, Ask(hall, day, from, "17:00", title: "Practice"), AskingReader.Facilitator(who.Id), Today, Eight, at));
        Assert.Equal([(Today, "16:00", ada.Id), (Tuesday, "09:00", bo.Id), (Tuesday, "09:00", bo.Id), (Tuesday, "14:00", ada.Id)],
            Asking.SpaceAsksWaiting(t, Today, Eight).Select(a => (a.Date, Week.Hm(a.Start), a.FacilitatorId)));
        Assert.Equal([At.AddMinutes(2), At.AddMinutes(3)], Asking.SpaceAsksWaiting(t, Today, Eight).Where(a => a.FacilitatorId == bo.Id).Select(a => a.AskedAt));
        // Bo kept as former: his wait for nobody.
        var formerBo = t with { Facilitators = t.Facilitators.Put(bo with { FormerSince = Today }) };
        Assert.Equal([ada.Id, ada.Id], Asking.SpaceAsksWaiting(formerBo, Today, Eight).Select(a => a.FacilitatorId));
    }

    [Fact]
    public void WhatWasAskedGoesWithWhoeverAskedAndFollowsAGroupAClassOrAMergeWhileItWaits()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var hall = b.Room("Hall");
        var ba1 = b.Group("BA 1");
        var ba2 = b.Group("BA 2");
        var acro = b.Class("Acrobatics");
        var tumbling = b.Class("Tumbling");
        var t = b.T;
        SpaceAsk Asked(Facilitator who, Audience aud, Class? cls, string title = "")
        {
            var batch = Edits.AskForSpace(t, Ask(hall, Tuesday, "10:00", "11:00", aud, cls, title), AskingReader.Facilitator(who.Id), Today, Eight, At);
            t = Do(t, batch);
            return t.SpaceAsks[batch.Ops.Single().Id]!;
        }
        var both = Asked(ada, Audience.ForGroups([ba1.Id, ba2.Id]), acro);
        var onlyBa1 = Asked(ada, Audience.ForGroup(ba1.Id), null, "Rehearsal");
        var bos = Asked(bo, Audience.ForGroup(ba1.Id), acro);
        var answered = Asked(bo, Audience.ForGroups([ba1.Id, ba2.Id]), acro, "Acrobatics");
        t = Do(t, Edits.AnswerSpaceAsk(t, answered.Id, Answer.No, AskingReader.Administrator, Today, Eight, At));

        // Whoever asked has gone: what they asked goes, answered or not, and nobody else's.
        Assert.Equal(new[] { both.Id, onlyBa1.Id }.Order(), Do(t, Edits.RemoveFacilitator(t, bo.Id, Today)).SpaceAsks.Select(a => a.Id).Order());
        Assert.Equal(new[] { bos.Id, answered.Id }.Order(), Do(t, Edits.RemoveFacilitator(t, ada.Id, Today)).SpaceAsks.Select(a => a.Id).Order());

        // A group deleted: a waiting ask is for whoever is left, or for nobody (and a yes says so); answered, it stays as it was.
        var noBa1 = Do(t, Edits.RemoveGroup(t, ba1.Id, Today));
        Assert.Equal(AudienceKind.Group, noBa1.SpaceAsks[both.Id]!.Audience.Kind);
        Assert.Equal([ba2.Id], noBa1.SpaceAsks[both.Id]!.Audience.GroupIds);
        Assert.Empty(noBa1.SpaceAsks[onlyBa1.Id]!.Audience.GroupIds);
        Refused(() => Edits.AnswerSpaceAsk(noBa1, onlyBa1.Id, Answer.Yes, AskingReader.Administrator, Today, Eight, At), "The group it was for has gone");
        Assert.True(answered.Audience.Equivalent(noBa1.SpaceAsks[answered.Id]!.Audience));

        // A class deleted: a waiting ask is asked for by its name, as the classes from today on keep running.
        var noAcro = Do(t, Edits.RemoveClass(t, acro.Id, Today));
        Assert.Equal(((Guid?)null, "Acrobatics"), (noAcro.SpaceAsks[both.Id]!.ClassId, noAcro.SpaceAsks[both.Id]!.Title));
        Assert.Equal((Guid?)acro.Id, noAcro.SpaceAsks[answered.Id]!.ClassId);
        var granted = Do(noAcro, Edits.AnswerSpaceAsk(noAcro, both.Id, Answer.Yes, AskingReader.Administrator, Today, Eight, At));
        Assert.Equal(((Guid?)null, "Acrobatics"), (granted.Sessions.Single().ClassId, granted.Sessions.Single().Title));

        // Merged into another, every ask counts as the one kept, and one called by the merged name takes the kept one's.
        var merged = Do(t, Edits.MergeClass(t, acro.Id, tumbling.Id, Today));
        Assert.All(new[] { both.Id, bos.Id, answered.Id }, id => Assert.Equal((Guid?)tumbling.Id, merged.SpaceAsks[id]!.ClassId));
        Assert.Equal(("", "Tumbling", "Rehearsal"), (merged.SpaceAsks[both.Id]!.Title, merged.SpaceAsks[answered.Id]!.Title, merged.SpaceAsks[onlyBa1.Id]!.Title));
    }

    /// <summary>The made-up school has one waiting, for a time a yes runs into nothing.</summary>
    [Fact]
    public void TheSampleSchoolHasFinnAskingForStudioBNextThursdayAndAYesRunsIntoNothing()
    {
        var t = SampleSchool.Create(Monday, Monday);
        var finn = t.Facilitators.Single(f => f.Name == "Finn Aalborg-Reyes");
        var a = Assert.Single(Asking.SpaceAsksWaiting(t, Monday, Eight));
        Assert.Equal((finn.Id, Monday.AddDays(10), "Studio B", "Duo act rehearsal"), (a.FacilitatorId, a.Date, t.Rooms[a.RoomId]!.Name, a.Title));
        Assert.Empty(Asking.WhatAYesClashesWith(t, a));
        var yes = Do(t, Edits.AnswerSpaceAsk(t, a.Id, Answer.Yes, AskingReader.Administrator, Monday, Eight, At));
        Assert.Equal(Clashes.ForWeek(t, a.Date).Select(c => c.Description), Clashes.ForWeek(yes, a.Date).Select(c => c.Description));
        Assert.Equal(t.Sessions.Count + 1, yes.Sessions.Count);
    }

    [Fact]
    public void NextYearKeepsOnlyWhatStillWaitsForOneOfItsOwnDays()
    {
        var t = SampleSchool.Create(Monday, Monday);
        var plan = NextYear.For(t)!;
        var ada = t.Facilitators.Single(f => f.Name == "Ada Brightwater");
        var dag = t.Facilitators.Single(f => f.Name == "Dag Øverlie");
        var hall = t.Rooms.Single(r => r.Name == "Big Top");
        SpaceAsk On(Facilitator f, DateOnly d, Answer? answer = null) =>
            new() { FacilitatorId = f.Id, RoomId = hall.Id, Date = d, Start = new(10, 0), End = new(11, 0), Title = "Practice", AskedAt = At, Answer = answer };
        var september = On(ada, plan.Starts.AddDays(2));
        var answered = On(ada, plan.Starts.AddDays(3), Answer.No);
        var thisYear = On(ada, plan.Starts.AddDays(-1));
        var dags = On(dag, plan.Starts.AddDays(2));
        var withAsks = t with
        {
            SpaceAsks = EntitySet<SpaceAsk>.Of([september, answered, thisYear, dags]),
            Facilitators = t.Facilitators.Put(dag with { FormerSince = Monday }),
        };
        var next = NextYear.Make(withAsks, "Next", keepClosures: false);
        Assert.Equal([september.Id], next.SpaceAsks.Select(a => a.Id));
    }
}
