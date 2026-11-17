using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using static Cccc.Domain.Tests.Build;

namespace Cccc.Domain.Tests;

/// <summary>What changed from one week to the next, and a semester's weeks side by side. Every date is fixed.</summary>
public class TermTests
{
    private static readonly DateOnly SampleMonday = new(2026, 11, 16);
    private static readonly DateOnly SampleToday = SampleMonday.AddDays(1);
    private static readonly DateOnly Next = Monday.AddDays(7);

    private static string Line(Timetable t, WeekChange c) =>
        $"{c.Kind} {c.Either.DisplayTitle} {Week.ShortDay(c.Either.Date.DayOfWeek)} {Week.Hm(c.Either.Start)}";

    [Fact]
    public void AWeekCopiedFromTheOneBeforeIsNoChangeAndEachDifferenceIsFoundAgainByWhatTheClassIs()
    {
        var b = new Build();
        var ba1 = b.Group("BA 1");
        var ba2 = b.Group("BA 2");
        var bigTop = b.Room("Big Top");
        var studio = b.Room("Studio");
        var ada = b.Facilitator("Ada");
        var cleo = b.Facilitator("Cleo");
        var dag = b.Facilitator("Dag");
        var acro = b.Class("Acrobatics");
        var juggling = b.Class("Juggling");
        var dance = b.Class("Dance");
        var aerial = b.Class("Aerial");
        var lunch = b.Class("Lunch", Workload.NotCounted);
        foreach (var w in new[] { Monday, Next })
        {
            b.Session(w, "09:00", "11:00", Audience.ForGroup(ba1.Id), bigTop, acro, new Assignment(ada.Id));
            foreach (var d in Week.Days(w).Take(5)) b.Session(d, "12:00", "12:45", Audience.Everyone, null, lunch);
        }
        // The earlier week.
        b.Session(Monday.AddDays(1), "10:00", "11:00", Audience.ForGroup(ba2.Id), studio, juggling, new Assignment(cleo.Id));
        b.Session(Monday.AddDays(2), "09:00", "10:00", Audience.ForGroup(ba1.Id), studio, dance, new Assignment(cleo.Id));
        b.Session(Monday.AddDays(3), "14:00", "15:00", Audience.ForGroup(ba1.Id), bigTop);   // a class with no class, by its name
        // The later week: juggling moved to Wednesday; dance an hour later in the Big Top with Dag; acrobatics for BA 2
        // too, as a class of its own; the untitled Thursday class gone; aerial new on Friday.
        b.Session(Next.AddDays(2), "10:00", "11:00", Audience.ForGroup(ba2.Id), studio, juggling, new Assignment(cleo.Id));
        b.Session(Next.AddDays(2), "10:00", "11:00", Audience.ForGroup(ba1.Id), bigTop, dance, new Assignment(dag.Id));
        b.Session(Next, "09:00", "11:00", Audience.ForGroup(ba2.Id), studio, acro, new Assignment(ada.Id));
        b.Session(Next.AddDays(4), "13:00", "14:00", Audience.ForGroup(ba1.Id), studio, aerial);

        var changes = WeekChanges.Between(b.T, Monday, Next);
        Assert.Equal(
            [
                "Added Acrobatics Mon 09:00",
                "Changed Dance Wed 10:00",
                "Changed Juggling Wed 10:00",
                "Gone Class Thu 14:00",
                "Added Aerial Fri 13:00",
            ],
            changes.Select(c => Line(b.T, c)));
        var movedDance = changes[1];
        Assert.True(movedDance is { DayChanged: false, TimesChanged: true, SpaceChanged: true, TeachersChanged: true, AudienceChanged: false, TitleChanged: false });
        var movedJuggling = changes[2];
        Assert.True(movedJuggling is { DayChanged: true, TimesChanged: false, SpaceChanged: false, TeachersChanged: false, PartsChanged: false });
        Assert.Equal(Monday.AddDays(1), movedJuggling.Was!.Date);
        Assert.Equal(Audience.ForGroup(ba2.Id).GroupIds, changes[0].Now!.Audience.GroupIds);

        // Compared the other way round, what was added has gone and what has gone was added.
        Assert.Equal(
            ["Gone Acrobatics Mon 09:00", "Changed Juggling Tue 10:00", "Changed Dance Wed 09:00", "Added Class Thu 14:00", "Gone Aerial Fri 13:00"],
            WeekChanges.Between(b.T, Next, Monday).Select(c => Line(b.T, c)));
        Assert.Empty(WeekChanges.Between(b.T, Monday, Monday));
    }

