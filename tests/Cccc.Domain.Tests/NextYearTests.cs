using System.Globalization;
using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Sample;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>
/// <see cref="NextYear"/>: next year is this year's shape with an empty history, every semester starting
/// in the same week number. Built on 2026/27 on purpose: 2026 holds an ISO week 53, which is where moving
/// 52 weeks on lands a week early.
/// </summary>
public class NextYearTests
{
    private static readonly DateOnly AutumnStart = new(2026, 8, 17);     // week 34 of 2026
    private static readonly DateOnly SpringStart = new(2027, 2, 1);      // week 5 of 2027

    private static (int Year, int Week) Iso(DateOnly d)
    {
        var dt = d.ToDateTime(TimeOnly.MinValue);
        return (ISOWeek.GetYear(dt), ISOWeek.GetWeekOfYear(dt));
    }

    private static Build School()
    {
        var b = new Build();
        b.T = b.T with
        {
            Settings = b.T.Settings with { Name = "School year 2026/27", WeeklyScheduledTarget = TimeSpan.FromHours(15) },
            Semesters = EntitySet<Semester>.Of(
            [
                new Semester { Name = "Autumn semester", StartMonday = AutumnStart, WeekCount = 22 },
                new Semester { Name = "Spring semester", StartMonday = SpringStart, WeekCount = 20 },
            ]),
        };
        return b;
    }

    [Fact]
    public void EverySemesterStartsInTheSameWeekNumberNextYearEvenAfterAYearWithAWeek53()
    {
        var next = NextYear.Make(School().T, "Next", keepClosures: false);
        var autumn = next.Semesters.Single(s => s.Name == "Autumn semester");
        var spring = next.Semesters.Single(s => s.Name == "Spring semester");
        Assert.Equal(((2027, 34), 22), (Iso(autumn.FirstMonday), autumn.WeekCount));
        Assert.Equal(((2028, 5), 20), (Iso(spring.FirstMonday), spring.WeekCount));
        // 52 weeks on would have been week 33: the reason this is not a fixed shift.
        Assert.Equal((2027, 33), Iso(AutumnStart.AddDays(364)));
        // A year with no week 53 moves 52 weeks; week 53 itself lands on next year's last week.
        Assert.Equal(new DateOnly(2026, 8, 17), NextYear.SameWeekNextYear(new DateOnly(2025, 8, 20)));
        Assert.Equal((2027, 52), Iso(NextYear.SameWeekNextYear(new DateOnly(2026, 12, 31))));
    }

    [Fact]
    public void TheFirstTimetabledWeekCarriesOverOntoNextYearsAndEveryOtherWeekAndRegisterIsDropped()
    {
        var b = School();
        var ada = b.Facilitator("Ada");
        var cleo = b.Facilitator("Cleo");
        var room = b.Room("Studio A");
        b.Session(AutumnStart.AddDays(-21), "10:00", "12:00", null, room);                   // a summer course before the year
        var acro = b.Session(AutumnStart, "09:00", "11:00", null, room, null, In(ada));
        var dance = b.Session(AutumnStart.AddDays(3), "13:00", "15:00", null, room, null, In(ada, until: "14:00"));
        b.Put(acro with { Cancelled = true, CancelNote = "Rain", CoverIds = [cleo.Id], CoverNote = "Cleo" });
        b.Session(AutumnStart.AddDays(7), "09:00", "11:00", null, room, null, In(ada));
        b.Session(SpringStart, "09:00", "11:00", null, room, null, In(ada));
        b.T = b.T with { Registers = b.T.Registers.Put(new Register { Id = dance.Id, PresentIds = [] }) };

        var next = NextYear.Make(b.T, "Next", keepClosures: false);

        var nextStart = new DateOnly(2027, 8, 23);
        Assert.Equal(
            [(nextStart, "09:00", false, 0, (string?)null), (nextStart.AddDays(3), "13:00", false, 0, null)],
            next.Sessions.OrderBy(s => s.Date).Select(s => (s.Date, Week.Hm(s.Start), s.Cancelled, s.CoverIds.Count, s.CancelNote)));
        // What a class is carries over (the room, who teaches and their part); what happened to it that week does not.
        var nextDance = next.Sessions.Single(s => s.Date == nextStart.AddDays(3));
        Assert.Equal((room.Id, dance.Facilitators), (nextDance.RoomId, nextDance.Facilitators));
        Assert.DoesNotContain(next.Sessions, s => b.T.Sessions.Contains(s.Id));
        Assert.Empty(next.Registers);
    }

    [Fact]
    public void EventsAndAbsencesWithADateInTheNewYearStayWhereTheyAreAndTheRestStayWithThisYear()
    {
        var b = School();
        var ada = b.Facilitator("Ada");
        var gone = new SchoolEvent { Title = "Graduation show", Date = new DateOnly(2027, 6, 18) };
        var booked = new SchoolEvent { Title = "Auditions", Date = new DateOnly(2027, 9, 10) };
        var running = new SchoolEvent { Title = "Staff meeting", Date = new DateOnly(2027, 6, 7), Repeat = new Repeat { Days = [DayOfWeek.Monday], Count = 14 } };
        var acrossTheSummer = new Absence { Person = AbsentPerson.Facilitator, PersonId = ada.Id, From = new DateOnly(2027, 6, 21), To = new DateOnly(2027, 9, 3) };
        var autumnIllness = new Absence { Person = AbsentPerson.Facilitator, PersonId = ada.Id, From = new DateOnly(2026, 10, 1), To = new DateOnly(2026, 10, 2) };
        b.T = b.T with
        {
            Events = EntitySet<SchoolEvent>.Of([gone, booked, running]),
            Absences = EntitySet<Absence>.Of([acrossTheSummer, autumnIllness]),
        };

        var next = NextYear.Make(b.T, "Next", keepClosures: false);

        Assert.Equal([booked, running], next.Events.OrderBy(e => e.Title, StringComparer.Ordinal));
        Assert.Equal([acrossTheSummer], next.Absences);
    }

