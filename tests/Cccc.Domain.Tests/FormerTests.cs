using System.Text.Json;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// Deleting a class, a space or a facilitator the days already run still name keeps it as former, so those
/// days keep their name, colour, area and hours; and merging two classes into one, the past included.
/// </summary>
public class FormerTests
{
    /// <summary>Thursday of the current week: Monday has run this week, and the week is not locked.</summary>
    private static readonly DateOnly Thursday = Monday.AddDays(3);
    private static readonly DateOnly LastWeek = Monday.AddDays(-7);

    private static string Json(Session? s) => JsonSerializer.Serialize(s, ModelJson.Options);

    [Fact]
    public void DeletingAClassThatRanKeepsItAsFormerSoTheDaysItRanKeepItsWorkloadAndCredit()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var acro = b.Class("Acro");
        var lunch = b.Class("Lunch", Workload.NotCounted);
        b.TimeRule(new TimeRule { Name = "Prep", Kind = TimeRuleKind.Preparation, Percent = 50, ClassIds = [acro.Id] });
        var lastWeek = b.Session(LastWeek, "09:00", "11:00", cls: acro, who: In(ada));
        var monday = b.Session(Monday, "09:00", "11:00", cls: acro, who: In(ada));      // this week, before today
        var today = b.Session(Thursday, "09:00", "11:00", cls: acro, who: In(ada));     // today is from today on
        var untitled = b.Session(Monday.AddDays(7), "09:00", "11:00", cls: acro);
        b.Put(untitled with { Title = "" });
        var ate = b.Session(LastWeek, "12:00", "13:00", cls: lunch);

        var t = Apply.Batch(b.T, Edits.RemoveClass(b.T, acro.Id, Thursday));
        t = Apply.Batch(t, Edits.RemoveClass(t, lunch.Id, Thursday));

