using System.Collections.Concurrent;
using System.Collections.Immutable;
using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;

namespace Cccc.Store;

/// <summary>What a tab is told after any change: the new state, who made it and what it touched.</summary>
public sealed record Changed(Timetable State, long Sequence, string By, string Label, ImmutableHashSet<Guid> Touched);

/// <summary>The result of an undo or redo: what was done, and what was left because somebody changed it since.</summary>
public sealed record UndoResult(bool Done, string Label, ImmutableList<string> Skipped);

/// <summary>
/// One open project, held on the server. Every change is applied in order under one lock and appended
/// to the log before anybody sees it, so every tab renders the same state and nothing is ever merged.
/// Undo is per person: the inverse of their own batches, skipping anything changed since by somebody else.
/// </summary>
public sealed class LiveProject
{
    public const int UndoDepth = 100;

    private readonly ProjectStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<DateOnly> _today;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, Stack<Change>> _undo = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Stack<Change>> _redo = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _present = new(StringComparer.OrdinalIgnoreCase);

    public Guid Id { get; }
    public Timetable State { get; private set; }
    public long Sequence { get; private set; }
    public Change? LastChange { get; private set; }

    /// <summary>The project's name in the store, read when asked so a rename shows at once.</summary>
    public string Name => _store.Find(Id)?.Name ?? "";

    /// <summary>Raised after every change, outside the lock. Handlers must hand off to their own thread.</summary>
    public event Action<Changed>? ChangedEvent;

    internal LiveProject(ProjectStore store, Guid id, Func<DateTimeOffset> clock, Func<DateOnly> today)
    {
        _store = store;
        _clock = clock;
        _today = today;
        Id = id;
        (State, Sequence) = store.Load(id);
    }

    /// <summary>
    /// Applies a batch as one person's change. Refuses (with <see cref="EditRefused"/>) a batch the lock
    /// rule forbids, and writes nothing if the log cannot be written.
    /// </summary>
    public Changed Commit(Batch batch, string by)
    {
        Changed result;
        lock (_gate)
        {
            result = CommitLocked(batch, by, pushUndo: true);
        }
        ChangedEvent?.Invoke(result);
        return result;
    }

    /// <summary>Builds the batch from the state as it is at the moment the lock is held, then commits it.</summary>
    public Changed Commit(Func<Timetable, Batch> build, string by)
    {
        Changed result;
        lock (_gate)
        {
            result = CommitLocked(build(State), by, pushUndo: true);
        }
        ChangedEvent?.Invoke(result);
        return result;
    }

    /// <summary>
    /// Makes the project exactly <paramref name="to"/>, as one change by <paramref name="by"/>, for a
    /// timetable that follows another app's file. That app keeps its own week lock, so this does not
    /// apply one, and nobody here undoes it. Nothing is written, and nobody is told, when nothing differs.
    /// </summary>
    public Changed Follow(Timetable to, string by, string label)
    {
        Changed result;
        lock (_gate)
        {
            result = CommitLocked(Difference(State, to, label), by, pushUndo: false, checkLocks: false);
        }
        if (result.Touched.Count > 0) ChangedEvent?.Invoke(result);
        return result;
    }

    private Changed CommitLocked(Batch batch, string by, bool pushUndo, bool checkLocks = true)
    {
        if (batch.IsEmpty)
            return new Changed(State, Sequence, by, batch.Label, []);
        if (checkLocks) Edits.CheckLocks(State, batch, _today());
        var next = Apply.Batch(State, batch);
        var change = new Change(Sequence + 1, by, _clock(), batch);
        _store.Append(Id, change, () => next);
        State = next;
        Sequence = change.Sequence;
        LastChange = change;
        if (pushUndo)
        {
            Push(_undo, by, change);
            if (_redo.TryGetValue(by, out var r)) r.Clear();
        }
        return new Changed(State, Sequence, by, batch.Label, [.. batch.Ops.Select(o => o.Id)]);
    }

    private static void Push(Dictionary<string, Stack<Change>> stacks, string by, Change c)
    {
        if (!stacks.TryGetValue(by, out var s)) stacks[by] = s = new Stack<Change>();
        s.Push(c);
        if (s.Count > UndoDepth)
        {
            var keep = s.Take(UndoDepth).Reverse().ToList();
            s.Clear();
            foreach (var k in keep) s.Push(k);
        }
    }

