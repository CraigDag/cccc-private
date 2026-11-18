using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// Swapping two classes of a week: their times, their places or, while both are on, their spaces; each keeping its
/// own length and people, each way saying what it would leave, and one change that one Undo takes back.
/// </summary>
public class SwapTests
{
    private static readonly DateOnly Tuesday = Monday.AddDays(1), Wednesday = Monday.AddDays(2), Thursday = Monday.AddDays(3), Friday = Monday.AddDays(4);
    private static readonly TimeOnly Early = new(7, 0);

    private static string Json(object? o) => System.Text.Json.JsonSerializer.Serialize(o, ModelJson.Options);

    private static string Where(Timetable t, Session s) =>
        $"{s.DisplayTitle} {(s.RoomId is { } r ? t.Rooms[r]!.Name : "no space")} {s.Date:ddd} {s.Start:HH:mm}–{s.End:HH:mm}";

    /// <summary>What a way leaves, in the clash check's words.</summary>
    private static List<string> Leaves(Swap w) => [.. w.After.Select(c => c.Description)];

    /// <summary>The way of that kind offered for the two, as the side panel lists it.</summary>
    private static Swap Offered(Timetable t, Guid a, Guid b, SwapKind kind) =>
        Swaps.For(t, a, b, Today, Early).Ways.Single(w => w.Kind == kind);

    /// <summary>The way of that kind offered for the two, taken.</summary>
    private static Batch Take(Timetable t, Guid a, Guid b, SwapKind kind) => Edits.Swap(t, Offered(t, a, b, kind), Today, Early);

    /// <summary>
    /// BA 1's Juggling on Tuesday morning in Studio A, Ada for all of it and Bo for its last hour; their Strength on
    /// Thursday afternoon in the Gym, with Cleo.
    /// </summary>
    private sealed record School(Build B, Facilitator Ada, Facilitator Bo, Facilitator Cleo, Group Ba1, Group Ba2,
        Room StudioA, Room Gym, Session Juggling, Session Strength);

    private static School Week(int weeksAhead = 0)
    {
        var b = new Build();
        var (ada, bo, cleo) = (b.Facilitator("Ada"), b.Facilitator("Bo"), b.Facilitator("Cleo"));
        var (ba1, ba2) = (b.Group("BA 1"), b.Group("BA 2"));
        var (studioA, gym) = (b.Room("Studio A"), b.Room("Gym"));
        var on = Audience.ForGroup(ba1.Id);
        var juggling = b.Session(Tuesday.AddDays(7 * weeksAhead), "09:00", "10:30", on, studioA, b.Class("Juggling"), In(ada), In(bo, "09:30"));
        var strength = b.Session(Thursday.AddDays(7 * weeksAhead), "14:00", "15:00", on, gym, b.Class("Strength"), In(cleo));
        return new(b, ada, bo, cleo, ba1, ba2, studioA, gym, juggling, strength);
    }

