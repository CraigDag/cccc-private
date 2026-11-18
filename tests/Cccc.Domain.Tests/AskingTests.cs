using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// What facilitators ask: that they can't come, and to cover a class that needs it. Nothing on the timetable
/// changes until an administrator says yes, and then exactly what the forms would record.
/// </summary>
public class AskingTests
{
    private static readonly DateTimeOffset At = new(2030, 3, 4, 7, 30, 0, TimeSpan.Zero);
    private static readonly DateOnly Tuesday = Monday.AddDays(1);

    private static Timetable Do(Timetable t, Batch b) => Apply.Batch(t, b);

    private static void Refused(Func<Batch> edit, string words) =>
        Assert.Contains(words, Assert.Throws<EditRefused>(() => edit()).Message);

    [Fact]
    public void AskingToBeAwayChangesNothingUntilAnAdministratorSaysYesWhichRecordsTheAbsenceItAskedFor()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var acro = b.Session(Tuesday, "10:00", "11:00", null, null, b.Class("Acrobatics"), In(ada));
        var shared = b.Session(Tuesday, "13:00", "15:00", null, null, b.Class("Creation lab"), In(ada), In(bo));
        b.Session(Tuesday, "16:00", "17:00", null, null, b.Class("Juggling"), In(bo));
        var before = b.T;

        var asked = Do(before, Edits.AskToBeAway(before, Tuesday, Tuesday, null, null, AskingReader.Facilitator(ada.Id), Today, At));
        var r = Assert.Single(asked.Requests);
        Assert.Equal((ada.Id, Tuesday, Tuesday, (TimeOnly?)null, (TimeOnly?)null, Today, At), (r.FacilitatorId, r.From, r.To, r.StartTime, r.EndTime, r.AskedOn, r.AskedAt));
        Assert.Equal(RequestState.Waiting, Asking.StateOf(r, Today));
        Assert.Equal([r.Id], Asking.RequestsWaiting(asked, Today).Select(x => x.Id));
        // Nothing that counts absences can see it: the class still runs, nothing needs cover, the hours are as they were.
        Assert.Empty(asked.Absences);
        Assert.Equal(OccurrenceState.Runs, Occurrences.Resolve(asked, acro).State);
        Assert.Empty(Cover.NeededInWeek(asked, Monday));
        Assert.Equal(Hours.For(before, Occurrences.HappeningInWeek(before, Monday), ada.Id).Total, Hours.For(asked, Occurrences.HappeningInWeek(asked, Monday), ada.Id).Total);
        Assert.True(Hours.For(asked, Occurrences.HappeningInWeek(asked, Monday), ada.Id).Total > TimeSpan.Zero);

        // What a yes would leave, said before it is: Acrobatics needing cover, the creation lab still Bo's.
        Assert.Equal([(acro.Id, true), (shared.Id, false)], Asking.WhatAYesLeaves(asked, r).Select(x => (x.Now.Session.Id, x.After.NeedsCover)));

        var yes = Edits.AnswerRequest(asked, r.Id, Answer.Yes, AskingReader.Administrator, At.AddHours(1));
        var recorded = Do(asked, yes);
        var away = Assert.Single(recorded.Absences);
        Assert.Equal((AbsentPerson.Facilitator, ada.Id, Tuesday, Tuesday, (TimeOnly?)null, (TimeOnly?)null, (AbsenceNotice?)AbsenceNotice.Planned),
            (away.Person, away.PersonId, away.From, away.To, away.StartTime, away.EndTime, away.Notice));
        Assert.Null(away.Reason);
        Assert.Null(away.Cause);
        var answered = recorded.Requests[r.Id]!;
        Assert.Equal((Answer.Yes, away.Id, At.AddHours(1), RequestState.Recorded), (answered.Answer!.Value, answered.AbsenceId!.Value, answered.AnsweredAt!.Value, Asking.StateOf(answered, Today)));
        Assert.Equal([acro.Id], Cover.NeededInWeek(recorded, Monday).Select(o => o.Session.Id));
        Assert.Empty(Asking.RequestsWaiting(recorded, Today));
        Refused(() => Edits.AnswerRequest(recorded, r.Id, Answer.No, AskingReader.Administrator, At), "answered already");

