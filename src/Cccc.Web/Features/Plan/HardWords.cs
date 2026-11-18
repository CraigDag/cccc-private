using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using DomainWeek = Cccc.Domain.Week;

namespace Cccc.Web.Features.Plan;

/// <summary>What a person reads about how hard a group's day is: the figure beside its name, and the longer look.</summary>
public static class HardWords
{
    /// <summary>"4h 45m hard".</summary>
    public static string Sum(HardDay d) => DomainWeek.Duration(d.Total) + " hard";

    /// <summary>
    /// Each stretch, the day's total, and what goes past the school's limits: "Hard training 09:00–12:00 (2h 45m),
    /// 13:00–15:00 (2h); 4h 45m in all. More than 2h in a row without a 30m rest. More than 4h in a day."
    /// </summary>
    public static string Title(Timetable t, HardDay d)
    {
        var line = "Hard training " + string.Join(", ", d.Stretches.Select(s => $"{DomainWeek.Hm(s.Start)}–{DomainWeek.Hm(s.End)} ({DomainWeek.Duration(s.Hard)})"))
                   + $"; {DomainWeek.Duration(d.Total)} in all.";
        if (d.TooLong.Count > 0 && t.Settings.MostHardInARow is { } most)
            line += $" More than {DomainWeek.Duration(most)} in a row without a {DomainWeek.Duration(HardTraining.Rest(t))} rest.";
        if (d.TooMuch && t.Settings.MostHardInADay is { } inADay)
            line += $" More than {DomainWeek.Duration(inADay)} in a day.";
        return line;
    }
}
