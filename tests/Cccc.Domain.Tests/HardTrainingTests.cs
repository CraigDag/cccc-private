using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// How hard a group's day is on the body: hard classes with less than a rest between them run together, only
/// the time in them counts, only what the group is timetabled to be at is theirs, and the school's own limits
/// are advice beside the day, never a ring and never a refusal.
/// </summary>
public class HardTrainingTests
{
    private static string Said(HardDay d) =>
        string.Join(" / ", d.Stretches.Select(s => $"{s.Start:HH:mm}–{s.End:HH:mm} {Week.Duration(s.Hard)}"));

    private static Timetable Limits(Timetable t, double? inARow = 2, double? inADay = 4, int? rest = null) =>
        t with
        {
            Settings = t.Settings with
            {
                MostHardInARow = inARow is { } r ? TimeSpan.FromHours(r) : null,
                MostHardInADay = inADay is { } d ? TimeSpan.FromHours(d) : null,
                RestBetweenHard = rest is { } m ? TimeSpan.FromMinutes(m) : null,
            },
        };

    private static HardDay Day(Timetable t, Group g, DateOnly? date = null) =>
        HardTraining.For(t, Occurrences.ForWeek(t, date ?? Monday), g.Id, date ?? Monday);

    [Fact]
    public void HardClassesLessThanARestApartRunTogetherAndOnlyTheTimeInThemCounts()
    {
        var b = new Build();
        var ba = b.Group("BA 1");
        var (strength, acro, aerial) = (b.Class("Strength", effort: Effort.Hard), b.Class("Acrobatics", effort: Effort.Hard), b.Class("Aerial", effort: Effort.Hard));
        var juggling = b.Class("Juggling", effort: Effort.Moderate);
        var on = Audience.ForGroup(ba.Id);
        b.Session(Monday, "09:00", "10:30", on, cls: strength);
        b.Session(Monday, "10:45", "12:00", on, cls: acro);       // a quarter of an hour: no rest
        b.Session(Monday, "12:30", "13:30", on, cls: aerial);     // half an hour: a rest, so a stretch of its own
        b.Session(Monday, "13:30", "14:30", on, cls: juggling);   // moderate: not hard, so it is time away from it
        b.Session(Monday, "14:30", "15:00", on, cls: acro);
        var day = Day(b.T, ba);
        Assert.Equal("09:00–12:00 2h 45m / 12:30–13:30 1h / 14:30–15:00 30m", Said(day));
        Assert.Equal(TimeSpan.FromMinutes(255), day.Total);

        // The school's own rest: under an hour, the aerial joins the morning and the last half hour the aerial.
        Assert.Equal("09:00–13:30 3h 45m / 14:30–15:00 30m", Said(Day(Limits(b.T, rest: 60), ba)));
        Assert.Equal("09:00–15:00 4h 15m", Said(Day(Limits(b.T, rest: 61), ba)));
    }

    [Fact]
    public void AnOverlapCountsOnceAndBackToBackIsOneStretchWhateverTheRest()
    {
        var b = new Build();
        var ba = b.Group("BA 1");
        var acro = b.Class("Acrobatics", effort: Effort.Hard);
        var on = Audience.ForGroup(ba.Id);
        b.Session(Monday, "09:00", "10:00", on, cls: acro);
        b.Session(Monday, "09:30", "11:00", on, cls: acro);       // the group double-booked: the hour and a half once
        b.Session(Monday, "09:40", "09:50", on, cls: acro);       // inside both
        b.Session(Monday, "11:00", "12:00", on, cls: acro);       // straight after
        Assert.Equal("09:00–12:00 3h", Said(Day(b.T, ba)));
        Assert.Equal("09:00–12:00 3h", Said(Day(Limits(b.T, rest: 0), ba)));
        Assert.Equal(4, Day(b.T, ba).Stretches.Single().SessionIds.Count);
    }

    [Fact]
    public void OnlyHardTrainingTheGroupIsTimetabledToBeAtIsTheirs()
    {
        var b = new Build();
        var degree = b.Programme("Degree");
        var (ba, ba2, prep) = (b.Group("BA 1", degree), b.Group("BA 2", degree), b.Group("Prep"));
        var hard = b.Class("Acrobatics", effort: Effort.Hard);
        var ada = b.Facilitator("Ada");
        b.Away(ada, Monday, "07:00", "09:00");
        // Theirs: their own, the programme's, the whole building's, and one waiting for cover (they are still there).
        b.Session(Monday, "07:00", "08:00", Audience.ForGroup(ba.Id), cls: hard, who: In(ada));
        b.Session(Monday, "09:00", "09:30", new Audience { Kind = AudienceKind.Programme, ProgrammeId = degree.Id, GroupIds = [ba.Id, ba2.Id] }, cls: hard);
        b.Session(Monday, "10:00", "10:15", Audience.Everyone, cls: hard);
        // Not theirs: another group's, an open session, a break, cancelled, a class not said or not hard, no class at all.
        b.Session(Monday, "11:00", "12:00", Audience.ForGroup(prep.Id), cls: hard);
        b.Session(Monday, "12:00", "13:00", Audience.ForGroup(ba.Id), cls: b.Class("Open training", Workload.DropIn, Effort.Hard));
        b.Session(Monday, "13:00", "14:00", Audience.ForGroup(ba.Id), cls: b.Class("Warm lunch", Workload.NotCounted, Effort.Hard));
        b.Put(b.Session(Monday, "14:00", "15:00", Audience.ForGroup(ba.Id), cls: hard) with { Cancelled = true });
        b.Session(Monday, "15:00", "16:00", Audience.ForGroup(ba.Id), cls: b.Class("Juggling", effort: Effort.Moderate));
        b.Session(Monday, "16:00", "17:00", Audience.ForGroup(ba.Id), cls: b.Class("Theory"));
        b.Session(Monday, "17:00", "18:00", Audience.ForGroup(ba.Id));
        Assert.Equal("07:00–08:00 1h / 09:00–09:30 30m / 10:00–10:15 15m", Said(Day(b.T, ba)));
        Assert.Equal("09:00–09:30 30m / 10:00–10:15 15m", Said(Day(b.T, ba2)));
        Assert.Equal("10:00–10:15 15m / 11:00–12:00 1h", Said(Day(b.T, prep)));
        // A day the building is closed has none.
        b.Closure(new Closure { Name = "Closed", From = Monday, To = Monday });
        Assert.Empty(Day(b.T, ba).Stretches);
    }

