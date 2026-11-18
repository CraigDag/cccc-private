using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Data;

namespace Cccc.Web.Features.Sessions;

/// <summary>What the Cover page says about somebody who could take everything one person being away leaves.</summary>
public static class CoverWords
{
    /// <summary>
    /// "Free for all 3 · has taught Creation lab · 5h this week", or "Offered · Free for 2 of 3, busy for 1 · 6h 45m
    /// this week · would go over their limit": whether they offered, how much of it they are free for and what keeps
    /// them from the rest, which of its classes they have taught before, and their week.
    /// </summary>
    public static string Taker(Timetable t, StretchTaker c, int of)
    {
        var said = new List<string>();
        if (c.Offered) said.Add("Offered");
        var rest = new List<string>();
        if (c.Busy.Count > 0) rest.Add($"busy for {c.Busy.Count}");
        if (of - c.Free.Count - c.Busy.Count is > 0 and var away) rest.Add($"away for {away}");
        said.Add((c.Free.Count == of ? $"Free for all {of}" : $"Free for {c.Free.Count} of {of}") + (rest.Count > 0 ? ", " + string.Join(", ", rest) : ""));
        var taught = c.Taught.Select(id => t.Classes[id]?.Name).OfType<string>().Order(NameOrder.Comparer).ToList();
        if (taught.Count > 0) said.Add("has taught " + EquipmentWords.And(taught));
        said.Add($"{Cccc.Domain.Week.Duration(c.Week)} this week");
        if (c.WouldBeOverLimit) said.Add("would go over their limit");
        return string.Join(" · ", said);
    }

    /// <summary>"Cleo covers all of it", "Dag covers 2 of them": what pressing their name does, for a screen reader.</summary>
    public static string Press(StretchTaker c, int of) => $"{c.Person.Name} covers {(c.Free.Count == of ? "all of it" : $"{c.Free.Count} of them")}";
}
