using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// Which spaces are in use, and by what: the SPACE and WHEN of the sentence the app exists for, asked
/// from the space's side. The add form's busy marks and free chips read it.
/// </summary>
public static class Spaces
{
    /// <summary>
    /// Each space in use at any moment between <paramref name="start"/> and <paramref name="end"/> on
    /// <paramref name="date"/>, by the earliest class in it then. A class that ends as another starts does
    /// not overlap it. Cancelled and closed classes leave the space free; a guest class does not, because
    /// hiding is drawing, never counting. <paramref name="except"/> is the class being edited, which never
    /// makes its own space look busy.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Occurrence> InUse(Timetable t, DateOnly date, TimeOnly start, TimeOnly end, Guid? except = null)
    {
        var busy = new Dictionary<Guid, Occurrence>();
        if (end <= start) return busy;
        foreach (var o in Occurrences.Between(t, date, date).OrderBy(o => o.Start))
            if (o.Session.Id != except && o.Happens && o.Session.RoomId is { } room && o.Start < end && start < o.End)
                busy.TryAdd(room, o);
        return busy;
    }
}