    [Fact]
    public void AWeekHappeningIsNotItsTimetableChangingButAPartOrAnAudienceIs()
    {
        var b = new Build();
        var ba1 = b.Group("BA 1");
        var ba2 = b.Group("BA 2");
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var acro = b.Class("Acrobatics");
        var juggling = b.Class("Juggling");
        b.Session(Monday, "09:00", "11:00", Audience.ForGroup(ba1.Id), null, acro, new Assignment(ada.Id), new Assignment(bo.Id));
        b.Session(Monday.AddDays(1), "09:00", "11:00", Audience.ForGroup(ba1.Id), null, juggling, new Assignment(ada.Id));
        b.Session(Monday.AddDays(2), "09:00", "11:00", Audience.ForGroup(ba1.Id), null, juggling, new Assignment(ada.Id));
        var later = new Build { T = b.T };
        // Cancelled, covered, with a note, a student left off and provisional: the same timetable.
        later.Put(new Session
        {
            Date = Next, Start = new(9, 0), End = new(11, 0), Title = "Acrobatics", ClassId = acro.Id, Audience = Audience.ForGroup(ba1.Id),
            Facilitators = [new Assignment(ada.Id), new Assignment(bo.Id)], Cancelled = true, CancelNote = "Snow", CoverIds = [bo.Id],
            Notes = "Bring mats", Provisional = true, LeftOffStudentIds = [Guid.NewGuid()],
        });
        Assert.DoesNotContain(WeekChanges.Between(later.T, Monday, Next), c => c.Either.ClassId == acro.Id);
        Assert.Equal(2, WeekChanges.Between(later.T, Monday, Next).Count(c => c.Kind == WeekChangeKind.Gone && c.Either.ClassId == juggling.Id));

        // Bo in only the second hour is a different part; juggling for both groups is a different audience.
        var t = later.T with { Sessions = later.T.Sessions.Remove(later.T.Sessions.Single(s => s.Date == Next).Id) };
        var b2 = new Build { T = t };
        b2.Session(Next, "09:00", "11:00", Audience.ForGroup(ba1.Id), null, acro, new Assignment(ada.Id), new Assignment(bo.Id, From: new TimeOnly(10, 0)));
        b2.Session(Next.AddDays(1), "09:00", "11:00", Audience.ForGroups([ba1.Id, ba2.Id]), null, juggling, new Assignment(ada.Id));
        b2.Session(Next.AddDays(2), "09:00", "11:00", Audience.ForGroup(ba1.Id), null, juggling, new Assignment(ada.Id));
        var changes = WeekChanges.Between(b2.T, Monday, Next);
        Assert.Equal(2, changes.Count);
        Assert.True(changes[0] is { Kind: WeekChangeKind.Changed, PartsChanged: true, TeachersChanged: false, TimesChanged: false });
        Assert.True(changes[1] is { Kind: WeekChangeKind.Changed, AudienceChanged: true, DayChanged: false });
        // The Wednesday juggling stays paired with its own day, not taken by the Tuesday one for being for the same group.
        Assert.Equal(Monday.AddDays(1), changes[1].Was!.Date);
    }

