using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>What the lane layout needs to know about one block.</summary>
public sealed record LaneItem(Guid Id, DateOnly Date, int StartMinutes, int EndMinutes, Audience Audience)
{
    public static LaneItem Of(Session s) =>
        new(s.Id, s.Date, s.Start.Hour * 60 + s.Start.Minute, Math.Max(s.Start.Hour * 60 + s.Start.Minute + 1, s.End.Hour * 60 + s.End.Minute), s.Audience);
}

public enum LaneKind { Cohort, Nobody }

public sealed record Lane(LaneKind Kind, Guid? GroupId, string Name);

/// <summary>
/// Where a block is drawn inside its day column: Left and Width as fractions of the column. A block
/// drawn <see cref="Beneath"/> keeps its whole span under what runs inside it, and its words start at
/// <see cref="ClearFrom"/> so they are not hidden.
/// </summary>
public sealed record Placed(Guid Id, DateOnly Date, int StartMinutes, int EndMinutes, double Left, double Width,
    int LaneIndex, int LaneCount, bool Beneath, double ClearFrom);

/// <summary>
/// The one lane implementation, shared by the screen and the printed sheets (the old <c>WeekLanes</c>).
/// Craig's rules, in his words: "1 group, own the whole column; 2 groups, split the column; shared
/// blocks, if they contain the filtered group, still own the width as normal."
/// </summary>
public static class Lanes
{
    private readonly record struct Span(int First, int Last)
    {
        public int Count => Last - First + 1;
        public bool Covers(int lane) => lane >= First && lane <= Last;
    }

    /// <summary>The week's lanes, in Danish name order: one per group with a class of its own; none for one person's week.</summary>
    public static IReadOnlyList<Lane> ForWeek(IEnumerable<LaneItem> items, Timetable t, bool onePerson = false)
    {
        if (onePerson) return [];
        var present = new Dictionary<Guid, string>();
        bool nobody = false;
        foreach (var i in items)
        {
            if (i.Audience.Kind != AudienceKind.Group) continue;
            bool any = false;
            foreach (var id in i.Audience.Cohorts)
                if (t.Groups[id] is { } g && !string.IsNullOrWhiteSpace(g.Name)) { present[id] = g.Name; any = true; }
            if (!any) nobody = true;
        }
        var lanes = present.Select(kv => new Lane(LaneKind.Cohort, kv.Key, kv.Value))
            .OrderBy(l => l.Name, NameOrder.Comparer).ThenBy(l => l.GroupId).ToList();
        if (nobody) lanes.Add(new Lane(LaneKind.Nobody, null, ""));
        return lanes;
    }

    /// <summary>A day splits only when two or more groups have sessions of their own that day.</summary>
    public static IReadOnlyList<Lane> OnDay(IEnumerable<LaneItem> thatDay, IReadOnlyList<Lane> weekLanes)
    {
        if (weekLanes.Count == 0) return weekLanes;
        var here = new HashSet<int>();
        bool nobody = false;
        foreach (var i in thatDay)
        {
            if (i.Audience.Kind != AudienceKind.Group) continue;
            var ids = i.Audience.Cohorts.ToHashSet();
            bool any = false;
            for (int k = 0; k < weekLanes.Count; k++)
                if (weekLanes[k].Kind == LaneKind.Cohort && ids.Contains(weekLanes[k].GroupId!.Value)) { here.Add(k); any = true; }
            if (!any) nobody = true;
        }
        if (here.Count >= 2 && nobody)
            for (int k = 0; k < weekLanes.Count; k++)
                if (weekLanes[k].Kind == LaneKind.Nobody) here.Add(k);
        return here.Count == 0 ? [] : weekLanes.Where((_, k) => here.Contains(k)).ToList();
    }

    private static Span SpanOf(LaneItem item, IReadOnlyList<Lane> lanes)
    {
        if (lanes.Count == 0) return new Span(0, 0);
        if (item.Audience.IsEveryone) return new Span(0, lanes.Count - 1);
        var ids = item.Audience.Cohorts.ToHashSet();
        int first = -1, last = -1;
        for (int i = 0; i < lanes.Count; i++)
            if (lanes[i].Kind == LaneKind.Cohort && ids.Contains(lanes[i].GroupId!.Value))
            {
                if (first < 0) first = i;
                last = i;
            }
        if (first >= 0) return new Span(first, last);
        if (item.Audience.Kind is AudienceKind.Groups or AudienceKind.Programme) return new Span(0, lanes.Count - 1);
        for (int i = 0; i < lanes.Count; i++)
            if (lanes[i].Kind == LaneKind.Nobody) return new Span(i, i);
        return new Span(lanes.Count - 1, lanes.Count - 1);
    }

