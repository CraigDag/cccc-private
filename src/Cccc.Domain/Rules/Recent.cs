using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// A class that changed in a way somebody in the building would notice: as it was when the window opened
/// and as it is now. <see cref="Was"/> null means it was added in the window; <see cref="Now"/> null, deleted.
/// <see cref="By"/> is everybody who changed it, by login, in the order they first did; <see cref="At"/> the last time.
/// </summary>
public sealed record ClassChange(Guid Id, Session? Was, Session? Now, ImmutableList<string> By, DateTimeOffset At)
{
    private bool Both => Was is not null && Now is not null;
    public bool Added => Was is null;
    public bool Deleted => Now is null;
    public bool DayChanged => Both && Was!.Date != Now!.Date;
    public bool TimesChanged => Both && (Was!.Start != Now!.Start || Was.End != Now.End);
    public bool SpaceChanged => Both && Was!.RoomId != Now!.RoomId;
    public bool CancelledChanged => Both && Was!.Cancelled != Now!.Cancelled;
    public bool TeachersChanged => Both && !Recent.SameSet(Was!.FacilitatorIds, Now!.FacilitatorIds);
    /// <summary>The same people teach, but somebody's part of the class is a different stretch of it.</summary>
    public bool PartsChanged => Both && !TeachersChanged && !Recent.SameSet(Was!.Facilitators, Now!.Facilitators);
    public bool CoverChanged => Both && !Recent.SameSet(Was!.CoverIds, Now!.CoverIds);
    public bool AudienceChanged => Both && !Was!.Audience.Equivalent(Now!.Audience);
    public bool TitleChanged => Both && Was!.DisplayTitle != Now!.DisplayTitle;
}

/// <summary>
/// What changed lately for one day's classes, read from the project's own history. A class is the day's
/// if it was on that day when the window opened or is on it now, so a class moved away from the day is
/// news on that day too. Only what a person in the building would notice counts: the day, the times, the
/// space, who teaches (and for which part) or covers, who it is for, its name, and whether it is cancelled. A change put back
/// inside the window (moved and moved back, or undone) is no change at all. Registers, notes and a
/// class's roster exceptions are not the timetable changing.
/// </summary>
public static class Recent
{
    /// <summary>The day the window opens, at its start: the weekday before <paramref name="today"/>, so on a Monday it is Friday.</summary>
    public static DateOnly Since(DateOnly today)
    {
        var d = today.AddDays(-1);
        while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(-1);
        return d;
    }

    /// <summary>The day's classes that changed in <paramref name="changes"/>, the latest first, against the timetable as it is now.</summary>
    public static IReadOnlyList<ClassChange> ForDay(Timetable t, IEnumerable<Change> changes, DateOnly day)
    {
        var seen = new Dictionary<Guid, (Session? Was, List<string> By, DateTimeOffset At)>();
        foreach (var c in changes.OrderBy(c => c.Sequence))
        {
            foreach (var op in c.Batch.Ops)
            {
                if (op.Collection != Collections.Sessions.Name) continue;
                var (was, by, _) = seen.TryGetValue(op.Id, out var e) ? e : (Read(op.Before), new List<string>(), c.At);
                if (!by.Contains(c.By, StringComparer.OrdinalIgnoreCase)) by.Add(c.By);
                seen[op.Id] = (was, by, c.At);
            }
        }
        return seen
            .Select(kv => new ClassChange(kv.Key, kv.Value.Was, t.Sessions[kv.Key], [.. kv.Value.By], kv.Value.At))
            .Where(x => (x.Was?.Date == day || x.Now?.Date == day) && Noticeable(x.Was, x.Now))
            .OrderByDescending(x => x.At)
            .ToList();
    }

    /// <summary>Whether somebody in the building would notice the difference between the two.</summary>
    public static bool Noticeable(Session? was, Session? now) =>
        was is null || now is null
            ? (was is null) != (now is null)
            : was.Date != now.Date || was.Start != now.Start || was.End != now.End || was.RoomId != now.RoomId
              || was.Cancelled != now.Cancelled || was.DisplayTitle != now.DisplayTitle
              || !SameSet(was.Facilitators, now.Facilitators) || !SameSet(was.CoverIds, now.CoverIds)
              || !was.Audience.Equivalent(now.Audience);

    internal static bool SameSet<T>(IEnumerable<T> a, IEnumerable<T> b) => a.ToHashSet().SetEquals(b);

    private static Session? Read(JsonNode? node) => node?.Deserialize<Session>(ModelJson.Options);
}
