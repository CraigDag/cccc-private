using Cccc.Domain.Model;

namespace Cccc.Domain.Rules;

/// <summary>
/// Classes, spaces and facilitators the school has stopped using. Deleting one that was used before
/// today keeps it, marked former from that day: the days already run keep its name, colour, area and
/// hours, so a report over the year still adds up, while it is taken off everything from today on and
/// nothing ahead offers it. One never used before today is simply gone. Students are not kept this way:
/// a filed register keeps their id, and nothing more of a person who has left is held than that.
/// </summary>
public static class Former
{
    public static bool IsFormer(this IRetirable x) => x.FormerSince is not null;

    /// <summary>The ones still in use: what a picker, Find, Now or a list offers.</summary>
    public static IEnumerable<T> Current<T>(this IEnumerable<T> xs) where T : IRetirable => xs.Where(x => x.FormerSince is null);

    /// <summary>How many classes name it before today, and from today on (the Scratch Week's among them).</summary>
    public readonly record struct Uses(int Before, int Ahead);

    public static Uses Of(Timetable t, IRetirable x, DateOnly today)
    {
        var names = Names(x);
        return new(t.Sessions.Count(s => s.Date < today && names(s)),
            t.Sessions.Count(s => s.Date >= today && names(s)) + t.Scratch.Count(names));
    }

    /// <summary>Whether a session names it: as its class, its space, or somebody teaching or covering.</summary>
    public static Func<Session, bool> Names(IRetirable x) => x switch
    {
        Class c => s => s.ClassId == c.Id,
        Room r => s => s.RoomId == r.Id,
        Facilitator f => s => s.FacilitatorIds.Contains(f.Id) || s.CoverIds.Contains(f.Id),
        _ => throw new ArgumentException("Only a class, a space or a facilitator can be former.", nameof(x)),
    };

    /// <summary>
    /// A copy made to land ahead (copy a week or a day, the Scratch Week, next year) leaves out whatever is
    /// former, as deleting it did for the classes ahead: a former class's copy keeps the name it showed and
    /// has no class, a former space's has no space, and a former facilitator is not on it.
    /// </summary>
    public static Session Strip(Timetable t, Session s)
    {
        var cls = t.Classes.Find(s.ClassId);
        bool classGone = cls is { FormerSince: not null };
        bool IsFormerPerson(Guid id) => t.Facilitators[id] is { FormerSince: not null };
        // Nothing former: the very same session, so a copy differs from its source only where it must.
        if (!classGone && t.Rooms.Find(s.RoomId) is not { FormerSince: not null }
            && !s.Facilitators.Any(a => IsFormerPerson(a.FacilitatorId)) && !s.CoverIds.Any(IsFormerPerson))
            return s;
        return s with
        {
            ClassId = classGone ? null : s.ClassId,
            Title = classGone && string.IsNullOrWhiteSpace(s.Title) ? cls!.Name : s.Title,
            RoomId = t.Rooms.Find(s.RoomId) is { FormerSince: not null } ? null : s.RoomId,
            Facilitators = [.. s.Facilitators.Where(a => t.Facilitators[a.FacilitatorId] is not { FormerSince: not null })],
            CoverIds = [.. s.CoverIds.Where(c => t.Facilitators[c] is not { FormerSince: not null })],
        };
    }

    /// <summary>"since Sat 14 Nov": the words beside a former one wherever it is listed.</summary>
    public static string Since(IRetirable x) => x.FormerSince is { } d ? "since " + Week.ShortDate(d) : "";
}