        // Undone, it waits again and nobody is away.
        var undone = Do(recorded, yes.Inverse());
        Assert.Empty(undone.Absences);
        Assert.Equal(RequestState.Waiting, Asking.StateOf(undone.Requests[r.Id]!, Today));

        // No changes nothing on the timetable; the request says so.
        var no = Do(asked, Edits.AnswerRequest(asked, r.Id, Answer.No, AskingReader.Administrator, At));
        Assert.Empty(no.Absences);
        Assert.Equal(RequestState.NotThisTime, Asking.StateOf(no.Requests[r.Id]!, Today));
        Assert.Empty(Asking.RequestsWaiting(no, Today));
        Assert.Equal(asked with { Requests = no.Requests }, no);

        // Asked on the day itself it is unplanned; and a request whose last day has passed waits for nobody.
        var onTheDay = Do(before, Edits.AskToBeAway(before, Today, Today, null, null, AskingReader.Facilitator(ada.Id), Today, At));
        var single = Assert.Single(onTheDay.Requests);
        Assert.Equal(AbsenceNotice.Unplanned, single.AsAbsence(Guid.NewGuid()).Notice);
        Assert.Equal(RequestState.Passed, Asking.StateOf(single, Today.AddDays(1)));
        Assert.Empty(Asking.RequestsWaiting(onTheDay, Today.AddDays(1)));
    }

    [Fact]
    public void ItTakesThemOffOnlyTheClassesItsTimesReachTheirPartOfCoveringIncluded()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var dag = b.Facilitator("Dag");
        var acro = b.Session(Tuesday, "10:00", "11:00", null, null, b.Class("Acrobatics"), In(ada));
        // Ada is in the creation lab from 14:00 only, Bo all of it.
        var lab = b.Session(Tuesday, "13:00", "15:00", null, null, b.Class("Creation lab"), In(bo), In(ada, from: "14:00"));
        // Ada covers Dag's juggling.
        var juggling = b.Session(Tuesday, "15:30", "16:30", null, null, b.Class("Juggling"), In(dag));
        b.Put(juggling with { CoverIds = [ada.Id] });
        b.Away(dag, Tuesday);
        var cancelled = b.Session(Tuesday, "17:00", "18:00", null, null, null, In(ada));
        b.Put(cancelled with { Cancelled = true });
        b.Session(Tuesday.AddDays(1), "10:00", "11:00", null, null, null, In(ada));   // another day

        TimeOffRequest Ask(string? from, string? until) => new()
        {
            FacilitatorId = ada.Id, From = Tuesday, To = Tuesday, AskedOn = Today,
            StartTime = from is null ? null : TimeOnly.Parse(from), EndTime = until is null ? null : TimeOnly.Parse(until),
        };
        Assert.Equal([acro.Id, lab.Id, juggling.Id], Asking.TakesOff(b.T, Ask(null, null)).Select(o => o.Session.Id));
        // 10:30–13:45 reaches Acrobatics, and not the creation lab, whose 13:00–14:00 is not Ada's.
        Assert.Equal([acro.Id], Asking.TakesOff(b.T, Ask("10:30", "13:45")).Select(o => o.Session.Id));
        Assert.Equal([lab.Id], Asking.TakesOff(b.T, Ask("14:30", "15:00")).Select(o => o.Session.Id));
        Assert.Empty(Asking.TakesOff(b.T, Ask("11:00", "13:00")));
        // A yes leaves the class she covers running on paper, so it is said as hers to change; Bo still has all of the lab.
        var leaves = Asking.WhatAYesLeaves(b.T, Ask(null, null));
        Assert.Equal([true, false, false], leaves.Select(x => x.After.NeedsCover));
        Assert.Contains(ada.Id, leaves[2].Now.Session.CoverIds);
        Assert.Contains(bo.Id, leaves[1].After.InTheRoom);
    }

    [Fact]
    public void OnlyTheFacilitatorThemselvesAsksFromTodayOnForWholeDaysOrBetweenTwoTimes()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var gone = b.Facilitator("Former") ;
        b.T = b.T with { Facilitators = b.T.Facilitators.Put(gone with { FormerSince = Today }) };
        var t = b.T;
        var me = AskingReader.Facilitator(ada.Id);
        Refused(() => Edits.AskToBeAway(t, Tuesday, Tuesday, null, null, AskingReader.Nobody, Today, At), "not linked");
        Refused(() => Edits.AskToBeAway(t, Tuesday, Tuesday, null, null, AskingReader.Administrator, Today, At), "not linked");
        Refused(() => Edits.AskToBeAway(t, Tuesday, Tuesday, null, null, AskingReader.Facilitator(Guid.NewGuid()), Today, At), "not linked");
        Refused(() => Edits.AskToBeAway(t, Tuesday, Tuesday, null, null, AskingReader.Facilitator(gone.Id), Today, At), "not linked");
        Refused(() => Edits.AskToBeAway(t, Today.AddDays(-1), Tuesday, null, null, me, Today, At), "has gone");
        Refused(() => Edits.AskToBeAway(t, Tuesday, Today, null, null, me, Today, At), "on or after");
        Refused(() => Edits.AskToBeAway(t, Tuesday, Tuesday, new(9, 0), null, me, Today, At), "both times");
        Refused(() => Edits.AskToBeAway(t, Tuesday, Tuesday, null, new(9, 0), me, Today, At), "both times");
        Refused(() => Edits.AskToBeAway(t, Tuesday, Tuesday, new(9, 0), new(9, 0), me, Today, At), "finish after");
        // Today itself, and a stretch of days between two times, are both fine; each is Ada's own.
        var asked = Do(t, Edits.AskToBeAway(t, Today, Today.AddDays(2), new(9, 0), new(12, 0), me, Today, At));
        var r = Assert.Single(asked.Requests);
        Assert.Equal((ada.Id, Today, Today.AddDays(2), (TimeOnly?)new TimeOnly(9, 0), (TimeOnly?)new TimeOnly(12, 0)), (r.FacilitatorId, r.From, r.To, r.StartTime, r.EndTime));
        // A stretch of days waits until its last day has gone, not its first: the rest of it is still ahead.
        Assert.Equal([RequestState.Waiting, RequestState.Waiting, RequestState.Passed], new[] { 1, 2, 3 }.Select(n => Asking.StateOf(r, Today.AddDays(n))));
        Assert.Equal([r.Id], Asking.RequestsWaiting(asked, Today.AddDays(2)).Select(x => x.Id));

        // Taken back by Ada only, and only while nobody has answered; answered by an administrator only.
        Refused(() => Edits.TakeBackRequest(asked, r.Id, AskingReader.Facilitator(bo.Id)), "Only whoever asked");
        Refused(() => Edits.TakeBackRequest(asked, r.Id, AskingReader.Administrator), "Only whoever asked");
        Refused(() => Edits.AnswerRequest(asked, r.Id, Answer.Yes, me, At), "Only an administrator");
        Refused(() => Edits.AnswerRequest(asked, r.Id, Answer.Yes, AskingReader.Facilitator(bo.Id), At), "Only an administrator");
        Assert.Empty(Do(asked, Edits.TakeBackRequest(asked, r.Id, me)).Requests);
        var no = Do(asked, Edits.AnswerRequest(asked, r.Id, Answer.No, AskingReader.Administrator, At));
        Refused(() => Edits.TakeBackRequest(no, r.Id, me), "answered it already");
        // Whoever asked has gone from the timetable: there is nobody to record as away.
        var left = Do(asked, Edits.RemoveFacilitator(asked, ada.Id, Today));
        Assert.Empty(left.Requests);
        Refused(() => Edits.AnswerRequest(asked with { Facilitators = asked.Facilitators.Put(ada with { FormerSince = Today }) }, r.Id, Answer.Yes, AskingReader.Administrator, At), "no longer on this timetable");
    }

    [Fact]
    public void AnOfferToCoverIsForAClassNeedingItThatIsNotTheirsAndAYesMakesThemItsCover()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var cleo = b.Facilitator("Cleo");
        var acro = b.Session(Tuesday, "10:00", "11:00", null, null, b.Class("Acrobatics"), In(ada));
        var juggling = b.Session(Tuesday, "13:00", "14:00", null, null, b.Class("Juggling"), In(bo));
        var past = b.Session(Monday, "10:00", "11:00", null, null, null, In(ada));
        b.Away(ada, Tuesday);
        b.Away(ada, Monday);
        var t = b.T;
        var asBo = AskingReader.Facilitator(bo.Id);

        Refused(() => Edits.OfferCover(t, acro.Id, AskingReader.Nobody, Today, At), "not linked");
        Refused(() => Edits.OfferCover(t, acro.Id, AskingReader.Facilitator(ada.Id), Today, At), "yours already");
        Refused(() => Edits.OfferCover(t, juggling.Id, AskingReader.Facilitator(cleo.Id), Today, At), "has cover now");
        Refused(() => Edits.OfferCover(t, past.Id, asBo, Tuesday, At), "has been");
        Refused(() => Edits.OfferCover(t, Guid.NewGuid(), asBo, Today, At), "has gone");

        var offered = Do(t, Edits.OfferCover(t, acro.Id, asBo, Today, At));
        var bos = Assert.Single(offered.Offers);
        Assert.Equal((acro.Id, bo.Id, At, OfferState.Waiting), (bos.SessionId, bos.FacilitatorId, bos.OfferedAt, Asking.StateOf(offered, bos, Today)));
        Refused(() => Edits.OfferCover(offered, acro.Id, asBo, Today, At), "offered to cover it already");
        // Offering changes nothing on the timetable.
        Assert.Equal(t with { Offers = offered.Offers }, offered);
        offered = Do(offered, Edits.OfferCover(offered, acro.Id, AskingReader.Facilitator(cleo.Id), Today, At.AddMinutes(5)));
        var cleos = offered.Offers.Single(o => o.FacilitatorId == cleo.Id);
        Assert.Equal([bos.Id, cleos.Id], Asking.OffersWaiting(offered, Today).Select(o => o.Id));

        Refused(() => Edits.AnswerOffer(offered, bos.Id, Answer.Yes, asBo, Today, At), "Only an administrator");
        Refused(() => Edits.TakeBackOffer(offered, bos.Id, AskingReader.Facilitator(cleo.Id)), "Only whoever offered");
        Assert.Single(Do(offered, Edits.TakeBackOffer(offered, bos.Id, asBo)).Offers);

        var yes = Do(offered, Edits.AnswerOffer(offered, bos.Id, Answer.Yes, AskingReader.Administrator, Today, At));
        Assert.Equal([bo.Id], yes.Sessions[acro.Id]!.CoverIds);
        Assert.Equal(OccurrenceState.Runs, Occurrences.Resolve(yes, yes.Sessions[acro.Id]!).State);
        Assert.Equal(OfferState.Covering, Asking.StateOf(yes, yes.Offers[bos.Id]!, Today));
        // Cleo's offer is not needed now, and waits for nobody; answering it yes is refused, no still says so.
        Assert.Equal(OfferState.NotNeeded, Asking.StateOf(yes, cleos, Today));
        Assert.Empty(Asking.OffersWaiting(yes, Today));
        Refused(() => Edits.AnswerOffer(yes, cleos.Id, Answer.Yes, AskingReader.Administrator, Today, At), "does not need cover");
        Refused(() => Edits.TakeBackOffer(yes, bos.Id, asBo), "answered it already");

        // No thanks changes nothing on the timetable.
        var no = Do(offered, Edits.AnswerOffer(offered, cleos.Id, Answer.No, AskingReader.Administrator, Today, At));
        Assert.Equal(OfferState.NotThisTime, Asking.StateOf(no, no.Offers[cleos.Id]!, Today));
        Assert.Equal(offered.Sessions[acro.Id], no.Sessions[acro.Id]);
        Assert.Equal([bos.Id], Asking.OffersWaiting(no, Today).Select(o => o.Id));

        // Covered another way, by the Cover form, it is not needed either; a class gone or passed is passed.
        var covered = Do(offered, Edits.SetCover(offered, acro.Id, [cleo.Id], null));
        Assert.Equal(OfferState.NotNeeded, Asking.StateOf(covered, bos, Today));
        Assert.Equal(OfferState.Covering, Asking.StateOf(covered, cleos, Today));
        Assert.Equal(OfferState.Passed, Asking.StateOf(offered, bos, Tuesday.AddDays(1)));
        Assert.Equal(OfferState.Passed, Asking.StateOf(Do(offered, Edits.RemoveSessions(offered, [acro.Id])), bos, Today));
        // A yes undone by taking the cover off asks nobody again: it was answered.
        var uncovered = Do(yes, Edits.SetCover(yes, acro.Id, [], null));
        Assert.Equal(OfferState.NotNeeded, Asking.StateOf(uncovered, uncovered.Offers[bos.Id]!, Today));
        Assert.Equal([cleos.Id], Asking.OffersWaiting(uncovered, Today).Select(o => o.Id));
    }

    [Fact]
    public void WhoCouldCoverIsWhoeverTheCoverRankingFindsFreeNotStartedNotTheirsAndWhoeverOfferedRanksFirst()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var cleo = b.Facilitator("Cleo");
        var dag = b.Facilitator("Dag");
        var acro = b.Session(Tuesday, "10:00", "11:00", null, null, b.Class("Acrobatics"), In(ada));
        var early = b.Session(Today, "08:00", "09:00", null, null, b.Class("Strength"), In(ada));
        var later = b.Session(Today, "15:00", "16:00", null, null, b.Class("Dance"), In(ada));
        var farOff = b.Session(Today.AddDays(Asking.CoverDaysAhead), "10:00", "11:00", null, null, null, In(ada));
        b.Session(Tuesday, "10:30", "12:00", null, null, b.Class("Juggling"), In(bo));        // Bo is busy then
        b.Away(ada, Today);
        b.Away(ada, Tuesday);
        b.Away(ada, Today.AddDays(Asking.CoverDaysAhead));
        b.Away(cleo, Tuesday, "09:00", "10:30");                                               // Cleo is away for part of it
        var t = b.T;

        // At 10:00 today: the 08:00 class has started; the far-off one is beyond the two weeks.
        Assert.Equal([later.Id, acro.Id], Asking.CouldCover(t, dag.Id, Today, new(10, 0)).Select(o => o.Session.Id));
        Assert.Equal([early.Id, later.Id, acro.Id], Asking.CouldCover(t, dag.Id, Today, new(7, 0)).Select(o => o.Session.Id));
        Assert.Equal([later.Id], Asking.CouldCover(t, bo.Id, Today, new(10, 0)).Select(o => o.Session.Id));
        Assert.Equal([later.Id], Asking.CouldCover(t, cleo.Id, Today, new(10, 0)).Select(o => o.Session.Id));
        Assert.Empty(Asking.CouldCover(t, ada.Id, Today, new(7, 0)));

        // Ranked: free before busy, then whoever offered, then the lightest week; an answered offer is no offer.
        var gap = Occurrences.Resolve(t, acro);
        Assert.Equal([dag.Id, bo.Id], Cover.Ranked(t, gap).Select(c => c.Person.Id));
        var offered = Do(t, Edits.OfferCover(t, acro.Id, AskingReader.Facilitator(bo.Id), Today, At));
        var ranked = Cover.Ranked(offered, gap);
        Assert.Equal([(dag.Id, false), (bo.Id, true)], ranked.Select(c => (c.Person.Id, c.Offered)));
        var eve = b.Facilitator("Eve");
        offered = Do(offered with { Facilitators = offered.Facilitators.Put(eve) }, Edits.OfferCover(offered with { Facilitators = offered.Facilitators.Put(eve) }, acro.Id, AskingReader.Facilitator(eve.Id), Today, At));
        Assert.Equal([(eve.Id, true), (dag.Id, false), (bo.Id, true)], Cover.Ranked(offered, gap).Select(c => (c.Person.Id, c.Offered)));
        var eves = offered.Offers.Single(o => o.FacilitatorId == eve.Id);
        var declined = Do(offered, Edits.AnswerOffer(offered, eves.Id, Answer.No, AskingReader.Administrator, Today, At));
        Assert.False(Cover.Ranked(declined, gap).Single(c => c.Person.Id == eve.Id).Offered);
    }

    [Fact]
    public void AClassTheyTeachPartOfIsNotOneToOfferForThoughTheCoverFormStillRanksThem()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var dag = b.Facilitator("Dag");
        // Ada has all of the lab and Bo the second hour: with Ada away, its first hour has nobody.
        var lab = b.Session(Tuesday, "13:00", "15:00", null, null, b.Class("Creation lab"), In(ada), In(bo, from: "14:00"));
        b.Away(ada, Tuesday);
        var t = b.T;
        var gap = Occurrences.Resolve(t, lab);
        Assert.True(gap.NeedsCover);
        // Bo is free then by the ranking, as an administrator may well want him to stretch his part...
        Assert.Contains(Cover.Ranked(t, gap), c => c.Person.Id == bo.Id && c.IsFree);
        // ...but a coach is offered other people's classes: his own is already his, and offering it is refused.
        Assert.Empty(Asking.CouldCover(t, bo.Id, Today, new(8, 0)));
        Assert.Equal([lab.Id], Asking.CouldCover(t, dag.Id, Today, new(8, 0)).Select(o => o.Session.Id));
        Refused(() => Edits.OfferCover(t, lab.Id, AskingReader.Facilitator(bo.Id), Today, At), "yours already");
    }

    [Fact]
    public void NothingWaitsForAnAnswerFromSomebodyNoLongerOnTheTimetable()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var acro = b.Session(Tuesday, "10:00", "11:00", null, null, b.Class("Acrobatics"), In(ada));
        b.Away(ada, Tuesday);
        var t = b.T;
        // Eve is added, asks to be away and offers to cover; then whoever added her undoes it.
        var eve = new Facilitator { Name = "Eve" };
        var add = new Draft(t).Put(Collections.Facilitators, eve).ToBatch("Added a facilitator");
        var added = Do(t, add);
        var asEve = AskingReader.Facilitator(eve.Id);
        var asked = Do(added, Edits.AskToBeAway(added, Tuesday.AddDays(1), Tuesday.AddDays(1), null, null, asEve, Today, At));
        asked = Do(asked, Edits.OfferCover(asked, acro.Id, asEve, Today, At));
        var (request, offer) = (Assert.Single(asked.Requests), Assert.Single(asked.Offers));
        Assert.Equal([request.Id], Asking.RequestsWaiting(asked, Today).Select(r => r.Id));
        Assert.Equal([offer.Id], Asking.OffersWaiting(asked, Today).Select(o => o.Id));

        var undone = Do(asked, add.Inverse());
        Assert.Null(undone.Facilitators[eve.Id]);
        Assert.Equal((1, 1), (undone.Requests.Count, undone.Offers.Count));
        Assert.Empty(Asking.RequestsWaiting(undone, Today));
        Assert.Empty(Asking.OffersWaiting(undone, Today));
        Refused(() => Edits.AnswerRequest(undone, request.Id, Answer.Yes, AskingReader.Administrator, At), "no longer on this timetable");
        Refused(() => Edits.AnswerOffer(undone, offer.Id, Answer.Yes, AskingReader.Administrator, Today, At), "no longer on this timetable");

        // Nor for somebody kept only as former.
        var former = asked with { Facilitators = asked.Facilitators.Put(eve with { FormerSince = Today }) };
        Assert.Empty(Asking.RequestsWaiting(former, Today));
        Assert.Empty(Asking.OffersWaiting(former, Today));
    }

    [Fact]
    public void WhatWasAskedGoesWithWhoeverAskedAndNextYearKeepsOnlyAskingForItsOwnDays()
    {
        var t = SampleSchool.Create(Monday, Monday);
        var cleo = t.Facilitators.Single(f => f.Name == "Cleo Marchetti");
        var dag = t.Facilitators.Single(f => f.Name == "Dag Øverlie");
        Assert.Single(t.Requests);
        Assert.Single(t.Offers);
        Assert.Empty(Do(t, Edits.RemoveFacilitator(t, cleo.Id, Monday)).Requests);
        Assert.Single(Do(t, Edits.RemoveFacilitator(t, cleo.Id, Monday)).Offers);
        Assert.Empty(Do(t, Edits.RemoveFacilitator(t, dag.Id, Monday)).Offers);
        Assert.Single(Do(t, Edits.RemoveFacilitator(t, dag.Id, Monday)).Requests);

        // Next year: an offer was for this year's class; asking for a day of next year goes with it, as an absence does.
        var plan = NextYear.For(t)!;
        var september = new TimeOffRequest { FacilitatorId = cleo.Id, From = plan.Starts.AddDays(1), To = plan.Starts.AddDays(1), AskedOn = Monday, AskedAt = At };
        var next = NextYear.Make(t with { Requests = t.Requests.Put(september) }, "Next", keepClosures: false);
        Assert.Equal([september.Id], next.Requests.Select(r => r.Id));
        Assert.Empty(next.Offers);
    }

    /// <summary>The made-up school asks one of each, and the story runs from a coach's offer to a yes.</summary>
    [Fact]
    public void TheSampleSchoolHasOneOfEachWaitingAndAYesToOneMakesAClassAnotherCoachCouldCover()
    {
        var t = SampleSchool.Create(Monday, Monday);
        Facilitator Named(string name) => t.Facilitators.Single(f => f.Name == name);
        var (ada, cleo, dag) = (Named("Ada Brightwater"), Named("Cleo Marchetti"), Named("Dag Øverlie"));
        var request = Assert.Single(Asking.RequestsWaiting(t, Monday));
        Assert.Equal((cleo.Id, Monday.AddDays(8)), (request.FacilitatorId, request.From));
        var offer = Assert.Single(Asking.OffersWaiting(t, Monday));
        var thursday = t.Sessions[offer.SessionId]!;
        Assert.Equal((dag.Id, Monday.AddDays(3), "Acrobatics"), (offer.FacilitatorId, thursday.Date, thursday.Title));
        Assert.True(Occurrences.Resolve(t, thursday).NeedsCover);
        Assert.True(Cover.Ranked(t, Occurrences.Resolve(t, thursday))[0] is { Offered: true } first && first.Person.Id == dag.Id);

        // Nothing for Ada to cover until Cleo's Tuesday is recorded; then her Aerial is.
        Assert.Empty(Asking.CouldCover(t, ada.Id, Monday, new(8, 0)));
        var yes = Do(t, Edits.AnswerRequest(t, request.Id, Answer.Yes, AskingReader.Administrator, At));
        var aerial = Assert.Single(Asking.CouldCover(yes, ada.Id, Monday, new(8, 0)));
        Assert.Equal(("Aerial", Monday.AddDays(8)), (aerial.Session.Title, aerial.Date));
        var offered = Do(yes, Edits.OfferCover(yes, aerial.Session.Id, AskingReader.Facilitator(ada.Id), Monday, At));
        Assert.Equal(2, Asking.OffersWaiting(offered, Monday).Count);
        var adas = offered.Offers.Single(o => o.FacilitatorId == ada.Id);
        var covering = Do(offered, Edits.AnswerOffer(offered, adas.Id, Answer.Yes, AskingReader.Administrator, Monday, At));
        Assert.Equal(OfferState.Covering, Asking.StateOf(covering, adas, Monday));
        Assert.Equal([ada.Id], covering.Sessions[aerial.Session.Id]!.CoverIds);
    }
}