    public bool CanUndo(string by) { lock (_gate) return _undo.TryGetValue(by, out var s) && s.Count > 0; }

    public bool CanRedo(string by) { lock (_gate) return _redo.TryGetValue(by, out var s) && s.Count > 0; }

    public string? UndoLabel(string by) { lock (_gate) return _undo.TryGetValue(by, out var s) && s.Count > 0 ? s.Peek().Batch.Label : null; }

    public string? RedoLabel(string by) { lock (_gate) return _redo.TryGetValue(by, out var s) && s.Count > 0 ? s.Peek().Batch.Label : null; }

    public UndoResult Undo(string by) => Step(by, _undo, _redo, "Undid");

    public UndoResult Redo(string by) => Step(by, _redo, _undo, "Redid");

    private UndoResult Step(string by, Dictionary<string, Stack<Change>> from, Dictionary<string, Stack<Change>> to, string verb)
    {
        Changed? changed = null;
        UndoResult result;
        lock (_gate)
        {
            if (!from.TryGetValue(by, out var stack) || stack.Count == 0) return new UndoResult(false, "", []);
            var step = stack.Pop();
            var ops = new List<Op>();
            var skipped = new List<string>();
            var today = _today();
            foreach (var op in step.Batch.Ops.Reverse())
            {
                if (!Apply.StillAsWritten(State, op))
                {
                    skipped.Add(WhoChanged(op, step.Sequence) is { } who ? $"{Describe(op)} (changed since by {who})" : Describe(op));
                    continue;
                }
                var inverse = op.Inverse() with { Before = Collections.Named(op.Collection).Read(State, op.Id) };
                try
                {
                    Edits.CheckLocks(State, new Batch("", [inverse]), today);
                    ops.Add(inverse);
                }
                catch (EditRefused)
                {
                    skipped.Add($"{Describe(op)} (its week has ended)");
                }
            }
            var batch = new Batch($"{verb}: {step.Batch.Label}", [.. ops]);
            if (!batch.IsEmpty)
            {
                changed = CommitLocked(batch, by, pushUndo: false);
                Push(to, by, LastChange!);
            }
            result = new UndoResult(!batch.IsEmpty, step.Batch.Label, [.. skipped]);
        }
        if (changed is not null) ChangedEvent?.Invoke(changed);
        return result;
    }

    private string? WhoChanged(Op op, long after)
    {
        var later = _store.ChangesSince(Id, after).LastOrDefault(c => c.Batch.Ops.Any(o => o.Id == op.Id && o.Collection == op.Collection));
        return later?.By;
    }

    private static string Describe(Op op) => op.Collection switch
    {
        "sessions" => "a session",
        "scratch" => "a class in the Scratch Week",
        "registers" => "a register",
        "students" => "a student",
        "facilitators" => "a facilitator",
        "rooms" => "a room",
        "classes" => "a class",
        "events" => "an event",
        "absences" => "an absence",
        _ => "one change",
    };

    /// <summary>Who has this project open now: "Also editing: …".</summary>
    private (DateTimeOffset Since, long Sequence, IReadOnlyList<Change> Changes)? _recent;

    /// <summary>
    /// Every change made at or after <paramref name="since"/>, read from the store once for each new
    /// change rather than on every drawing of every page that asks.
    /// </summary>
    public IReadOnlyList<Change> ChangesMadeSince(DateTimeOffset since)
    {
        long seq;
        lock (_gate)
        {
            if (_recent is { } r && r.Since == since && r.Sequence == Sequence) return r.Changes;
            seq = Sequence;
        }
        var changes = _store.ChangesMadeSince(Id, since);
        lock (_gate)
        {
            // Only keep it if nothing was committed while it was being read.
            if (Sequence == seq) _recent = (since, seq, changes);
        }
        return changes;
    }

    public void Here(string who) => _present[who] = _clock();

    public void Left(string who) => _present.TryRemove(who, out _);

    public IReadOnlyList<string> Present(TimeSpan within)
    {
        var since = _clock() - within;
        return _present.Where(kv => kv.Value >= since).Select(kv => kv.Key).OrderBy(n => n, NameOrder.Comparer).ToList();
    }

