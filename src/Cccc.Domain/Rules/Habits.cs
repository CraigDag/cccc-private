using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// What a class is usually like: the session of it most like one about to be added, so that whatever the
/// person did not say (who it is for, where, who teaches it, how long it runs) can be what it was. The
/// rebuild's own; the add form shows the result before anything is saved, so it is a suggestion, never a rule
/// anything counts by.
/// </summary>
public static class Habits
{
    /// <summary>
    /// The session of the class most like one on <paramref name="date"/>: for the groups or with the
    /// facilitators named, if any were; then on the same weekday; then nearest the start named; then the
    /// latest before that date, and only after every earlier one, the soonest after it. A cancelled session
    /// did not run, so it is never the one. Anything former is stripped from it, as from every copy
    /// (<see cref="Former.Strip"/>). Null when the class has never been timetabled.
    /// </summary>
    public static Session? Like(Timetable t, Guid classId, DateOnly date, TimeOnly? start = null,
        IReadOnlyCollection<Guid>? groupIds = null, IReadOnlyCollection<Guid>? facilitatorIds = null)
    {
        var best = t.Sessions
            .Where(s => s.ClassId == classId && !s.Cancelled)
            .OrderBy(s => groupIds is { Count: > 0 } && !s.Audience.Cohorts.Any(groupIds.Contains) ? 1 : 0)
            .ThenBy(s => facilitatorIds is { Count: > 0 } && !s.FacilitatorIds.Any(facilitatorIds.Contains) ? 1 : 0)
            .ThenBy(s => s.Date.DayOfWeek == date.DayOfWeek ? 0 : 1)
            .ThenBy(s => start is { } at ? Math.Abs(Week.Span(at, s.Start).TotalMinutes) : 0)
            .ThenBy(s => s.Date <= date ? 0 : 1)
            .ThenBy(s => Math.Abs(s.Date.DayNumber - date.DayNumber))
            .ThenBy(s => s.Start)
            .ThenBy(s => s.Id)
            .FirstOrDefault();
        return best is null ? null : Former.Strip(t, best);
    }
}
