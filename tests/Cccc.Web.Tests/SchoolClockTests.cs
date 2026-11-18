using Cccc.Web.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cccc.Web.Tests;

/// <summary>
/// The preview's clock: moved to a day of this week and a time of day, the instant itself moves, so today,
/// the time and what a change is stamped with agree; and nothing moves it outside Development.
/// </summary>
public class SchoolClockTests
{
    private static SchoolClock Clock(string environment, params (string Key, string Value)[] settings) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value))).Build(),
            new HostingEnvironment { EnvironmentName = environment }, NullLogger<SchoolClock>.Instance);

    [Fact]
    public void AWeekdayIsReadFromTwoLettersOnAndAnythingElseIsNoDay()
    {
        Assert.Equal(DayOfWeek.Wednesday, SchoolClock.Weekday("wed"));
        Assert.Equal(DayOfWeek.Wednesday, SchoolClock.Weekday("Wednesday"));
        Assert.Equal(DayOfWeek.Thursday, SchoolClock.Weekday("TH"));
        Assert.Equal(DayOfWeek.Sunday, SchoolClock.Weekday(" su "));
        Assert.Null(SchoolClock.Weekday("t"));
        Assert.Null(SchoolClock.Weekday("someday"));
        Assert.Null(SchoolClock.Weekday(null));
    }

    [Fact]
    public void ADayAndATimeMoveTheInstantSoTodayAndTheTimeAgreeWithIt()
    {
        foreach (var (name, day) in new[] { ("wed", DayOfWeek.Wednesday), ("mon", DayOfWeek.Monday), ("sun", DayOfWeek.Sunday), ("fri", DayOfWeek.Friday) })
        {
            var before = Cccc.Domain.Week.MondayOf(Clock("Production").Today);
            var c = Clock("Development", ("Clock:Day", name), ("Clock:Time", "10:15"));
            var after = Cccc.Domain.Week.MondayOf(Clock("Production").Today);
            Assert.Equal(day, c.Today.DayOfWeek);
            Assert.Contains(Cccc.Domain.Week.MondayOf(c.Today), new[] { before, after });   // this week, even if it turned under the test
            Assert.InRange(c.TimeOfDay, new TimeOnly(10, 15), new TimeOnly(10, 16));
            Assert.Equal(c.Today, DateOnly.FromDateTime(c.Local(c.Now).DateTime));
        }
        // A day alone keeps the time of day it is; a time alone, the day.
        var real = Clock("Production");
        var thursday = Clock("Development", ("Clock:Day", "thursday"));
        Assert.Equal(DayOfWeek.Thursday, thursday.Today.DayOfWeek);
        var apart = thursday.TimeOfDay - real.TimeOfDay;     // wraps past midnight, so either side of nought
        Assert.True(apart < TimeSpan.FromMinutes(1) || apart > TimeSpan.FromHours(24) - TimeSpan.FromMinutes(1), $"{thursday.TimeOfDay} against {real.TimeOfDay}");
        var timed = Clock("Development", ("Clock:Time", "07:30"));
        Assert.InRange(timed.TimeOfDay, new TimeOnly(7, 30), new TimeOnly(7, 31));
        Assert.Contains(timed.Today, new[] { real.Today, Clock("Production").Today });
    }

    [Fact]
    public void OutsideDevelopmentNothingMovesIt()
    {
        var real = Clock("Production");
        var asked = Clock("Production", ("Clock:Day", "wed"), ("Clock:Time", "03:00"));
        Assert.Equal(real.Today, asked.Today);
        Assert.InRange(asked.Now - real.Now, TimeSpan.FromSeconds(-5), TimeSpan.FromSeconds(5));
    }
}
