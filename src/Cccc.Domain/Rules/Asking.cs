using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// Who is asking or answering: an administrator answers; a facilitator asks for themselves, through the
/// account linked to them, and nobody asks for anybody else.
/// </summary>
public sealed record AskingReader(bool Answers, Guid? FacilitatorId)
{
    public static AskingReader Administrator { get; } = new(true, null);
    public static AskingReader Nobody { get; } = new(false, null);
    public static AskingReader Facilitator(Guid id) => new(false, id);
}

public enum RequestState { Waiting, Recorded, NotThisTime, Passed }

public enum OfferState { Waiting, Covering, NotThisTime, NotNeeded, Passed }

public enum SpaceAskState { Waiting, Booked, NotThisTime, Passed }

/// <summary>
/// What facilitators ask: that they cannot come (<see cref="TimeOffRequest"/>), to cover a class that needs it
/// (<see cref="CoverOffer"/>), and for a space at a time (<see cref="SpaceAsk"/>). None changes the timetable
/// until an administrator says yes, which records the absence, the cover or the class just as the forms do; a
/// no changes nothing on it at all.
/// </summary>
public static class Asking
{
    /// <summary>How far ahead a facilitator is shown classes they could cover: today and the thirteen days after it.</summary>
    public const int CoverDaysAhead = 14;

    public static RequestState StateOf(TimeOffRequest r, DateOnly today) => r.Answer switch
    {
        Answer.Yes => RequestState.Recorded,
        Answer.No => RequestState.NotThisTime,
        _ => r.To < today ? RequestState.Passed : RequestState.Waiting,
    };

    /// <summary>The classes a request would take them off (<see cref="Away.TakesOff"/>), soonest first.</summary>
    public static IReadOnlyList<Occurrence> TakesOff(Timetable t, TimeOffRequest r) => Away.TakesOff(t, r.AsAbsence(Guid.Empty));

    /// <summary>
    /// Each class a request takes them off, as it is and as a yes would leave it (<see cref="Away.WhatItLeaves"/>):
    /// needing cover, or still taught by somebody else for all of it.
    /// </summary>
    public static IReadOnlyList<(Occurrence Now, Occurrence After)> WhatAYesLeaves(Timetable t, TimeOffRequest r) =>
        Away.WhatItLeaves(t, r.AsAbsence(Guid.NewGuid()));

    /// <summary>
    /// What is waiting for an administrator's answer: requests whose last day has not passed, from somebody
    /// still on the timetable, soonest first. Somebody can go without what they asked going with them (whoever
    /// added them undoes it), and an answer is refused for them, so asking for one would be a dead end.
    /// </summary>
    public static IReadOnlyList<TimeOffRequest> RequestsWaiting(Timetable t, DateOnly today) =>
        t.Requests.Where(r => StateOf(r, today) == RequestState.Waiting && StillHere(t, r.FacilitatorId))
            .OrderBy(r => r.From).ThenBy(r => r.AskedAt).ThenBy(r => r.Id).ToList();

    /// <summary>
    /// Where an offer stands, from the class as it is now: whoever covers it, however they came to, the
    /// offer of somebody else is no longer needed, and a class back to needing cover after a yes was undone
    /// is waiting again only for an offer nobody answered.
    /// </summary>
    public static OfferState StateOf(Timetable t, CoverOffer o, DateOnly today)
    {
        if (t.Sessions[o.SessionId] is not { } s || s.Date < today) return OfferState.Passed;
        if (s.CoverIds.Contains(o.FacilitatorId)) return OfferState.Covering;
        if (o.Answer == Answer.No) return OfferState.NotThisTime;
        if (o.Answer == Answer.Yes) return OfferState.NotNeeded;
        return Occurrences.Resolve(t, s).NeedsCover ? OfferState.Waiting : OfferState.NotNeeded;
    }

