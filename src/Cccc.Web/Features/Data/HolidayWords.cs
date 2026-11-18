using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Features.Data;

using Week = Cccc.Domain.Week;

/// <summary>What closing the public holidays says about each closure it offers (<see cref="PublicHolidays"/>).</summary>
public static class HolidayWords
{
    /// <summary>"Thu 6 May", or "Thu 25 Mar – Mon 29 Mar" for several days.</summary>
    public static string When(HolidayClosure h) =>
        h.From == h.To ? Week.ShortDate(h.From) : $"{Week.ShortDate(h.From)} – {Week.ShortDate(h.To)}";

    /// <summary>
    /// When, then the holidays in it when there are several, then why it is not ticked to start with: closed
    /// already, or not a public holiday though many places close.
    /// </summary>
    public static string Line(HolidayClosure h)
    {
        var parts = new List<string> { When(h) };
        if (h.Days.Count > 1) parts.Add(string.Join(", ", h.Days.Select(d => d.Name)));
        if (h.ClosedBy is { } c) parts.Add("closed already: " + c.Name.Trim());
        if (!h.Official) parts.Add("not a public holiday, but many places close");
        return string.Join(" · ", parts);
    }

    /// <summary>The holidays Start next year would close, each with its days: "Easter (Påske), Thu 13 Apr – Mon 17 Apr; …".</summary>
    public static string Listed(IEnumerable<HolidayClosure> holidays) => string.Join("; ", holidays.Select(h => h.Name + ", " + When(h)));

    /// <summary>What the button adds.</summary>
    public static string Adds(int ticked) => ticked == 0 ? "Add them" : "Add " + Looks.Count(ticked, "closure");

    /// <summary>What Undo is offered as, and what is said once they are closed.</summary>
    public static string Done(IReadOnlyList<HolidayClosure> closed) =>
        closed.Count == 1 ? "Closed " + closed[0].Name : $"Closed {closed.Count} public holidays";
}