    /// <summary>Places every block of the week, one day at a time against that day's own lanes.</summary>
    public static IReadOnlyList<Placed> Place(IEnumerable<LaneItem> items, IReadOnlyList<Lane> weekLanes)
    {
        var placed = new List<Placed>();
        foreach (var day in items.GroupBy(i => i.Date))
        {
            var dayLanes = OnDay(day, weekLanes);
            var thatDay = new List<Placed>();
            PlaceOneDay([.. day], dayLanes, thatDay);
            WidenIntoEmptyLanes(thatDay, Math.Max(dayLanes.Count, 1));
            placed.AddRange(thatDay);
        }
        return placed.OrderBy(p => p.Date).ThenBy(p => p.StartMinutes).ThenBy(p => p.LaneIndex).ToList();
    }

    /// <summary>A block widens into a neighbouring lane that is empty for its whole time (1.64.2).</summary>
    private static void WidenIntoEmptyLanes(List<Placed> day, int laneCount)
    {
        if (laneCount < 2) return;
        double lw = 1.0 / laneCount;
        const double Eps = 1e-9;
        bool Fills(Placed p) => !p.Beneath && Math.Abs(p.Left - lw * p.LaneIndex) < Eps && Math.Abs(p.Width - lw * p.LaneCount) < Eps;
        bool LaneIsFree(Placed p, int lane)
        {
            double left = lw * lane, right = left + lw;
            return !day.Any(o => !ReferenceEquals(o, p) && o.StartMinutes < p.EndMinutes && p.StartMinutes < o.EndMinutes
                                 && o.Left < right - Eps && left < o.Left + o.Width - Eps);
        }
        for (int i = 0; i < day.Count; i++)
        {
            var p = day[i];
            if (!Fills(p) || day.Count(o => o.Id == p.Id) > 1) continue;
            int first = p.LaneIndex, last = p.LaneIndex + p.LaneCount - 1;
            while (last + 1 < laneCount && LaneIsFree(p, last + 1)) last++;
            while (first - 1 >= 0 && LaneIsFree(p, first - 1)) first--;
            if (first == p.LaneIndex && last == p.LaneIndex + p.LaneCount - 1) continue;
            day[i] = p with { Left = lw * first, Width = lw * (last - first + 1), LaneIndex = first, LaneCount = last - first + 1, ClearFrom = lw * first };
        }
    }

    private sealed record Packed(LaneItem Item, int ColIndex, int ColCount);

    /// <summary>Columns inside overlapping clusters: the classic packing, each cluster as wide as it needs.</summary>
    private static Dictionary<Guid, Packed> Pack(IEnumerable<LaneItem> items)
    {
        var result = new Dictionary<Guid, Packed>();
        var sorted = items.OrderBy(x => x.StartMinutes).ThenBy(x => x.EndMinutes).ThenBy(x => x.Id).ToList();
        int i = 0;
        while (i < sorted.Count)
        {
            int start = i, maxEnd = sorted[i].EndMinutes, j = i + 1;
            while (j < sorted.Count && sorted[j].StartMinutes < maxEnd) { maxEnd = Math.Max(maxEnd, sorted[j].EndMinutes); j++; }
            var ends = new List<int>();
            var col = new int[j - start];
            for (int k = start; k < j; k++)
            {
                int c = ends.FindIndex(e => e <= sorted[k].StartMinutes);
                if (c < 0) { c = ends.Count; ends.Add(0); }
                ends[c] = sorted[k].EndMinutes;
                col[k - start] = c;
            }
            for (int k = start; k < j; k++) result[sorted[k].Id] = new Packed(sorted[k], col[k - start], ends.Count);
            i = j;
        }
        return result;
    }

