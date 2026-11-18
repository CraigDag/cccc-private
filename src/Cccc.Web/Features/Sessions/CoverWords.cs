using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Data;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Features.Sessions;

/// <summary>What the Cover page says about somebody who could take everything one person being away leaves.</summary>
public static class CoverWords
{
    /// <summary>
    /// "Free for all 3 · has taught Creation lab · 5h this week", or "Offered · Free for 2 of 3, busy for 1 · 6h 45m
    /// this week · would go over their limit": whether they offered, how much of it they are free for and what keeps
    /// them from the rest, which of its classes they have taught before, and their week. For one class, "Free · …".
    /// </summary>
    public static string Taker(Timetable t, StretchTaker c, int of)
    {
        var said = new List<string>();
        if (c.Offered) said.Add("Offered");
        var rest = new List<string>();
        if (c.Busy.Count > 0) rest.Add($"busy for {c.Busy.Count}");
        if (of - c.Free.Count - c.Busy.Count is > 0 and var away) rest.Add($"away for {away}");
        said.Add((of == 1 ? "Free" : c.Free.Count == of ? $"Free for all {of}" : $"Free for {c.Free.Count} of {of}") + (rest.Count > 0 ? ", " + string.Join(", ", rest) : ""));
        var taught = c.Taught.Select(id => t.Classes[id]?.Name).OfType<string>().Order(NameOrder.Comparer).ToList();
        if (taught.Count > 0) said.Add("has taught " + EquipmentWords.And(taught));
        said.Add($"{Cccc.Domain.Week.Duration(c.Week)} this week");
        if (c.WouldBeOverLimit) said.Add("would go over their limit");
        return string.Join(" · ", said);
    }

    /// <summary>
    /// What OK does with somebody chosen as an absence is recorded: "OK records it with Cleo Marchetti covering all 3.",
    /// "… covering Creation lab.", or "… covering 2 of the 3; Creation lab on Tue 24 Nov still needs cover."; and that
    /// one Undo takes both back.
    /// </summary>
    public static string Recording(Timetable t, StretchTaker c, AwayStretch s)
    {
        int of = s.Gaps.Count;
        var covering = of == 1 ? Looks.Title(t, s.Gaps[0].Session) : c.Free.Count == of ? $"all {of}" : $"{c.Free.Count} of the {of}";
        var left = s.Gaps.Where(o => !c.Free.Any(f => f.Session.Id == o.Session.Id)).ToList();
        var still = left.Count switch
        {
            0 => "",
            1 => $"; {Looks.Title(t, left[0].Session)} on {Cccc.Domain.Week.ShortDate(left[0].Date)} still needs cover",
            _ => $"; {left.Count} still need cover",
        };
        return $"OK records it with {c.Person.Name} covering {covering}{still}. One Undo takes both back.";
    }

    /// <summary>"Cleo covers all of it", "Dag covers 2 of them": what pressing their name does, for a screen reader.</summary>
    public static string Press(StretchTaker c, int of) => $"{c.Person.Name} covers {(c.Free.Count == of ? "all of it" : $"{c.Free.Count} of them")}";
}
