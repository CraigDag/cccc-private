using System.Text.Json;
using Cccc.Domain.Model;
using Cccc.Store;

namespace Cccc.Web.Features.Print;

/// <summary>
/// Makes a person's phone calendar and remembers what it carried, so the next download can cancel what
/// has gone since: a class deleted, the cover given to somebody else, an event taken out. Without this a
/// phone importing the new file keeps the old entry, because the new file never mentions it, and somebody
/// turns up to a class that is not there. What is remembered is ids and times only, kept per timetable
/// and person, and dropped once its day has passed, when an old entry can no longer send anybody anywhere.
/// Every download of a person's file is remembered, whoever takes it: an administrator's download for a
/// coach cancels at most what the coach's phone never had, which a calendar passes over.
/// </summary>
public sealed class PhoneCalendarFiles(ProjectStore store)
{
    private readonly Lock _gate = new();

    public (byte[] Bytes, string Name) Make(Guid project, Timetable t, Guid facilitatorId, DateOnly anyDate, DateTimeOffset now, DateOnly today)
    {
        lock (_gate)
        {
            var made = PhoneCalendar.Make(t, facilitatorId, anyDate, now, Read(project, facilitatorId));
            store.SetCalendarHeld(project, facilitatorId, JsonSerializer.Serialize(made.Holds.Where(h => h.To >= today), JsonSerializerOptions.Web));
            return (PhoneCalendar.Encode(made.Text), PhoneCalendar.FileName(t, facilitatorId, anyDate));
        }
    }

    /// <summary>What earlier downloads carried. A record that cannot be read is no record: the file is still made.</summary>
    public IReadOnlyList<PhoneCalendar.Held> Read(Guid project, Guid facilitatorId)
    {
        if (store.CalendarHeld(project, facilitatorId) is not { } text) return [];
        try { return JsonSerializer.Deserialize<List<PhoneCalendar.Held>>(text, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { return []; }
    }
}