    [Fact]
    public void EachKindOfDifferenceIsSaidOnItsOwnAndTheSameClassIsPairedByItsGroupsThenTheNearestTime()
    {
        var b = new Build();
        var ba1 = b.Group("BA 1");
        var ba2 = b.Group("BA 2");
        var gym = b.Room("Gym");
        var studio = b.Room("Studio");
        var strength = b.Class("Strength");
        var aerial = b.Class("Aerial");
        var creation = b.Class("Creation lab");
        var acro = b.Class("Acrobatics");
        var juggling = b.Class("Juggling");
        var one = Audience.ForGroup(ba1.Id);
        b.Session(Monday.AddDays(3), "09:00", "10:00", one, gym, strength);
        b.Session(Next.AddDays(3), "09:00", "10:00", one, studio, strength);
        b.Session(Monday.AddDays(4), "09:00", "10:00", one, null, aerial);
        b.Session(Next.AddDays(4), "09:00", "10:30", one, null, aerial);
        b.Session(Monday.AddDays(2), "13:00", "15:00", one, null, creation);
        b.Put(b.Session(Next.AddDays(2), "13:00", "15:00", one, null, creation) with { Title = "Creation lab: showing" });
        // Acrobatics for each group swaps its time: each is paired with its own group's, not the nearest in time.
        b.Session(Monday, "09:00", "10:00", one, null, acro);
        b.Session(Monday, "14:00", "15:00", Audience.ForGroup(ba2.Id), null, acro);
        b.Session(Next, "13:00", "14:00", one, null, acro);
        b.Session(Next, "10:00", "11:00", Audience.ForGroup(ba2.Id), null, acro);
        // Two juggling classes a quarter later each: each with the nearest.
        b.Session(Monday.AddDays(1), "09:00", "10:00", one, null, juggling);
        b.Session(Monday.AddDays(1), "15:00", "16:00", one, null, juggling);
        b.Session(Next.AddDays(1), "09:15", "10:15", one, null, juggling);
        b.Session(Next.AddDays(1), "15:15", "16:15", one, null, juggling);

        var changes = WeekChanges.Between(b.T, Monday, Next);
        Assert.All(changes, c => Assert.Equal(WeekChangeKind.Changed, c.Kind));
        WeekChange Of(Class c, string nowStart) => changes.Single(x => x.Now!.ClassId == c.Id && Week.Hm(x.Now.Start) == nowStart);
        Assert.True(Of(strength, "09:00") is { SpaceChanged: true, TimesChanged: false, DayChanged: false, TitleChanged: false, TeachersChanged: false, AudienceChanged: false });
        Assert.True(Of(aerial, "09:00") is { TimesChanged: true, SpaceChanged: false, TitleChanged: false });
        Assert.True(Of(creation, "13:00") is { TitleChanged: true, TimesChanged: false, SpaceChanged: false });
        Assert.True(Of(acro, "13:00") is { TimesChanged: true, AudienceChanged: false });
        Assert.Equal(new TimeOnly(9, 0), Of(acro, "13:00").Was!.Start);
        Assert.Equal(new TimeOnly(14, 0), Of(acro, "10:00").Was!.Start);
        Assert.Equal(new TimeOnly(9, 0), Of(juggling, "09:15").Was!.Start);
        Assert.Equal(new TimeOnly(15, 0), Of(juggling, "15:15").Was!.Start);
        Assert.Equal(7, changes.Count);
    }

