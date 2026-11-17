using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Features.Print;

/// <summary>A class on the printed day. <see cref="Shown"/> is false for a class that is not on this sheet but still fills its space.</summary>
public sealed record SheetBar(Occurrence O, int Start, int End, bool Shown, int Lane);

/// <summary>One space's row on the printed day, its classes stacked where they overlap.</summary>
public sealed record SheetRow(string Key, string Name, string? Colour, string Section, IReadOnlyList<SheetBar> Bars, int Lanes);

/// <summary>
/// What the printed building's day draws, worked out apart from the drawing so it can be tested: a row
/// a space, in the plan's order (by category, then name), and in each row its classes on that day,
/// stacked in lanes where they overlap, exactly as the plan's Spaces view stacks them.
/// </summary>
/// <summary>An event in the band along the top of the printed day. An all-day one spans the whole day.</summary>
public sealed record SheetEvent(EventOccurrence E, int Start, int End, bool AllDay, int Lane);

/// <summary>One sheet of a printed day: its rows, how tall a lane is, where the rows start and end, and whether it needs the grey bar's key.</summary>
public sealed record SheetPage(IReadOnlyList<SheetRow> Rows, float Lane, float Top, float Bottom, bool Legend);

public static class SpacesDay
{
    public const string NoSpace = "none";

    /// <summary>The height a sheet's grid has: A4 landscape, less the margins, the heading and the foot.</summary>
    public const float PageHeight = 595 - 2 * 24 - 50;
    public const float Ruler = 13, Section = 14, Legend = 14, EventLane = 13;
    /// <summary>The tallest lane, for a school with few spaces, and the thinnest that still holds a line of words.</summary>
    public const float MaxLane = 48, MinLane = 14;

    /// <summary>The day's printable events, stacked where they overlap. Never an Administrators-only one.</summary>
    public static IReadOnlyList<SheetEvent> Events(WeekModel m, DateOnly day, EventViewer viewer)
    {
        var laid = new List<SheetEvent>();
        var ends = new List<int>();
        foreach (var e in m.Events.Where(e => e.Covers(day) && viewer.Prints(e.Event))
                     .OrderBy(e => e.IsAllDayStrip ? 0 : 1).ThenBy(e => e.Event.Start).ThenBy(e => e.Event.Title, NameOrder.Comparer))
        {
            bool all = e.IsAllDayStrip;
            int start = all ? 0 : WeekModel.Minutes(e.Event.Start), end = all ? 24 * 60 : WeekModel.Minutes(e.Event.End);
            int lane = ends.FindIndex(x => x <= start);
            if (lane < 0) { lane = ends.Count; ends.Add(end); } else ends[lane] = end;
            laid.Add(new SheetEvent(e, start, end, all, lane));
        }
        return laid;
    }

    /// <summary>
    /// Shares a day's rows out over sheets. A few spaces fill one sheet with tall lanes (up to
    /// <see cref="MaxLane"/>); when even the thinnest readable lane (<see cref="MinLane"/>) would not fit
    /// them all, the day goes on over another sheet rather than leaving a space off, because a space
    /// missing from a sheet reads as free. Every sheet of a day has the same lane, and the row of classes
    /// with no space repeats at the top of each (the current app's rule for its room grid).
    /// </summary>
    public static IReadOnlyList<SheetPage> Pages(IReadOnlyList<SheetRow> rows, int eventLanes)
    {
        float top = Ruler + (eventLanes == 0 ? 0 : eventLanes * EventLane + 4);
        var lead = rows.FirstOrDefault(r => r.Key == NoSpace);
        static int Sections(IEnumerable<SheetRow> rs) => rs.Select(r => r.Section).Where(x => x.Length > 0).Distinct().Count();
        static bool NeedsKey(IEnumerable<SheetRow> rs) => rs.Any(r => r.Bars.Any(b => !b.Shown));
        float Room(IReadOnlyCollection<SheetRow> rs) => PageHeight - top - Sections(rs) * Section - (NeedsKey(rs) ? Legend : 0);

        var sheets = new List<List<SheetRow>>();
        var current = lead is null ? new List<SheetRow>() : [lead];
        foreach (var r in rows.Where(r => r.Key != NoSpace))
        {
            var with = current.Append(r).ToList();
            if (current.Any(x => x.Key != NoSpace) && with.Sum(x => x.Lanes) * MinLane > Room(with))
            {
                sheets.Add(current);
                current = lead is null ? [r] : [lead, r];
            }
            else current = with;
        }
        sheets.Add(current);

        float lane = Math.Min(MaxLane, sheets.Min(p => Room(p) / Math.Max(1, p.Sum(r => r.Lanes))));
        return sheets.Select(p => new SheetPage(p, lane, top, top + Sections(p) * Section + p.Sum(r => r.Lanes) * lane, NeedsKey(p))).ToList();
    }

    /// <summary>
    /// The classes on the sheet are the week as narrowed (<see cref="WeekModel.Visible"/>). Every other
    /// class that happens in a space that day is still drawn in its row, as a bar with no words, so that
    /// narrowed to a group a space busy with another group never looks free; a hidden guest class does the
    /// same, because hiding is drawing, never counting. Classes with no space get a row of their own,
    /// FIRST: this sheet is how a building gets scheduled, and what still needs a space leads (the current
    /// app's rule, Craig's call of 2 September 2026). Narrowed to spaces, only those spaces have rows.
    /// </summary>
    public static IReadOnlyList<SheetRow> Rows(Timetable t, WeekModel m, DateOnly day, IReadOnlySet<Guid>? onlyRooms = null)
    {
        var shown = m.Visible.Where(o => o.Date == day).ToList();
        var shownIds = shown.Select(o => o.Session.Id).ToHashSet();
        string KeyOf(Occurrence o) => o.Session.RoomId is { } id && t.Rooms[id] is not null ? id.ToString() : NoSpace;
        var bars = shown.Select(o => (Key: KeyOf(o), O: o, Shown: true))
            .Concat(m.All.Where(o => o.Date == day && o.Happens && !shownIds.Contains(o.Session.Id) && KeyOf(o) != NoSpace)
                .Select(o => (Key: KeyOf(o), O: o, Shown: false)))
            .ToLookup(x => x.Key);

        var rows = new List<(string Key, string Name, string? Colour, string Section)>();
        if (bars[NoSpace].Any()) rows.Add((NoSpace, "No space given", null, ""));
        foreach (var r in t.Rooms.Where(r => onlyRooms is not { Count: > 0 } || onlyRooms.Contains(r.Id))
                     .OrderBy(r => r.Category is null).ThenBy(r => r.Category, NameOrder.Comparer).ThenBy(r => r.Name, NameOrder.Comparer))
            rows.Add((r.Id.ToString(), r.Name, r.Colour, r.Category ?? "Other spaces"));

        return rows.Select(r =>
        {
            var laid = new List<SheetBar>();
            var ends = new List<int>();
            foreach (var x in bars[r.Key].OrderBy(x => x.O.Start).ThenByDescending(x => x.O.End).ThenBy(x => x.Shown ? 0 : 1))
            {
                int start = WeekModel.Minutes(x.O.Start), end = WeekModel.Minutes(x.O.End);
                int lane = ends.FindIndex(e => e <= start);
                if (lane < 0) { lane = ends.Count; ends.Add(end); } else ends[lane] = end;
                laid.Add(new SheetBar(x.O, start, end, x.Shown, lane));
            }
            return new SheetRow(r.Key, r.Name, r.Colour, r.Section, laid, Math.Max(1, ends.Count));
        }).ToList();
    }
}
