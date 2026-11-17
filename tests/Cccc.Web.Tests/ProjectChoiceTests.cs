using Cccc.Domain.Model;
using Cccc.Domain.Sample;
using Cccc.Store;
using Cccc.Web.Features.Plan;
using Cccc.Web.Features.Shell;
using Cccc.Web.Features.Week;
using Cccc.Web.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Cccc.Web.Tests;

public class ProjectChoiceTests
{
    private static readonly DateOnly Day = new(2030, 3, 6);

    private sealed class Stores : IDisposable
    {
        private readonly string _folder = Path.Combine(AppContext.BaseDirectory, "stores", Guid.NewGuid().ToString("N"));
        public readonly ProjectStore Store;
        public readonly LiveProjects Live;
        public readonly ProjectChoice Choice;

        public Stores()
        {
            Store = ProjectStore.Open(_folder, create: true);
            Live = new LiveProjects(Store, () => DateTimeOffset.UtcNow, () => Day);
            Choice = new ProjectChoice(Store, Live);
        }

        public void Dispose()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(_folder, true); } catch (IOException) { }
        }
    }

    internal static Access As(Role role, bool suspended = false) =>
        new(new Account(Guid.NewGuid(), "someone", "Someone", role, default, null, suspended, false, "s", DateTimeOffset.UnixEpoch, true));

    [Fact]
    public void BringingInANewTimetableDoesNotMoveTheSchoolOntoIt()
    {
        using var s = new Stores();
        var school = s.Store.Create("School", SampleSchool.Create(Day, Day), "a", DateTimeOffset.UtcNow);
        Assert.Equal(school, s.Choice.OpenId());
        s.Store.Create("Imported", SampleSchool.Create(Day, Day), "a", DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(school, s.Choice.OpenId());
    }

    [Fact]
    public void OnlyAnAdministratorsTabLooksAtAnotherTimetableAndOnlyWhileItIsThere()
    {
        using var s = new Stores();
        var school = s.Store.Create("School", SampleSchool.Create(Day, Day), "a", DateTimeOffset.UtcNow);
        var next = s.Store.Create("Next", Timetable.Empty, "a", DateTimeOffset.UtcNow.AddMinutes(1));
        s.Choice.Choose(school);
        var admin = As(Role.Administrator);

        Assert.Equal(next, s.Choice.ShownId(next, admin));
        Assert.Equal(school, s.Choice.ShownId(null, admin));
        Assert.Equal(school, s.Choice.ShownId(Guid.NewGuid(), admin));
        // Everybody else only ever sees the open one, whatever their tab asks for.
        Assert.Equal(school, s.Choice.ShownId(next, As(Role.Editor)));
        Assert.Equal(school, s.Choice.ShownId(next, As(Role.Viewer)));
        Assert.Equal(school, s.Choice.ShownId(next, As(Role.Administrator, suspended: true)));
        Assert.True(s.Choice.MaySee(school, As(Role.Viewer)));
        Assert.False(s.Choice.MaySee(next, As(Role.Viewer)));
        Assert.False(s.Choice.MaySee(next, As(Role.Editor)));
        Assert.False(s.Choice.MaySee(school, Access.Nobody));

        // The open one is never deleted; another is, and every tab is told, so one looking at it goes back.
        int moved = 0;
        s.Choice.Moved += () => moved++;
        Assert.Throws<InvalidOperationException>(() => s.Choice.Delete(school, DateTimeOffset.UtcNow));
        Assert.Null(s.Store.Find(school)!.Deleted);
        Assert.Equal(0, moved);
        s.Choice.Delete(next, DateTimeOffset.UtcNow);
        Assert.Equal(1, moved);
        Assert.False(s.Choice.MaySee(next, admin));
        Assert.Equal(school, s.Choice.ShownId(next, admin));
    }

    [Fact]
    public void LookingAtATimetableGoesWhereItsClassesAreWhenTheWeekOnScreenHasNone()
    {
        var w10 = new DateOnly(2030, 3, 4);
        var w14 = new DateOnly(2030, 4, 1);
        var t = Timetable.Empty with
        {
            Sessions = EntitySet<Session>.Of([new Session { Date = w10.AddDays(2) }, new Session { Date = w14.AddDays(1) }]),
        };
        Assert.Null(TabTimetable.Landing(t, w10.AddDays(4)));                     // its week has classes: stay
        Assert.Equal(w14, TabTimetable.Landing(t, w10.AddDays(7)));              // the next week that has
        Assert.Equal(w10, TabTimetable.Landing(t, w10.AddDays(-30)));            // from before them all, the first
        Assert.Equal(w14, TabTimetable.Landing(t, w14.AddDays(60)));             // after them all, the last
        Assert.Null(TabTimetable.Landing(Timetable.Empty, w10));                 // nowhere to go
    }
}