    [Fact]
    public void SwappingTimesKeepsEachItsOwnLengthSpaceAndPeopleWithEachPersonsPartMovingWithIt()
    {
        var s = Week();
        var t = s.B.T;
        var found = Swaps.For(t, s.Juggling.Id, s.Strength.Id, Today, Early);
        // On different days in different spaces: their times, or their places; not their spaces alone, as they are never on at once.
        Assert.Equal([SwapKind.Times, SwapKind.Places], found.Ways.Select(w => w.Kind));
        Assert.Empty(found.Notes);

        var times = found.Ways[0];
        Assert.Equal("Juggling Studio A Thu 14:00–15:30", Where(t, times.A));
        Assert.Equal("Strength Gym Tue 09:00–10:00", Where(t, times.B));
        Assert.Equal([new Assignment(s.Ada.Id), new Assignment(s.Bo.Id, new(14, 30))], times.A.Facilitators);
        Assert.Equal([new Assignment(s.Cleo.Id)], times.B.Facilitators);
        Assert.Empty(times.After);
        Assert.Empty(times.PutRight);
        Assert.Equal("Juggling to Thu 7 Mar 14:00, Strength to Tue 5 Mar 09:00", Swaps.Says(t, times));

        var places = found.Ways[1];
        Assert.Equal("Juggling Gym Thu 14:00–15:30", Where(t, places.A));
        Assert.Equal("Strength Studio A Tue 09:00–10:00", Where(t, places.B));
        Assert.Equal("Juggling to Thu 7 Mar 14:00 in Gym, Strength to Tue 5 Mar 09:00 in Studio A", Swaps.Says(t, places));

        var batch = Take(t, s.Juggling.Id, s.Strength.Id, SwapKind.Times);
        Assert.Equal("Swapped the times of Juggling and Strength", batch.Label);
        var swapped = Apply.Batch(t, batch);
        var (j, st) = (swapped.Sessions[s.Juggling.Id]!, swapped.Sessions[s.Strength.Id]!);
        Assert.Equal("Juggling Studio A Thu 14:00–15:30", Where(swapped, j));
        Assert.Equal("Strength Gym Tue 09:00–10:00", Where(swapped, st));
        Assert.Equal([new Assignment(s.Ada.Id), new Assignment(s.Bo.Id, new(14, 30))], j.Facilitators);
        // This week has begun, so each says where it was, as a class moved by a way out does.
        Assert.Equal(new MovedFrom(Tuesday, new(9, 0), new(10, 30)), j.MovedFrom);
        Assert.Equal(new MovedFrom(Thursday, new(14, 0), new(15, 0)), st.MovedFrom);
        Assert.Equal(2, batch.Ops.Count);

        // One Undo takes the whole swap back.
        Assert.Equal(Json(t.Sessions), Json(Apply.Batch(swapped, batch.Inverse()).Sessions));

        // Swapped back, both are where they were first, and neither says it has moved.
        var back = Apply.Batch(swapped, Take(swapped, s.Juggling.Id, s.Strength.Id, SwapKind.Times));
        Assert.Null(back.Sessions[s.Juggling.Id]!.MovedFrom);
        Assert.Null(back.Sessions[s.Strength.Id]!.MovedFrom);
        Assert.Equal("Juggling Studio A Tue 09:00–10:30", Where(back, back.Sessions[s.Juggling.Id]!));
    }

    [Fact]
    public void SwappingPlacesTakesEachToTheOthersSpaceTooAndAWeekNotBegunSaysNothingHasMoved()
    {
        var s = Week(weeksAhead: 1);
        var batch = Take(s.B.T, s.Juggling.Id, s.Strength.Id, SwapKind.Places);
        Assert.Equal("Swapped the places of Juggling and Strength", batch.Label);
        var t = Apply.Batch(s.B.T, batch);
        var (j, st) = (t.Sessions[s.Juggling.Id]!, t.Sessions[s.Strength.Id]!);
        Assert.Equal("Juggling Gym Thu 14:00–15:30", Where(t, j));
        Assert.Equal("Strength Studio A Tue 09:00–10:00", Where(t, st));
        Assert.Null(j.MovedFrom);
        Assert.Null(st.MovedFrom);
    }

    [Fact]
    public void TwoClassesOnAtOnceSwapTheirSpacesAndTheSwapSaysWhatItPutsRight()
    {
        var b = new Build();
        var (ba1, ba2) = (b.Group("BA 1"), b.Group("BA 2"));
        var rigging = b.Equipment("Rigging points");
        var (studioA, hall) = (b.Room("Studio A"), b.Room("Aerial Hall", rigging));
        var juggling = b.Session(Wednesday, "10:45", "12:00", Audience.ForGroup(ba1.Id), hall, b.Class("Juggling"));
        var aerial = b.Session(Wednesday, "10:45", "12:00", Audience.ForGroup(ba2.Id), studioA, b.Class("Aerial", needs: [rigging]));
        var t = b.T;

        var found = Swaps.For(t, juggling.Id, aerial.Id, Today, Early);
        var spaces = Assert.Single(found.Ways);
        Assert.Equal(SwapKind.Spaces, spaces.Kind);
        Assert.Equal("Juggling to Studio A, Aerial to Aerial Hall", Swaps.Says(t, spaces));
        Assert.Empty(spaces.After);
        // Aerial is in a space without its rigging now; in the hall it is not.
        Assert.Equal(["Space \"Studio A\" lacks what \"Aerial\" needs: Rigging points (Wed 10:45–12:00)"], spaces.PutRight.Select(c => c.Description));

        var batch = Take(t, juggling.Id, aerial.Id, SwapKind.Spaces);
        Assert.Equal("Swapped the spaces of Juggling and Aerial", batch.Label);
        var swapped = Apply.Batch(t, batch);
        Assert.Equal("Juggling Studio A Wed 10:45–12:00", Where(swapped, swapped.Sessions[juggling.Id]!));
        Assert.Equal("Aerial Aerial Hall Wed 10:45–12:00", Where(swapped, swapped.Sessions[aerial.Id]!));
        // At their own times, so neither has moved in time.
        Assert.Null(swapped.Sessions[juggling.Id]!.MovedFrom);
        Assert.Null(swapped.Sessions[aerial.Id]!.MovedFrom);
    }

