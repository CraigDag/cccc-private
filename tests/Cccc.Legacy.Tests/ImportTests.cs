using System.IO.Compression;
using System.Text;
using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Old = CCCCScheduler.Models;
using OldResolver = CCCCScheduler.ViewModels.ScheduleResolver;

namespace Cccc.Legacy.Tests;

public class ImportTests
{
    private static readonly DateOnly Today = new(2030, 3, 6);        // a Wednesday
    private static readonly DateOnly ThisMonday = new(2030, 3, 4);

    private static byte[] Package(string json)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("project.json").Open(), new UTF8Encoding(false));
            w.Write(json);
        }
        return ms.ToArray();
    }

    [Fact]
    public void AFileWithNoSchemaNumberIsTheOldestFormatAndItsRoomSchedulesBecomeDatedSessions()
    {
        // The shape of the very first files: sessions inside rooms, one term, no format number.
        var json = """
        {
          "Term": { "Name": "Autumn", "StartMonday": "2030-02-18", "WeekCount": 4 },
          "Holidays": [ { "Name": "Winter break", "WeekMonday": "2030-02-25" } ],
          "Building": {
            "Rooms": [ { "Name": "Studio", "Schedule": [
              { "Day": "Monday", "Start": "09:00:00", "End": "10:30:00", "Title": "Acro" },
              { "Day": "Thursday", "Start": "13:00:00", "End": "14:00:00", "Title": "Dance" } ] } ]
          }
        }
        """;
        var (t, report) = LegacyImport.FromBytes(Package(json), Today);
        Assert.Equal(1, report.SchemaVersion);
        Assert.Single(t.Semesters);
        Assert.Single(t.Rooms);
        Assert.Equal(8, t.Sessions.Count);                 // two classes on each of four weeks
        Assert.Equal(4, report.WeeksStampedFromPattern);
        Assert.All(t.Sessions, s => Assert.Equal(t.Rooms.Single().Id, s.RoomId));
        // The holiday closes its week: still in the timetable, drawn closed.
        var closedWeek = Occurrences.ForWeek(t, new DateOnly(2030, 2, 25));
        Assert.Equal(2, closedWeek.Count);
        Assert.All(closedWeek, o => Assert.Equal(OccurrenceState.Closed, o.State));
    }

    [Fact]
    public void AFileFromANewerVersionIsRefusedInWords()
    {
        var json = $$"""{ "SchemaVersion": {{LegacyImport.NewestSchema + 1}}, "Building": {} }""";
        var e = Assert.Throws<InvalidDataException>(() => LegacyImport.FromBytes(Package(json), Today));
        Assert.Contains("newer version", e.Message);
    }

    [Fact]
    public void ASchema23FileKeepsPartsGroupsRostersAndGuests()
    {
        var json = """
        {
          "SchemaVersion": 23,
          "Terms": [ { "Name": "Spring", "StartMonday": "2030-03-04", "WeekCount": 2 } ],
          "Building": {
            "WeeksAreSeparate": true,
            "Strands": [ { "Id": "00000000-0000-0000-0000-0000000000a1", "Name": "Visitors", "IsGuest": true } ],
            "Groups": [
              { "Id": "00000000-0000-0000-0000-0000000000b1", "Name": "BA 1" },
              { "Id": "00000000-0000-0000-0000-0000000000b2", "Name": "Troupe", "StrandId": "00000000-0000-0000-0000-0000000000a1" } ],
            "Students": [
              { "Id": "00000000-0000-0000-0000-0000000000c1", "FirstName": "Alba", "Surname": "Test", "GroupId": "00000000-0000-0000-0000-0000000000b1" },
              { "Id": "00000000-0000-0000-0000-0000000000c2", "FirstName": "Benno", "Surname": "Test", "GroupId": "00000000-0000-0000-0000-0000000000b1" } ],
            "Facilitators": [
              { "Id": "00000000-0000-0000-0000-0000000000d1", "Name": "Ada", "IsGuest": false },
              { "Id": "00000000-0000-0000-0000-0000000000d2", "Name": "Bo" } ],
            "Sessions": [
              { "Id": "00000000-0000-0000-0000-0000000000e1", "Day": "Monday", "WeekMonday": "2030-03-04",
                "Start": "13:00:00", "End": "15:00:00", "Title": "Circus disciplines",
                "Scope": "Group", "GroupId": "00000000-0000-0000-0000-0000000000b1",
                "FacilitatorIds": [ "00000000-0000-0000-0000-0000000000d1", "00000000-0000-0000-0000-0000000000d2" ],
                "FacilitatorParts": [ { "FacilitatorId": "00000000-0000-0000-0000-0000000000d2", "From": "14:00:00" } ],
                "ExcludedStudentIds": [ "00000000-0000-0000-0000-0000000000c2" ],
                "Attendance": [ { "WeekMonday": "2030-03-04", "PresentStudentIds": [ "00000000-0000-0000-0000-0000000000c1" ] } ] },
              { "Id": "00000000-0000-0000-0000-0000000000e2", "Day": "Tuesday", "WeekMonday": "2030-03-04",
                "Start": "10:00:00", "End": "11:00:00", "Title": "Workshop",
                "Scope": "Group", "GroupId": "00000000-0000-0000-0000-0000000000b2" } ]
          }
        }
        """;
        var (t, _) = LegacyImport.FromBytes(Package(json), Today);
        var cd = t.Sessions[Guid.Parse("00000000-0000-0000-0000-0000000000e1")]!;
        Assert.Equal(new DateOnly(2030, 3, 4), cd.Date);
        Assert.Equal(new TimeOnly(14, 0), cd.Facilitators.Single(a => a.FacilitatorId == Guid.Parse("00000000-0000-0000-0000-0000000000d2")).From);
        Assert.Equal([Guid.Parse("00000000-0000-0000-0000-0000000000c1")], Roster.For(t, cd));
        var reg = t.Registers[cd.Id]!;
        Assert.Equal(Mark.Present, reg.MarkFor(Guid.Parse("00000000-0000-0000-0000-0000000000c1")));
        Assert.True(Guests.IsGuestOnly(t, t.Sessions[Guid.Parse("00000000-0000-0000-0000-0000000000e2")]!));
        Assert.Equal(TimeSpan.FromHours(1), Hours.Teaching(Occurrences.HappeningInWeek(t, cd.Date), Guid.Parse("00000000-0000-0000-0000-0000000000d2")));
    }

    /// <summary>
    /// The round trip that matters: a project in the old shape — a standing pattern, a frozen week,
    /// a cancellation, a reschedule, a cover, absences, closures and registers — must run the same
    /// classes at the same times in the same states, week by week, in the new model as in the old.
    /// </summary>
    [Fact]
    public void EveryWeekRunsTheSameInTheNewModelAsInTheOld()
    {
        var p = OldProject();
        var (t, report) = LegacyImport.Translate(p, Today);
        Assert.True(report.PatternSessions > 0);
        int weeksCompared = 0;
        foreach (var monday in Enumerable.Range(-5, 12).Select(i => ThisMonday.AddDays(7 * i)))
        {
            // The old pattern ran in every week there has ever been; here it is laid onto the year's
            // semesters, so a week outside every semester is blank (docs/spec/06).
            if (t.SemesterFor(monday) is null)
            {
                Assert.Empty(Occurrences.ForWeek(t, monday));
                continue;
            }
            var old = OldResolver.SessionsForWeekDetailed(p.Building, p.Holidays, p.Absences, monday, includeClosedByHoliday: true)
                .Select(r => (r.Entry.Title, Date: Week.DateOf(monday, r.Day), r.Start, r.End,
                              State: r.Cancelled ? (r.Note == "Closed" || p.Holidays.Any(h => h.Name == r.Note) ? "closed" : "cancelled") : r.NeedsCover ? "cover" : "runs",
                              Covered: r.Covered))
                .OrderBy(x => x.Date).ThenBy(x => x.Start).ThenBy(x => x.Title).ToList();
            var now = Occurrences.ForWeek(t, monday)
                .Select(o => (o.Session.Title, o.Date, o.Start, o.End,
                              State: o.State switch { OccurrenceState.Closed => "closed", OccurrenceState.Cancelled => "cancelled", OccurrenceState.NeedsCover => "cover", _ => "runs" },
                              o.Covered))
                .OrderBy(x => x.Date).ThenBy(x => x.Start).ThenBy(x => x.Title).ToList();
            Assert.Equal(old, now);
            if (old.Count > 0) weeksCompared++;
        }
        Assert.True(weeksCompared >= 6, $"Only {weeksCompared} weeks had anything in them to compare.");

        // And the registers came across onto the right weeks' sessions.
        Assert.Equal(p.Building.Sessions.Sum(s => s.Attendance.Count), t.Registers.Count);
    }

    /// <summary>
    /// Following the current app reads its file again after every save there, so one file must come in as
    /// one timetable every time, ids and all, whatever the date: an id made up afresh on each read would
    /// make every save look like the whole timetable deleted and added again.
    /// </summary>
    [Fact]
    public void TheSameFileComesInAsTheSameTimetableEveryTime()
    {
        var p = OldProject();
        var b = p.Building;
        var branch = new Old.Branch { Name = "Aerial" };
        b.Branches.Add(branch);
        b.Disciplines.Add(new Old.Discipline { Name = "Trapeze", BranchId = branch.Id });
        var area = new Old.Area { Name = "Physical" };
        b.Areas.Add(area);
        var acro = new Old.ClassEntry { Name = "Acro", AreaId = area.Id };
        b.Classes.Add(acro);
        b.TimeRules.Add(new Old.TimeRule { Name = "Preparation", ClassIds = [acro.Id] });
        b.Strands.Add(new Old.Strand { Name = "BA" });
        p.Events.Add(new Old.SchoolEvent { Title = "Show", Date = ThisMonday.AddDays(3) });
        var bytes = CCCCScheduler.Services.ProjectPackage.SaveToBytes(p, savedBy: "test");

        var (t, _) = LegacyImport.FromBytes(bytes, Today);
        var again = LegacyImport.FromBytes(bytes, Today.AddDays(9)).Timetable;
        Assert.Equal(ModelJson.Serialise(t), ModelJson.Serialise(again));
        // Every kind came across, so the comparison above was about each of them.
        Assert.All(new (string Kind, int Count)[]
        {
            ("sessions", t.Sessions.Count), ("registers", t.Registers.Count), ("semesters", t.Semesters.Count),
            ("closures", t.Closures.Count), ("absences", t.Absences.Count), ("events", t.Events.Count),
            ("classes", t.Classes.Count), ("time rules", t.TimeRules.Count), ("areas", t.Areas.Count),
            ("disciplines", t.Disciplines.Count), ("branches", t.Branches.Count), ("programmes", t.Programmes.Count),
            ("groups", t.Groups.Count), ("students", t.Students.Count), ("facilitators", t.Facilitators.Count), ("rooms", t.Rooms.Count),
        }, k => Assert.True(k.Count > 0, $"No {k.Kind} came across."));
    }

    private static Old.ProjectFile OldProject()
    {
        var group = new Old.Group { Name = "BA 1" };
        var alba = new Old.Student { FirstName = "Alba", Surname = "Test", GroupId = group.Id };
        var ada = new Old.Facilitator { Name = "Ada" };
        var bo = new Old.Facilitator { Name = "Bo" };
        var room = new Old.Room { Name = "Studio" };
        var acro = new Old.ScheduleEntry { Title = "Acro", Day = DayOfWeek.Monday, Start = new(9, 0), End = new(11, 0), Scope = Old.SessionScope.Group, GroupId = group.Id, RoomId = room.Id, FacilitatorIds = [ada.Id] };
        var dance = new Old.ScheduleEntry { Title = "Dance", Day = DayOfWeek.Wednesday, Start = new(13, 0), End = new(14, 0), Scope = Old.SessionScope.Group, GroupId = group.Id, FacilitatorIds = [bo.Id] };
        var lunch = new Old.ScheduleEntry { Title = "Lunch", Day = DayOfWeek.Tuesday, Start = new(12, 0), End = new(12, 30), Scope = Old.SessionScope.WholeBuilding };
        // One-off changes on the pattern, in weeks still ahead and just behind.
        acro.Exceptions.Add(new Old.SessionException { WeekMonday = ThisMonday.AddDays(7), Kind = Old.ExceptionKind.Cancelled, Note = "Show week" });
        dance.Exceptions.Add(new Old.SessionException { WeekMonday = ThisMonday.AddDays(14), Kind = Old.ExceptionKind.Rescheduled, NewDay = DayOfWeek.Friday, NewStart = new(10, 0), NewEnd = new(11, 30) });
        acro.Exceptions.Add(new Old.SessionException { WeekMonday = ThisMonday.AddDays(21), Kind = Old.ExceptionKind.Covered, CoverFacilitatorIds = [bo.Id] });
        dance.Attendance.Add(new Old.AttendanceRecord { WeekMonday = ThisMonday.AddDays(-7), PresentStudentIds = [alba.Id] });

        var building = new Old.Building
        {
            Groups = [group], Students = [alba], Facilitators = [ada, bo], Sessions = [acro, dance, lunch],
        };
        building.Rooms.Add(room);
        // A frozen week two weeks back, its own copies a little different from the pattern.
        var frozen = ThisMonday.AddDays(-14);
        building.FrozenWeeks.Add(frozen);
        foreach (var e in new[] { acro, dance, lunch })
        {
            var copy = new Old.ScheduleEntry
            {
                Id = CCCCScheduler.ViewModels.RegisterFiling.FrozenCopyId(e.Id, frozen), Title = e.Title, Day = e.Day, Start = e.Start,
                End = e == dance ? new TimeOnly(15, 0) : e.End, Scope = e.Scope, GroupId = e.GroupId, RoomId = e.RoomId,
                FacilitatorIds = [.. e.FacilitatorIds], WeekMonday = frozen,
            };
            if (e == acro) copy.Attendance.Add(new Old.AttendanceRecord { WeekMonday = frozen, PresentStudentIds = [alba.Id] });
            building.Sessions.Add(copy);
        }
        return new Old.ProjectFile
        {
            SchemaVersion = Old.ProjectFile.CurrentSchemaVersion,
            Terms = [new Old.TermInfo { Name = "Spring", StartMonday = ThisMonday.AddDays(-21), WeekCount = 8 }],
            Holidays = [new Old.Holiday { Name = "Easter", From = ThisMonday.AddDays(28), To = ThisMonday.AddDays(34) }],
            Absences = [new Old.Absence { Person = Old.AbsentPerson.Facilitator, PersonId = bo.Id, From = ThisMonday.AddDays(2), To = ThisMonday.AddDays(2) }],
            Building = building,
        };
    }
}
