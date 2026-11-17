using System.Text.Json;
using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;

namespace Cccc.Web.Features.Admin;

/// <summary>
/// The people a timetable's stored history still names who are no longer on it: taken off in the last 30
/// days, here or by the current app when this server follows it. Their name is in a stored change until
/// the change is folded away, so erasing a person has to reach them as well as the people still on it.
/// </summary>
public static class FormerPeople
{
    public sealed record Former(bool Student, Guid Id, string Name);

    public static IReadOnlyList<Former> In(IEnumerable<Change> history, Timetable now)
    {
        var found = new Dictionary<Guid, Former>();
        foreach (var op in history.SelectMany(c => c.Batch.Ops))
        {
            bool student = op.Collection == Collections.Students.Name;
            if ((!student && op.Collection != Collections.Facilitators.Name) || op.Before is null) continue;
            if (student ? now.Students.Contains(op.Id) : now.Facilitators.Contains(op.Id)) continue;
            var name = student ? op.Before.Deserialize<Student>(ModelJson.Options)?.Name : op.Before.Deserialize<Facilitator>(ModelJson.Options)?.Name;
            // Somebody already erased is named "[erased]" everywhere, and has nothing left to take out.
            if (string.IsNullOrWhiteSpace(name) || name.Contains("[erased]", StringComparison.Ordinal)) continue;
            found[op.Id] = new Former(student, op.Id, name);
        }
        return found.Values.OrderBy(f => f.Name, NameOrder.Comparer).ToList();
    }
}
