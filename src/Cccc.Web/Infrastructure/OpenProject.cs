using Cccc.Store;

namespace Cccc.Web.Infrastructure;

/// <summary>
/// Which project the school is looking at. Signing in opens it rather than a picker; choosing it is an
/// administrator's. Unless one has been chosen, it is the project most recently changed.
/// </summary>
public sealed class ProjectChoice(ProjectStore store, LiveProjects live)
{
    private const string Key = "open_project";

    public event Action? Moved;

    public Guid? OpenId()
    {
        if (Guid.TryParse(store.GetMeta(Key), out var g) && store.Find(g) is { Deleted: null }) return g;
        return store.Landing()?.Id;
    }

    public LiveProject? Open() => OpenId() is { } id ? live.Get(id) : null;

    public void Choose(Guid id)
    {
        store.SetMeta(Key, id.ToString());
        Moved?.Invoke();
    }
}
