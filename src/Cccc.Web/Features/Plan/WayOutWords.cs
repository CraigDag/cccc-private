using Cccc.Domain.Model;
using Cccc.Domain.Rules;

namespace Cccc.Web.Features.Plan;

/// <summary>A way out of a clash in a few words, on the button that takes it.</summary>
public static class WayOutWords
{
    /// <summary>
    /// Where it puts the class: another space at the same time ("Studio B, same time"), or another time in its own
    /// space ("11:00–12:00, same space"), in another ("09:00–10:00 in Studio B"), or with no space at all.
    /// </summary>
    public static string Where(Timetable t, Session s, WayOut way)
    {
        var room = way.Moved.RoomId is { } r && t.Rooms[r] is { } x ? x.Name : null;
        if (way.SameTime) return $"{room ?? "No space"}, same time";
        var when = $"{Cccc.Domain.Week.Hm(way.Moved.Start)}–{Cccc.Domain.Week.Hm(way.Moved.End)}";
        return room is null ? when : way.Moved.RoomId == s.RoomId ? $"{when}, same space" : $"{when} in {room}";
    }
}
