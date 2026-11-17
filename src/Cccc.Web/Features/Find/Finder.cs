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

/// <summary>One answer in Find: a thing to look at, or a thing to do.</summary>
public sealed record FindHit(string Kind, string Label, string? Detail, Guid? Id, string? Colour);

public static class FindIndex
{
    /// <summary>
    /// Everything Find can reach, ranked: a name that starts with the words beats one that only
    /// contains them, and people come before places before groups. Nothing here is logged or put in a URL.
    /// </summary>
    public static IReadOnlyList<FindHit> Search(Cccc.Domain.Model.Timetable t, string query, bool mayEdit, bool admin, int max = 12)
    {
        var q = query.Trim();
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

        var actions = new List<FindHit>
        {
            new("go-today", "Go to today", "Plan", null, null),
            new("go-week", "Show the whole week", "Plan", null, null),
            new("go-registers", "Take a register", "Registers", null, null),
        };
        if (mayEdit)
        {
            actions.Insert(0, new FindHit("add-class", "Add a class", "Plan", null, null));
            actions.Insert(1, new FindHit("add-event", "Add an event", "Plan", null, null));
        }
        if (admin)
        {
            actions.Add(new FindHit("go-data", "People & places", "Add or change students, facilitators, spaces, classes", null, null));
            actions.Add(new FindHit("go-reports", "Reports", "Hours and attendance", null, null));
        }
        if (q.Length == 0) return actions;
        foreach (var a in actions) Add(5, a.Label, a);
        return hits.OrderBy(h => h.Rank).ThenBy(h => h.Hit.Label, Cccc.Domain.NameOrder.Comparer).Select(h => h.Hit).Take(max).ToList();
    }
}
