using Microsoft.JSInterop;

namespace Cccc.Web.Features.Week;

/// <summary>What week.js reports at the end of a gesture: one call per gesture, never one per move.</summary>
public sealed class WeekGestures
{
    public Func<Guid, bool, Task> OnChoose { get; init; } = (_, _) => Task.CompletedTask;
    public Func<IReadOnlyList<Guid>, bool, Task> OnChooseMany { get; init; } = (_, _) => Task.CompletedTask;
    public Func<Task> OnChooseNone { get; init; } = () => Task.CompletedTask;
    public Func<Guid, Task> OnOpen { get; init; } = _ => Task.CompletedTask;
    public Func<Guid, int, int, int, Task> OnMove { get; init; } = (_, _, _, _) => Task.CompletedTask;
    public Func<int, int, Task> OnAddAt { get; init; } = (_, _) => Task.CompletedTask;
    public Func<string, Task> OnKey { get; init; } = _ => Task.CompletedTask;

    [JSInvokable] public Task Choose(string id, bool add) => Guid.TryParse(id, out var g) ? OnChoose(g, add) : Task.CompletedTask;

    [JSInvokable] public Task ChooseMany(string[] ids, bool add) =>
        OnChooseMany(ids.Select(i => Guid.TryParse(i, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList(), add);

    [JSInvokable] public Task ChooseNone() => OnChooseNone();

    [JSInvokable] public Task Open(string id) => Guid.TryParse(id, out var g) ? OnOpen(g) : Task.CompletedTask;

    [JSInvokable] public Task Move(string id, int day, int start, int end) =>
        Guid.TryParse(id, out var g) ? OnMove(g, day, start, end) : Task.CompletedTask;

    [JSInvokable] public Task AddAt(int day, int minute) => OnAddAt(day, minute);

    [JSInvokable] public Task Key(string name) => OnKey(name);
}
