using Cccc.Domain.Rules;

namespace Cccc.Web.Features.Find;

/// <summary>Whether this tab's Find box is open. One per tab; Ctrl+K or / opens it from anywhere.</summary>
public sealed class Finder
{
    public bool IsOpen { get; private set; }
    public int Generation { get; private set; }
    public event Action? Moved;

    public void Open()
    {
        IsOpen = true;
        Generation++;
        Moved?.Invoke();
    }

    public void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        Moved?.Invoke();
    }
}

/// <summary>
/// One answer in Find: a thing to look at, or a thing to do. A class typed in to add carries its draft,
/// somebody typed in as away carries the absence, and an answer about a day (who is free then) carries the day,
/// which Enter opens the plan on.
/// </summary>
public sealed record FindHit(string Kind, string Label, string? Detail, Guid? Id, string? Colour, Cccc.Domain.Model.Session? Draft = null,
    Cccc.Domain.Model.Absence? Away = null, DateOnly? On = null);

public static class FindIndex
{
    /// <summary>
    /// Everything Find can reach, ranked: a name that starts with the words beats one that only
    /// contains them, and people come before places before groups. Nothing here is logged or put in a URL.
    /// For somebody who may edit, a line that reads as a class to add (<see cref="QuickAdd"/>) comes first:
    /// <paramref name="today"/> and the day the plan shows say which Friday "fri" is. A line that says somebody
    /// is away (<see cref="QuickAway"/>) comes before it: an administrator (<paramref name="recordsAway"/>)
    /// records anybody, and the facilitator signed in (<paramref name="me"/>) asks for themselves, and asks for the
    /// space a line reads as a class to add when they add no classes themselves (<see cref="QuickAdd.AskHit"/>). Such a line
    /// is never a class with its words for a title ("Off", "Sick"), whoever reads it. Nor is a question about who or
    /// what is free (<see cref="QuickFree"/>), which anybody may ask, unless it is one only by naming something with a
    /// time and has words left over for a title (<see cref="QuickFree.Reading.MayBeAnAdd"/>): its answer comes next,
    /// the time it is <paramref name="now"/> being when "now" is, and an event named only to a reader who may see it
    /// (<paramref name="viewer"/>).
    /// </summary>
    public static IReadOnlyList<FindHit> Search(Cccc.Domain.Model.Timetable t, string query, bool mayEdit, bool admin, int max = 12,
        DateOnly? today = null, DateOnly? shown = null, bool recordsAway = false, Guid? me = null, TimeOnly? now = null,
        Cccc.Domain.Rules.EventViewer? viewer = null)
    {
        var q = query.Trim();
        var away = today is { } day && q.Length > 0 ? QuickAway.Read(t, q, day, shown ?? day, me) : null;
        var asked = away is null && today is { } asOf && now is { } at && q.Length > 0 ? QuickFree.Read(t, q, asOf, shown ?? asOf, at) : null;
        var quick = mayEdit && today is { } addOn && q.Length > 0 && QuickAdd.Read(t, q, addOn, shown ?? addOn) is { } reading
                    && (away is null && asked is not { MayBeAnAdd: false } || reading.Draft.ClassId is not null)
            ? QuickAdd.Hit(t, reading) : null;
        // A coach who adds no classes asks for the space instead: the same reading, sent as an ask (SpaceAskForm).
        var space = !mayEdit && me is not null && today is { } askOn && q.Length > 0 && QuickAdd.Read(t, q, askOn, shown ?? askOn) is { } wanted
                    && (away is null && asked is not { MayBeAnAdd: false } || wanted.Draft.ClassId is not null)
            ? QuickAdd.AskHit(t, wanted) : null;
        var said = away is null || today is not { } on ? null
            : recordsAway ? QuickAway.Hit(t, away, records: true, on)
            : me is { } mine && away.Draft.Person == Cccc.Domain.Model.AbsentPerson.Facilitator && away.Draft.PersonId == mine
                ? QuickAway.Hit(t, away, records: false, on) : null;
        var hits = new List<(int Rank, FindHit Hit)>();
        int Score(string name)
        {
            if (q.Length == 0) return -1;
            var c = Cccc.Domain.NameOrder.Culture.CompareInfo;
            const System.Globalization.CompareOptions o = System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace;
            if (c.IsPrefix(name, q, o)) return 0;
            if (name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Any(w => c.IsPrefix(w, q, o))) return 1;
            return c.IndexOf(name, q, o) >= 0 ? 2 : -1;
        }
        void Add(int kindRank, string name, FindHit hit)
        {
            int s = Score(name);
            if (s >= 0) hits.Add((s * 10 + kindRank, hit));
        }
        // A former facilitator or space is still found, last among names that match as well, so the weeks
        // they were in can be looked at; nothing ahead has them.
        foreach (var f in t.Facilitators) Add(f.IsFormer() ? 5 : 0, f.Name, new FindHit("facilitator", f.Name, f.IsFormer() ? "Former facilitator, " + Former.Since(f) : f.IsGuest ? "Guest facilitator" : "Facilitator", f.Id, f.Colour));
        foreach (var s in t.Students) Add(1, s.Name, new FindHit("student", s.Name, "Student" + (t.Groups.Find(s.GroupId) is { } g ? " · " + g.Name : ""), s.Id, null));
        foreach (var r in t.Rooms) Add(r.IsFormer() ? 6 : 2, r.Name, new FindHit("room", r.Name, r.IsFormer() ? "Former space, " + Former.Since(r) : r.Category ?? "Space", r.Id, r.Colour));
        foreach (var g in t.Groups) Add(3, g.Name, new FindHit("group", g.Name, "Group" + (t.Programmes.Find(g.ProgrammeId) is { } p ? " · " + p.Name : ""), g.Id, g.Colour));
        foreach (var p in t.Programmes) Add(4, p.Name, new FindHit("programme", p.Name, p.IsGuest ? "Guest programme" : "Programme", p.Id, p.Colour));
        // A space is found by what it has too ("rigging" finds the spaces with rigging points), after every name.
        foreach (var r in t.Rooms.Current().Where(r => Score(r.Name) < 0))
        {
            var has = r.Has.Select(id => t.Equipment[id]).OfType<Cccc.Domain.Model.Equipment>()
                .Select(e => (e.Name, Score: Score(e.Name))).Where(x => x.Score >= 0).ToList();
            if (has.Count == 0) continue;
            var names = string.Join(", ", has.Select(x => x.Name).Distinct().OrderBy(n => n, Cccc.Domain.NameOrder.Comparer));
            hits.Add(((3 + has.Min(x => x.Score)) * 10 + 2, new FindHit("room", r.Name, (r.Category ?? "Space") + " · has " + names, r.Id, r.Colour)));
        }

        var actions = new List<FindHit>
        {
            new("go-today", "Go to today", "Plan", null, null),
            new("go-week", "Show the whole week", "Plan", null, null),
            new("go-term", "Show the whole semester", "Plan ▸ Term · every week side by side, and what changed", null, null),
            new("go-registers", "Take a register", "Registers", null, null),
        };
        if (mayEdit)
        {
            actions.Insert(0, new FindHit("add-class", "Add a class", "Plan", null, null));
            actions.Insert(1, new FindHit("add-event", "Add an event", "Plan", null, null));
            actions.Insert(2, new FindHit("find-time", "Find a time", "When everybody a class needs, and a space, are free", null, null));
        }
        if (admin)
        {
            actions.Add(new FindHit("go-data", "People & places", "Add or change students, facilitators, spaces, classes", null, null));
            actions.Add(new FindHit("go-reports", "Reports", "Hours and attendance", null, null));
        }
        if (q.Length == 0) return actions;
        // An action is found by its own words, and then by where it lives ("Plan ▸ Term"), after every name found.
        foreach (var a in actions)
        {
            int s = Score(a.Label);
            if (s < 0 && a.Detail is { } where && Score(where) is var w and >= 0) s = 3 + w;
            if (s >= 0) hits.Add((s * 10 + 5, a));
        }
        var found = hits.OrderBy(h => h.Rank).ThenBy(h => h.Hit.Label, Cccc.Domain.NameOrder.Comparer).Select(h => h.Hit);
        FindHit[] first = [.. new[] { said, quick, space }.OfType<FindHit>()];
        var answer = asked is null ? [] : QuickFree.Hits(t, asked, viewer ?? Cccc.Domain.Rules.EventViewer.Nobody, max - first.Length);
        return [.. first, .. answer, .. found.Take(max - first.Length - answer.Count)];
    }
}
