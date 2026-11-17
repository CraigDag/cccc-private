using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// <see cref="Hours.Lines"/>: every piece of time in a facilitator's hours, one line each, adding up to
/// the total the reports and My week show.
/// </summary>
public class HoursLinesTests
{
    [Fact]
    public void EveryFacilitatorsLinesAddUpToTheirTotalInEveryWeekOfTheMadeUpSchool()
    {
        var day = new DateOnly(2030, 3, 6);
        var t = SampleSchool.Create(day, day);
        var kinds = new HashSet<string>();
        int weeksWithHours = 0;
        foreach (var monday in t.Semesters.SelectMany(s => s.Mondays()).Distinct())
        {
            var week = Occurrences.HappeningInWeek(t, monday);
            var totals = Hours.ByFacilitator(t, week);
            foreach (var f in t.Facilitators)
            {
                var lines = Hours.Lines(t, week, f.Id);
                var sum = lines.Aggregate(TimeSpan.Zero, (a, l) => a + l.Time);
                Assert.True(totals.GetValueOrDefault(f.Id)?.Total == sum || (sum == TimeSpan.Zero && !totals.ContainsKey(f.Id)),
                    $"{f.Name}, week of {monday}: the lines add up to {sum}, the total is {totals.GetValueOrDefault(f.Id)?.Total}");
                Assert.All(lines, l => Assert.True(l.Time > TimeSpan.Zero, "a line of no time"));
                if (sum > TimeSpan.Zero) weeksWithHours++;
                foreach (var l in lines) kinds.Add(l.Kind == HoursLineKind.Rule ? "rule:" + l.Rule!.Kind : l.Kind.ToString());
            }
        }
        // Not about nothing: the made-up school has teaching, cover, preparation and a paid break in it.
        Assert.True(weeksWithHours > 20, $"only {weeksWithHours} facilitator-weeks with hours");
        Assert.Equal(["Covering", "Teaching", "rule:Break", "rule:Preparation"], kinds.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ACoveredClassIsALineOfTheCoverersNamingWhoTheyStoodInForAndNoneOfThePersonAway()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var cleo = b.Facilitator("Cleo");
        var s = b.Session(Monday, "09:00", "11:00", null, null, null, In(ada));
        b.Away(ada, Monday);
        b.Put(s with { CoverIds = [cleo.Id] });
        var week = Occurrences.HappeningInWeek(b.T, Monday);

        var line = Assert.Single(Hours.Lines(b.T, week, cleo.Id));
        Assert.Equal(HoursLineKind.Covering, line.Kind);
        Assert.Equal([ada.Id], line.Covering);
        Assert.Equal(TimeSpan.FromHours(2), line.Time);
        Assert.Empty(Hours.Lines(b.T, week, ada.Id));
    }

    [Fact]
    public void APersonInPartOfAClassHasALineOfTheirOwnPartOnly()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        b.Session(Monday, "13:00", "16:00", null, null, null, In(ada), In(bo, from: "14:00", until: "15:00"));
        var week = Occurrences.HappeningInWeek(b.T, Monday);

        var line = Assert.Single(Hours.Lines(b.T, week, bo.Id));
        Assert.Equal((new TimeOnly(14, 0), new TimeOnly(15, 0), TimeSpan.FromHours(1), HoursLineKind.Teaching), (line.From, line.Until, line.Time, line.Kind));
        Assert.Equal(TimeSpan.FromHours(3), Assert.Single(Hours.Lines(b.T, week, ada.Id)).Time);
    }

    [Fact]
    public void ACreditFollowsTheClassItWasEarnedOnAndABreakSitsAtItsOwnTime()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var creation = b.Class("Creation");
        var lunch = b.Class("Lunch", Workload.NotCounted);
        var prep = new TimeRule { Name = "Prep", Kind = TimeRuleKind.Preparation, Percent = 50, ClassIds = [creation.Id] };
        var paid = new TimeRule { Name = "Paid lunch", Kind = TimeRuleKind.Break, Minutes = 30, ClassIds = [lunch.Id] };
        b.TimeRule(prep);
        b.TimeRule(paid);
        b.Session(Monday, "13:00", "16:00", null, null, creation, In(ada), In(bo, from: "14:00", until: "15:00"));
        b.Session(Monday, "12:00", "12:30", null, null, lunch);
        b.Session(Monday, "09:00", "12:00", null, null, null, In(ada));
        var week = Occurrences.HappeningInWeek(b.T, Monday);

