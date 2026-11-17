using System.Collections.Concurrent;
using System.Security.Cryptography;
using Cccc.Web.Features.Shell;
using Cccc.Web.Features.Week;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Features.Print;

/// <summary>
/// What a sheet is of: a frozen copy of how one tab was looking at the week, in which timetable. The
/// timetable is the tab's (<see cref="ProjectChoice.ShownId"/>), so a sheet printed from a tab looking
/// at next year is next year's, whatever is open for everybody by the time it is opened.
/// </summary>
public sealed record PrintRequest(
    string Kind, Guid AccountId, Guid ProjectId, DateOnly Monday, string Search,
    Guid[] Programmes, Guid[] Groups, Guid[] Rooms, Guid[] Facilitators, Guid[] Students,
    bool ShowOpen, bool ShowGuests, bool ShowEvents, Guid? Person, DateTimeOffset Made)
{
    public string Key => string.Join("|", Kind, AccountId, ProjectId, Monday, Search, string.Join(",", Programmes), string.Join(",", Groups),
        string.Join(",", Rooms), string.Join(",", Facilitators), string.Join(",", Students), ShowOpen, ShowGuests, ShowEvents, Person);

    public WeekView ToView()
    {
        var v = new WeekView();
        v.Start(Monday, null);
        v.Search = Search;
        v.Programmes.UnionWith(Programmes); v.Groups.UnionWith(Groups); v.Rooms.UnionWith(Rooms);
        v.Facilitators.UnionWith(Facilitators); v.Students.UnionWith(Students);
        v.ShowOpen = ShowOpen; v.ShowGuests = ShowGuests; v.ShowEvents = ShowEvents;
        return v;
    }
}

/// <summary>
/// Print links name a short-lived token, never the filters themselves: a search box's words or a
/// person's name must not reach a URL. A token is good for its own account for two hours.
/// </summary>
public sealed class PrintRequests(SchoolClock clock)
{
    private static readonly TimeSpan Life = TimeSpan.FromHours(2);
    private readonly ConcurrentDictionary<string, PrintRequest> _byToken = new();
    private readonly ConcurrentDictionary<string, string> _tokenByKey = new();

    /// <summary>A link to the sheet of the timetable this tab shows.</summary>
    public string Link(string kind, WeekView v, Workspace w, Guid? person = null) =>
        w.Project is { } p ? Link(kind, v, w.Access, p.Id, person) : "/signin";

    public string Link(string kind, WeekView v, Access access, Guid projectId, Guid? person = null)
    {
        if (access.Account is null) return "/signin";
        var r = new PrintRequest(kind, access.Account.Id, projectId, v.Monday, v.Search.Trim(), [.. v.Programmes], [.. v.Groups], [.. v.Rooms],
            [.. v.Facilitators], [.. v.Students], v.ShowOpen, v.ShowGuests, v.ShowEvents, person, clock.Now);
        Sweep();
        var token = _tokenByKey.GetOrAdd(r.Key, _ => Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant());
        _byToken[token] = r;
        return $"/print/{token}";
    }

    public PrintRequest? Find(string token, Guid accountId) =>
        _byToken.TryGetValue(token, out var r) && r.AccountId == accountId && clock.Now - r.Made < Life ? r : null;

    private void Sweep()
    {
        foreach (var (token, r) in _byToken)
            if (clock.Now - r.Made > Life && _byToken.TryRemove(token, out _)) _tokenByKey.TryRemove(r.Key, out _);
    }
}
