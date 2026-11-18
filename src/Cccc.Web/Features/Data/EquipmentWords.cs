using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;

namespace Cccc.Web.Features.Data;

/// <summary>What a person reads about the school's equipment: where a piece is, what needs it, and lists of it.</summary>
public static class EquipmentWords
{
    /// <summary>"In Aerial Hall and Big Top · needed by Aerial", or "In no space · needed by no class".</summary>
    public static string Uses(Timetable t, Guid id)
    {
        var spaces = t.Rooms.Current().Where(r => r.Has.Contains(id)).Select(r => r.Name).OrderBy(n => n, NameOrder.Comparer).ToList();
        var classes = t.Classes.Current().Where(c => c.Needs.Contains(id)).Select(c => c.Name).OrderBy(n => n, NameOrder.Comparer).ToList();
        return (spaces.Count == 0 ? "In no space" : "In " + And(spaces)) + " · " + (classes.Count == 0 ? "needed by no class" : "needed by " + And(classes));
    }

    /// <summary>The pieces named, in the order a person reads names: "Crash mats, Rigging points". Gone ones are left out.</summary>
    public static string Names(Timetable t, IEnumerable<Guid> ids) =>
        string.Join(", ", ids.Select(i => t.Equipment[i]?.Name).OfType<string>().Distinct().OrderBy(n => n, NameOrder.Comparer));

    /// <summary>"without Crash mats and Rigging points": what a space lacks for a class, said beside the space.</summary>
    public static string Without(IEnumerable<Equipment> lacking) => "without " + And([.. lacking.Select(e => e.Name)]);

    /// <summary>
    /// Find a time's line on what a class needs, when it left spaces out for lacking it: which spaces it looked in,
    /// or that none of those asked has it all. Nothing when nothing was left out.
    /// </summary>
    public static string? LeftOut(Timetable t, Session c, IReadOnlyList<Guid> asked, IReadOnlyList<Guid> unsuited)
    {
        var needs = Spaces.Needed(t, c.ClassId);
        if (unsuited.Count == 0 || needs.Count == 0) return null;
        var start = $"{Cccc.Web.Features.Week.Looks.Title(t, c)} needs {And([.. needs.Select(e => e.Name)])}";
        var looked = asked.Distinct().Where(r => !unsuited.Contains(r)).Select(r => t.Rooms[r]?.Name).OfType<string>().ToList();
        if (looked.Count > 0) return $"{start}, so only {And(looked)} {(looked.Count == 1 ? "is" : "are")} looked in.";
        return unsuited.Count == 1 && t.Rooms[unsuited[0]] is { } only
            ? $"{start}, and {only.Name} lacks {And([.. Spaces.Lacking(t, c.ClassId, only.Id).Select(e => e.Name)])}."
            : $"{start}, and none of these spaces has {needs.Count switch { 1 => "it", 2 => "both", _ => "all of them" }}.";
    }

    /// <summary>"Crash mats and Rigging points".</summary>
    public static string And(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };
}