        var lines = Hours.Lines(b.T, week, ada.Id);
        Assert.Equal(
            [("09:00", HoursLineKind.Teaching, null, 180), ("12:00", HoursLineKind.Rule, "Paid lunch", 30),
             ("13:00", HoursLineKind.Teaching, null, 180), ("13:00", HoursLineKind.Rule, "Prep", 90)],
            lines.Select(l => (Week.Hm(l.Occurrence.Start), l.Kind, l.Rule?.Name, (int)l.Time.TotalMinutes)));
        Assert.Equal(TimeSpan.FromHours(3), lines[3].Of);
        Assert.Equal(Hours.For(b.T, week, ada.Id).Total, lines.Aggregate(TimeSpan.Zero, (a, l) => a + l.Time));
        // Somebody in part of the class earns on their part, and the line says what it was taken of.
        var hers = Hours.Lines(b.T, week, bo.Id);
        Assert.Equal([(HoursLineKind.Teaching, 60), (HoursLineKind.Rule, 30)], hers.Select(l => (l.Kind, (int)l.Time.TotalMinutes)));
        Assert.Equal(TimeSpan.FromHours(1), hers[1].Of);
    }

    [Fact]
    public void ACancelledClassAndAGapAwaitingCoverAreNobodysLine()
    {
        var b = new Build();
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var gap = b.Session(Monday, "09:00", "11:00", null, null, null, In(ada));
        b.Away(ada, Monday);
        var cancelled = b.Session(Monday.AddDays(1), "09:00", "11:00", null, null, null, In(bo));
        b.Put(cancelled with { Cancelled = true });
        b.Session(Monday.AddDays(2), "09:00", "10:00", null, null, null, In(bo));
        var week = Occurrences.HappeningInWeek(b.T, Monday);

        Assert.Contains(week, o => o.Session.Id == gap.Id && o.NeedsCover);
        Assert.Empty(Hours.Lines(b.T, week, ada.Id));
        Assert.Equal(Monday.AddDays(2), Assert.Single(Hours.Lines(b.T, week, bo.Id)).Date);
    }
}

/// <summary>
/// <see cref="Hours.StudentLines"/>: every class a student is on, each saying whether it counts towards
/// their load and why not, the ones that count adding up to <see cref="Hours.StudentLoad"/>.
/// </summary>
public class StudentLinesTests
{
    [Fact]
    public void AStudentsLoadIsWhatItWasAndTheLinesThatCountAddUpToIt()
    {
        var day = new DateOnly(2030, 3, 6);
        var t = SampleSchool.Create(day, day);
        int withLoad = 0;
        var reasons = new HashSet<string>();
        foreach (var monday in t.Semesters.SelectMany(s => s.Mondays()).Distinct())
        {
            var all = Occurrences.ForWeek(t, monday);
            foreach (var st in t.Students)
            {
                // The load as it was defined before the lines existed: Counted, happening, on their roster.
                var before = all.Where(o => o.Happens && Hours.IsRequired(t, o.Session) && Roster.Includes(t, o.Session, st.Id))
                                .Aggregate(TimeSpan.Zero, (a, o) => a + o.Session.Length);
                Assert.Equal(before, Hours.StudentLoad(t, all, st.Id));
                var lines = Hours.StudentLines(t, all, st.Id);
                Assert.Equal(before, lines.Where(l => l.Counted).Aggregate(TimeSpan.Zero, (a, l) => a + l.Time));
                if (before > TimeSpan.Zero) withLoad++;
                foreach (var l in lines.Where(l => !l.Counted)) reasons.Add(l.NotCounted!.Split(':')[0]);
            }
        }
        Assert.True(withLoad > 50, $"only {withLoad} student-weeks with a load");
        Assert.NotEmpty(reasons);
    }

    [Fact]
    public void AClassThatDoesNotCountSaysWhyAndAClassWaitingForCoverStillCounts()
    {
        var b = new Build();
        var ba = b.Group("BA 1");
        var alba = b.Student("Alba", ba);
        var ada = b.Facilitator("Ada");
        var open = b.Class("Open training", Workload.DropIn);
        var lunch = b.Class("Lunch", Workload.NotCounted);
        var forBa = Audience.ForGroup(ba.Id);
        b.Session(Monday, "09:00", "11:00", forBa);
        b.Session(Monday, "11:00", "12:00", forBa, null, open);
        b.Session(Monday, "12:00", "12:30", forBa, null, lunch);
        var cancelled = b.Session(Monday.AddDays(1), "09:00", "11:00", forBa);
        b.Put(cancelled with { Cancelled = true });
        b.Closure(new Closure { Name = "Study day", From = Monday.AddDays(2), To = Monday.AddDays(2), Scope = ClosureScope.WholeBuilding });
        b.Session(Monday.AddDays(2), "09:00", "11:00", forBa);
        b.Session(Monday.AddDays(3), "13:00", "15:00", forBa, null, null, In(ada));
        b.Away(ada, Monday.AddDays(3));

        var lines = Hours.StudentLines(b.T, Occurrences.ForWeek(b.T, Monday), alba.Id);
        Assert.Equal(
            [("Mon 09:00", null), ("Mon 11:00", "Open session"), ("Mon 12:00", "Not counted"), ("Tue 09:00", "Cancelled"),
             ("Wed 09:00", "Closed: Study day"), ("Thu 13:00", null)],
            lines.Select(l => ($"{Week.ShortDay(l.Occurrence.Day)} {Week.Hm(l.Occurrence.Start)}", l.NotCounted)));
        Assert.True(lines[^1].Occurrence.NeedsCover);
        Assert.Equal(TimeSpan.FromHours(4), Hours.StudentLoad(b.T, Occurrences.ForWeek(b.T, Monday), alba.Id));
    }

