using Microsoft.JSInterop;

namespace Cccc.Web.Features.Plan;

/// <summary>What plan.js reports at the end of a gesture: one call per gesture, never one per move.</summary>
public sealed class PlanGestures
{
    public Func<Guid, Task> OnChoose { get; init; } = _ => Task.CompletedTask;
    public Func<Task> OnChooseNone { get; init; } = () => Task.CompletedTask;
    public Func<Guid, Task> OnOpen { get; init; } = _ => Task.CompletedTask;
    /// <summary>A block let go: its id, the row it landed on, and its new start and end in minutes.</summary>
    public Func<Guid, string, int, int, Task> OnMove { get; init; } = (_, _, _, _) => Task.CompletedTask;
    public Func<string, int, Task> OnAddAt { get; init; } = (_, _) => Task.CompletedTask;
    /// <summary>Empty time dragged across: the row, and the start and end in minutes, snapped.</summary>
    public Func<string, int, int, Task> OnDraw { get; init; } = (_, _, _) => Task.CompletedTask;
    public Func<string, Task> OnKey { get; init; } = _ => Task.CompletedTask;

    [JSInvokable] public Task Choose(string id) => Guid.TryParse(id, out var g) ? OnChoose(g) : Task.CompletedTask;
    [JSInvokable] public Task ChooseNone() => OnChooseNone();
    [JSInvokable] public Task Open(string id) => Guid.TryParse(id, out var g) ? OnOpen(g) : Task.CompletedTask;
    [JSInvokable] public Task Move(string id, string row, int start, int end) =>
        Guid.TryParse(id, out var g) ? OnMove(g, row, start, end) : Task.CompletedTask;
    [JSInvokable] public Task AddAt(string row, int minute) => OnAddAt(row, minute);
    [JSInvokable] public Task Draw(string row, int start, int end) => end > start ? OnDraw(row, start, end) : Task.CompletedTask;
    [JSInvokable] public Task Key(string name) => OnKey(name);
}
