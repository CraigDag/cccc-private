using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;
using DomainWeek = Cccc.Domain.Week;

namespace Cccc.Web.Features.Now;

/// <summary>Next week on Now (<see cref="WeekAhead"/>), in the words the plan, the cover page and Copy week use.</summary>
public static class WeekAheadWords
{
    /// <summary>"Next week · Week 48".</summary>
    public static string Head(DateOnly monday) => $"Next week · Week {DomainWeek.IsoNumber(monday)}";

    /// <summary>What a blank week offers: the last week built, copied onto it, or the plan open on it to build it in.</summary>
    public static string Fill(DateOnly? from) =>
        from is { } f ? $"Copy week {DomainWeek.IsoNumber(f)} onto it…" : "Open it in the plan to build it";

    /// <summary>"2 clashes".</summary>
    public static string Clashes(IReadOnlyList<Clash> clashes) => Looks.Count(clashes.Count, "clash", "clashes");

    /// <summary>The first clash in the clash check's own words, and how many more.</summary>
    public static string FirstClash(IReadOnlyList<Clash> clashes) =>
        clashes.Count == 0 ? "" : clashes[0].Description + (clashes.Count > 1 ? $" and {clashes.Count - 1} more" : "");

    /// <summary>"1 class needs cover".</summary>
    public static string Cover(IReadOnlyList<Occurrence> cover) => Looks.Count(cover.Count, "class needs", "classes need") + " cover";

    /// <summary>The first two needing cover, "Acrobatics Thu 26 Nov 09:00", and how many more.</summary>
    public static string FirstCover(Timetable t, IReadOnlyList<Occurrence> cover) =>
        string.Join(", ", cover.Take(2).Select(o => $"{Looks.Title(t, o.Session)} {DomainWeek.ShortDate(o.Date)} {DomainWeek.Hm(o.Start)}"))
        + (cover.Count > 2 ? $" and {cover.Count - 2} more" : "");

    /// <summary>"Closed Thursday: Ascension Day", one closure's days together: "Closed Thursday and Friday: Easter (Påske)".</summary>
    public static string Closed(IReadOnlyList<(DateOnly Day, Closure By)> days) => "Closed " + string.Join("; ",
        days.GroupBy(d => d.By.Id).Select(g => Cccc.Web.Features.Data.EquipmentWords.And([.. g.Select(d => d.Day.DayOfWeek.ToString())]) + ": " + g.First().By.Name));

    /// <summary>"2 changes from week 47": what is different from the week it is compared with.</summary>
    public static string Changes(TermWeek w) =>
        $"{Looks.Count(w.Changes.Count, "change")} from week {(w.ComparedWith is { } before ? DomainWeek.IsoNumber(before) : DomainWeek.IsoNumber(w.Monday.AddDays(-7)))}";

    /// <summary>Said when nothing in it needs putting right: "Ready: 23 classes, nothing clashes, nobody needs cover."</summary>
    public static string Ready(TermWeek w) => $"Ready: {Looks.Count(w.Happening, "class", "classes")}, nothing clashes, nobody needs cover.";
}
