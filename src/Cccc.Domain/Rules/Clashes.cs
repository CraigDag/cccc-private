using System.Collections.Immutable;
using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

public enum ClashKind
{
    RoomDoubleBooked,
    FacilitatorDoubleBooked,
    FacilitatorUnavailable,
    NeedsCover,
    FacilitatorOverHours,
    FacilitatorLongGap,
    StudentDoubleBooked,
    GroupDoubleBooked,
    /// <summary>A group's hard training runs longer without a rest than the school's limit (<see cref="HardTraining"/>).</summary>
    GroupHardInARow,
    /// <summary>A group has more hard training in a day than the school's limit.</summary>
    GroupHardInADay,
}

/// <summary>A warning about the week. Never a refusal.</summary>
public sealed record Clash(ClashKind Kind, string Description, ImmutableList<Guid> SessionIds)
{
    /// <summary>The clashes drawn as a ring on the block — not cover, which has its own dashed edge.</summary>
    public bool IsRing => Kind is not (ClashKind.NeedsCover or ClashKind.FacilitatorOverHours or ClashKind.FacilitatorLongGap) && !IsHardTraining;

    /// <summary>About how hard a group's day is on the body: advice about the day's shape, never a ring on a class.</summary>
    public bool IsHardTraining => Kind is ClashKind.GroupHardInARow or ClashKind.GroupHardInADay;
}

public static class Clashes
{
    /// <summary>Every warning for the week, over what actually happens in it.</summary>
    public static IReadOnlyList<Clash> ForWeek(Timetable t, DateOnly anyDateInWeek) =>
        Find(t, Occurrences.HappeningInWeek(t, anyDateInWeek));

    /// <summary>The warnings a proposed session would add to its week, for the session form.</summary>
    public static IReadOnlyList<Clash> CausedBy(Timetable t, Session proposal)
    {
        var without = t with { Sessions = t.Sessions.Remove(proposal.Id) };
        var with = t with { Sessions = t.Sessions.Put(proposal) };
        var before = Find(without, Occurrences.HappeningInWeek(without, proposal.Date)).Select(c => (c.Kind, c.Description)).ToHashSet();
        return Find(with, Occurrences.HappeningInWeek(with, proposal.Date))
            .Where(c => !before.Contains((c.Kind, c.Description)))
            .ToList();
    }

    private static bool Overlap(Occurrence a, Occurrence b) => a.Date == b.Date && a.Start < b.End && b.Start < a.End;

    private static string When(DateOnly d, TimeOnly a, TimeOnly b) => $"{Week.ShortDay(d.DayOfWeek)} {Week.Hm(a)}–{Week.Hm(b)}";

    private static string When(Occurrence o) => When(o.Date, o.Start, o.End);

    private static HashSet<Guid> AudienceOf(Timetable t, Session s) =>
        s.Audience.IsEveryone ? t.Groups.Select(g => g.Id).ToHashSet() : s.Audience.Cohorts.ToHashSet();

