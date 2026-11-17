// A made-up school written in the CURRENT app's own file format, with the current app's own code, for
// trying "follow the current app" on the preview (scripts/preview.sh with FOLLOW=1) and for the
// walk-through that checks it. Made-up names only; it never reads or writes a real file.
//
//   make <file>            this week, every day: four classes a day for two groups
//   cancel <file> <class>  cancels today's class of that name, as the current app records a one-off cancel
//   later <file> <class>   moves today's class of that name an hour later
//   remove <file> <first>  takes the student with that first name out of the file, as erasing them there does
//
// Each writes the whole file aside and renames it over the old one, as the current app saves.
using CCCCScheduler.Services;
using Old = CCCCScheduler.Models;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: make <file> | cancel <file> <class> | later <file> <class> | remove <file> <first name>");
    return 2;
}
var (mode, path) = (args[0], args[1]);
var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Copenhagen");
var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone).DateTime);
var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

Old.ProjectFile p;
if (mode == "make")
{
    var ba = new Old.Group { Name = "BA 1", ColourHex = "#2E7D32" };
    var foundation = new Old.Group { Name = "Foundation", ColourHex = "#1565C0" };
    var students = new[] { ("Alba", "Kestrel", ba), ("Benno", "Marsh", ba), ("Cleo", "Vantor", foundation), ("Dario", "Fjelstad", foundation) }
        .Select(x => new Old.Student { FirstName = x.Item1, Surname = x.Item2, GroupId = x.Item3.Id }).ToList();
    var ada = new Old.Facilitator { Name = "Ada Brightwater", ColourHex = "#00838F" };
    var dag = new Old.Facilitator { Name = "Dag Øverlie", ColourHex = "#6A1B9A" };
    var rooms = new[] { "Big Top", "Studio A", "Studio B" }.Select(n => new Old.Room { Name = n }).ToList();
    var sessions = new List<Old.ScheduleEntry>();
    // Every day of the week, so the walk-through has a today whatever day it runs.
    foreach (var d in Enum.GetValues<DayOfWeek>())
    {
        Old.ScheduleEntry Class(string title, int h, int m, int h2, int m2, Old.Group g, Old.Room r, Old.Facilitator f) => new()
        {
            Title = title, Day = d, WeekMonday = monday, Start = new(h, m), End = new(h2, m2),
            Scope = Old.SessionScope.Group, GroupId = g.Id, RoomId = r.Id, FacilitatorIds = [f.Id],
        };
        sessions.Add(Class("Acro", 9, 0, 11, 0, ba, rooms[0], ada));
        sessions.Add(Class("Dance", 11, 15, 12, 30, foundation, rooms[1], dag));
        sessions.Add(Class("Aerial", 13, 30, 16, 0, ba, rooms[2], dag));
        sessions.Add(Class("Handstands", 16, 15, 18, 0, foundation, rooms[0], ada));
    }
    var building = new Old.Building
    {
        Name = "Northlight", Groups = [ba, foundation], Students = students, Facilitators = [ada, dag], Sessions = sessions,
        WeeksAreSeparate = true,
    };
    foreach (var r in rooms) building.Rooms.Add(r);
    p = new Old.ProjectFile
    {
        SchemaVersion = Old.ProjectFile.CurrentSchemaVersion,
        Terms = [new Old.TermInfo { Name = "Autumn", StartMonday = monday.AddDays(-28), WeekCount = 12 }],
        Building = building,
    };
}
else if (args.Length >= 3 && mode == "remove")
{
    p = ProjectPackage.LoadFromBytes(File.ReadAllBytes(path));
    if (p.Building.Students.RemoveAll(s => s.FirstName == args[2]) == 0)
    {
        Console.Error.WriteLine($"No student called {args[2]}.");
        return 1;
    }
}
else if (args.Length >= 3 && mode is "cancel" or "later")
{
    p = ProjectPackage.LoadFromBytes(File.ReadAllBytes(path));
    var target = p.Building.Sessions.FirstOrDefault(s => s.WeekMonday == monday && s.Day == today.DayOfWeek && s.Title == args[2]);
    if (target is null)
    {
        Console.Error.WriteLine($"No class called {args[2]} today.");
        return 1;
    }
    if (mode == "cancel")
        target.Exceptions.Add(new Old.SessionException { WeekMonday = monday, Kind = Old.ExceptionKind.Cancelled, Note = "Made up" });
    else
        (target.Start, target.End) = (target.Start.AddHours(1), target.End.AddHours(1));
}
else
{
    Console.Error.WriteLine("Usage: make <file> | cancel <file> <class> | later <file> <class> | remove <file> <first name>");
    return 2;
}
var bytes = ProjectPackage.SaveToBytes(p, savedBy: "made-up");
var part = path + ".part";
File.WriteAllBytes(part, bytes);
File.Move(part, path, overwrite: true);
Console.WriteLine($"{mode}: the week of {monday:yyyy-MM-dd}, written as the current app writes it.");
return 0;
