using System.Collections.Immutable;
using System.Text.Json;
using Cccc.Domain.Editing;
using Cccc.Domain.Rules;
using Cccc.Store;

namespace Cccc.Web.Features.Now;

/// <summary>
/// What each facilitator has said they saw of what changed for them ("Got it" on Now), kept per timetable and
/// person outside the timetable, as class ids and places in the history and nothing else: it is a person's own
/// reading, not the school's timetable, so it is never part of a change, an Undo or a download. It goes with its
/// timetable when that is purged and with the person when they are erased. <see cref="News"/> says what it means.
/// </summary>
public sealed class SeenNews(ProjectStore store, AccountDirectory accounts)
{
    private readonly Lock _gate = new();

    /// <summary>
    /// The sign-ins linked to each of <paramref name="facilitatorIds"/> that has one able to sign in, so could have seen
    /// anything: whatever is done through any of them is theirs, and seen.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<string>> LoginsOf(IEnumerable<Guid> facilitatorIds)
    {
        var wanted = facilitatorIds.ToHashSet();
        return accounts.All().Where(a => a.FacilitatorId is { } f && wanted.Contains(f))
            .GroupBy(a => a.FacilitatorId!.Value)
            .Where(g => g.Any(a => !a.Suspended))
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)[.. g.Select(a => a.Login)]);
    }

    /// <summary>What they have seen, by class. A record that cannot be read is no record: everything is news again.</summary>
    public IReadOnlyDictionary<Guid, long> Read(Guid project, Guid facilitatorId)
    {
        if (store.NewsSeen(project, facilitatorId) is not { } text) return ImmutableDictionary<Guid, long>.Empty;
        try { return JsonSerializer.Deserialize<Dictionary<Guid, long>>(text, JsonSerializerOptions.Web) ?? []; }
        catch (JsonException) { return ImmutableDictionary<Guid, long>.Empty; }
    }

    /// <summary>
    /// Remembers that they have seen <paramref name="items"/>, each up to its last change, read out of
    /// <paramref name="changes"/>: the changes the page read, whose first decides what is no longer worth keeping.
    /// </summary>
    public void GotIt(Guid project, Guid facilitatorId, IEnumerable<ClassChange> items, IReadOnlyList<Change> changes)
    {
        long oldest = changes.Count == 0 ? long.MaxValue : changes.Min(c => c.Sequence);
        lock (_gate)
        {
            var seen = News.GotIt(Read(project, facilitatorId), items, oldest);
            store.SetNewsSeen(project, facilitatorId, JsonSerializer.Serialize(seen, JsonSerializerOptions.Web));
        }
    }
}