    [Fact]
    public void OneWeeksFiguresAreTheWeeksOwnRulesAskedOfIt()
    {
        var b = new Build();
        var ba1 = b.Group("BA 1");
        var ba2 = b.Group("BA 2");
        var ba3 = b.Group("BA 3");
        var s1 = b.Student("Alba", ba1);
        var s2 = b.Student("Benno", ba1);
        var gym = b.Room("Gym");
        var studio = b.Room("Studio");
        var ada = b.Facilitator("Ada");
        var bo = b.Facilitator("Bo");
        var training = b.Class("Training");
        var warmup = b.Class("Warm-up", Workload.NotCounted);
        var jam = b.Class("Jam", Workload.DropIn);
        var one = Audience.ForGroup(ba1.Id);
        var today = Monday.AddDays(3);
        b.T = b.T with { Settings = b.T.Settings with { WeeklyScheduledTarget = TimeSpan.FromHours(10) } };
        b.Session(Monday, "09:00", "12:00", one, gym, training, new Assignment(ada.Id));
        b.Session(Monday, "13:00", "14:00", one, gym, warmup, new Assignment(bo.Id));
        b.Session(Monday.AddDays(1), "09:00", "11:00", one, studio, jam, new Assignment(bo.Id));
        b.Put(b.Session(Monday.AddDays(1), "13:00", "16:00", one, gym, training, new Assignment(ada.Id)) with { Cancelled = true });
        b.Put(b.Session(Monday.AddDays(2), "09:00", "12:00", one, studio, training, new Assignment(ada.Id)) with { LeftOffStudentIds = [s2.Id] });
        b.Session(Monday.AddDays(2), "13:00", "14:00", one, studio, training, new Assignment(bo.Id));
        b.Session(Monday.AddDays(3), "09:00", "12:00", one, gym, training, new Assignment(ada.Id));
        b.Session(Monday.AddDays(4), "09:00", "10:00", one, gym, training, new Assignment(ada.Id));
        b.Away(bo, Monday.AddDays(2));
        b.Away(ada, Monday.AddDays(3));
        b.T = b.T with { Closures = b.T.Closures.Put(new Closure { Name = "Open day", From = Monday.AddDays(4), To = Monday.AddDays(4) }) };
        // BA 2 is away Monday and Tuesday, BA 3 Monday to Thursday: with the open day, BA 3 is closed all week and BA 2 is not.
        b.T = b.T with { Closures = b.T.Closures.Put(new Closure { Name = "Tour", From = Monday, To = Monday.AddDays(1), Scope = ClosureScope.Groups, GroupIds = [ba2.Id] }) };
        b.T = b.T with { Closures = b.T.Closures.Put(new Closure { Name = "Residency", From = Monday, To = Monday.AddDays(3), Scope = ClosureScope.Groups, GroupIds = [ba3.Id] }) };

        var w = TermGlance.For(b.T, [Monday.AddDays(2)], today, EventViewer.Administrator, RegisterReader.Administrator).Single();
        Assert.Equal(Monday, w.Monday);
        Assert.Equal((8, 6, 1), (w.Written, w.Happening, w.Cancelled));
        Assert.Equal([Monday.AddDays(4)], w.ClosedDays.Select(d => d.Day));
        Assert.Null(w.ClosedAllWeek);
        Assert.Equal([ba3.Id], w.GroupsClosedAllWeek);
        // Counted hours that happen: not the warm-up, not the jam, not the cancelled or closed classes; a class
        // waiting for cover still counts for the group.
        Assert.Equal(TimeSpan.FromHours(3 + 3 + 1 + 3), w.GroupHours[ba1.Id]);
        // Benno is left off Wednesday morning: 7h, short of 10h; Alba's 10h is not short.
        Assert.Equal(1, w.GroupStudentsShort[ba1.Id]);
        Assert.Equal(TimeSpan.FromHours(3 + 1 + 3), w.SpaceHours[gym.Id]);
        Assert.Equal(TimeSpan.FromHours(2 + 3 + 1), w.SpaceHours[studio.Id]);
        // Thursday's class needs cover and is still to come; Wednesday's needed it and has gone by.
        Assert.Equal(1, w.CoverNeeded);
        // Registers missing before Thursday: Monday's two, Tuesday's jam, Wednesday's two; Ada may file only hers.
        Assert.Equal(5, w.RegistersMissing);
        Assert.Equal(2, TermGlance.For(b.T, [Monday], today, EventViewer.Administrator, new RegisterReader(false, ada.Id)).Single().RegistersMissing);
        Assert.Equal(TimeSpan.FromHours(6), w.PersonHours[ada.Id].Teaching);
        Assert.Equal(TimeSpan.FromHours(3), w.PersonHours[bo.Id].Teaching);
        Assert.Equal(2, w.PersonClasses[ada.Id]);
        Assert.Equal(2, w.PersonClasses[bo.Id]);
        Assert.Equal(0, w.Clashes);
        _ = s1;

        // Two classes for BA 1 at once are two clashes drawn as rings: the group twice, and its students.
        b.Session(Monday.AddDays(3), "10:00", "11:00", one, studio, warmup, new Assignment(bo.Id));
        Assert.Equal(2, TermGlance.For(b.T, [Monday], today, EventViewer.Administrator, RegisterReader.Administrator).Single().Clashes);
    }