    [Fact]
    public void TheSchoolsLimitsAreAdviceBesideTheDayAndNoLimitIsNoWarning()
    {
        var b = new Build();
        var (ba, prep) = (b.Group("BA 1"), b.Group("Prep"));
        var (strength, acro) = (b.Class("Strength", effort: Effort.Hard), b.Class("Acrobatics", effort: Effort.Hard));
        var on = Audience.ForGroup(ba.Id);
        var s1 = b.Session(Monday, "09:00", "10:30", on, cls: strength);
        var s2 = b.Session(Monday, "10:45", "12:00", on, cls: acro);
        var s3 = b.Session(Monday, "13:00", "15:00", on, cls: acro);      // exactly two hours in a row: not over
        b.Session(Monday, "09:00", "11:00", Audience.ForGroup(prep.Id), cls: acro);
        Assert.DoesNotContain(Clashes.ForWeek(b.T, Monday), c => c.IsHardTraining);
        Assert.Equal(TimeSpan.FromMinutes(285), Day(b.T, ba).Total);      // still there to be looked at

        var t = Limits(b.T);
        var warned = Clashes.ForWeek(t, Monday).Where(c => c.IsHardTraining).ToList();
        Assert.Equal(
        [
            "BA 1: 2h 45m of hard training in a row, Mon 09:00–12:00 (at most 2h without a 30m rest) — \"Strength\", \"Acrobatics\"",
            "BA 1: 4h 45m of hard training on Mon (at most 4h in a day)",
        ], warned.Select(c => c.Description));
        Assert.Equal([s1.Id, s2.Id], warned[0].SessionIds);
        Assert.Equal([s1.Id, s2.Id, s3.Id], warned[1].SessionIds);
        Assert.All(warned, c => Assert.False(c.IsRing));
        Assert.Empty(WaysOut.InClash(t, s2));
        Assert.True(Day(t, ba).Over);
        Assert.False(Day(t, prep).Over);

        // Exactly the limit is not past it: a day of 4h 45m against a day's 4h 45m, a stretch of 2h 45m against 2h 45m.
        Assert.DoesNotContain(Clashes.ForWeek(Limits(b.T, inARow: 2.75, inADay: 4.75), Monday), c => c.IsHardTraining);
        Assert.Single(Clashes.ForWeek(Limits(b.T, inARow: 2.75, inADay: 4.74), Monday), c => c.IsHardTraining);

        // One limit without the other warns of that one alone.
        Assert.Equal([ClashKind.GroupHardInADay], Clashes.ForWeek(Limits(b.T, inARow: null), Monday).Where(c => c.IsHardTraining).Select(c => c.Kind));
        Assert.Equal([ClashKind.GroupHardInARow], Clashes.ForWeek(Limits(b.T, inADay: null), Monday).Where(c => c.IsHardTraining).Select(c => c.Kind));
    }

    [Fact]
    public void AddingOrFindingATimeSaysWhatItWouldDoToAGroupsDay()
    {
        var b = new Build();
        var ba = b.Group("BA 1");
        var acro = b.Class("Acrobatics", effort: Effort.Hard);
        var gym = b.Room("Gym");
        var on = Audience.ForGroup(ba.Id);
        b.Session(Monday, "09:00", "10:30", on, gym, acro);
        var t = Limits(b.T);
        var proposal = new Session { Date = Monday, Start = new(10, 30), End = new(11, 30), Title = "Acrobatics", ClassId = acro.Id, Audience = on, RoomId = gym.Id };
        Assert.Equal(["BA 1: 2h 30m of hard training in a row, Mon 09:00–11:30 (at most 2h without a 30m rest) — \"Acrobatics\", \"Acrobatics\""],
            Clashes.CausedBy(t, proposal).Select(c => c.Description));
        Assert.Contains(Openings.NotesAt(t, proposal, EventViewer.Administrator), n => !n.Good && n.Text.StartsWith("BA 1: 2h 30m of hard training in a row"));
        // After the rest, nothing to say about it.
        var later = proposal with { Start = new(11, 0), End = new(12, 0) };
        Assert.Empty(Clashes.CausedBy(t, later));
    }

    [Fact]
    public void TheMadeUpSchoolHasAHardMondayMorningAndAHardWednesdayMorning()
    {
        var monday = new DateOnly(2026, 11, 16);
        var t = SampleSchool.Create(monday, monday);
        Assert.Equal(
        [
            "BA 1: 2h 45m of hard training in a row, Mon 09:00–12:00 (at most 2h without a 30m rest) — \"Strength and conditioning\", \"Acrobatics\"",
            "BA 1: 4h 45m of hard training on Mon (at most 4h in a day)",
            "BA 2: 2h 45m of hard training in a row, Wed 09:00–12:00 (at most 2h without a 30m rest) — \"Strength and conditioning\", \"Aerial\"",
        ], Clashes.ForWeek(t, monday).Where(c => c.IsHardTraining).Select(c => c.Description));
    }
}
