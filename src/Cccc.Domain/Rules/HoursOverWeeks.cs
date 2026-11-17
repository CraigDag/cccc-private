using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>One week of a facilitator's hours, and whether the school ran at all that week.</summary>
public sealed record WeekHours(DateOnly Monday, HoursSplit Split, bool SchoolRan)
{
    public TimeSpan Total => Split.Total;
}

/// <summary>
/// A facilitator's hours over chosen weeks and how they spread. Reports ▸ Facilitator hours (both views) and
/// its spreadsheet read this one, so an average or a busiest week cannot mean one thing on the screen and
/// another in the sheet. A week in which no class happened anywhere (the building closed, or a week nobody
/// has timetabled yet) says nothing about anybody's load: it is left out of the average and is never the
/// quietest. A week the school ran in which this person had nothing IS a quiet week, and counts.
/// </summary>
public sealed record FacilitatorWeeks(Facilitator Person, IReadOnlyList<WeekHours> Weeks)
{
    public TimeSpan Total => Sum(Weeks.Select(w => w.Total));

    public TimeSpan Teaching => Sum(Weeks.Select(w => w.Split.Teaching));

    public TimeSpan ForRule(Guid ruleId) => Sum(Weeks.SelectMany(w => w.Split.Rules).Where(r => r.RuleId == ruleId).Select(r => r.Time));

    /// <summary>How many of the weeks the school ran: what the average is taken over.</summary>
    public int WeeksRan => Weeks.Count(w => w.SchoolRan);

    /// <summary>Their average week over the weeks the school ran, to the minute; none when no week ran.</summary>
    public TimeSpan? Average => WeeksRan == 0 ? null
        : TimeSpan.FromMinutes(Math.Round(Weeks.Where(w => w.SchoolRan).Sum(w => w.Total.TotalMinutes) / WeeksRan, MidpointRounding.AwayFromZero));

    /// <summary>How many weeks went over their limit. A count, never a refusal.</summary>
    public int WeeksOver => Person.MaxWeeklyHours is { } max ? Weeks.Count(w => w.Total > max) : 0;

    /// <summary>Their lightest week the school ran, the earliest of equals; none when no week ran.</summary>
    public WeekHours? Quietest => Weeks.Where(w => w.SchoolRan).MinBy(w => w.Total);

    /// <summary>Their heaviest week, the earliest of equals; none when they have no hours at all.</summary>
    public WeekHours? Busiest => Weeks.Where(w => w.Total > TimeSpan.Zero).MaxBy(w => w.Total);

    private static TimeSpan Sum(IEnumerable<TimeSpan> xs) => xs.Aggregate(TimeSpan.Zero, (a, x) => a + x);
}