    [Fact]
    public void TwoClassesOverlappingInTheirDayHaveAllThreeWaysEachSaidAtItsTime()
    {
        var b = new Build();
        var (ba1, ba2) = (b.Group("BA 1"), b.Group("BA 2"));
        var (studioA, hall) = (b.Room("Studio A"), b.Room("Aerial Hall"));
        var juggling = b.Session(Wednesday, "10:00", "11:00", Audience.ForGroup(ba1.Id), studioA, b.Class("Juggling"));
        var aerial = b.Session(Wednesday, "10:30", "11:30", Audience.ForGroup(ba2.Id), hall, b.Class("Aerial"));
        var found = Swaps.For(b.T, juggling.Id, aerial.Id, Today, Early);
        Assert.Equal([SwapKind.Times, SwapKind.Places, SwapKind.Spaces], found.Ways.Select(w => w.Kind));
        Assert.Equal(["Juggling to 10:30, Aerial to 10:00", "Juggling to 10:30 in Aerial Hall, Aerial to 10:00 in Studio A", "Juggling to Aerial Hall, Aerial to Studio A"],
            found.Ways.Select(w => Swaps.Says(b.T, w)));

        // Back to back, or at the same hours on another day, two are never on at once: their spaces alone are no swap.
        var next = b.Session(Wednesday, "11:30", "12:30", Audience.ForGroup(ba1.Id), studioA, b.Class("Rigging"));
        Assert.Equal([SwapKind.Times, SwapKind.Places], Swaps.For(b.T, aerial.Id, next.Id, Today, Early).Ways.Select(w => w.Kind));
        var thursday = b.Session(Thursday, "10:30", "11:30", Audience.ForGroup(ba1.Id), studioA, b.Class("Dance"));
        Assert.Equal([SwapKind.Times, SwapKind.Places], Swaps.For(b.T, aerial.Id, thursday.Id, Today, Early).Ways.Select(w => w.Kind));

        // A class with no space swaps into one, and leaves the other without.
        var stretching = b.Session(Wednesday, "10:15", "11:15", Audience.ForGroup(ba2.Id), null, b.Class("Stretching"));
        Assert.Equal(["Juggling to 10:15, Stretching to 10:00", "Juggling to 10:15 without a space, Stretching to 10:00 in Studio A", "Juggling without a space, Stretching to Studio A"],
            Swaps.For(b.T, juggling.Id, stretching.Id, Today, Early).Ways.Select(w => Swaps.Says(b.T, w)));
    }

