using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Features.RollCall;

using Week = Cccc.Domain.Week;

/// <summary>
/// The roll call's words: what is in a space, what to count by eye, and where the timetable has somebody who
/// is in none of the spaces. Never why somebody is away: that is theirs, and a roll call needs only that they are.
/// </summary>
public static class RollCallWords
{
    /// <summary>The space's name, or what the classes given no space are listed under.</summary>
    public static string Space(Timetable t, Guid? roomId) =>
        roomId is null ? "No space given" : t.Rooms.Find(roomId)?.Name ?? "A space that has gone";

    /// <summary>
    /// Whom the roll call would list, before it is called: "2 facilitators and 20 students expected", and what
    /// is to count by eye.
    /// </summary>
    public static string Expected(RollList list)
    {
        var expected = list.Expected.ToList();
        int eyes = list.Spaces.Sum(s => s.ByEye.Count);
        string by = eyes == 0 ? "" : $", and {Looks.Count(eyes, "class or event", "classes or events")} to count by eye";
        if (expected.Count == 0) return eyes == 0 ? "The timetable expects nobody in the building now." : $"Nobody expected by name{by}.";
        int students = expected.Count(p => p.IsStudent), staff = expected.Count - students;
        string who = string.Join(" and ", new[] { staff > 0 ? Looks.Count(staff, "facilitator") : null, students > 0 ? Looks.Count(students, "student") : null }.OfType<string>());
        return $"{who} expected{by}.";
    }

    /// <summary>What is in a space at the moment, each until when: "Aerial until 11:00 · Staff meeting until 10:30".</summary>
    public static string InIt(Timetable t, RollSpace s) =>
        string.Join(" · ", s.Classes.Select(o => $"{Looks.Title(t, o.Session)} until {Week.Hm(o.End)}")
            .Concat(s.ByEye.Where(x => x.Event is not null).Select(x => Event(x.Event!))));

    /// <summary>
    /// Something in a space that names nobody, or not everybody there, and why its people are counted by eye.
    /// Until when is said above it, with what is in the space.
    /// </summary>
    public static string ByEye(Timetable t, RollByEye x) => x.Class is { } o
        ? Looks.Title(t, o.Session) + (o.Session.Audience.Kind == AudienceKind.Everyone ? ", for the whole building" : ", with nobody on its list")
        : x.Event!.Event.Title.Trim() + ", an event";

    /// <summary>
    /// Where the timetable has somebody who is in none of the spaces, under the heading their kind is listed
    /// under (<see cref="Heading"/>): their classes either side, the event they are at, the register that says
    /// they are not there. Nothing for somebody away, whose reason is theirs.
    /// </summary>
    public static string Detail(Timetable t, RollPerson p) => p.Where switch
    {
        RollWhere.Between => $"{Part(t, p.Day.Last!)} until {Week.Hm(p.Day.Last!.Until)}, then {Part(t, p.Day.Next!)} at {Week.Hm(p.Day.Next!.From)}",
        RollWhere.Elsewhere => Event(p.Event!) + (p.Event!.Event.Place is { Length: > 0 } place && place.Trim().Length > 0 ? $" ({place.Trim()})" : ""),
        RollWhere.NotInYet => $"{Part(t, p.Day.Next!)} at {Week.Hm(p.Day.Next!.From)}",
        RollWhere.Gone => $"{Part(t, p.Day.Last!)}, until {Week.Hm(p.Day.Last!.Until)}",
        RollWhere.MarkedAbsent => $"{Part(t, p.Day.Now!)}: {(t.Registers[p.Day.Now!.O.Session.Id]?.MarkFor(p.Id) == Mark.Excused ? "excused" : "absent")}",
        _ => "",
    };

    /// <summary>The headings over the people outside the spaces, one for each kind, in the order they are listed.</summary>
    public static string Heading(RollWhere where) => where switch
    {
        RollWhere.Between => "Between classes",
        RollWhere.Elsewhere => "At an event in none of the spaces",
        RollWhere.NotInYet => "First class still to come",
        RollWhere.Gone => "Classes over for today",
        RollWhere.MarkedAbsent => "Not there, says the register",
        RollWhere.Away => "Recorded away",
        _ => "",
    };

    private static string Part(Timetable t, Stint s) =>
        Looks.Title(t, s.O.Session) + (Looks.Room(t, s.O.Session) is { Length: > 0 } room ? " in " + room : "");

    private static string Event(EventOnDay e) =>
        e.AllDay || e.Until == TimeOnly.MaxValue ? $"{e.Event.Title.Trim()}, all day" : $"{e.Event.Title.Trim()} until {Week.Hm(e.Until)}";
}
