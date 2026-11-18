using Cccc.Domain;
using Cccc.Domain.Model;

namespace Cccc.Domain.Tests;

/// <summary>A small timetable built by hand, so each test says exactly what is in it. Every date is fixed.</summary>
internal sealed class Build
{
    /// <summary>A Monday, chosen so no test depends on today's date.</summary>
    public static readonly DateOnly Monday = new(2030, 3, 4);
    public static readonly DateOnly Today = Monday;      // the week of Monday 4 March 2030 is "this week"

    public Timetable T = Timetable.Empty with
    {
        Semesters = EntitySet<Semester>.Of([new Semester { Name = "Spring", StartMonday = Monday.AddDays(-28), WeekCount = 12 }]),
    };

    public Group Group(string name, Programme? p = null)
    {
        var g = new Group { Name = name, ProgrammeId = p?.Id };
        T = T with { Groups = T.Groups.Put(g) };
        return g;
    }

    public Programme Programme(string name, bool guest = false)
    {
        var p = new Programme { Name = name, IsGuest = guest };
        T = T with { Programmes = T.Programmes.Put(p) };
        return p;
    }

    public Facilitator Facilitator(string name, double? max = null)
    {
        var f = new Facilitator { Name = name, MaxWeeklyHours = max is { } m ? TimeSpan.FromHours(m) : null };
        T = T with { Facilitators = T.Facilitators.Put(f) };
        return f;
    }

    public Student Student(string first, Group? g = null)
    {
        var s = new Student { FirstName = first, Surname = "Test", GroupId = g?.Id };
        T = T with { Students = T.Students.Put(s) };
        return s;
    }

    public Room Room(string name)
    {
        var r = new Room { Name = name };
        T = T with { Rooms = T.Rooms.Put(r) };
        return r;
    }

    public Class Class(string name, Workload w = Workload.Counted, Effort effort = Effort.NotSet)
    {
        var c = new Class { Name = name, Workload = w, Effort = effort };
        T = T with { Classes = T.Classes.Put(c) };
        return c;
    }

    public Session Session(DateOnly date, string from, string to, Audience? audience = null, Room? room = null,
        Class? cls = null, params Assignment[] who)
    {
        var s = new Session
        {
            Date = date, Start = TimeOnly.Parse(from), End = TimeOnly.Parse(to), Title = cls?.Name ?? "Class",
            ClassId = cls?.Id, Audience = audience ?? Audience.Everyone, RoomId = room?.Id, Facilitators = [.. who],
        };
        T = T with { Sessions = T.Sessions.Put(s) };
        return s;
    }

    public void Put(Session s) => T = T with { Sessions = T.Sessions.Put(s) };

    public void Away(Facilitator f, DateOnly date, string? from = null, string? to = null) =>
        T = T with
        {
            Absences = T.Absences.Put(new Absence
            {
                Person = AbsentPerson.Facilitator, PersonId = f.Id, From = date, To = date,
                StartTime = from is null ? null : TimeOnly.Parse(from), EndTime = to is null ? null : TimeOnly.Parse(to),
            }),
        };

    public void TimeRule(TimeRule r) => T = T with { TimeRules = T.TimeRules.Put(r) };

    public void Closure(Closure c) => T = T with { Closures = T.Closures.Put(c) };

    public static Assignment In(Facilitator f, string? from = null, string? until = null) =>
        new(f.Id, from is null ? null : TimeOnly.Parse(from), until is null ? null : TimeOnly.Parse(until));
}
