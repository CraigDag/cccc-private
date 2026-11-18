using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// One person for everything somebody being away leaves needing cover (<see cref="CoverAll"/>, <see cref="Edits.CoverAll"/>):
/// who could take it, best first, and one press giving them all of it they are free for, as one Undo.
/// </summary>
public class CoverAllTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 4, 7, 30, 0, TimeSpan.Zero);
    private static readonly TimeOnly Early = new(7, 30);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);
    private static readonly DateOnly Wednesday = Monday.AddDays(2);
    private static readonly DateOnly Thursday = Monday.AddDays(3);

    private static void Refused(Func<Batch> edit, string words) =>
        Assert.Contains(words, Assert.Throws<EditRefused>(() => edit()).Message);

    // The week's classes handed over last first, so nothing here leans on the order they come in.
    private static IReadOnlyList<Occurrence> Week(Build b) => [.. Occurrences.ForWeek(b.T, Monday).Reverse()];

    private static IReadOnlyList<AwayStretch> Stretches(Build b) => CoverAll.Stretches(b.T, Week(b), Today, Early);

    private static void AwayMondayToWednesday(Build b, Facilitator f)
    {
        b.T = b.T with
        {
            Absences = b.T.Absences.Put(new Absence { Person = AbsentPerson.Facilitator, PersonId = f.Id, From = Monday, To = Wednesday }),
        };
    }

    [Fact]
    public void EachPersonsClassesNeedingCoverAreGatheredInOrderAndOneAloneOrOneStartedIsLeftToItsOwnCard()
    {
        var b = new Build();
        var esme = b.Facilitator("Esme");
        var aage = b.Facilitator("Aage");
        var ada = b.Facilitator("Ada");
        var finn = b.Facilitator("Finn");
        var strength = b.Class("Strength");
        var lab = b.Class("Creation lab");
        var wed = b.Session(Wednesday, "09:00", "10:30", null, null, strength, In(esme));
        var mon = b.Session(Monday, "09:00", "10:30", null, null, strength, In(esme));
        var tue = b.Session(Tuesday, "13:00", "16:00", null, null, lab, In(esme));
        b.Session(Tuesday, "09:00", "10:00", null, null, lab, In(esme), In(finn));       // Finn still teaches it
        b.Session(Monday, "07:00", "08:00", null, null, strength, In(esme));             // started by half past seven
        b.Session(Monday, "07:30", "08:30", null, null, strength, In(esme));             // starting as it is asked
        var aageMon = b.Session(Monday, "11:00", "12:00", null, null, strength, In(aage));
        var aageThu = b.Session(Thursday, "11:00", "12:00", null, null, lab, In(aage));
        b.Session(Monday, "13:00", "14:00", null, null, strength, In(ada));              // one class alone
        AwayMondayToWednesday(b, esme);
        b.Away(aage, Monday);
        b.Away(aage, Thursday);
        b.Away(ada, Monday);

        var stretches = Stretches(b);
        // By name in Danish, Esme before Aage (Aa is Å, after Z), though Aage's Thursday comes first in the list.
        Assert.Equal([esme.Id, aage.Id], stretches.Select(s => s.AwayId));
        Assert.Equal([mon.Id, tue.Id, wed.Id], stretches[0].Gaps.Select(o => o.Session.Id));
        Assert.Equal([aageMon.Id, aageThu.Id], stretches[1].Gaps.Select(o => o.Session.Id));
        // At 11:00 Monday's have started, leaving Aage one; on Tuesday at 13:00, each of them one, a day gone not counted.
        Assert.Equal([esme.Id], CoverAll.Stretches(b.T, Week(b), Today, new TimeOnly(11, 0)).Select(s => s.AwayId));
        Assert.Empty(CoverAll.Stretches(b.T, Week(b), Tuesday, new TimeOnly(13, 0)));
    }

    [Fact]
    public void WhoeverIsFreeForMoreOfItComesFirstAndSaysWhatKeepsThemFromTheRest()
    {
        var b = new Build();
        var esme = b.Facilitator("Esme");
        var cleo = b.Facilitator("Cleo");
        var dag = b.Facilitator("Dag");
        var bo = b.Facilitator("Bo");
        var gus = b.Facilitator("Gus");
        b.T = b.T with { Facilitators = b.T.Facilitators.Put(gus with { IsGuest = true }) };
        var hal = b.Facilitator("Hal");
        var strength = b.Class("Strength");
        var lab = b.Class("Creation lab");
        var mon = b.Session(Monday, "09:00", "10:30", null, null, strength, In(esme));
        var tue = b.Session(Tuesday, "13:00", "16:00", null, null, lab, In(esme));
        var wed = b.Session(Wednesday, "09:00", "10:30", null, null, strength, In(esme));
        b.Session(Tuesday, "13:00", "15:00", null, null, b.Class("Juggling"), In(dag));   // Dag teaches then
        b.Session(Monday, "09:00", "10:30", null, null, lab, In(hal));                     // Hal teaches at all three
        b.Session(Tuesday, "13:00", "16:00", null, null, lab, In(hal));
        b.Session(Wednesday, "09:00", "10:30", null, null, lab, In(hal));
        AwayMondayToWednesday(b, esme);
        b.Away(bo, Wednesday);

        var ranked = CoverAll.Ranked(b.T, Assert.Single(Stretches(b)), Today);
        // Cleo is free for all three. Bo and Dag for two: Bo's week is lighter. The guest last; Hal free for none.
        Assert.Equal(["Cleo", "Bo", "Dag", "Gus"], ranked.Select(c => c.Person.Name));
        Assert.Equal([mon.Id, tue.Id, wed.Id], ranked[0].Free.Select(o => o.Session.Id));
        Assert.Empty(ranked[0].Busy);
        Assert.Equal([mon.Id, tue.Id], ranked[1].Free.Select(o => o.Session.Id));
        Assert.Empty(ranked[1].Busy);                                                       // away for Wednesday's, not busy
        Assert.Equal([mon.Id, wed.Id], ranked[2].Free.Select(o => o.Session.Id));
        Assert.Equal([tue.Id], ranked[2].Busy.Select(o => o.Session.Id));
        Assert.Equal(TimeSpan.FromHours(2), ranked[2].Week);
        Assert.Equal(TimeSpan.Zero, ranked[1].Week);
    }

    [Fact]
    public void TiesGoByNameInDanishOrder()
    {
        var b = new Build();
        var esme = b.Facilitator("Esme");
        var aase = b.Facilitator("Aase");
        var zeb = b.Facilitator("Zeb");
        var strength = b.Class("Strength");
        var juggling = b.Class("Juggling");
        b.Session(Monday, "09:00", "10:00", null, null, strength, In(esme));
        b.Session(Tuesday, "09:00", "10:00", null, null, strength, In(esme));
        b.Session(Wednesday, "09:00", "10:00", null, null, strength, In(esme));
        b.Session(Monday, "09:00", "10:00", null, null, juggling, In(zeb));                   // busy for the first
        b.Session(Wednesday, "09:00", "10:00", null, null, juggling, In(aase));               // busy for the last
        AwayMondayToWednesday(b, esme);
        // Free for two of three each, with the same week: Aa is Å, after Z, though Aase comes first for Monday's.
        Assert.Equal(["Zeb", "Aase"], CoverAll.Ranked(b.T, Assert.Single(Stretches(b)), Today).Select(c => c.Person.Name));
    }

    [Fact]
    public void AnOfferComesBeforeKnowingTheClassAndKnowingItBeforeALighterWeek()
    {
        var b = new Build();
        var esme = b.Facilitator("Esme");
        var ada = b.Facilitator("Ada");
        var cleo = b.Facilitator("Cleo");
        var finn = b.Facilitator("Finn");
        var ida = b.Facilitator("Ida");
        var strength = b.Class("Strength");
        var juggling = b.Class("Juggling");
        var mon = b.Session(Monday, "09:00", "10:30", null, null, strength, In(esme));
        var tue = b.Session(Tuesday, "09:00", "10:30", null, null, strength, In(esme));
        AwayMondayToWednesday(b, esme);
        b.Session(Thursday, "13:00", "15:00", null, null, juggling, In(finn));                 // Finn's week: 2h
        b.Session(Thursday, "09:00", "12:00", null, null, juggling, In(ida));                  // Ida's: 3h
        b.T = b.T with { Offers = b.T.Offers.Put(new CoverOffer { SessionId = tue.Id, FacilitatorId = ida.Id, OfferedAt = At }) };
        // Finn in the room for Strength before today: twice his own, once covering. Not when it was called off,
        // not when he was away, not for another class, and not next week.
        b.Session(Monday.AddDays(-7), "09:00", "10:30", null, null, strength, In(finn));
        b.Session(Monday.AddDays(-6), "09:00", "10:30", null, null, strength, In(finn));
        var covered = b.Session(Monday.AddDays(-5), "09:00", "10:30", null, null, strength, In(ada));
        b.Put(covered with { CoverIds = [finn.Id] });
        b.Away(ada, Monday.AddDays(-5));
        var off = b.Session(Monday.AddDays(-4), "09:00", "10:30", null, null, strength, In(finn));
        b.Put(off with { Cancelled = true });
        b.Session(Monday.AddDays(-3), "09:00", "10:30", null, null, strength, In(finn));
        b.Away(finn, Monday.AddDays(-3));
        b.Session(Monday.AddDays(-2), "09:00", "10:30", null, null, juggling, In(finn));
        b.Session(Monday, "13:00", "14:00", null, null, strength, In(finn));                    // today: not yet taught
        b.Session(Monday.AddDays(7), "09:00", "10:30", null, null, strength, In(finn));
        b.Session(Monday.AddDays(-7), "11:00", "12:00", null, null, strength, In(ada));         // Ada's own, once

        var ranked = CoverAll.Ranked(b.T, Assert.Single(Stretches(b)), Today);
        Assert.Equal(["Ida", "Finn", "Ada", "Cleo"], ranked.Select(c => c.Person.Name));
        Assert.True(ranked[0].Offered);
        Assert.Equal([strength.Id], ranked[1].Taught);
        Assert.Equal(3, ranked[1].Times);
        Assert.Equal(1, ranked[2].Times);                                                     // not the class Finn covered for her
        Assert.Empty(ranked[3].Taught);
        Assert.False(ranked[3].Offered);
        _ = mon;
    }

    [Fact]
    public void AClassAtTheSameTimeAsOneTheyWouldTakeIsBusyForThemSoTheCountIsWhatOnePressGives()
    {
        var b = new Build();
        var esme = b.Facilitator("Esme");
        var cleo = b.Facilitator("Cleo");
        var strength = b.Class("Strength");
        var a = b.Session(Monday, "09:00", "10:00", null, null, strength, In(esme));
        var twin = b.Session(Monday, "09:00", "10:00", null, null, strength, In(esme));          // Esme in two at once
        var overlapping = b.Session(Monday, "09:30", "10:30", null, null, strength, In(esme));
        var after = b.Session(Monday, "10:00", "11:00", null, null, strength, In(esme));       // starts as the first ends
        var tue = b.Session(Tuesday, "09:30", "10:30", null, null, strength, In(esme));        // the same time, another day
        AwayMondayToWednesday(b, esme);
        // Of two at the same time, the one first in the stretch's order is the one taken, by the card and the press alike.
        var (first, other) = a.Id.CompareTo(twin.Id) < 0 ? (a, twin) : (twin, a);

        var stretch = Assert.Single(Stretches(b));
        var c = Assert.Single(CoverAll.Ranked(b.T, stretch, Today));
        Assert.Equal([first.Id, after.Id, tue.Id], c.Free.Select(o => o.Session.Id));
        Assert.Equal([other.Id, overlapping.Id], c.Busy.Select(o => o.Session.Id));

        var asked = stretch.Gaps.Select(o => o.Session.Id).Reverse();
        var t = Apply.Batch(b.T, Edits.CoverAll(b.T, esme.Id, cleo.Id, asked, AskingReader.Nobody, At));
        Assert.Equal([cleo.Id], t.Sessions[first.Id]!.CoverIds);
        Assert.Equal([cleo.Id], t.Sessions[after.Id]!.CoverIds);
        Assert.Equal([cleo.Id], t.Sessions[tue.Id]!.CoverIds);
        Assert.True(Occurrences.Resolve(t, t.Sessions[other.Id]!).NeedsCover);
        Assert.True(Occurrences.Resolve(t, t.Sessions[overlapping.Id]!).NeedsCover);
    }

    [Fact]
    public void TakingItCanGoOverTheirLimitWhichIsSaidNotRefused()
    {
        var b = new Build();
        var esme = b.Facilitator("Esme");
        var cleo = b.Facilitator("Cleo", max: 4);
        var dag = b.Facilitator("Dag", max: 5);
        b.Facilitator("Bo");
        var strength = b.Class("Strength");
        b.Session(Monday, "09:00", "10:30", null, null, strength, In(esme));
        b.Session(Tuesday, "13:00", "16:00", null, null, strength, In(esme));
        b.Session(Thursday, "09:00", "10:00", null, null, strength, In(cleo));
        b.Session(Thursday, "11:00", "11:30", null, null, strength, In(dag));
        AwayMondayToWednesday(b, esme);

        var ranked = CoverAll.Ranked(b.T, Assert.Single(Stretches(b)), Today).ToDictionary(c => c.Person.Name);
        Assert.Equal(TimeSpan.FromHours(4.5), ranked["Cleo"].Taking);
        Assert.True(ranked["Cleo"].WouldBeOverLimit);                                         // 1h + 4h 30m over 4h
        Assert.False(ranked["Dag"].WouldBeOverLimit);                                         // 30m + 4h 30m is 5h exactly
        Assert.False(ranked["Bo"].WouldBeOverLimit);                                          // no limit
    }

    [Fact]
    public void OnePressGivesThemEveryClassTheyAreFreeForAsOneStepAndLeavesTheRestNeedingCover()
    {
        var b = new Build();
        var esme = b.Facilitator("Esme");
        var dag = b.Facilitator("Dag");
        var strength = b.Class("Strength");
        var mon = b.Session(Monday, "09:00", "10:30", null, null, strength, In(esme));
        var tue = b.Session(Tuesday, "13:00", "16:00", null, null, strength, In(esme));
        var wed = b.Session(Wednesday, "09:00", "10:30", null, null, strength, In(esme));
        b.Session(Tuesday, "13:00", "15:00", null, null, b.Class("Juggling"), In(dag));
        b.Put(b.T.Sessions[wed.Id]! with { CoverNote = "Ask the office" });
        AwayMondayToWednesday(b, esme);

        var batch = Edits.CoverAll(b.T, esme.Id, dag.Id, [wed.Id, mon.Id, tue.Id, mon.Id], AskingReader.Nobody, At);
        Assert.Equal("Dag covering 2 of Esme's classes", batch.Label);
        Assert.Equal(2, batch.Ops.Count);
        var t = Apply.Batch(b.T, batch);
        Assert.True(Occurrences.Resolve(t, t.Sessions[mon.Id]!).Covered);
        Assert.Equal([dag.Id], t.Sessions[wed.Id]!.CoverIds);
        Assert.Null(t.Sessions[wed.Id]!.CoverNote);
        Assert.True(Occurrences.Resolve(t, t.Sessions[tue.Id]!).NeedsCover);
    }

    [Fact]
    public void WhatHasCoverSinceOrIsNotTheirsIsLeftAloneAndNothingLeftIsRefused()
    {
        var b = new Build();
        var esme = b.Facilitator("Esme");
        var bo = b.Facilitator("Bo");
        var cleo = b.Facilitator("Cleo");
        var dag = b.Facilitator("Dag");
        var strength = b.Class("Strength");
        var mon = b.Session(Monday, "09:00", "10:30", null, null, strength, In(esme));
        var tue = b.Session(Tuesday, "13:00", "16:00", null, null, strength, In(esme));
        var bos = b.Session(Wednesday, "13:00", "14:00", null, null, strength, In(bo));
        AwayMondayToWednesday(b, esme);
        b.Away(bo, Wednesday);
        var covered = Apply.Batch(b.T, Edits.SetCover(b.T, mon.Id, [cleo.Id], null));        // covered since the card was drawn

        var batch = Edits.CoverAll(covered, esme.Id, dag.Id, [mon.Id, tue.Id, bos.Id, Guid.NewGuid()], AskingReader.Nobody, At);
        Assert.Equal("Dag covering Strength", batch.Label);
        var t = Apply.Batch(covered, batch);
        Assert.Equal([cleo.Id], t.Sessions[mon.Id]!.CoverIds);
        Assert.Equal([dag.Id], t.Sessions[tue.Id]!.CoverIds);
        Assert.Empty(t.Sessions[bos.Id]!.CoverIds);                                           // Bo's, not Esme's

        Refused(() => Edits.CoverAll(t, esme.Id, dag.Id, [mon.Id, tue.Id], AskingReader.Nobody, At), "None of it is left for Dag");
        var gone = t with { Facilitators = t.Facilitators.Put(t.Facilitators[cleo.Id]! with { FormerSince = Monday }) };
        Refused(() => Edits.CoverAll(gone, bo.Id, cleo.Id, [bos.Id], AskingReader.Nobody, At), "no longer on this timetable");
    }

    [Fact]
    public void TheirOwnOffersAreAnsweredYesWhenTheReaderAnswersOffersAndSomebodyElsesAreLeftAsTheyWere()
    {
        var b = new Build();
        var esme = b.Facilitator("Esme");
        var cleo = b.Facilitator("Cleo");
        var dag = b.Facilitator("Dag");
        var strength = b.Class("Strength");
        var mon = b.Session(Monday, "09:00", "10:30", null, null, strength, In(esme));
        var tue = b.Session(Tuesday, "13:00", "16:00", null, null, strength, In(esme));
        AwayMondayToWednesday(b, esme);
        var hers = new CoverOffer { SessionId = tue.Id, FacilitatorId = cleo.Id, OfferedAt = At.AddDays(-1) };
        var his = new CoverOffer { SessionId = tue.Id, FacilitatorId = dag.Id, OfferedAt = At.AddDays(-1) };
        var declined = new CoverOffer { SessionId = mon.Id, FacilitatorId = cleo.Id, OfferedAt = At.AddDays(-2), Answer = Answer.No, AnsweredAt = At.AddDays(-1) };
        var thursday = b.Session(Thursday, "09:00", "10:00", null, null, strength, In(dag));
        b.Away(dag, Thursday);
        var elsewhere = new CoverOffer { SessionId = thursday.Id, FacilitatorId = cleo.Id, OfferedAt = At.AddDays(-1) };
        b.T = b.T with { Offers = b.T.Offers.Put(hers).Put(his).Put(declined).Put(elsewhere) };

        var answered = Apply.Batch(b.T, Edits.CoverAll(b.T, esme.Id, cleo.Id, [mon.Id, tue.Id], AskingReader.Administrator, At));
        Assert.Equal(Answer.Yes, answered.Offers[hers.Id]!.Answer);
        Assert.Equal(At, answered.Offers[hers.Id]!.AnsweredAt);
        Assert.Null(answered.Offers[his.Id]!.Answer);
        Assert.Equal(declined, answered.Offers[declined.Id]);                                 // answered once already
        Assert.Null(answered.Offers[elsewhere.Id]!.Answer);                                   // for a class not taken
        Assert.Equal(OfferState.NotNeeded, Asking.StateOf(answered, answered.Offers[his.Id]!, Today));

        // An editor, who does not answer offers, makes the same cover, and her offer reads as covering all the same.
        var editor = Apply.Batch(b.T, Edits.CoverAll(b.T, esme.Id, cleo.Id, [mon.Id, tue.Id], AskingReader.Nobody, At));
        Assert.Null(editor.Offers[hers.Id]!.Answer);
        Assert.Equal(OfferState.Covering, Asking.StateOf(editor, editor.Offers[hers.Id]!, Today));
        Assert.Equal([cleo.Id], editor.Sessions[mon.Id]!.CoverIds);
    }
}
