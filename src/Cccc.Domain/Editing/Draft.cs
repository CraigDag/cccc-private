using System.Collections.Immutable;
using Cccc.Domain.Model;

namespace Cccc.Domain.Editing;

/// <summary>
/// Builds one batch against a timetable: each put or remove is applied to <see cref="Current"/> at once,
/// so later steps of the same command see earlier ones, and the operations carry true befores.
/// </summary>
public sealed class Draft(Timetable start)
{
    private readonly List<Op> _ops = [];

    public Timetable Start { get; } = start;
    public Timetable Current { get; private set; } = start;

    public Draft Put<T>(Collection<T> c, T item) where T : class, IEntity
    {
        var op = c.Put(Current, item);
        if (System.Text.Json.Nodes.JsonNode.DeepEquals(op.Before, op.After)) return this;
        _ops.Add(op);
        Current = c.Write(Current, item.Id, op.After);
        return this;
    }

    public Draft Remove<T>(Collection<T> c, Guid id) where T : class, IEntity
    {
        if (!c.Get(Current).Contains(id)) return this;
        var op = c.Remove(Current, id);
        _ops.Add(op);
        Current = c.Write(Current, id, null);
        return this;
    }

    public Draft PutSettings(Settings s)
    {
        var op = Collections.Settings.Put(Current, s);
        if (System.Text.Json.Nodes.JsonNode.DeepEquals(op.Before, op.After)) return this;
        _ops.Add(op);
        Current = Collections.Settings.Write(Current, Settings.TheId, op.After);
        return this;
    }

    public int Count => _ops.Count;

    /// <summary>The batch, with consecutive operations on one thing folded into one.</summary>
    public Batch ToBatch(string label)
    {
        var folded = new List<Op>();
        foreach (var op in _ops)
        {
            int at = folded.FindIndex(o => o.Collection == op.Collection && o.Id == op.Id);
            if (at >= 0) folded[at] = folded[at] with { After = op.After };
            else folded.Add(op);
        }
        folded.RemoveAll(o => System.Text.Json.Nodes.JsonNode.DeepEquals(o.Before, o.After));
        return new Batch(label, [.. folded]);
    }
}
