using System.Collections.Immutable;

namespace Cccc.Web.Features.RollCall;

/// <summary>A roll call running: the moment it lists the building at, who called it and when, and who has been found since.</summary>
public sealed record RollCallRun(DateOnly Date, TimeOnly At, DateTimeOffset StartedAt, string StartedBy, ImmutableHashSet<Guid> Found);

/// <summary>
/// The roll calls running, one for each timetable, held in memory only. Who has been found is never written
/// down: ending one forgets it, and so does the app restarting, so nothing about who was where outlives it.
/// One left running ends by itself after <see cref="Lasts"/>. Everybody looking at it is told of each tick,
/// so several people can tick at once, each on their own phone.
/// </summary>
public sealed class RollCalls(Func<DateTimeOffset> now)
{
    /// <summary>Long enough for any drill or evacuation; short enough that one forgotten is gone by the evening.</summary>
    public static readonly TimeSpan Lasts = TimeSpan.FromHours(3);

    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, RollCallRun> _runs = [];

    /// <summary>Raised with the timetable's id when a roll call starts or ends, and at every tick.</summary>
    public event Action<Guid>? Changed;

    public RollCallRun? For(Guid projectId)
    {
        lock (_gate) return Running(projectId);
    }

    /// <summary>
    /// Calls the roll at this moment, or gives back the one already called: two people pressing at once call one
    /// roll, and tick the same list.
    /// </summary>
    public RollCallRun Start(Guid projectId, DateOnly date, TimeOnly at, string by)
    {
        RollCallRun run;
        lock (_gate)
        {
            if (Running(projectId) is { } running) return running;
            run = new RollCallRun(date, at, now(), by, []);
            _runs[projectId] = run;
        }
        Changed?.Invoke(projectId);
        return run;
    }

    /// <summary>Ticks somebody found, or something counted by eye, or takes the tick off again.</summary>
    public void Mark(Guid projectId, Guid id, bool found) => Mark(projectId, [id], found);

    /// <summary>Ticks several at once (everybody in one space), or takes their ticks off, telling everybody once.</summary>
    public void Mark(Guid projectId, IReadOnlyCollection<Guid> ids, bool found)
    {
        lock (_gate)
        {
            if (Running(projectId) is not { } run) return;
            var to = found ? run.Found.Union(ids) : run.Found.Except(ids);
            if (to.SetEquals(run.Found)) return;
            _runs[projectId] = run with { Found = to };
        }
        Changed?.Invoke(projectId);
    }

    /// <summary>Ends it, forgetting every tick.</summary>
    public void End(Guid projectId)
    {
        lock (_gate)
            if (!_runs.Remove(projectId)) return;
        Changed?.Invoke(projectId);
    }

    // Under the gate: the one running, dropping one that has lasted too long.
    private RollCallRun? Running(Guid projectId)
    {
        if (!_runs.TryGetValue(projectId, out var run)) return null;
        if (now() - run.StartedAt < Lasts) return run;
        _runs.Remove(projectId);
        return null;
    }
}