    [Fact]
    public void AWeekIsComparedWithTheLastWeekThatHadAnythingInItWithinHalfAYear()
    {
        var b = new Build();
        b.Session(Monday, "09:00", "10:00");
        b.Session(Monday.AddDays(7 * 3), "09:00", "10:00");
        Assert.Equal(Monday, WeekChanges.Previous(b.T, Monday.AddDays(7 * 3 + 2)));
        Assert.Equal(Monday, WeekChanges.Previous(b.T, Monday.AddDays(7)));
        Assert.Equal(Monday.AddDays(7 * 3), WeekChanges.Previous(b.T, Monday.AddDays(7 * 4)));
        Assert.Null(WeekChanges.Previous(b.T, Monday));
        Assert.Equal(Monday.AddDays(7 * 3), WeekChanges.Previous(b.T, Monday.AddDays(7 * (3 + WeekChanges.LookBackWeeks))));
        Assert.Null(WeekChanges.Previous(b.T, Monday.AddDays(7 * (4 + WeekChanges.LookBackWeeks))));
    }

    [Fact]
    public void TheSampleSemesterSideBySideSaysWhichWeeksAreBuiltBlankOrClosedAndWhatEachHas()
    {
        var t = SampleSchool.Create(SampleMonday, SampleToday);
        var weeks = TermGlance.WeeksFor(t, SampleToday);
        Assert.Equal(18, weeks.Count);
        Assert.Equal(SampleMonday.AddDays(-7 * 4), weeks[0]);
        var glance = TermGlance.For(t, weeks, SampleToday, EventViewer.Administrator, RegisterReader.Administrator);
        TermWeek At(int w) => glance.Single(x => x.Monday == SampleMonday.AddDays(7 * w));

        // Two weeks behind, this one and the next are built; the autumn break shuts the building three weeks on.
        Assert.Equal([false, false, true, true, true, true, false, false], glance.Take(8).Select(w => !w.Blank));
        Assert.Equal("Autumn break", At(3).ClosedAllWeek?.Name);
        Assert.Equal(5, At(3).ClosedDays.Count);
        Assert.Null(At(0).ClosedAllWeek);
        Assert.Empty(At(0).ClosedDays);
        // The Prep study day closes Prep on this week's Friday only, which is not the whole week.
        Assert.Empty(At(0).GroupsClosedAllWeek);
        Assert.Equal(t.Groups.Count, At(3).GroupsClosedAllWeek.Count);

        // This week has the visiting company's two workshops the weeks around it do not.
        Assert.Null(At(-2).ComparedWith);
        Assert.Empty(At(-2).Changes);
        Assert.Equal(SampleMonday.AddDays(-14), At(-1).ComparedWith);
        Assert.Empty(At(-1).Changes);
        Assert.Equal(["Added Guest workshop Thu 10:00", "Added Guest workshop Fri 09:30"], At(0).Changes.Select(c => Line(t, c)));
        Assert.Equal(["Gone Guest workshop Thu 10:00", "Gone Guest workshop Fri 09:30"], At(1).Changes.Select(c => Line(t, c)));
        // A blank week is compared with nothing: there is nothing in it to compare.
        Assert.Equal(SampleMonday.AddDays(7), At(2).ComparedWith);
        Assert.Empty(At(2).Changes);

        // Ada is away on Thursday, so her acrobatics needs cover this week and nobody's hours have it.
        Assert.Equal(1, At(0).CoverNeeded);
        Assert.Equal(0, At(-1).CoverNeeded);
        var ada = t.Facilitators.Single(f => f.Name == "Ada Brightwater").Id;
        Assert.Equal(TimeSpan.FromHours(5.25), At(-1).PersonHours[ada].Teaching);
        Assert.Equal(TimeSpan.FromHours(3.25), At(0).PersonHours[ada].Teaching);
        Assert.Equal(3, At(-1).PersonClasses[ada]);
        Assert.Equal(2, At(0).PersonClasses[ada]);

        // Counted hours by group: BA 1 16h, BA 2 16h 30m; Prep 9h 30m, and an hour less this week, its Friday closed.
        // Lunch is not counted and open training is nobody's: neither is in any group's hours.
        Guid G(string name) => t.Groups.Single(g => g.Name == name).Id;
        Assert.Equal(TimeSpan.FromHours(16), At(-1).GroupHours[G("BA 1")]);
        Assert.Equal(TimeSpan.FromHours(16.5), At(-1).GroupHours[G("BA 2")]);
        Assert.Equal(TimeSpan.FromHours(9.5), At(-1).GroupHours[G("Prep")]);
        Assert.Equal(TimeSpan.FromHours(8.5), At(0).GroupHours[G("Prep")]);
        // Every Prep student is short of the 15h target; nobody in BA 1 or BA 2 is; the visitors are never measured.
        Assert.Equal(6, At(0).GroupStudentsShort[G("Prep")]);
        Assert.False(At(0).GroupStudentsShort.ContainsKey(G("BA 1")));
        Assert.False(At(0).GroupStudentsShort.ContainsKey(G("Nordlys Troupe")));
        Assert.Empty(At(2).GroupStudentsShort);

        // Space hours: the Big Top has acrobatics, disciplines, open training and this week's workshop.
        var bigTop = t.Rooms.Single(r => r.Name == "Big Top").Id;
        Assert.Equal(TimeSpan.FromHours(1.25 + 2 + 4 + 2), At(-1).SpaceHours[bigTop]);
        Assert.Equal(TimeSpan.FromHours(1.25 + 2 + 4 + 2 + 2), At(0).SpaceHours[bigTop]);
        Assert.False(At(2).SpaceHours.ContainsKey(bigTop));

        // Events this week: the staff meeting, the budget review, the visit and the showing, though an editor is not
        // shown the budget review; a blank week can still have the staff meeting.
        Assert.Equal(4, At(0).Events);
        Assert.Equal(3, TermGlance.For(t, [SampleMonday], SampleToday, new EventViewer(false, true, false, false, false), RegisterReader.Administrator).Single().Events);
        Assert.Equal(1, At(2).Events);
        Assert.Equal(0, At(2).Written);
    }

    [Fact]
    public void AWeekWithNoSemestersLaysOutThirteenWeeksAndALongSemesterTheStretchAroundTheDay()
    {
        var none = Timetable.Empty;
        var weeks = TermGlance.WeeksFor(none, Monday.AddDays(2));
        Assert.Equal(13, weeks.Count);
        Assert.Equal(Monday.AddDays(-28), weeks[0]);
        var longOne = Timetable.Empty with
        {
            Semesters = EntitySet<Semester>.Of([new Semester { Name = "Long", StartMonday = Monday.AddDays(-7 * 50), WeekCount = 100 }]),
        };
        var stretch = TermGlance.WeeksFor(longOne, Monday);
        Assert.Equal(TermGlance.MostWeeks, stretch.Count);
        Assert.Equal(Monday.AddDays(-28), stretch[0]);
        Assert.Contains(Monday, stretch);
        // Near its end, the stretch is its last weeks.
        var end = TermGlance.WeeksFor(longOne, longOne.Semesters.Single().LastMonday);
        Assert.Equal(longOne.Semesters.Single().LastMonday, end[^1]);
    }
}
