using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// Who is teaching at a time: the WHO and WHEN of the sentence the app exists for, asked from the
/// person's side. Cover's ranking and the class form's marks both read it, so the form cannot call
/// somebody free whom cover calls busy.
/// </summary>
public static class Teaching
{
    /// <summary>
    /// Each facilitator in the room at any moment between <paramref name="start"/> and
    /// <paramref name="end"/> on <paramref name="date"/>, by the earliest class they are in then. "In
    /// the room" is the occurrence's own answer: somebody away is not, cover is, and a person who is in
    /// only part of a class is busy only for their part. A cancelled or closed class keeps nobody busy; a
    /// guest class does, because hiding is drawing, never counting. A class that ends as another starts
    /// does not overlap it, and <paramref name="except"/> (the class being edited or covered) never
    /// makes anybody look busy.
    /// </summary>
    public static IReadOnlyDictionary<Guid, Occurrence> At(Timetable t, DateOnly date, TimeOnly start, TimeOnly end, Guid? except = null)
    {
        var busy = new Dictionary<Guid, Occurrence>();
        if (end <= start) return busy;
        foreach (var o in Occurrences.Between(t, date, date).OrderBy(o => o.Start))
        {
            if (o.Session.Id == except || !o.Happens || o.Start >= end || start >= o.End) continue;
            foreach (var id in o.InTheRoom)
            {
                var (a, b) = TimeWindows.InRoom(o, id);
                if (a < end && start < b) busy.TryAdd(id, o);
            }
        }
        return busy;
    }
}