    /// <summary>Offers waiting for an administrator's answer, from somebody still on the timetable, the soonest class first.</summary>
    public static IReadOnlyList<CoverOffer> OffersWaiting(Timetable t, DateOnly today) =>
        t.Offers.Where(o => StateOf(t, o, today) == OfferState.Waiting && StillHere(t, o.FacilitatorId))
            .Select(o => (Offer: o, Session: t.Sessions[o.SessionId]!))
            .OrderBy(x => x.Session.Date).ThenBy(x => x.Session.Start).ThenBy(x => x.Offer.OfferedAt).ThenBy(x => x.Offer.Id)
            .Select(x => x.Offer).ToList();

    private static bool StillHere(Timetable t, Guid facilitatorId) => t.Facilitators[facilitatorId] is { } f && !f.IsFormer();

    /// <summary>
    /// Where an ask for a space stands: answered, or passed once its time has started with no answer (a class
    /// half over is no use to anybody), or waiting.
    /// </summary>
    public static SpaceAskState StateOf(SpaceAsk a, DateOnly today, TimeOnly now) => a.Answer switch
    {
        Answer.Yes => SpaceAskState.Booked,
        Answer.No => SpaceAskState.NotThisTime,
        _ => a.Date < today || a.Date == today && a.Start <= now ? SpaceAskState.Passed : SpaceAskState.Waiting,
    };

    /// <summary>
    /// Asks for a space waiting for an administrator's answer: for a time still to come, from somebody still on
    /// the timetable, the soonest first.
    /// </summary>
    public static IReadOnlyList<SpaceAsk> SpaceAsksWaiting(Timetable t, DateOnly today, TimeOnly now) =>
        t.SpaceAsks.Where(a => StateOf(a, today, now) == SpaceAskState.Waiting && StillHere(t, a.FacilitatorId))
            .OrderBy(a => a.Date).ThenBy(a => a.Start).ThenBy(a => a.AskedAt).ThenBy(a => a.Id).ToList();

    /// <summary>
    /// What the class a yes adds would clash with, as the clash check says it of any class added then
    /// (<see cref="Clashes.CausedBy"/>): the space in use, whoever asked teaching or away, a group with a class.
    /// In <paramref name="roomId"/> when an administrator would put it somewhere else. Said beside the ask and the
    /// answer, never a reason to refuse either: bounds advise, never refuse.
    /// </summary>
    public static IReadOnlyList<Clash> WhatAYesClashesWith(Timetable t, SpaceAsk a, Guid? roomId = null) =>
        Clashes.CausedBy(t, a.AsSession(t, a.Id) with { RoomId = roomId ?? a.RoomId });

    /// <summary>
    /// The other spaces free for the whole time asked, for an administrator who would say yes somewhere else:
    /// no class in them then and no event booking them, the ones with what the class needs first.
    /// </summary>
    public static IReadOnlyList<Room> OtherSpacesFree(Timetable t, SpaceAsk a)
    {
        var inUse = Spaces.InUse(t, a.Date, a.Start, a.End);
        var booked = Spaces.Booked(t, a.Date, a.Start, a.End);
        return t.Rooms.Current().Where(r => r.Id != a.RoomId && !inUse.ContainsKey(r.Id) && !booked.ContainsKey(r.Id))
            .OrderBy(r => !Spaces.Suits(t, a.ClassId, r.Id)).ThenBy(r => r.Name, NameOrder.Comparer).ThenBy(r => r.Id).ToList();
    }

    /// <summary>
    /// Classes needing cover from now until <see cref="CoverDaysAhead"/> days on that a facilitator could
    /// take: not one of theirs, not started yet, and they are free then by the ranking the Cover form gives
    /// (<see cref="Cover.Ranked"/>: not away, teaching nothing else then).
    /// </summary>
    public static IReadOnlyList<Occurrence> CouldCover(Timetable t, Guid facilitatorId, DateOnly today, TimeOnly now) =>
        Occurrences.Between(t, today, today.AddDays(CoverDaysAhead - 1))
            .Where(o => o.NeedsCover && (o.Date > today || o.Start > now))
            .Where(o => !o.Session.FacilitatorIds.Contains(facilitatorId))
            .Where(o => Cover.Ranked(t, o).Any(c => c.Person.Id == facilitatorId && c.IsFree))
            .ToList();
}