public class TabTimetableTests : IClassFixture<Host>
{
    private readonly Host _host;
    public TabTimetableTests(Host host) => _host = host;

    [Fact]
    public void LookingAwayMovesThisTabsPlanOntoTheClassesAndBackPutsItWhereItWas()
    {
        var store = _host.Services.GetRequiredService<ProjectStore>();
        var clock = _host.Services.GetRequiredService<SchoolClock>();
        var later = Cccc.Domain.Week.MondayOf(clock.Today).AddDays(7 * 40);
        var next = store.Create("Next", Timetable.Empty with { Sessions = EntitySet<Session>.Of([new Session { Date = later.AddDays(1) }]) },
            "test", DateTimeOffset.UtcNow);
        using var tab = _host.Services.CreateScope();
        using var other = _host.Services.CreateScope();
        var looking = tab.ServiceProvider.GetRequiredService<TabTimetable>();
        var plan = tab.ServiceProvider.GetRequiredService<PlanView>();
        var week = tab.ServiceProvider.GetRequiredService<WeekView>();
        var otherPlan = other.ServiceProvider.GetRequiredService<PlanView>();
        plan.Start(clock.Today);
        otherPlan.Start(clock.Today);
        var before = clock.Today.AddDays(-7);
        plan.GoTo(before);
        plan.FocusOn(new Focus("room", Guid.NewGuid(), "A room of this year's"));
        week.Rooms.Add(Guid.NewGuid());
        int moved = 0;
        looking.Moved += () => moved++;

        looking.LookAt(next);

        Assert.Equal((next, 1), (looking.LookingAt, moved));
        Assert.Equal(later, plan.Monday);
        Assert.Equal(later, week.Monday);
        Assert.Null(plan.Focus);
        Assert.False(week.AnyFilter);
        // Another tab is not moved.
        Assert.Null(other.ServiceProvider.GetRequiredService<TabTimetable>().LookingAt);
        Assert.Equal(clock.Today, otherPlan.Day);

        looking.Back();

        Assert.Equal((null, 2), (looking.LookingAt, moved));
        Assert.Equal(before, plan.Day);
        Assert.Equal(Cccc.Domain.Week.MondayOf(before), week.Monday);
        looking.Back();
        Assert.Equal(2, moved);
    }

    [Fact]
    public void AfterAReloadBackStillReturnsToTheDayBeforeOrTodayWhenThatWasNotKept()
    {
        var clock = _host.Services.GetRequiredService<SchoolClock>();
        var next = Guid.NewGuid();
        var before = clock.Today.AddDays(-21);
        foreach (var (kept, expected) in new (DateOnly?, DateOnly)[] { (before, before), (null, clock.Today) })
        {
            // A reload: a fresh tab whose plan the page has put back on the day it was looking at.
            using var tab = _host.Services.CreateScope();
            var looking = tab.ServiceProvider.GetRequiredService<TabTimetable>();
            var plan = tab.ServiceProvider.GetRequiredService<PlanView>();
            plan.Start(clock.Today);
            plan.GoTo(clock.Today.AddDays(300));
            looking.Restore(next, kept);
            Assert.Equal((next, kept), (looking.LookingAt, looking.DayBefore));

            looking.Back();

            Assert.Equal(expected, plan.Day);
            Assert.Equal(Cccc.Domain.Week.MondayOf(expected), tab.ServiceProvider.GetRequiredService<WeekView>().Monday);
        }
    }
}
