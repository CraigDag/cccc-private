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

    /// <summary>
    /// Whether this account may see that timetable: anybody signed in the one open for everybody, and an
    /// administrator any other that has not been deleted. A page, a printed sheet and a spreadsheet all ask
    /// this, so a link made in one tab cannot show a timetable to somebody who may not see it.
    /// </summary>
    public bool MaySee(Guid id, Access access) =>
        access.SignedIn && (id == OpenId() || (access.MayLookAtOtherTimetables && store.Find(id) is { Deleted: null }));

    /// <summary>
    /// The timetable a tab shows: the one it is looking at (<paramref name="lookingAt"/>, held by the tab
    /// in <c>TabTimetable</c>) while its account may see it, otherwise the one open for everybody.
    /// </summary>
    public Guid? ShownId(Guid? lookingAt, Access access) =>
        lookingAt is { } id && MaySee(id, access) ? id : OpenId();

    /// <summary>
    /// Deletes a timetable, which comes back for 30 days. Never the one open for everybody: another is
    /// opened first. Every tab is told, so one that was looking at it goes back to the open one.
    /// </summary>
    public void Delete(Guid id, DateTimeOffset at)
    {
        if (id == OpenId()) throw new InvalidOperationException("The timetable open for everybody cannot be deleted. Open another for everybody first.");
        store.Delete(id, at);
        live.Forget(id);
        Moved?.Invoke();
    }
}
