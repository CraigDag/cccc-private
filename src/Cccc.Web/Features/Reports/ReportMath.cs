using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;

namespace Cccc.Web.Features.Reports;

using Week = Cccc.Domain.Week;

public sealed record HoursRow(Guid? Id, string Name, string? Colour, TimeSpan Hours, TimeSpan? Target = null, string? Note = null);

public sealed record AttendanceRow(Guid Id, string Name, string? Colour, int Registers, int Present, int Excused, int Absent)
{
    public int Marked => Present + Excused + Absent;
    public double? PresentShare => Marked == 0 ? null : (double)Present / Marked;
}

/// <summary>The sums behind the reports. Every one reads occurrences, so a closure or a cancellation counts the same here as on the week.</summary>
public static class ReportMath
{
    public static IEnumerable<DateOnly> Mondays(DateOnly from, DateOnly to)
    {
        for (var m = Week.MondayOf(from); m <= Week.MondayOf(to); m = m.AddDays(7)) yield return m;
    }

    public static IReadOnlyList<Occurrence> Happening(Timetable t, DateOnly from, DateOnly to) =>
        Occurrences.Between(t, Week.MondayOf(from), Week.MondayOf(to).AddDays(6)).Where(o => o.Happens).ToList();

    /// <summary>
    /// Hours by group. A class for several groups counts in full for each, so this column can add up to
    /// more than the total; a whole-building session's hours are their own last row, never every group's.
    /// Every group is seeded at zero so one whose week is all whole-building does not vanish.
    /// </summary>
    public static List<HoursRow> ByGroup(Timetable t, IReadOnlyList<Occurrence> occ)
    {
        var rows = t.Groups.ToDictionary(g => g.Id, _ => TimeSpan.Zero);
        var everyone = TimeSpan.Zero;
        foreach (var o in occ)
        {
            if (o.Session.Audience.IsEveryone) { everyone += o.Session.Length; continue; }
            foreach (var g in o.Session.Audience.Cohorts)
                if (rows.ContainsKey(g)) rows[g] += o.Session.Length;
        }
        var list = rows.Select(kv => new HoursRow(kv.Key, t.Groups[kv.Key]!.Name, t.Groups[kv.Key]!.Colour, kv.Value))
            .OrderBy(r => r.Name, NameOrder.Comparer).ToList();
        list.Add(new HoursRow(null, "Whole building", null, everyone, Note: "On for everyone; not added to any group"));
        return list;
    }

    public static List<HoursRow> ByProgramme(Timetable t, IReadOnlyList<Occurrence> occ)
    {
        var list = new List<HoursRow>();
        foreach (var p in t.Programmes.OrderBy(p => p.Name, NameOrder.Comparer))
        {
            var groups = t.GroupsOf(p.Id).Select(g => g.Id).ToHashSet();
            var hours = occ.Where(o => !o.Session.Audience.IsEveryone && (o.Session.Audience.ProgrammeId == p.Id || o.Session.Audience.Cohorts.Any(groups.Contains)))
                .Aggregate(TimeSpan.Zero, (a, o) => a + o.Session.Length);
            list.Add(new HoursRow(p.Id, p.Name + (p.IsGuest ? " (guest)" : ""), p.Colour, hours));
        }
        return list;
    }

    /// <summary>Hours by area, each session once, against the area's weekly target for one group.</summary>
    public static List<HoursRow> ByArea(Timetable t, IReadOnlyList<Occurrence> occ, int weeks)
    {
        var list = new List<HoursRow>();
        foreach (var a in t.Areas.OrderBy(a => a.Name, NameOrder.Comparer))
        {
            var mine = occ.Where(o => t.Classes.Find(o.Session.ClassId)?.AreaId == a.Id).ToList();
            var hours = mine.Aggregate(TimeSpan.Zero, (x, o) => x + o.Session.Length);
            int groups = Math.Max(1, mine.SelectMany(o => o.Session.Audience.Cohorts).Distinct().Count());
            var perGroupWeek = weeks == 0 ? TimeSpan.Zero : TimeSpan.FromMinutes(mine.Sum(o => o.Session.Length.TotalMinutes * Math.Max(1, o.Session.Audience.Cohorts.Count())) / groups / weeks);
            list.Add(new HoursRow(a.Id, a.Name, null, hours, a.WeeklyTarget, $"{Week.Duration(perGroupWeek)} a week for a group"));
        }
        var none = occ.Where(o => t.Classes.Find(o.Session.ClassId)?.AreaId is null).Aggregate(TimeSpan.Zero, (x, o) => x + o.Session.Length);
        if (none > TimeSpan.Zero) list.Add(new HoursRow(null, "No area", null, none));
        return list;
    }

    public static (List<AttendanceRow> Classes, List<AttendanceRow> Students) Attendance(Timetable t, IReadOnlyList<Occurrence> occ)
    {
        var classes = new Dictionary<Guid, (int R, int P, int E, int A)>();
        var students = t.Students.ToDictionary(s => s.Id, _ => (R: 0, P: 0, E: 0, A: 0));
        foreach (var o in occ)
        {
            if (t.Registers[o.Session.Id] is not { } r) continue;
            var key = o.Session.ClassId ?? Guid.Empty;
            var (p, e, a) = r.Tally();
            var c = classes.GetValueOrDefault(key);
            classes[key] = (c.R + 1, c.P + p, c.E + e, c.A + a);
            foreach (var id in r.RosterIds)
            {
                if (!students.TryGetValue(id, out var s)) continue;
                var m = r.MarkFor(id);
                students[id] = (s.R + 1, s.P + (m == Mark.Present ? 1 : 0), s.E + (m == Mark.Excused ? 1 : 0), s.A + (m == Mark.Absent ? 1 : 0));
            }
        }
        var cl = classes.Select(kv => new AttendanceRow(kv.Key, t.Classes[kv.Key]?.Name ?? "No class", t.Classes[kv.Key]?.Colour, kv.Value.R, kv.Value.P, kv.Value.E, kv.Value.A))
            .OrderBy(r => r.Name, NameOrder.Comparer).ToList();
        var st = students.Where(kv => kv.Value.R > 0)
            .Select(kv => new AttendanceRow(kv.Key, t.Students[kv.Key]!.Name, t.Groups.Find(t.Students[kv.Key]!.GroupId)?.Colour, kv.Value.R, kv.Value.P, kv.Value.E, kv.Value.A))
            .OrderBy(r => r.Name, NameOrder.Comparer).ToList();
        return (cl, st);
    }

    public static string Share(double? d) => d is { } x ? $"{x * 100:0}%" : "–";
}