    private static void PlaceOneDay(List<LaneItem> all, IReadOnlyList<Lane> lanes, List<Placed> placed)
    {
        int laneCount = Math.Max(lanes.Count, 1);
        double lw = 1.0 / laneCount;
        var spans = all.ToDictionary(r => r.Id, r => SpanOf(r, lanes));

        // A stretch of the day is divided only between the lanes something in it reaches (1.63.0).
        var onTheirOwn = new HashSet<Guid>();
        if (laneCount > 1)
            foreach (var cluster in Clusters(all))
            {
                if (cluster.All(r => spans[r.Id] == spans[cluster[0].Id])) continue;
                var reached = new SortedSet<int>();
                foreach (var r in cluster)
                    for (int k = spans[r.Id].First; k <= spans[r.Id].Last; k++) reached.Add(k);
                if (reached.Count == 0 || reached.Count >= laneCount) continue;
                var inner = new List<Placed>();
                PlaceOneDay(cluster, reached.Select(k => lanes[k]).ToList(), inner);
                foreach (var piece in inner)
                {
                    int first = Math.Clamp((int)Math.Floor(piece.Left / lw + 1e-9), 0, laneCount - 1);
                    int last = Math.Clamp((int)Math.Ceiling((piece.Left + piece.Width) / lw - 1e-9) - 1, first, laneCount - 1);
                    placed.Add(piece with { LaneIndex = first, LaneCount = last - first + 1 });
                }
                foreach (var r in cluster) onTheirOwn.Add(r.Id);
            }
        if (onTheirOwn.Count > 0) all = all.Where(r => !onTheirOwn.Contains(r.Id)).ToList();

        // A block nothing runs beside owns the whole column; so do several blocks of one group that
        // run only beside each other.
        var ownColumn = new Dictionary<Guid, Packed>();
        if (laneCount > 1)
            foreach (var cluster in Clusters(all))
            {
                var span = spans[cluster[0].Id];
                if (span.Count >= laneCount || cluster.Any(r => spans[r.Id] != span)) continue;
                foreach (var r in cluster) spans[r.Id] = new Span(0, laneCount - 1);
                if (cluster.Count < 2) continue;
                foreach (var kv in Pack(cluster)) ownColumn[kv.Key] = kv.Value;
            }

        var beneath = Beneath(all, spans);
        var byLane = new Dictionary<int, Dictionary<Guid, Packed>>();
        for (int lane = 0; lane < laneCount; lane++)
            byLane[lane] = Pack(all.Where(r => !beneath.Contains(r.Id) && !ownColumn.ContainsKey(r.Id) && spans[r.Id].Covers(lane)));

        foreach (var r in all)
        {
            var s = spans[r.Id];
            if (beneath.Contains(r.Id))
            {
                placed.Add(new Placed(r.Id, r.Date, r.StartMinutes, r.EndMinutes, lw * s.First, lw * s.Count, s.First, s.Count,
                    true, lw * FirstClearLane(r, all, spans)));
                continue;
            }
            if (ownColumn.TryGetValue(r.Id, out var own))
            {
                double w = 1.0 / own.ColCount;
                placed.Add(new Placed(r.Id, r.Date, r.StartMinutes, r.EndMinutes, w * own.ColIndex, w, 0, laneCount, false, w * own.ColIndex));
                continue;
            }
            var mine = Enumerable.Range(s.First, s.Count).Select(k => byLane[k][r.Id]).ToList();
            Placed At(int lane, double left, double width, int covered) =>
                new(r.Id, r.Date, r.StartMinutes, r.EndMinutes, left, width, lane, covered, false, left);
            if (s.Count > 1 && mine.All(b => b.ColCount == 1))
            {
                placed.Add(At(s.First, lw * s.First, lw * s.Count, s.Count));
                continue;
            }
            for (int k = 0; k < mine.Count; k++)
            {
                double width = lw / mine[k].ColCount;
                placed.Add(At(s.First + k, lw * (s.First + k) + width * mine[k].ColIndex, width, 1));
            }
        }
    }

    private static HashSet<Guid> Beneath(List<LaneItem> all, Dictionary<Guid, Span> spans)
    {
        var beneath = new HashSet<Guid>();
        foreach (var r in all)
        {
            var mine = spans[r.Id];
            if (mine.Count < 2) continue;
            var covered = new HashSet<int>();
            bool overlapped = false, widest = true;
            foreach (var o in all)
            {
                if (o.Id == r.Id || !Overlaps(o, r) || !Shares(spans[o.Id], mine)) continue;
                overlapped = true;
                if (spans[o.Id].Count >= mine.Count) { widest = false; break; }
                for (int k = spans[o.Id].First; k <= spans[o.Id].Last; k++) if (mine.Covers(k)) covered.Add(k);
            }
            if (overlapped && widest && covered.Count < mine.Count) beneath.Add(r.Id);
        }
        return beneath;
    }

    private static int FirstClearLane(LaneItem r, List<LaneItem> all, Dictionary<Guid, Span> spans)
    {
        var mine = spans[r.Id];
        for (int lane = mine.First; lane <= mine.Last; lane++)
            if (!all.Any(o => o.Id != r.Id && Overlaps(o, r) && spans[o.Id].Covers(lane))) return lane;
        return mine.First;
    }

    private static List<List<LaneItem>> Clusters(List<LaneItem> all)
    {
        var clusters = new List<List<LaneItem>>();
        List<LaneItem>? current = null;
        int end = int.MinValue;
        foreach (var r in all.OrderBy(r => r.StartMinutes).ThenBy(r => r.EndMinutes).ThenBy(r => r.Id))
        {
            if (current is null || r.StartMinutes >= end) { current = []; clusters.Add(current); end = r.EndMinutes; }
            else end = Math.Max(end, r.EndMinutes);
            current.Add(r);
        }
        return clusters;
    }

    private static bool Shares(Span a, Span b) => a.First <= b.Last && b.First <= a.Last;

    private static bool Overlaps(LaneItem a, LaneItem b) => a.StartMinutes < b.EndMinutes && b.StartMinutes < a.EndMinutes;
}

/// <summary>The grid's frame: the week's earliest class to its latest, plus an hour each side, in whole hours.</summary>
public static class GridFrame
{
    public static (int StartMinutes, int EndMinutes) FittedTo(IEnumerable<(int Start, int End)> spans)
    {
        var list = spans.ToList();
        if (list.Count == 0) return (8 * 60, 18 * 60);
        int start = Math.Max(0, (list.Min(s => s.Start) / 60 - 1) * 60);
        int end = Math.Min(24 * 60, ((list.Max(s => s.End) + 59) / 60 + 1) * 60);
        return (start, end);
    }
}