    [Fact]
    public void EachWaySaysWhatTheTwoWouldClashWithOrNeedThereInTheClashChecksWords()
    {
        var s = Week();
        var b = s.B;
        // Dance is in Studio A on Thursday afternoon, and Cleo is away on Tuesday.
        var dance = b.Session(Thursday, "14:00", "15:00", Audience.ForGroup(s.Ba2.Id), s.StudioA, b.Class("Dance"));
        b.Away(s.Cleo, Tuesday);
        // Elsewhere in the week, two classes are in the Gym at once; and Cleo is over their weekly limit, which names Strength.
        b.Session(Friday, "09:00", "10:00", Audience.ForGroup(s.Ba2.Id), s.Gym, b.Class("Rigging"));
        b.Session(Friday, "09:00", "10:00", Audience.ForGroup(s.Ba2.Id), s.Gym, b.Class("Tumbling"));
        b.T = b.T with { Facilitators = b.T.Facilitators.Put(s.Cleo with { MaxWeeklyHours = TimeSpan.FromMinutes(30) }) };
        var t = b.T;
        Assert.Contains(Clashes.ForWeek(t, Monday), c => c.Kind == ClashKind.FacilitatorOverHours && c.SessionIds.Contains(s.Strength.Id));
        Assert.Contains(Clashes.ForWeek(t, Monday), c => !c.SessionIds.Contains(s.Juggling.Id) && !c.SessionIds.Contains(s.Strength.Id));
        var found = Swaps.For(t, s.Juggling.Id, s.Strength.Id, Today, Early);
        var (times, places) = (found.Ways[0], found.Ways[1]);

        // As the clash check would say it once swapped: the same words, about these two alone.
        foreach (var way in found.Ways)
        {
            var swapped = Apply.Batch(t, Take(t, s.Juggling.Id, s.Strength.Id, way.Kind));
            var said = Clashes.ForWeek(swapped, Monday)
                .Where(c => c.Kind != ClashKind.FacilitatorOverHours && (c.SessionIds.Contains(s.Juggling.Id) || c.SessionIds.Contains(s.Strength.Id)))
                .Select(c => c.Description).ToList();
            Assert.NotEmpty(said);
            Assert.Equal(said, Leaves(way));
        }
        // Their times put Juggling in Studio A beside Dance; their places put it in the Gym, which Strength has left.
        Assert.Equal([(ClashKind.RoomDoubleBooked, true), (ClashKind.NeedsCover, false)],
            times.After.Select(c => (c.Kind, c.SessionIds.Contains(dance.Id))));
        Assert.Equal([ClashKind.NeedsCover], places.After.Select(c => c.Kind));
        Assert.Equal([s.Strength.Id], places.After[0].SessionIds);
        Assert.Empty(times.PutRight);
        // Nothing was against them before, so all of it is the swap's.
        Assert.All(found.Ways, w => Assert.Equal(Leaves(w), w.Brings.Select(c => c.Description)));
        Assert.All(found.Ways, w => Assert.Empty(w.Still));
    }

    [Fact]
    public void ASwapOutOfAClashPutsItRightAndOneIntoAnotherSaysSoBesideIt()
    {
        var s = Week();
        var b = s.B;
        // Hand balance is in Studio A on Tuesday morning too.
        var hand = b.Session(Tuesday, "09:00", "10:30", Audience.ForGroup(s.Ba2.Id), s.StudioA, b.Class("Hand balance"));
        var found = Swaps.For(b.T, s.Juggling.Id, s.Strength.Id, Today, Early);
        var (times, places) = (found.Ways[0], found.Ways[1]);
        // What it puts right is said as the clash check says it now.
        var room = Assert.Single(Clashes.ForWeek(b.T, Monday), c => c.Kind == ClashKind.RoomDoubleBooked);
        Assert.Equal([room.Description], times.PutRight.Select(c => c.Description));
        Assert.Empty(times.After);
        // Strength into Studio A meets Hand balance there instead: that one is put right, and this one is said.
        Assert.Equal([room.Description], places.PutRight.Select(c => c.Description));
        var still = Assert.Single(places.After);
        Assert.Equal(ClashKind.RoomDoubleBooked, still.Kind);
        Assert.Equal(new[] { hand.Id, s.Strength.Id }.Order(), still.SessionIds.Order());
    }

