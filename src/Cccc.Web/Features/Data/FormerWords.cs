using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Features.Data;

/// <summary>
/// What a class, space or facilitator's form says about deleting it, or about it being former: counted
/// off the timetable by <see cref="Former.Of"/>, the same count the delete itself decides by, so the
/// sentence and what the button does cannot disagree.
/// </summary>
public static class FormerWords
{
    /// <summary>The line under a form: what Delete (Remove, for a person) would do, or what being former means. Null when there is nothing to say.</summary>
    public static string? For(Timetable t, IRetirable x, DateOnly today)
    {
        var (before, ahead) = Former.Of(t, x, today);
        string Classes(int n) => Looks.Count(n, "class", "classes");
        if (x.IsFormer())
            return x switch
            {
                Facilitator => $"A former facilitator, {Former.Since(x)}. Kept for the {Classes(before)} they taught before then, so those weeks and the reports still name them; nothing ahead offers them. Bringing them back offers them again; it does not put them back on the classes they were taken off.",
                Room => $"A former space, {Former.Since(x)}. Kept for the {Classes(before)} held in it before then, so those weeks still say where they were; nothing ahead offers it. Bringing it back offers it again; it does not put it back on the classes it was taken off.",
                _ => $"A former class, {Former.Since(x)}. Kept for the {Classes(before)} it ran before then, so those weeks and the reports still show it; nothing ahead offers it. Bringing it back offers it again; it does not put it back on the classes it was taken off.",
            };
        if (before == 0 && ahead == 0) return null;
        return x switch
        {
            Facilitator => before > 0
                ? $"Removing keeps them as a former facilitator for the {Classes(before)} they taught before today" + (ahead > 0 ? $", and takes them off the {Classes(ahead)} from today on." : ".")
                : $"Removing takes them off the {Classes(ahead)} from today on.",
            Room => before > 0
                ? $"Deleting keeps it as a former space for the {Classes(before)} held in it before today" + (ahead > 0 ? $"; the {Classes(ahead)} from today on keep running, with no space." : ".")
                : $"Deleting leaves the {Classes(ahead)} from today on running, with no space.",
            _ => before > 0
                ? $"Deleting keeps it as a former class for the {Classes(before)} it ran before today" + (ahead > 0 ? $", and takes it off the {Classes(ahead)} from today on, which keep their name." : ".")
                : $"Deleting takes it off the {Classes(ahead)} from today on, which keep their name.",
        };
    }

    /// <summary>"Former since Sat 14 Nov · 46 classes before then": a former row's detail in the list.</summary>
    public static string Detail(Timetable t, IRetirable x, DateOnly today) =>
        $"Former {Former.Since(x)} · {Looks.Count(Former.Of(t, x, today).Before, "class", "classes")} before then";
}