    /// <summary>
    /// Drops every undo and redo step held in memory, for every person. Erasing a person needs it: those
    /// steps carry whole entities, the erased person's name among them.
    /// </summary>
    public void ForgetUndo()
    {
        lock (_gate)
        {
            _undo.Clear();
            _redo.Clear();
            LastChange = null;
        }
    }

    /// <summary>
    /// Rewrites this project's stored history through <paramref name="scrub"/> while nobody can change it,
    /// and then holds what the rewritten store says, with every undo and redo step dropped (they carry whole
    /// entities). The copy every tab already holds stays the one copy, so nothing is left in memory to write
    /// the old words back on the next snapshot. Erasing a person needs it. Returns how many stored values changed.
    /// </summary>
    public int Rewrite(Func<string, string> scrub)
    {
        int changed;
        Changed result;
        lock (_gate)
        {
            _store.Snapshot(Id, Sequence, State, _clock());
            changed = _store.Rewrite(Id, scrub);
            (State, Sequence) = _store.Load(Id);
            _undo.Clear();
            _redo.Clear();
            LastChange = null;
            _recent = null;
            result = new Changed(State, Sequence, "", "", []);
        }
        ChangedEvent?.Invoke(result);
        return changed;
    }

    /// <summary>Writes a snapshot so the next load replays nothing.</summary>
    public void Settle()
    {
        lock (_gate) _store.Snapshot(Id, Sequence, State, _clock());
    }

    /// <summary>
    /// Puts the project back as it was after batch <paramref name="sequence"/>, as a NEW batch — history
    /// is never rewritten by this.
    /// </summary>
    public Changed PutBack(long sequence, string by)
    {
        var (then, _) = _store.LoadAt(Id, sequence);
        return Commit(now => Difference(now, then, $"Put back the timetable as it was after change {sequence}"), by);
    }

    /// <summary>The batch that turns <paramref name="from"/> into <paramref name="to"/>.</summary>
    public static Batch Difference(Timetable from, Timetable to, string label)
    {
        var ops = new List<Op>();
        foreach (var c in Collections.All)
        {
            var ids = c.All(from).Select(e => e.Id).Concat(c.All(to).Select(e => e.Id)).Distinct();
            foreach (var id in ids)
            {
                var a = c.Read(from, id);
                var b = c.Read(to, id);
                if (!System.Text.Json.Nodes.JsonNode.DeepEquals(a, b)) ops.Add(new Op(c.Name, id, a, b));
            }
        }
        return new Batch(label, [.. ops]);
    }
}

/// <summary>Every open project, one live copy each.</summary>
public sealed class LiveProjects(ProjectStore store, Func<DateTimeOffset> clock, Func<DateOnly> today)
{
    private readonly ConcurrentDictionary<Guid, Lazy<LiveProject>> _open = new();

    public ProjectStore Store => store;

    public LiveProject Get(Guid id) =>
        _open.GetOrAdd(id, key => new Lazy<LiveProject>(() => new LiveProject(store, key, clock, today))).Value;

    /// <summary>The live copy if somebody has it open already; never loads one.</summary>
    public LiveProject? Loaded(Guid id) => _open.TryGetValue(id, out var p) && p.IsValueCreated ? p.Value : null;

    /// <summary>Forgets a project's live copy, e.g. after it was deleted.</summary>
    public void Forget(Guid id)
    {
        if (_open.TryRemove(id, out var p) && p.IsValueCreated) p.Value.Settle();
    }

    /// <summary>
    /// Rewrites every project's stored history, deleted ones too, each through its one live copy and under
    /// its lock (<see cref="LiveProject.Rewrite"/>). Forgetting a copy and rewriting the store behind it is
    /// not the same: a tab still holding that copy (an administrator looking at another timetable) would
    /// write the old words back with its next change. A deleted one is loaded only for this, and let go after.
    /// </summary>
    public int RewriteEverywhere(Func<string, string> scrub)
    {
        int changed = 0;
        foreach (var p in store.List(includeDeleted: true))
        {
            changed += Get(p.Id).Rewrite(scrub);
            if (p.Deleted is not null) Forget(p.Id);
        }
        return changed;
    }

    public void SettleAll()
    {
        foreach (var p in _open.Values.Where(v => v.IsValueCreated)) p.Value.Settle();
    }
}