    [Fact]
    public void ClosuresGoUnlessKeptAndAKeptOneMovesWithItsSemesterSoAWeekNumberedOneLandsRight()
    {
        var b = School();
        var autumnBreak = new Closure { Name = "Autumn break", From = new DateOnly(2026, 10, 12), To = new DateOnly(2026, 10, 18) };   // week 42
        var winterBreak = new Closure { Name = "Winter break", From = new DateOnly(2027, 2, 15), To = new DateOnly(2027, 2, 19) };     // week 7
        var summer = new Closure { Name = "Summer course week", From = new DateOnly(2026, 7, 27), To = new DateOnly(2026, 7, 31) };   // before any semester
        b.Closure(autumnBreak);
        b.Closure(winterBreak);
        b.Closure(summer);

        Assert.Empty(NextYear.Make(b.T, "Next", keepClosures: false).Closures);
        var kept = NextYear.Make(b.T, "Next", keepClosures: true).Closures.ToDictionary(c => c.Name);
        Assert.Equal(((2027, 42), 6), (Iso(kept["Autumn break"].From), kept["Autumn break"].To.DayNumber - kept["Autumn break"].From.DayNumber));
        Assert.Equal((2028, 7), Iso(kept["Winter break"].From));
        // Outside every semester: it moves with the nearest one, the autumn.
        Assert.Equal(new DateOnly(2026, 7, 27).AddDays(371), kept["Summer course week"].From);
    }

    [Fact]
    public void EverythingElseCarriesOverAsItIsAndNoPartOfTheTimetableIsForgotten()
    {
        var t = SampleSchool.Create(new DateOnly(2030, 3, 6), new DateOnly(2030, 3, 6));
        var next = NextYear.Make(t, "  Northlight 2031  ", keepClosures: false);

        Assert.Equal(t.Settings with { Name = "Northlight 2031" }, next.Settings);
        Assert.Same(t.Rooms, next.Rooms);
        Assert.Same(t.Programmes, next.Programmes);
        Assert.Same(t.Groups, next.Groups);
        Assert.Same(t.Students, next.Students);
        Assert.Same(t.Facilitators, next.Facilitators);
        Assert.Same(t.Classes, next.Classes);
        Assert.Same(t.TimeRules, next.TimeRules);
        Assert.Same(t.Areas, next.Areas);
        Assert.Same(t.Branches, next.Branches);
        Assert.Same(t.Disciplines, next.Disciplines);
        Assert.Same(t.Scratch, next.Scratch);
        Assert.NotEmpty(next.Sessions);
        // A collection added to the timetable later has to be decided about here, or this goes red: a
        // hand-written list of what carries over is how a rollover silently drops something.
        string[] decided = ["Settings", "Semesters", "Closures", "Rooms", "Programmes", "Groups", "Branches", "Disciplines", "Students",
                            "Facilitators", "Areas", "Classes", "TimeRules", "Sessions", "Registers", "Absences", "Events", "Scratch"];
        Assert.Equal(decided.Order(StringComparer.Ordinal),
            typeof(Timetable).GetProperties().Where(p => p.CanWrite).Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ThePlanCountsExactlyWhatMakeDoes()
    {
        var t = SampleSchool.Create(new DateOnly(2030, 3, 6), new DateOnly(2030, 3, 6));
        var plan = NextYear.For(t)!;
        var next = NextYear.Make(t, "Next", keepClosures: true);

        Assert.Equal(plan.Classes, next.Sessions.Count);
        Assert.Equal(t.Sessions.Count, plan.Classes + plan.OtherClasses);
        Assert.Equal(plan.FirstWeekTo, Assert.Single(next.Sessions.Select(s => s.WeekMonday).Distinct()));
        Assert.Equal((plan.EventsKept, plan.AbsencesKept, plan.Closures, plan.ScratchClasses), (next.Events.Count, next.Absences.Count, next.Closures.Count, next.Scratch.Count));
        Assert.Equal(t.Events.Count, plan.EventsKept + plan.EventsDropped);
        Assert.Equal(t.Registers.Count, plan.Registers);
        Assert.True(plan.Classes > 10 && plan.OtherClasses > 10 && plan.Registers > 10, $"{plan.Classes} carried, {plan.OtherClasses} dropped, {plan.Registers} registers");
        Assert.Equal(next.Semesters.Min(s => s.FirstMonday), plan.Starts);
    }

    [Fact]
    public void NoSemestersIsNoYearToRollForward()
    {
        var t = new Build().T with { Semesters = EntitySet<Semester>.Empty };
        Assert.Null(NextYear.For(t));
        Assert.Throws<EditRefused>(() => NextYear.Make(t, "Next", keepClosures: false));
    }

    [Theory]
    [InlineData("School year 2026/27", "School year 2027/28")]
    [InlineData("2026–2027", "2027–2028")]
    [InlineData("Autumn 2026", "Autumn 2027")]
    [InlineData("2099/00 odd", "2100/01 odd")]
    [InlineData("Year 2098/99", "Year 2099/00")]
    [InlineData("Northlight", "Northlight, next year")]
    [InlineData("Room 12026", "Room 12026, next year")]
    public void ANameWithItsYearsMovesThemOn(string name, string expected) => Assert.Equal(expected, NextYear.SuggestName(name));
}