    [Fact]
    public void TwoClashesBetweenTheSameTwoClassesAreTwoAndOnePutRightIsOne()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var (ba1, ba2, ma) = (b.Group("BA 1"), b.Group("BA 2"), b.Group("MA"));
        var (studioA, gym) = (b.Room("Studio A"), b.Room("Gym"));
        // Juggling and Hand balance are in Studio A at once, both with Ada; Rigging is in the Gym then.
        var juggling = b.Session(Tuesday, "09:00", "10:00", Audience.ForGroup(ba1.Id), studioA, b.Class("Juggling"), In(ada));
        var hand = b.Session(Tuesday, "09:00", "10:00", Audience.ForGroup(ba2.Id), studioA, b.Class("Hand balance"), In(ada));
        var rigging = b.Session(Tuesday, "09:00", "10:00", Audience.ForGroup(ma.Id), gym, b.Class("Rigging"));
        var spaces = Assert.Single(Swaps.For(b.T, juggling.Id, rigging.Id, Today, Early).Ways);
        // Juggling's space swapped puts Studio A right, and leaves Ada in two places and Rigging in Studio A with Hand balance.
        var putRight = Assert.Single(spaces.PutRight);
        Assert.Equal((ClashKind.RoomDoubleBooked, juggling.Id), (putRight.Kind, putRight.SessionIds.Single(id => id != hand.Id)));
        Assert.Equal([(ClashKind.RoomDoubleBooked, true), (ClashKind.FacilitatorDoubleBooked, false)],
            spaces.After.Select(c => (c.Kind, c.SessionIds.Contains(rigging.Id))).OrderBy(k => k.Kind));
        Assert.All(spaces.After, c => Assert.Contains(hand.Id, c.SessionIds));
        // Rigging beside Hand balance is the swap's; Ada in two places was there before it and is still.
        Assert.Equal([ClashKind.RoomDoubleBooked], spaces.Brings.Select(c => c.Kind));
        Assert.Contains(rigging.Id, spaces.Brings[0].SessionIds);
        Assert.Equal([ClashKind.FacilitatorDoubleBooked], spaces.Still.Select(c => c.Kind));
    }

    [Fact]
    public void AFacilitatorsLongGapIsSaidAsWhatASwapWouldLeave()
    {
        var s = Week();
        var b = s.B;
        // Ada may have two hours free in a day at most, and teaches Dance first thing on Thursday.
        b.T = b.T with { Facilitators = b.T.Facilitators.Put(s.Ada with { MaxGap = TimeSpan.FromHours(2) }) };
        var dance = b.Session(Thursday, "09:00", "10:00", Audience.ForGroup(s.Ba2.Id), s.Gym, b.Class("Dance"), In(s.Ada));
        var times = Swaps.For(b.T, s.Juggling.Id, s.Strength.Id, Today, Early).Ways[0];
        var gap = Assert.Single(times.After);
        Assert.Equal(ClashKind.FacilitatorLongGap, gap.Kind);
        Assert.Equal([dance.Id, s.Juggling.Id], gap.SessionIds);
        Assert.Equal([gap], times.Brings);
    }

    [Fact]
    public void ASwapThatWouldMakeAGroupTrainHardTooLongSaysSo()
    {
        var b = new Build();
        var ba1 = b.Group("BA 1");
        b.T = b.T with { Settings = b.T.Settings with { MostHardInARow = TimeSpan.FromHours(2) } };
        var on = Audience.ForGroup(ba1.Id);
        var strength = b.Session(Tuesday, "09:00", "10:30", on, cls: b.Class("Strength", effort: Effort.Hard));
        var juggling = b.Session(Tuesday, "10:30", "12:00", on, cls: b.Class("Juggling", effort: Effort.Moderate));
        b.Session(Tuesday, "12:00", "13:00", on, cls: b.Class("Acrobatics", effort: Effort.Hard));
        var found = Swaps.For(b.T, strength.Id, juggling.Id, Today, Early);
        var times = Assert.Single(found.Ways);
        // Strength at half past ten runs straight into Acrobatics: two and a half hours of hard training without a rest.
        Assert.Equal([ClashKind.GroupHardInARow], times.After.Select(c => c.Kind));
    }

    [Fact]
    public void NoSwapForTheSameClassAnotherWeekOneCancelledStartedOrOverOrTwoAlreadyAlike()
    {
        var s = Week();
        var b = s.B;
        string Why(Timetable t, Guid a, Guid c, DateOnly? today = null, TimeOnly? now = null)
        {
            var found = Swaps.For(t, a, c, today ?? Today, now ?? Early);
            Assert.Empty(found.Ways);
            return Assert.Single(found.Notes);
        }
        var t = b.T;
        Assert.Equal("That is the class being swapped. Choose another class to swap it with.", Why(t, s.Juggling.Id, s.Juggling.Id));
        Assert.Equal("One of those classes has gone.", Why(t, s.Juggling.Id, Guid.NewGuid()));

        var nextWeek = b.Session(Thursday.AddDays(7), "14:00", "15:00", Audience.ForGroup(s.Ba1.Id), s.Gym, b.Class("Dance"));
        Assert.Equal("Only two classes of the same week can be swapped.", Why(b.T, s.Juggling.Id, nextWeek.Id));

        t = b.T;
        var cancelled = t with { Sessions = t.Sessions.Put(s.Strength with { Cancelled = true }) };
        Assert.Equal("Strength is cancelled, so it is not swapped with anything.", Why(cancelled, s.Juggling.Id, s.Strength.Id));
        Assert.Equal("Juggling has started, so it stays where it is.", Why(t, s.Juggling.Id, s.Strength.Id, Tuesday, new(9, 30)));
        Assert.Equal("Juggling is over, so it stays where it was.", Why(t, s.Strength.Id, s.Juggling.Id, Tuesday, new(10, 30)));
        Assert.Equal("Juggling is over, so it stays where it was.", Why(t, s.Strength.Id, s.Juggling.Id, Wednesday));
        // A minute before it starts, it may still go.
        Assert.NotEmpty(Swaps.For(t, s.Juggling.Id, s.Strength.Id, Tuesday, new(8, 59)).Ways);

        var alike = b.Session(Tuesday, "09:00", "10:00", Audience.ForGroup(s.Ba2.Id), s.StudioA, b.Class("Dance"));
        Assert.Equal("They are at the same time in the same space, so swapping them would change nothing.", Why(b.T, s.Juggling.Id, alike.Id));
    }

    [Fact]
    public void TheirTimesAreNotSwappedPastMidnightOrOntoADayClosedForEitherButTheirSpacesStillAre()
    {
        var s = Week();
        var b = s.B;
        var late = b.Session(Thursday, "23:00", "23:45", Audience.ForGroup(s.Ba2.Id), s.StudioA, b.Class("Show"));
        var found = Swaps.For(b.T, s.Juggling.Id, late.Id, Today, Early);
        Assert.Empty(found.Ways);
        Assert.Equal(["Their times cannot be swapped: Juggling would run past midnight."], found.Notes);
        // Ending at midnight on the dot would end before it starts: no more a class than one past it.
        var onTheDot = b.T with { Sessions = b.T.Sessions.Put(late with { Start = new(22, 30), End = new(23, 30) }) };
        Assert.Equal(["Their times cannot be swapped: Juggling would run past midnight."], Swaps.For(onTheDot, s.Juggling.Id, late.Id, Today, Early).Notes);
        var aQuarterBefore = b.T with { Sessions = b.T.Sessions.Put(late with { Start = new(22, 15), End = new(23, 0) }) };
        Assert.Equal("Juggling Studio A Thu 22:15–23:45", Where(aQuarterBefore, Assert.Single(Swaps.For(aQuarterBefore, s.Juggling.Id, late.Id, Today, Early).Ways).A));

        // A trip closes Thursday for BA 1: Juggling cannot go there, whichever of the two is asked first.
        b.Closure(new Closure { Name = "Trip", From = Thursday, To = Thursday, Scope = ClosureScope.Groups, GroupIds = [s.Ba1.Id] });
        var other = b.Session(Thursday, "09:00", "10:00", Audience.ForGroup(s.Ba2.Id), s.Gym, b.Class("Dance"));
        Assert.Equal(["Their times cannot be swapped: Juggling would not run on Thu 7 Mar (Trip)."], Swaps.For(b.T, s.Juggling.Id, other.Id, Today, Early).Notes);
        Assert.Equal(["Their times cannot be swapped: Juggling would not run on Thu 7 Mar (Trip)."], Swaps.For(b.T, other.Id, s.Juggling.Id, Today, Early).Notes);
        // Dance may go to Tuesday, and on its own that would be a swap.
        var tuesday = b.Session(Tuesday, "11:00", "12:00", Audience.ForGroup(s.Ba2.Id), s.Gym, b.Class("Rigging"));
        Assert.Equal([SwapKind.Times], Swaps.For(b.T, tuesday.Id, other.Id, Today, Early).Ways.Select(w => w.Kind));

        // On at once in different spaces, their spaces swap whatever their times would do.
        var show = b.Session(Friday, "22:00", "23:30", Audience.ForGroup(s.Ba1.Id), s.StudioA, b.Class("Late show"));
        var warmUp = b.Session(Friday, "22:30", "23:00", Audience.ForGroup(s.Ba2.Id), s.Gym, b.Class("Warm-up"));
        var both = Swaps.For(b.T, show.Id, warmUp.Id, Today, Early);
        Assert.Equal([SwapKind.Spaces], both.Ways.Select(w => w.Kind));
        Assert.Equal(["Their times cannot be swapped: Late show would run past midnight."], both.Notes);
    }

    [Fact]
    public void ASwapNoLongerPossibleIsRefusedSayingWhy()
    {
        var s = Week();
        var t = s.B.T;
        var times = Offered(t, s.Juggling.Id, s.Strength.Id, SwapKind.Times);
        var cancelled = t with { Sessions = t.Sessions.Put(s.Strength with { Cancelled = true }) };
        var refused = Assert.Throws<EditRefused>(() => Edits.Swap(cancelled, times, Today, Early));
        Assert.Equal("Strength is cancelled, so it is not swapped with anything.", refused.Message);

        // Offered while the two were on at once; one has moved since, so their spaces alone are no swap now.
        var b = new Build();
        var (ba1, ba2) = (b.Group("BA 1"), b.Group("BA 2"));
        var (studioA, hall) = (b.Room("Studio A"), b.Room("Aerial Hall"));
        var juggling = b.Session(Wednesday, "10:00", "11:00", Audience.ForGroup(ba1.Id), studioA, b.Class("Juggling"));
        var aerial = b.Session(Wednesday, "10:30", "11:30", Audience.ForGroup(ba2.Id), hall, b.Class("Aerial"));
        var spaces = Offered(b.T, juggling.Id, aerial.Id, SwapKind.Spaces);
        var moved = b.T with { Sessions = b.T.Sessions.Put(aerial with { Start = new(11, 0), End = new(12, 0) }) };
        var notThatWay = Assert.Throws<EditRefused>(() => Edits.Swap(moved, spaces, Today, Early));
        Assert.Equal("Those two classes can no longer be swapped that way: something has changed since.", notThatWay.Message);
    }

    [Fact]
    public void ASwapOfferedBeforeSomebodyElsesChangeIsTakenOnlyWhileItStillDoesWhatItSaid()
    {
        var s = Week();
        var b = s.B;
        var hall = b.Room("Aerial Hall");
        var t = b.T;
        var times = Offered(t, s.Juggling.Id, s.Strength.Id, SwapKind.Times);
        Assert.Empty(times.Brings);
        const string changed = "Something has changed since that swap was offered, so it has not been made: look at what it would do now.";

        // Strength moved to the Aerial Hall meanwhile: their places would put Juggling there, never in the Gym.
        var places = Offered(t, s.Juggling.Id, s.Strength.Id, SwapKind.Places);
        var strengthInTheHall = t with { Sessions = t.Sessions.Put(s.Strength with { RoomId = hall.Id }) };
        Assert.Equal(changed, Assert.Throws<EditRefused>(() => Edits.Swap(strengthInTheHall, places, Today, Early)).Message);
        // Bo off Juggling, and Ada on Strength instead of Cleo, meanwhile, which brings nothing: taken, both as they are
        // now rather than as they were.
        var peopleChanged = t with
        {
            Sessions = t.Sessions.Put(s.Juggling with { Facilitators = [new Assignment(s.Ada.Id)] })
                .Put(s.Strength with { Facilitators = [new Assignment(s.Ada.Id)] }),
        };
        var taken = Apply.Batch(peopleChanged, Edits.Swap(peopleChanged, times, Today, Early));
        Assert.Equal("Juggling Studio A Thu 14:00–15:30", Where(taken, taken.Sessions[s.Juggling.Id]!));
        Assert.Equal("Strength Gym Tue 09:00–10:00", Where(taken, taken.Sessions[s.Strength.Id]!));
        Assert.Equal([new Assignment(s.Ada.Id)], taken.Sessions[s.Juggling.Id]!.Facilitators);
        Assert.Equal([new Assignment(s.Ada.Id)], taken.Sessions[s.Strength.Id]!.Facilitators);

        // Strength moved to Friday meanwhile: the swap offered was Juggling to Thursday, never to Friday.
        var strengthMoved = t with { Sessions = t.Sessions.Put(s.Strength with { Date = Friday, Start = new(10, 0), End = new(11, 0) }) };
        Assert.Equal(changed, Assert.Throws<EditRefused>(() => Edits.Swap(strengthMoved, times, Today, Early)).Message);
        // Juggling moved to Wednesday meanwhile: Strength would go there, not to Tuesday.
        var jugglingMoved = t with { Sessions = t.Sessions.Put(s.Juggling with { Date = Wednesday }) };
        Assert.Equal(changed, Assert.Throws<EditRefused>(() => Edits.Swap(jugglingMoved, times, Today, Early)).Message);
        // Juggling made longer meanwhile: it would no longer end where it said.
        var longer = t with { Sessions = t.Sessions.Put(s.Juggling with { End = new(11, 0) }) };
        Assert.Equal(changed, Assert.Throws<EditRefused>(() => Edits.Swap(longer, times, Today, Early)).Message);
        // Dance put in Studio A on Thursday afternoon meanwhile: the swap would now bring a clash it never said.
        var dance = b.Session(Thursday, "14:00", "15:00", Audience.ForGroup(s.Ba2.Id), s.StudioA, b.Class("Dance"));
        Assert.Equal(changed, Assert.Throws<EditRefused>(() => Edits.Swap(b.T, times, Today, Early)).Message);

        // Offered with that clash said, and Dance moved out of the way since: bringing less than it said, it is taken.
        var saying = Offered(b.T, s.Juggling.Id, s.Strength.Id, SwapKind.Times);
        Assert.Equal([ClashKind.RoomDoubleBooked], saying.Brings.Select(c => c.Kind));
        var danceGone = b.T with { Sessions = b.T.Sessions.Put(dance with { RoomId = s.Gym.Id, Date = Friday }) };
        var swapped = Apply.Batch(danceGone, Edits.Swap(danceGone, saying, Today, Early));
        Assert.Equal("Juggling Studio A Thu 14:00–15:30", Where(swapped, swapped.Sessions[s.Juggling.Id]!));
        // And taken as it was offered, it brings what it said.
        Assert.Equal("Juggling Studio A Thu 14:00–15:30", Where(b.T, Apply.Batch(b.T, Edits.Swap(b.T, saying, Today, Early)).Sessions[s.Juggling.Id]!));
    }

    [Fact]
    public void ASwapThatLeavesSomebodyALongGapBetweenTwoOtherClassesSaysSoAndSwappingBackPutsItRight()
    {
        var s = Week();
        var b = s.B;
        // Ada may have two hours free in a day at most. On Tuesday she teaches Warm-up before Juggling and Rigging after it.
        b.T = b.T with { Facilitators = b.T.Facilitators.Put(s.Ada with { MaxGap = TimeSpan.FromHours(2) }) };
        var warmUp = b.Session(Tuesday, "08:00", "09:00", Audience.ForGroup(s.Ba2.Id), s.Gym, b.Class("Warm-up"), In(s.Ada));
        var rigging = b.Session(Tuesday, "11:30", "12:30", Audience.ForGroup(s.Ba2.Id), s.Gym, b.Class("Rigging"), In(s.Ada));
        Assert.DoesNotContain(Clashes.ForWeek(b.T, Monday), c => c.Kind == ClashKind.FacilitatorLongGap);

        // Juggling to Thursday leaves her two and a half hours between the two, which names neither class swapped.
        var times = Offered(b.T, s.Juggling.Id, s.Strength.Id, SwapKind.Times);
        var gap = Assert.Single(times.Brings);
        Assert.Equal(ClashKind.FacilitatorLongGap, gap.Kind);
        Assert.Equal([warmUp.Id, rigging.Id], gap.SessionIds);
        Assert.Empty(times.After);
        Assert.Empty(times.Still);

        // Swapped, the gap is there; swapping them back puts it right.
        var swapped = Apply.Batch(b.T, Edits.Swap(b.T, times, Today, Early));
        var back = Offered(swapped, s.Juggling.Id, s.Strength.Id, SwapKind.Times);
        Assert.Equal([gap.Description], back.PutRight.Select(c => c.Description));
        Assert.Empty(back.Brings);
    }

    [Fact]
    public void AGroupsDayOfHardTrainingTheSameAfterASwapIsThereStillWhicheverClassesMakeIt()
    {
        var b = new Build();
        var ba1 = b.Group("BA 1");
        b.T = b.T with { Settings = b.T.Settings with { MostHardInADay = TimeSpan.FromHours(2) } };
        var on = Audience.ForGroup(ba1.Id);
        var strength = b.Session(Tuesday, "09:00", "10:30", on, cls: b.Class("Strength", effort: Effort.Hard));
        b.Session(Tuesday, "11:00", "12:30", on, cls: b.Class("Acrobatics", effort: Effort.Hard));
        var tumbling = b.Session(Thursday, "09:00", "10:30", on, cls: b.Class("Tumbling", effort: Effort.Hard));
        var tuesday = Assert.Single(Clashes.ForWeek(b.T, Monday), c => c.Kind == ClashKind.GroupHardInADay);

        // Tumbling on Tuesday instead of Strength: the same three hours, said in the same words.
        var times = Assert.Single(Swaps.For(b.T, strength.Id, tumbling.Id, Today, Early).Ways);
        Assert.Empty(times.Brings);
        Assert.Empty(times.PutRight);
        Assert.Equal([tuesday.Description], times.Still.Select(c => c.Description));
        Assert.Contains(tumbling.Id, times.Still[0].SessionIds);
    }
}
