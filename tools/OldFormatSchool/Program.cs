// A made-up school written in the CURRENT app's own file format, with the current app's own code, for
// trying "follow the current app" on the preview (scripts/preview.sh with FOLLOW=1) and for the
// walk-through that checks it. Made-up names only; it never reads or writes a real file.
//
//   make <file>            this week, every day: four classes a day for two groups, and a plan of two
//                          classes in the Scratch Week (schema 24), which counts for nothing
//   year <file>            a whole school year at about a real school's size, registers taken on every
//                          class that has been, for timing the import and following on a file that big
//   cancel <file> <class>  cancels today's class of that name, as the current app records a one-off cancel
//   later <file> <class>   moves today's class of that name an hour later
//   remove <file> <first>  takes the student with that first name out of the file, as erasing them there does
//
// Each writes the whole file aside and renames it over the old one, as the current app saves.
using CCCCScheduler.Services;
using Old = CCCCScheduler.Models;

if (args.Length < 2)
{
    Console.Error.WriteLine("Usage: make <file> | year <file> | cancel <file> <class> | later <file> <class> | remove <file> <first name>");
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
    // The current app's Scratch Week: a plan, dated in its own week no term reaches.
    var scratch = new List<Old.ScheduleEntry>
    {
        new() { Title = "Trial duet", Day = DayOfWeek.Monday, WeekMonday = Old.Building.ScratchMonday, Start = new(10, 0), End = new(11, 30),
                Scope = Old.SessionScope.Group, GroupId = ba.Id, RoomId = rooms[1].Id, FacilitatorIds = [ada.Id] },
        new() { Title = "Trial lifts", Day = DayOfWeek.Wednesday, WeekMonday = Old.Building.ScratchMonday, Start = new(14, 0), End = new(15, 0),
                Scope = Old.SessionScope.Group, GroupId = foundation.Id, RoomId = rooms[2].Id, FacilitatorIds = [dag.Id] },
    };
    var building = new Old.Building
    {
        Name = "Northlight", Groups = [ba, foundation], Students = students, Facilitators = [ada, dag], Sessions = sessions,
        ScratchSessions = scratch, WeeksAreSeparate = true,
    };
    foreach (var r in rooms) building.Rooms.Add(r);
    p = new Old.ProjectFile
    {
        SchemaVersion = Old.ProjectFile.CurrentSchemaVersion,
        Terms = [new Old.TermInfo { Name = "Autumn", StartMonday = monday.AddDays(-28), WeekCount = 12 }],
        Building = building,
    };
}
else if (mode == "year")
{
    p = Year(monday);
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
    Console.Error.WriteLine("Usage: make <file> | year <file> | cancel <file> <class> | later <file> <class> | remove <file> <first name>");
    return 2;
}
var bytes = ProjectPackage.SaveToBytes(p, savedBy: "made-up");
var part = path + ".part";
File.WriteAllBytes(part, bytes);
File.Move(part, path, overwrite: true);
Console.WriteLine($"{mode}: the week of {monday:yyyy-MM-dd}, written as the current app writes it ({bytes.Length / 1024} KB).");
return 0;

