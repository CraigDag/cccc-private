using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Store;
using Cccc.Web.Infrastructure;
using Microsoft.AspNetCore.Components;

namespace Cccc.Web.Features.Shell;

/// <summary>
/// One modal at a time. A form is put on screen only through here, so there is exactly one dialog in
/// the document.
/// </summary>
public sealed class Dialogs
{
    public Type? Component { get; private set; }
    public IDictionary<string, object?>? Parameters { get; private set; }
    public int Generation { get; private set; }
    public event Action? Moved;

    public bool IsOpen => Component is not null;

    public void Show<T>(IDictionary<string, object?>? parameters = null) where T : IComponent
    {
        Component = typeof(T);
        Parameters = parameters ?? new Dictionary<string, object?>();
        Generation++;
        Moved?.Invoke();
    }

    public void Close()
    {
        if (Component is null) return;
        Component = null;
        Parameters = null;
        Moved?.Invoke();
    }
}

public sealed record Toast(int Id, string Text, bool IsWarning, string? ActionLabel, Func<Task>? Action);

/// <summary>Short messages at the foot of the screen: what just happened, and Undo.</summary>
public sealed class Toasts
{
    private int _next;
    private readonly List<Toast> _shown = [];
    public IReadOnlyList<Toast> Shown => _shown;
    public event Action? Moved;

    public void Say(string text, string? actionLabel = null, Func<Task>? action = null) => Add(new Toast(++_next, text, false, actionLabel, action));

    public void Warn(string text) => Add(new Toast(++_next, text, true, null, null));

    private void Add(Toast t)
    {
        _shown.Add(t);
        if (_shown.Count > 3) _shown.RemoveAt(0);
        Moved?.Invoke();
        _ = Task.Delay(t.IsWarning ? 9000 : 6000).ContinueWith(_ => Dismiss(t.Id));
    }

    public void Dismiss(int id)
    {
        if (_shown.RemoveAll(t => t.Id == id) > 0) Moved?.Invoke();
    }
}

/// <summary>
/// What every page of a signed-in tab works with: who is signed in, the open project, today, and the
/// one door every change goes through.
/// </summary>
public sealed class Workspace(Access access, LiveProject? project, SchoolClock clock, Toasts toasts, AccountDirectory accounts)
{
    public Access Access { get; } = access;
    public LiveProject? Project { get; } = project;
    public SchoolClock Clock { get; } = clock;
    public Timetable T => Project?.State ?? Timetable.Empty;

    /// <summary>The project's name as the store has it. Shown inside the app only: never on paper, in a tab title or a URL.</summary>
    public string ProjectName => Project is null ? "" : Project.Name;

    /// <summary>
    /// The tab is looking at a timetable that is not the one open for everybody (<see cref="TabTimetable"/>):
    /// what changes here stays in it, and a band across every page says so.
    /// </summary>
    public bool LookingAway { get; init; }
    public DateOnly Today => Clock.Today;

    /// <summary>The name a person reads for whoever made a change (changes are recorded by login).</summary>
    public string NameOf(string by)
    {
        // Before any account is looked up: a sign-in name is trimmed, and this one starts with a space.
        if (by == Follower.By) return Follower.Display;
        if (string.Equals(by, Access.By, StringComparison.OrdinalIgnoreCase)) return "you";
        return accounts.FindByLogin(by)?.Display ?? by;
    }

    /// <summary>
    /// Makes a change. A refusal is said in words, never thrown at the screen; the return says whether
    /// it was made.
    /// </summary>
    public bool Commit(Func<Timetable, Batch> build, bool announce = true)
    {
        if (Project is null) return false;
        if (Access.Following) { toasts.Warn(FollowWords.Refusal); return false; }
        if (!Access.MayEditSomething) return false;
        try
        {
            var c = Project.Commit(t => Permitted(build(t)), Access.By);
            if (announce && c.Label.Length > 0)
                toasts.Say(c.Label, "Undo", () => { Undo(); return Task.CompletedTask; });
            return true;
        }
        catch (EditRefused e)
        {
            toasts.Warn(e.Message);
            return false;
        }
    }

    /// <summary>Every operation of a change is checked by <see cref="Access.MayChange"/> before it is applied.</summary>
    private Batch Permitted(Batch b)
    {
        foreach (var op in b.Ops)
        {
            if (!Access.MayChange(op.Collection)) throw new EditRefused(Access.MayEdit
                ? "Only an administrator can change that."
                : "Your account can look at the timetable and take your own registers, but not change the week.");
        }
        return b;
    }

    public void Warn(string text) => toasts.Warn(text);

    public void Say(string text) => toasts.Say(text);

    public void Undo()
    {
        if (Project is null) return;
        try
        {
            var r = Project.Undo(Access.By);
            if (!r.Done) { toasts.Warn("Nothing of yours to undo."); return; }
            toasts.Say("Undid: " + r.Label + Skipped(r));
        }
        catch (EditRefused e) { toasts.Warn(e.Message); }
    }

    public void Redo()
    {
        if (Project is null) return;
        try
        {
            var r = Project.Redo(Access.By);
            if (!r.Done) { toasts.Warn("Nothing of yours to redo."); return; }
            toasts.Say("Redid: " + r.Label + Skipped(r));
        }
        catch (EditRefused e) { toasts.Warn(e.Message); }
    }

    private string Skipped(UndoResult r) => r.Skipped.Count == 0 ? ""
        : $" — {r.Skipped.Count} part{(r.Skipped.Count == 1 ? " was" : "s were")} left, because {string.Join("; ", r.Skipped.Select(s => NameIn(s)))}";

    private string NameIn(string s)
    {
        const string marker = "changed since by ";
        int i = s.IndexOf(marker, StringComparison.Ordinal);
        return i < 0 ? s : s[..(i + marker.Length)] + NameOf(s[(i + marker.Length)..]);
    }
}
