using System.Text.Json;
using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Features.Plan;

using Week = Cccc.Domain.Week;

/// <summary>
/// One line of a chosen class's "Elsewhere that day": when, what is happening then, and who or what of the
/// class's is there. A class or an event that several of them are at is one line.
/// </summary>
public sealed record ElsewhereLine(string When, string What, string Who, Guid? ClassId, bool Away)
{
    public string Text => $"{When} {What}: {Who}";
}

/// <summary>
/// The words for the stretches a chosen class's people, groups and space are elsewhere (<see cref="Busy"/>):
/// the side panel's list, a band's title, and the few words beside a class while it is dragged over one.
/// Nothing is said that the reader may not see: an event they may not read is only "Booked", and students
/// are only ever counted, never named.
/// </summary>
public static class ElsewhereWords
{
    public static IReadOnlyList<ElsewhereLine> Lines(Timetable t, IEnumerable<BusyStretch> busy) =>
        busy.GroupBy(b => (b.From, b.Until, Key(b)))
            .Select(g => new ElsewhereLine(
                When(g.Key.From, g.Key.Until),
                What(t, g.First()),
                string.Join(", ", g.OrderBy(Rank).ThenBy(b => Who(t, b), NameOrder.Comparer).Select(b => Who(t, b)).Distinct()),
                g.First().Class?.Session.Id,
                g.Any(b => b.Kind == BusyKind.Away)))
            .ToList();

    /// <summary>What a band is, for its title: each of its lines on a line of its own.</summary>
    public static string Title(Timetable t, BusyBand band) => string.Join("\n", Lines(t, band.Stretches).Select(l => l.Text));

    /// <summary>A few words while a class is dragged over a stretch: "Ada Brightwater busy", "Big Top in use".</summary>
    public static string Short(Timetable t, BusyStretch b) => b.Kind switch
    {
        BusyKind.Away => $"{Person(t, b.PersonId)} away",
        BusyKind.Space => $"{Space(t, b.RoomId)} {(b.Class is null ? "booked" : "in use")}",
        _ when b.PersonId is not null => $"{Person(t, b.PersonId)} busy",
        _ when b.GroupIds.Count > 0 => $"{Groups(t, b.GroupIds)} busy",
        _ => $"{Looks.Count(b.Students, "student")} busy",
    };

    /// <summary>
    /// What the plan's script needs to say so while a class is dragged: each day's stretches as minutes, whose
    /// they are (a facilitator's, kept only from their part of the class, or everybody's), and the short words.
    /// </summary>
    public static string DragData(Timetable t, IReadOnlyDictionary<DateOnly, IReadOnlyList<BusyStretch>> elsewhere, bool withSpace) =>
        JsonSerializer.Serialize(elsewhere.ToDictionary(
            kv => kv.Key.ToString("yyyy-MM-dd"),
            kv => kv.Value.Where(b => withSpace || b.Kind != BusyKind.Space)
                .Select(b => new object[] { PlanModel.Minutes(b.From), PlanModel.Until(b.Until), b.PersonId?.ToString() ?? "", Short(t, b) })
                .ToList()));

    /// <summary>
    /// Each person's part of the chosen class, for the script: [id, from, until, set from, set until] in minutes,
    /// the last two -1 where the part runs to the class's own edge. A move carries a part with it; a stretch
    /// leaves a set time where it is and moves only the edges that follow the class.
    /// </summary>
    public static string Parts(Session s) =>
        JsonSerializer.Serialize(s.Facilitators.Select(a =>
        {
            var (from, until) = TimeWindows.Assigned(s, a.FacilitatorId);
            return new object[] { a.FacilitatorId.ToString(), PlanModel.Minutes(from), PlanModel.Minutes(until), a.From is { } f ? PlanModel.Minutes(f) : -1, a.Until is { } u ? PlanModel.Minutes(u) : -1 };
        }));

    public static string When(TimeOnly from, TimeOnly until) =>
        from == TimeOnly.MinValue && until == TimeOnly.MaxValue ? "All day" : $"{Week.Hm(from)}–{(until == TimeOnly.MaxValue ? "24:00" : Week.Hm(until))}";

    private static string Key(BusyStretch b) =>
        b.Class is { } o ? "class:" + o.Session.Id
        : b.Event is { } e ? "event:" + e.Id
        : b.Kind == BusyKind.Away ? "away"
        : "booked";

    private static string What(Timetable t, BusyStretch b) =>
        b.Class is { } o ? Looks.Title(t, o.Session)
        : b.Event is { } e ? EventSchedule.Title(e)
        : b.Kind == BusyKind.Away ? "Away"
        : "Booked";

    private static int Rank(BusyStretch b) => b.PersonId is not null ? 0 : b.GroupIds.Count > 0 ? 1 : b.Students > 0 ? 2 : 3;

    private static string Who(Timetable t, BusyStretch b) => b switch
    {
        { Kind: BusyKind.Space } => $"{Space(t, b.RoomId)} {(b.Class is null ? "booked" : "in use")}",
        { PersonId: { } id } => Person(t, id) + (b.Kind == BusyKind.Teaching && b.Class is { } o && o.Session.CoverIds.Contains(id) && !o.Session.FacilitatorIds.Contains(id) ? " covering" : ""),
        { GroupIds.Count: > 0 } => Groups(t, b.GroupIds),
        _ => $"{b.Students} of its students",
    };

    private static string Person(Timetable t, Guid? id) => t.Facilitators.Find(id)?.Name ?? "Somebody";

    private static string Space(Timetable t, Guid? id) => t.Rooms.Find(id)?.Name ?? "Its space";

    private static string Groups(Timetable t, IEnumerable<Guid> ids) => string.Join(", ", ids.Select(g => t.Groups[g]?.Name ?? "A group"));
}