    private static string Who(IReadOnlyList<string> names) => names.Count switch
    {
        1 => names[0],
        <= 3 => string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1],
        _ => $"{names.Count} students ({string.Join(", ", names.Take(2))} and {names.Count - 2} more)",
    };

    public static IReadOnlyList<Clash> Find(Timetable t, IReadOnlyList<Occurrence> week)
    {
        var list = week.Where(o => o.Happens).ToList();
        var found = new List<Clash>();
        string RoomOf(Occurrence o) => t.Rooms.Find(o.Session.RoomId)?.Name ?? "no space";

        for (int i = 0; i < list.Count; i++)
            for (int j = i + 1; j < list.Count; j++)
            {
                var a = list[i];
                var b = list[j];
                if (!Overlap(a, b)) continue;
                ImmutableList<Guid> both = [a.Session.Id, b.Session.Id];

                if (a.Session.RoomId is { } r && r == b.Session.RoomId && t.Rooms.Contains(r))
                    found.Add(new Clash(ClashKind.RoomDoubleBooked,
                        $"Space \"{RoomOf(a)}\": \"{a.Session.DisplayTitle}\" overlaps \"{b.Session.DisplayTitle}\" ({When(a)})", both));

                foreach (var fid in a.InTheRoom.Intersect(b.InTheRoom))
                    if (TimeWindows.Overlap(a, b, fid))
                        found.Add(new Clash(ClashKind.FacilitatorDoubleBooked,
                            $"{t.Facilitators[fid]?.Name ?? "A facilitator"} double-booked: \"{a.Session.DisplayTitle}\" ({RoomOf(a)}) and \"{b.Session.DisplayTitle}\" ({RoomOf(b)}) ({When(a)})", both));

                // One warning per pair of sessions, naming the students, rather than one per student: a
                // whole group put in two places at once is one mistake, and "8 clashes" for it reads as eight.
                var shared = Roster.For(t, a.Session).Intersect(Roster.For(t, b.Session))
                    .Select(sid => t.Students[sid]?.Name is { Length: > 0 } n ? n : "a student")
                    .OrderBy(n => n, NameOrder.Comparer).ToList();
                if (shared.Count > 0)
                    found.Add(new Clash(ClashKind.StudentDoubleBooked,
                        $"{Who(shared)} double-booked: \"{a.Session.DisplayTitle}\" ({RoomOf(a)}) and \"{b.Session.DisplayTitle}\" ({RoomOf(b)}) ({When(a)})", both));

                // The same people in two rooms: one session is on for a proper superset of the other's
                // groups. Never derived from the lanes, so a week with no lanes still raises it.
                var left = AudienceOf(t, a.Session);
                var right = AudienceOf(t, b.Session);
                (Occurrence wide, Occurrence narrow)? pair =
                    left.IsProperSupersetOf(right) ? (a, b) : right.IsProperSupersetOf(left) ? (b, a) : null;
                // The same groups twice at once is double-booked too, even for a group nobody is enrolled in yet.
                if (pair is null && left.Count > 0 && !a.Session.Audience.IsEveryone && !b.Session.Audience.IsEveryone && left.SetEquals(right))
                    found.Add(new Clash(ClashKind.GroupDoubleBooked,
                        $"{string.Join(", ", left.Select(g => t.Groups[g]?.Name ?? "A group").OrderBy(n => n, NameOrder.Comparer))} booked twice: \"{a.Session.DisplayTitle}\" ({RoomOf(a)}) and \"{b.Session.DisplayTitle}\" ({RoomOf(b)}) ({When(a)})", both));
                if (pair is { } p)
                    foreach (var gid in AudienceOf(t, p.narrow.Session).OrderBy(g => t.Groups[g]?.Name ?? "", NameOrder.Comparer))
                        found.Add(new Clash(ClashKind.GroupDoubleBooked,
                            $"{t.Groups[gid]?.Name ?? "A group"} double-booked: \"{p.wide.Session.DisplayTitle}\" ({RoomOf(p.wide)}) is on for all of them, and \"{p.narrow.Session.DisplayTitle}\" ({RoomOf(p.narrow)}) runs inside it ({When(p.wide)})", both));
            }

        foreach (var o in list)
            foreach (var fid in o.InTheRoom)
            {
                var f = t.Facilitators[fid];
                if (f is null || f.Availability.IsEmpty) continue;
                var (from, until) = TimeWindows.InRoom(o, fid);
                if (until <= from) continue;
                bool ok = f.Availability.Any(w => w.Day == o.Day && w.Start <= from && w.End >= until);
                if (!ok)
                    found.Add(new Clash(ClashKind.FacilitatorUnavailable,
                        $"{f.Name} is not available {When(o.Date, from, until)} (outside their availability) — \"{o.Session.DisplayTitle}\"", [o.Session.Id]));
            }

        foreach (var o in list.Where(o => o.NeedsCover))
        {
            var names = o.AwayIds.Select(id => t.Facilitators[id]?.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
            found.Add(new Clash(ClashKind.NeedsCover,
                $"\"{o.Session.DisplayTitle}\" needs cover — {(names.Count > 0 ? string.Join(", ", names) : "facilitator")} away ({When(o)})", [o.Session.Id]));
        }

        var splits = Hours.ByFacilitator(t, list);
        foreach (var f in t.Facilitators.OrderBy(f => f.Name, NameOrder.Comparer))
        {
            var theirs = Hours.SessionsFor(list, f.Id);
            if (theirs.Count == 0) continue;
            if (f.MaxWeeklyHours is { } max && splits.GetValueOrDefault(f.Id) is { } split && split.Total > max)
                found.Add(new Clash(ClashKind.FacilitatorOverHours,
                    $"{f.Name} is over their weekly limit: " +
                    (split.Added > TimeSpan.Zero
                        ? $"{Week.Duration(split.Teaching)} teaching and {string.Join(", ", split.Rules.Select(r => $"{Week.Duration(r.Time)} {r.Name}"))} this week — {Week.Duration(split.Total)} in total "
                        : $"{Week.Duration(split.Total)} teaching this week ") +
                    $"(maximum {Week.Duration(max)})", [theirs[0].Session.Id]));

            if (f.MaxGap is { } maxGap)
            {
                var hours = theirs.Select(o => (O: o, W: TimeWindows.InRoom(o, f.Id)))
                    .OrderBy(x => x.O.Date).ThenBy(x => x.W.Start).ToList();
                var freeFrom = hours[0];
                for (int i = 1; i < hours.Count; i++)
                {
                    var next = hours[i];
                    if (next.O.Date != freeFrom.O.Date) { freeFrom = next; continue; }
                    var gap = Week.Span(freeFrom.W.End, next.W.Start);
                    if (gap > TimeSpan.Zero && gap > maxGap)
                        found.Add(new Clash(ClashKind.FacilitatorLongGap,
                            $"{f.Name} has a {Week.Duration(gap)} gap {When(next.O.Date, freeFrom.W.End, next.W.Start)} (maximum {Week.Duration(maxGap)}) — between \"{freeFrom.O.Session.DisplayTitle}\" and \"{next.O.Session.DisplayTitle}\"",
                            [freeFrom.O.Session.Id, next.O.Session.Id]));
                    if (next.W.End > freeFrom.W.End) freeFrom = next;
                }
            }
        }
        found.AddRange(HardTraining.Warnings(t, list));
        return found;
    }

    public static IReadOnlySet<Guid> RingedSessions(IEnumerable<Clash> clashes) =>
        clashes.Where(c => c.IsRing).SelectMany(c => c.SessionIds).ToHashSet();
}
