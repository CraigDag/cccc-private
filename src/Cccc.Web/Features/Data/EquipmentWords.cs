using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;

namespace Cccc.Web.Features.Data;

/// <summary>What a person reads about the school's equipment: where a piece is, what needs it, and lists of it.</summary>
public static class EquipmentWords
{
    /// <summary>
    /// "In Aerial Hall (out of use, back Mon 30 Nov) and Big Top · needed by Aerial", or "In no space · needed by no
    /// class": out of use as it is on <paramref name="today"/>, when given.
    /// </summary>
    public static string Uses(Timetable t, Guid id, DateOnly? today = null)
    {
        var spaces = t.Rooms.Current().Where(r => r.Has.Contains(id)).OrderBy(r => r.Name, NameOrder.Comparer)
            .Select(r => r.Name + (today is { } day && Spaces.OutOn(t, r.Id, id, day) is { } o ? $" ({OutShort(o)})" : "")).ToList();
        var classes = t.Classes.Current().Where(c => c.Needs.Contains(id)).Select(c => c.Name).OrderBy(n => n, NameOrder.Comparer).ToList();
        return (spaces.Count == 0 ? "In no space" : "In " + And(spaces)) + " · " + (classes.Count == 0 ? "needed by no class" : "needed by " + And(classes));
    }

    /// <summary>The pieces named, in the order a person reads names: "Crash mats, Rigging points". Gone ones are left out.</summary>
    public static string Names(Timetable t, IEnumerable<Guid> ids) =>
        string.Join(", ", ids.Select(i => t.Equipment[i]?.Name).OfType<string>().Distinct().OrderBy(n => n, NameOrder.Comparer));

    /// <summary>"without Crash mats and Rigging points": what a space does not have for a class, said beside the space.</summary>
    public static string Without(IEnumerable<Equipment> lacking) => "without " + And([.. lacking.Select(e => e.Name)]);

    /// <summary>
    /// What a space lacks for a class on a day, said beside the space: what it does not have, then what it has out
    /// of use (<see cref="Spaces.SayOut"/>): "without Crash mats; Tumble track out of use, back Mon 30 Nov".
    /// </summary>
    public static string Without(IReadOnlyList<Lack> lacks) => string.Join("; ",
        new[] { lacks.Where(l => l.Out is null).Select(l => l.Piece).ToList() is { Count: > 0 } missing ? Without(missing) : "" }
            .Concat(lacks.Where(l => l.Out is not null).Select(Spaces.SayOut)).Where(x => x.Length > 0));

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
            ? $"{start}, and {only.Name} lacks {And([.. Spaces.Missing(t, c.ClassId, only.Id).Select(e => e.Name)])}."
            : $"{start}, and none of these spaces has {needs.Count switch { 1 => "it", 2 => "both", _ => "all of them" }}.";
    }

    /// <summary>"Crash mats in Gym": what is out of use, and where.</summary>
    public static string What(Timetable t, OutOfUse o) =>
        $"{t.Equipment[o.EquipmentId]?.Name ?? "Equipment no longer listed"} in {t.Rooms[o.RoomId]?.Name ?? "a space that has gone"}";

    /// <summary>"back Mon 30 Nov", the day after its last day out of use, or "until somebody says it is back".</summary>
    public static string Back(OutOfUse o) => o.Until is { } last ? "back " + Cccc.Domain.Week.ShortDate(last.AddDays(1)) : "until somebody says it is back";

    /// <summary>"out of use, back Mon 30 Nov", or only "out of use": said after a space's name.</summary>
    public static string OutShort(OutOfUse o) => "out of use" + (o.Until is { } last ? ", back " + Cccc.Domain.Week.ShortDate(last.AddDays(1)) : "");

    /// <summary>"Out of use since Wed 18 Nov, back Mon 30 Nov", or "from" a day still to come.</summary>
    public static string Spell(OutOfUse o, DateOnly today) =>
        (o.From <= today ? "Out of use since " : "Out of use from ") + Cccc.Domain.Week.ShortDate(o.From) + ", " + Back(o);

    /// <summary>
    /// The classes a piece out of use leaves without it (<see cref="Spaces.Affected"/>), the first few and how many
    /// more: "2 classes need it there while it is out: Aerial Tue 24 Nov 09:00, Aerial Thu 26 Nov 09:00."
    /// </summary>
    public static string Affects(Timetable t, IReadOnlyList<Occurrence> affected, int most = 4) => affected.Count == 0
        ? "No class needs it there while it is out."
        : $"{Cccc.Web.Features.Week.Looks.Count(affected.Count, "class needs", "classes need")} it there while it is out: "
          + string.Join(", ", affected.Take(most).Select(o => $"{Cccc.Web.Features.Week.Looks.Title(t, o.Session)} {Cccc.Domain.Week.ShortDate(o.Date)} {Cccc.Domain.Week.Hm(o.Start)}"))
          + (affected.Count > most ? $" and {affected.Count - most} more" : "") + ".";

    /// <summary>"Crash mats and Rigging points".</summary>
    public static string And(IReadOnlyList<string> names) => names.Count switch
    {
        0 => "",
        1 => names[0],
        _ => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
    };
}