    [Fact]
    public void SomebodyPutOnByHandCountsSomebodyLeftOffHasNoLineAndAWholeBuildingClassIsNobodys()
    {
        var b = new Build();
        var ba = b.Group("BA 1");
        var prep = b.Group("Prep");
        var alba = b.Student("Alba", ba);
        var bo = b.Student("Bo", ba);
        var cleo = b.Student("Cleo", prep);
        var s = b.Session(Monday, "09:00", "11:00", Audience.ForGroup(ba.Id));
        b.Put(s with { LeftOffStudentIds = [bo.Id], AddedStudentIds = [cleo.Id] });
        b.Session(Monday, "13:00", "14:00", Audience.Everyone);
        var week = Occurrences.ForWeek(b.T, Monday);

        Assert.Equal(TimeSpan.FromHours(2), Assert.Single(Hours.StudentLines(b.T, week, alba.Id)).Time);
        Assert.Empty(Hours.StudentLines(b.T, week, bo.Id));
        Assert.True(Assert.Single(Hours.StudentLines(b.T, week, cleo.Id)).Counted);
    }

    [Fact]
    public void TheTargetAppliesInsideASemesterAndNeverToAVisitingProgrammesStudent()
    {
        var b = new Build();
        var ba = b.Group("BA 1");
        var troupe = b.Group("Troupe", b.Programme("Visiting company", guest: true));
        var alba = b.Student("Alba", ba);
        var wren = b.Student("Wren", troupe);
        var nils = b.Student("Nils");
        b.T = b.T with { Settings = b.T.Settings with { WeeklyScheduledTarget = TimeSpan.FromHours(3) } };
        var lastWeek = b.T.Semesters.Single().LastMonday;

        Assert.Equal(TimeSpan.FromHours(3), Hours.StudentTarget(b.T, Monday.AddDays(2), alba.Id));
        Assert.Equal(TimeSpan.FromHours(3), Hours.StudentTarget(b.T, lastWeek.AddDays(6), alba.Id));
        Assert.Equal(TimeSpan.FromHours(3), Hours.StudentTarget(b.T, Monday, nils.Id));
        Assert.Null(Hours.StudentTarget(b.T, lastWeek.AddDays(7), alba.Id));
        Assert.Null(Hours.StudentTarget(b.T, Monday, wren.Id));
        // A school with no semesters is never outside one; a school with no target has none anywhere.
        Assert.Equal(TimeSpan.FromHours(3), Hours.StudentTarget(b.T with { Semesters = EntitySet<Semester>.Empty }, lastWeek.AddDays(7), alba.Id));
        Assert.Null(Hours.StudentTarget(b.T with { Settings = b.T.Settings with { WeeklyScheduledTarget = null } }, Monday, alba.Id));
    }

    [Fact]
    public void AWeekUnderTheTargetIsShortAndAWeekWithNothingCountedIsNotCalledShort()
    {
        var b = new Build();
        var ba = b.Group("BA 1");
        var alba = b.Student("Alba", ba);
        var forBa = Audience.ForGroup(ba.Id);
        b.T = b.T with { Settings = b.T.Settings with { WeeklyScheduledTarget = TimeSpan.FromHours(3) } };
        DateOnly shortWeek = Monday, fullWeek = Monday.AddDays(7), closedWeek = Monday.AddDays(14), blankWeek = Monday.AddDays(21);
        b.Session(shortWeek, "09:00", "11:00", forBa);
        b.Session(fullWeek, "09:00", "12:00", forBa);
        b.Session(closedWeek, "09:00", "12:00", forBa);
        b.Closure(new Closure { Name = "Winter break", From = closedWeek, To = closedWeek.AddDays(6), Scope = ClosureScope.WholeBuilding });
        StudentWeek Of(DateOnly m) => Hours.ForStudent(b.T, m, Occurrences.ForWeek(b.T, m), alba.Id);

        Assert.Equal((true, true, TimeSpan.FromHours(1)), (Of(shortWeek).Measured, Of(shortWeek).Short, Of(shortWeek).ShortBy));
        Assert.Equal((true, false, TimeSpan.Zero), (Of(fullWeek).Measured, Of(fullWeek).Short, Of(fullWeek).ShortBy));
        // Closed all week: a line saying so, nothing counted, a target, and still not called short.
        var closed = Of(closedWeek);
        Assert.Equal((1, TimeSpan.Zero, (TimeSpan?)TimeSpan.FromHours(3), false, false, TimeSpan.Zero), (closed.NotCounted, closed.Counted, closed.Target, closed.Measured, closed.Short, closed.ShortBy));
        Assert.Equal("Closed: Winter break", Assert.Single(closed.Lines).NotCounted);
        var blank = Of(blankWeek);
        Assert.Equal((0, false), (blank.Lines.Count, blank.Short));
    }
}