// Two programmes of three groups, ten students in each, sixteen facilitators, twelve spaces; each group
// has two classes a day, Monday to Friday, with lunch and Wednesday's open training for the whole
// building, for two terms of 18 weeks either side of this week. Every class that has been has its
// register. Made-up names only, some with Æ, Ø and Å so the Danish order shows.
static Old.ProjectFile Year(DateOnly monday)
{
    var rnd = new Random(7);
    var ba = new Old.Strand { Name = "BA", ColourHex = "#2E7D32" };
    var fd = new Old.Strand { Name = "Foundation", ColourHex = "#1565C0" };
    var groups = new[] { ("BA 1", ba), ("BA 2", ba), ("BA 3", ba), ("Foundation A", fd), ("Foundation B", fd), ("Foundation C", fd) }
        .Select((x, i) => new Old.Group { Name = x.Item1, StrandId = x.Item2.Id, ColourHex = new[] { "#2E7D32", "#558B2F", "#9E9D24", "#1565C0", "#0277BD", "#00838F" }[i] }).ToList();
    string[] firsts = ["Alba", "Benno", "Cleo", "Dario", "Edda", "Finn", "Greta", "Hugo", "Ilse", "Jonas", "Kaja", "Linus", "Maja", "Nils", "Olga",
        "Pelle", "Rune", "Sigrid", "Tove", "Ulla", "Vera", "Wilmer", "Ylva", "Øjvind", "Åse", "Ærtur", "Bodil", "Casper", "Ditte", "Egil"];
    string[] lasts = ["Kestrel", "Marsh", "Vantor", "Fjelstad", "Brightwater", "Øverlie", "Åkerholm", "Lindqvane", "Stormbæk", "Hjelmvik",
        "Ravnsholt", "Tidemark", "Aaskov", "Quillborg", "Eskerud", "Morrowby", "Nyhavnsen", "Sandvold", "Ulvetand", "Grønnemark"];
    var students = Enumerable.Range(0, 60).Select(i => new Old.Student
    {
        FirstName = firsts[i % firsts.Length], Surname = lasts[(i * 7) % lasts.Length], GroupId = groups[i / 10].Id,
    }).ToList();
    string[] staff = ["Ada Brightwater", "Dag Øverlie", "Inge Sølvhøj", "Rafael Moonfield", "Tess Quarrington", "Oskar Lindqvane", "Mira Fallowmere",
        "Henrik Ågaard", "Juno Crestwell", "Lars Tidemark", "Petra Vandersee", "Sven Haldorhus", "Nora Kittleby", "Aksel Ravnsholt", "Ines Moorcroft", "Bo Eskerud"];
    var facilitators = staff.Select((n, i) => new Old.Facilitator { Name = n, ColourHex = $"#{(i * 37 % 200) + 40:X2}{(i * 71 % 200) + 40:X2}{(i * 53 % 200) + 40:X2}" }).ToList();
    string[] spaceNames = ["Big Top", "Aerial Hall", "Studio A", "Studio B", "Studio C", "Studio D", "Gym", "Loft", "Dance Hall", "Black Box", "Garden", "Workshop"];
    var rooms = spaceNames.Select(n => new Old.Room { Name = n }).ToList();
    string[] classNames = ["Acro", "Dance", "Aerial", "Handstands", "Juggling", "Tightwire", "Clown", "Theatre", "Conditioning", "Flexibility", "Partner acro", "Trapeze"];
    string[] classColours = ["#C0504D", "#4F81BD", "#9BBB59", "#8064A2", "#4BACC6", "#F79646", "#2C4D75", "#772C2A", "#5F7530", "#4D3B62", "#276A7C", "#B65708"];
    var classes = classNames.Select((n, i) => new Old.ClassEntry { Name = n, ColourHex = classColours[i] }).ToList();
    var open = new Old.ClassEntry { Name = "Open training", ColourHex = "#7F7F7F", Workload = Old.WorkloadKind.DropIn };
    var lunch = new Old.ClassEntry { Name = "Lunch", ColourHex = "#BFBFBF", Workload = Old.WorkloadKind.NotCounted };
    var first = monday.AddDays(-7 * 20);
    var terms = new List<Old.TermInfo>
    {
        new() { Name = "Autumn", StartMonday = first, WeekCount = 18 },
        new() { Name = "Spring", StartMonday = first.AddDays(7 * 19), WeekCount = 18 },
    };
    (TimeOnly, TimeOnly)[] slots = [(new(9, 0), new(11, 0)), (new(11, 15), new(12, 45)), (new(13, 30), new(15, 30)), (new(15, 45), new(17, 30))];
    var sessions = new List<Old.ScheduleEntry>();
    foreach (var term in terms)
        for (int w = 0; w < term.WeekCount; w++)
        {
            var week = term.StartMonday.AddDays(7 * w);
            bool past = week < monday;
            for (int d = 0; d < 5; d++)
            {
                var day = (DayOfWeek)(d + 1);
                for (int g = 0; g < groups.Count; g++)
                    foreach (int slot in g < 3 ? new[] { 0, 2 } : new[] { 1, 3 })
                    {
                        var c = classes[(g * 5 + d * 3 + slot + w) % classes.Count];
                        var e = new Old.ScheduleEntry
                        {
                            Title = c.Name, ClassId = c.Id, Day = day, WeekMonday = week, Start = slots[slot].Item1, End = slots[slot].Item2,
                            Scope = Old.SessionScope.Group, GroupId = groups[g].Id, RoomId = rooms[(g % 3 + d * 2 + slot * 3 + w) % rooms.Count].Id,
                            FacilitatorIds = [facilitators[(g % 3 * 5 + d + slot * 2 + w) % facilitators.Count].Id],
                        };
                        if (past)
                        {
                            var roll = students.Where(s => s.GroupId == groups[g].Id).Select(s => s.Id).ToList();
                            var away = roll[rnd.Next(roll.Count)];
                            e.Attendance.Add(new Old.AttendanceRecord
                            {
                                WeekMonday = week, PresentStudentIds = roll.Where(x => x != away).ToList(), RosterStudentIds = roll,
                                FiledBy = "made-up", FiledAt = new DateTimeOffset(week.AddDays(d).ToDateTime(slots[slot].Item2), TimeSpan.Zero),
                            });
                        }
                        sessions.Add(e);
                    }
                sessions.Add(new Old.ScheduleEntry
                {
                    Title = lunch.Name, ClassId = lunch.Id, Day = day, WeekMonday = week, Start = new(12, 45), End = new(13, 30),
                    Scope = Old.SessionScope.WholeBuilding, RoomId = rooms[10].Id,
                });
                if (day == DayOfWeek.Wednesday)
                    sessions.Add(new Old.ScheduleEntry
                    {
                        Title = open.Name, ClassId = open.Id, Day = day, WeekMonday = week, Start = new(17, 45), End = new(20, 0),
                        Scope = Old.SessionScope.WholeBuilding, RoomId = rooms[0].Id, FacilitatorIds = [facilitators[(w + 3) % facilitators.Count].Id],
                    });
            }
        }
    var building = new Old.Building
    {
        Name = "Northlight", Strands = [ba, fd], Groups = groups, Students = students, Facilitators = facilitators,
        Classes = [.. classes, open, lunch], Sessions = sessions, WeeksAreSeparate = true,
    };
    foreach (var r in rooms) building.Rooms.Add(r);
    return new Old.ProjectFile
    {
        SchemaVersion = Old.ProjectFile.CurrentSchemaVersion,
        Terms = terms,
        Building = building,
        Holidays = [new Old.Holiday { Name = "Autumn break", From = first.AddDays(7 * 7), To = first.AddDays(7 * 7 + 4) }],
        Absences = [new Old.Absence { PersonId = facilitators[2].Id, From = monday.AddDays(7), To = monday.AddDays(9), Reason = "Course" }],
    };
}
