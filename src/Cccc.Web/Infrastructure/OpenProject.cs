using Cccc.Store;

namespace Cccc.Web.Infrastructure;

/// <summary>
/// Which project the school is looking at. Signing in opens it rather than a picker; choosing it is an
/// administrator's. Until one has been chosen it is the project most recently changed — and that answer
/// is written down the first time it is given, so bringing in a file or starting a new timetable never
/// silently moves the whole school onto it.
/// </summary>
public sealed class ProjectChoice(ProjectStore store, LiveProjects live)
{
    private const string Key = "open_project";

    private readonly Lock _gate = new();

    public event Action? Moved;

    public Guid? OpenId()
    {
        lock (_gate)
        {
            if (Guid.TryParse(store.GetMeta(Key), out var g) && store.Find(g) is { Deleted: null }) return g;
            var landing = store.Landing()?.Id;
            if (landing is { } l) store.SetMeta(Key, l.ToString());
            return landing;
        }
    }

    public LiveProject? Open() => OpenId() is { } id ? live.Get(id) : null;

    public void Choose(Guid id)
    {
        store.SetMeta(Key, id.ToString());
        Moved?.Invoke();
    }
}
