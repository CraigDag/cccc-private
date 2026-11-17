using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;

namespace Cccc.Web.Features.Week;

using Week = Cccc.Domain.Week;

/// <summary>How a session is described on a block, a list row or a sheet: one set of words for all of them.</summary>
public static class Looks
{
    /// <summary>A block is coloured by its CLASS; a class-less session is neutral, never its cohort's colour.</summary>
    public static string? ColourOf(Timetable t, Session s) => t.Classes.Find(s.ClassId)?.Colour;

    public static string Title(Timetable t, Session s) =>
        string.IsNullOrWhiteSpace(s.Title) ? t.Classes.Find(s.ClassId)?.Name ?? "(untitled)" : s.Title.Trim();

    public static string When(Session s) => $"{Week.Hm(s.Start)}–{Week.Hm(s.End)}";

    public static string WhenWithDay(Session s) => $"{Week.ShortDate(s.Date)}, {When(s)}";

    public static string Room(Timetable t, Session s) => t.Rooms.Find(s.RoomId)?.Name ?? "";

    public static string Audience(Timetable t, Session s) => s.Audience.Kind switch
    {
        AudienceKind.Everyone => "Everyone",
        AudienceKind.Programme when t.Programmes.Find(s.Audience.ProgrammeId) is { } p => p.Name,
        _ => string.Join(", ", s.Audience.Cohorts.Select(g => t.Groups[g]?.Name).Where(n => n is not null)
                .OrderBy(n => n, NameOrder.Comparer)) is { Length: > 0 } names ? names : "No group",
    };

    /// <summary>Who is in the room, as initials: a covered person shows as "AB→CD".</summary>
    public static string Initials(Timetable t, Occurrence o)
    {
        var s = o.Session;
        var parts = new List<string>();
        foreach (var id in s.FacilitatorIds)
        {
            var f = t.Facilitators[id];
            if (f is null) continue;
            parts.Add(o.AwayIds.Contains(id) ? f.Initials + "→" + (s.CoverIds.Count > 0 ? string.Join("+", s.CoverIds.Select(c => t.Facilitators[c]?.Initials ?? "?")) : "?") : f.Initials);
        }
        if (s.FacilitatorIds.All(id => !o.AwayIds.Contains(id)))
            foreach (var c in s.CoverIds.Where(c => !s.FacilitatorIds.Contains(c)))
                parts.Add(t.Facilitators[c]?.Initials ?? "?");
        return string.Join(" ", parts.Distinct());
    }

    public static string Names(Timetable t, IEnumerable<Guid> ids) =>
        string.Join(", ", ids.Select(i => t.Facilitators[i]?.Name).Where(n => n is not null).OrderBy(n => n, NameOrder.Comparer));

    public static string StateWord(Occurrence o) => o.State switch
    {
        OccurrenceState.Closed => "Closed" + (o.ClosedBy is { Name.Length: > 0 } c ? $" ({c.Name})" : ""),
        OccurrenceState.Cancelled => "Cancelled",
        OccurrenceState.NeedsCover => "Needs cover",
        _ when o.Covered => "Covered",
        _ => "",
    };

    public static string DayHeading(DateOnly d) => Week.ShortDate(d);
}