        Assert.Equal(Thursday, t.Classes[acro.Id]?.FormerSince);
        Assert.Equal([acro.Id, acro.Id], new[] { lastWeek, monday }.Select(s => t.Sessions[s.Id]!.ClassId));
        Assert.Equal(((Guid?)null, "Acro"), (t.Sessions[today.Id]!.ClassId, t.Sessions[today.Id]!.Title));
        Assert.Equal("Acro", t.Sessions[untitled.Id]!.Title);   // a blank title takes the name it showed
        Assert.Contains(acro.Id, t.TimeRules.Single().ClassIds);
        // Last week still earns the class's credit (2h and 50% of it), and lunch is still not a student's hours.
        Assert.Equal(TimeSpan.FromHours(3), Hours.For(t, Occurrences.HappeningInWeek(t, LastWeek), ada.Id).Total);
        Assert.Equal(Workload.NotCounted, Hours.WorkloadOf(t, t.Sessions[ate.Id]!));
    }

    [Fact]
    public void DeletingAClassThatNeverRanTakesItOffEverythingAndEveryRule()
    {
        var b = new Build();
        var acro = b.Class("Acro");
        b.TimeRule(new TimeRule { Kind = TimeRuleKind.Preparation, ClassIds = [acro.Id] });
        var ahead = b.Session(Thursday, "09:00", "10:00", cls: acro);

        var t = Apply.Batch(b.T, Edits.RemoveClass(b.T, acro.Id, Thursday));

        Assert.Null(t.Classes[acro.Id]);
        Assert.Empty(t.TimeRules.Single().ClassIds);
        Assert.Null(t.Sessions[ahead.Id]!.ClassId);
    }

    [Fact]
    public void DeletingASpaceThatWasUsedKeepsItFormerAndAnEventAlreadyOverKeepsIt()
    {
        var b = new Build();
        var studio = b.Room("Studio");
        var used = b.Session(Monday, "09:00", "10:00", room: studio);
        var ahead = b.Session(Thursday, "09:00", "10:00", room: studio);
        var over = new SchoolEvent { Title = "Showing", Date = LastWeek, RoomId = studio.Id };
        var coming = new SchoolEvent { Title = "Open day", Date = Monday.AddDays(10), RoomId = studio.Id };
        b.T = b.T with { Events = b.T.Events.Put(over).Put(coming) };

        var t = Apply.Batch(b.T, Edits.RemoveRoom(b.T, studio.Id, Thursday));

        Assert.Equal(Thursday, t.Rooms[studio.Id]?.FormerSince);
        Assert.Equal(studio.Id, t.Sessions[used.Id]!.RoomId);
        Assert.Null(t.Sessions[ahead.Id]!.RoomId);
        Assert.Equal(studio.Id, t.Events[over.Id]!.RoomId);
        Assert.Null(t.Events[coming.Id]!.RoomId);

        // A space never used before today is simply gone, from every event too.
        var b2 = new Build();
        var hall = b2.Room("Hall");
        b2.T = b2.T with { Events = b2.T.Events.Put(over with { RoomId = hall.Id }) };
        var t2 = Apply.Batch(b2.T, Edits.RemoveRoom(b2.T, hall.Id, Thursday));
        Assert.Null(t2.Rooms[hall.Id]);
        Assert.Null(t2.Events[over.Id]!.RoomId);
    }

    [Fact]
    public void RemovingAFacilitatorWhoTaughtKeepsThemWithTheirAbsencesSoTheHoursOfThoseDaysStand()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        b.Session(LastWeek, "09:00", "12:00", who: In(ada));                            // she taught: 3h
        var covered = b.Session(LastWeek.AddDays(1), "09:00", "11:00", who: In(ada));   // she was away: Bo's 2h
        b.Put(covered with { CoverIds = [bo.Id] });
        b.Away(ada, LastWeek.AddDays(1));
        b.Away(ada, Monday.AddDays(8));                                                  // a leave booked ahead
        var ahead = b.Session(Thursday, "09:00", "10:00", null, null, null, In(ada), In(bo));

        var t = Apply.Batch(b.T, Edits.RemoveFacilitator(b.T, ada.Id, Thursday));

        Assert.Equal(Thursday, t.Facilitators[ada.Id]?.FormerSince);
        Assert.Equal([bo.Id], t.Sessions[ahead.Id]!.FacilitatorIds);
        Assert.Equal([LastWeek.AddDays(1)], t.Absences.Select(a => a.From));   // last week's stays, the leave ahead goes
        var rows = Hours.OverWeeks(t, [LastWeek, Monday]);
        // 3h, not 5: with her absence kept she was not in the room for the class Bo covered.
        Assert.Equal(TimeSpan.FromHours(3), rows.Single(r => r.Person.Id == ada.Id).Total);
        Assert.Equal(TimeSpan.FromHours(3), rows.Single(r => r.Person.Id == bo.Id).Total);
        // Over weeks she taught in none of, a former facilitator is not listed at all.
        Assert.DoesNotContain(Hours.OverWeeks(t, [Monday.AddDays(7)]), r => r.Person.Id == ada.Id);
        Assert.Contains(Hours.OverWeeks(t, [Monday.AddDays(7)]), r => r.Person.Id == bo.Id);

        // Somebody who never taught is gone, absences and all.
        var b2 = new Build();
        var cy = b2.Facilitator("Cy");
        b2.Away(cy, LastWeek);
        var t2 = Apply.Batch(b2.T, Edits.RemoveFacilitator(b2.T, cy.Id, Thursday));
        Assert.Null(t2.Facilitators[cy.Id]);
        Assert.Empty(t2.Absences);
    }

    [Fact]
    public void EveryCopyLeavesOutWhatIsFormerAndKeepsTheRest()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var acro = b.Class("Acro");
        var aerial = b.Class("Aerial");
        var studio = b.Room("Studio");
        var hall = b.Room("Hall");
        var first = b.Session(LastWeek, "09:00", "10:00", null, studio, acro, In(ada), In(bo));
        b.Put(first with { Title = "" });
        // Ada also taught this one, beside Bo: nothing else on it is former, and still she is left out.
        var second = b.Session(LastWeek.AddDays(1), "09:00", "10:00", null, hall, aerial, In(bo), In(ada));
        var t = b.T;
        foreach (var gone in new Func<Timetable, Batch>[]
                 { x => Edits.RemoveClass(x, acro.Id, Thursday), x => Edits.RemoveRoom(x, studio.Id, Thursday), x => Edits.RemoveFacilitator(x, ada.Id, Thursday) })
            t = Apply.Batch(t, gone(t));

        var nextWeek = Monday.AddDays(7);
        var copies = Apply.Batch(t, Edits.CopyWeek(t, LastWeek, [nextWeek], Thursday)).SessionsInWeek(nextWeek).OrderBy(s => s.Date).ToList();
        Assert.Equal(2, copies.Count);
        Assert.Equal(((Guid?)null, "Acro", (Guid?)null), (copies[0].ClassId, copies[0].Title, copies[0].RoomId));
        Assert.Equal([bo.Id], copies[0].FacilitatorIds);
        Assert.Equal((aerial.Id, hall.Id, bo.Id), (copies[1].ClassId!.Value, copies[1].RoomId!.Value, copies[1].FacilitatorIds.Single()));
        // Somebody former only as cover is left out too, and a session with nothing former is the very same one.
        var covered = t.Sessions[second.Id]! with { Facilitators = [In(bo)], CoverIds = [ada.Id, bo.Id] };
        Assert.Equal([bo.Id], Former.Strip(t, covered).CoverIds);
        var plain = covered with { CoverIds = [bo.Id] };
        Assert.Same(plain, Former.Strip(t, plain));

        var day = Apply.Batch(t, Edits.CopyDay(t, LastWeek, nextWeek)).Sessions.Single(s => s.Date == nextWeek);
        Assert.Equal(((Guid?)null, (Guid?)null, new[] { bo.Id }), (day.ClassId, day.RoomId, day.FacilitatorIds.ToArray()), new CopyComparer());
        var plan = Apply.Batch(t, ScratchWeek.CopyWeekIn(t, LastWeek)).Scratch.ToList();
        Assert.Equal(2, plan.Count);
        Assert.DoesNotContain(plan, s => s.ClassId == acro.Id || s.RoomId == studio.Id || s.FacilitatorIds.Contains(ada.Id));

        // Next year starts without them: nothing there names them, and its first week was copied without them.
        var next = NextYear.Make(t, "Next", keepClosures: false);
        Assert.Equal((false, false, false), (next.Classes.Contains(acro.Id), next.Rooms.Contains(studio.Id), next.Facilitators.Contains(ada.Id)));
        Assert.Equal((true, true, true), (next.Classes.Contains(aerial.Id), next.Rooms.Contains(hall.Id), next.Facilitators.Contains(bo.Id)));
        Assert.NotEmpty(next.Sessions);
        Assert.DoesNotContain(next.Sessions, s => s.ClassId == acro.Id || s.RoomId == studio.Id || s.FacilitatorIds.Contains(ada.Id));
    }

    private sealed class CopyComparer : IEqualityComparer<(Guid?, Guid?, Guid[])>
    {
        public bool Equals((Guid?, Guid?, Guid[]) x, (Guid?, Guid?, Guid[]) y) => x.Item1 == y.Item1 && x.Item2 == y.Item2 && x.Item3.SequenceEqual(y.Item3);
        public int GetHashCode((Guid?, Guid?, Guid[]) x) => 0;
    }

    [Fact]
    public void BringingBackOffersItAgainAndPutsBackNothingDeletingTookOff()
    {
        var b = new Build();
        var acro = b.Class("Acro");
        b.Session(LastWeek, "09:00", "10:00", cls: acro);
        var ahead = b.Session(Thursday, "09:00", "10:00", cls: acro);
        var t = Apply.Batch(b.T, Edits.RemoveClass(b.T, acro.Id, Thursday));

        t = Apply.Batch(t, Edits.BringBack(t, t.Classes[acro.Id]!));

        Assert.False(t.Classes[acro.Id]!.IsFormer());
        Assert.Contains(t.Classes.Current(), c => c.Id == acro.Id);
        Assert.Null(t.Sessions[ahead.Id]!.ClassId);
    }

    [Fact]
    public void AMergeRepointsEveryClassTheEndedWeeksIncludedAndChangesNothingElseThere()
    {
        var b = new Build();
        var merged = b.Class("Floor Acrobatic");
        var keep = b.Class("Floor Acrobatics");
        b.TimeRule(new TimeRule { Kind = TimeRuleKind.Preparation, ClassIds = [merged.Id] });
        var ended = b.Session(LastWeek, "09:00", "10:00", cls: merged);
        var ahead = b.Session(Thursday, "09:00", "10:00", cls: merged);
        var own = b.Session(Thursday, "11:00", "12:00", cls: merged);
        b.Put(own with { Title = "Acro showcase" });
        var planned = Edits.CopyOf(b.T, ahead, Week.DateOf(Week.ScratchMonday, ahead.Date.DayOfWeek), Guid.NewGuid());
        b.T = b.T with { Scratch = b.T.Scratch.Put(planned) };

        var batch = Edits.MergeClass(b.T, merged.Id, keep.Id, Thursday);
        Edits.CheckLocks(b.T, batch, Thursday);   // allowed into the ended week, which nothing else is
        var t = Apply.Batch(b.T, batch);

        Assert.Null(t.Classes[merged.Id]);
        Assert.Equal(4, t.Sessions.Count + t.Scratch.Count);
        Assert.All(t.Sessions.Concat(t.Scratch), s => Assert.Equal(keep.Id, s.ClassId));
        Assert.Equal(Json(b.T.Sessions[ended.Id]! with { ClassId = keep.Id }), Json(t.Sessions[ended.Id]));   // only the class moved there
        Assert.Equal(("Floor Acrobatics", "Acro showcase", "Floor Acrobatics"), (t.Sessions[ahead.Id]!.Title, t.Sessions[own.Id]!.Title, t.Scratch[planned.Id]!.Title));
        Assert.Empty(t.TimeRules.Single().ClassIds);
    }

    [Fact]
    public void AnEndedWeekStillRefusesAClassChangeThatIsNotAMergeAndAMergeThatChangesMore()
    {
        var b = new Build();
        var merged = b.Class("Floor Acrobatic");
        var keep = b.Class("Floor Acrobatics");
        var ended = b.Session(LastWeek, "09:00", "10:00", cls: merged);

        // Re-pointing alone, with no class merged away beside it, is changing what ran.
        var alone = new Draft(b.T).Put(Collections.Sessions, ended with { ClassId = keep.Id }).ToBatch("Changed a class");
        Assert.Throws<EditRefused>(() => Edits.CheckLocks(b.T, alone, Thursday));
        // Beside a merge, anything more than the class is refused too.
        var more = new Draft(b.T).Put(Collections.Sessions, ended with { ClassId = keep.Id, Start = new TimeOnly(8, 0) })
            .Remove(Collections.Classes, merged.Id).ToBatch("Merged");
        Assert.Throws<EditRefused>(() => Edits.CheckLocks(b.T, more, Thursday));
        // Its undo, re-pointed to the class put back beside it, is allowed; to any other class it is not.
        var t = Apply.Batch(b.T, Edits.MergeClass(b.T, merged.Id, keep.Id, Thursday));
        var undo = new Batch("Undo", [new Op(Collections.Classes.Name, merged.Id, null, JsonSerializer.SerializeToNode(merged, ModelJson.Options)),
            Collections.Sessions.Put(t, t.Sessions[ended.Id]! with { ClassId = merged.Id })]);
        Edits.CheckLocks(t, undo, Thursday);
        var elsewhere = new Batch("Undo", [new Op(Collections.Classes.Name, merged.Id, null, JsonSerializer.SerializeToNode(merged, ModelJson.Options)),
            Collections.Sessions.Put(t, t.Sessions[ended.Id]! with { ClassId = Guid.NewGuid() })]);
        Assert.Throws<EditRefused>(() => Edits.CheckLocks(t, elsewhere, Thursday));
    }

    [Fact]
    public void AMergeSaysWhatItChangesBeyondTheName()
    {
        var b = new Build();
        var acrobatics = new Area { Name = "Acrobatics" };
        b.T = b.T with { Areas = b.T.Areas.Put(acrobatics) };
        var merged = b.Class("Floor Acrobatic", Workload.DropIn);
        var keep = b.Class("Floor Acrobatics") with { AreaId = acrobatics.Id };
        b.T = b.T with { Classes = b.T.Classes.Put(keep) };
        b.TimeRule(new TimeRule { Name = "Prep", Kind = TimeRuleKind.Preparation, ClassIds = [merged.Id] });
        b.Session(LastWeek, "09:00", "10:00", cls: merged);
        b.Session(Thursday, "09:00", "10:00", cls: merged);

        Assert.Equal(
            ["2 classes will count as Floor Acrobatics, the days already run included.",
             "Their hours start counting towards Acrobatics.",
             "They start counting towards a student's hours.",
             "Prep is no longer credited for them."],
            Edits.MergeDifferences(b.T, merged.Id, keep.Id));
        // Two classes alike in everything but the name: only the count is said.
        var twin = b.Class("Floor Acrobatix", Workload.DropIn);
        Assert.Equal(["0 classes will count as Floor Acrobatix, the days already run included.", "Their hours stop counting towards Acrobatics."],
            Edits.MergeDifferences(b.T, keep.Id, twin.Id).Take(2));
        var body = new Area { Name = "Body" };
        b.T = b.T with { Areas = b.T.Areas.Put(body) };
        var dance = b.Class("Dance") with { AreaId = body.Id };
        b.T = b.T with { Classes = b.T.Classes.Put(dance) };
        Assert.Contains("Their hours count towards Body, not Acrobatics.", Edits.MergeDifferences(b.T, keep.Id, dance.Id));
        Assert.Single(Edits.MergeDifferences(b.T with { TimeRules = EntitySet<TimeRule>.Empty }, merged.Id, twin.Id));
        // An open session into a class that is not counted: neither was a student's hours, so nothing is said.
        var lunch = b.Class("Lunch", Workload.NotCounted);
        Assert.Single(Edits.MergeDifferences(b.T with { TimeRules = EntitySet<TimeRule>.Empty }, merged.Id, lunch.Id));
    }

    [Fact]
    public void AMergeIntoAFormerClassOrIntoItselfIsRefused()
    {
        var b = new Build();
        var merged = b.Class("Floor Acrobatic");
        var old = b.Class("Old acro");
        b.Session(LastWeek, "09:00", "10:00", cls: old);
        var t = Apply.Batch(b.T, Edits.RemoveClass(b.T, old.Id, Thursday));
        Assert.Throws<EditRefused>(() => Edits.MergeClass(t, merged.Id, old.Id, Thursday));
        Assert.Throws<EditRefused>(() => Edits.MergeClass(t, merged.Id, merged.Id, Thursday));
        // A former class can itself be merged into one in use.
        Assert.Null(Apply.Batch(t, Edits.MergeClass(t, old.Id, merged.Id, Thursday)).Classes[old.Id]);
    }
}
