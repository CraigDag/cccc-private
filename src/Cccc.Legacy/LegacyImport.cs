using System.Collections.Immutable;
using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Old = CCCCScheduler.Models;

namespace Cccc.Legacy;

/// <summary>What an import brought across, by count — never by name.</summary>
public sealed record ImportReport(
    int SchemaVersion,
    int Rooms, int Programmes, int Groups, int Students, int Facilitators, int Classes,
    int Sessions, int PatternSessions, int WeeksStampedFromPattern, int Registers, int Absences, int Events,
    int CancelledOrMoved, int Covered, int RecordsNotPlaced, int SessionColoursDropped, int FloorPlansDropped)
{
    private static string N(int n, string one, string? many = null) => $"{n} {(n == 1 ? one : many ?? one + "s")}";

    public IEnumerable<string> Lines()
    {
        yield return $"Read a file of format {SchemaVersion}.";
        yield return $"{N(Rooms, "room")}, {N(Programmes, "programme")}, {N(Groups, "group")}, {N(Students, "student")}, {N(Facilitators, "facilitator")} and {N(Classes, "class", "classes")}.";
        yield return PatternSessions > 0
            ? $"{Sessions} sessions in all, including {PatternSessions} weekly classes from the old standing pattern, laid onto each of {WeeksStampedFromPattern} weeks as the old app drew them."
            : $"{Sessions} sessions.";
        yield return $"{N(Registers, "register")}, {N(Absences, "absence")} and {N(Events, "event")}.";
        if (CancelledOrMoved + Covered > 0) yield return $"{CancelledOrMoved} one-off cancellations or moves and {Covered} covers, each on its own week.";
        if (RecordsNotPlaced > 0) yield return $"{RecordsNotPlaced} one-off changes or registers belonged to a week where their class no longer runs, and were not brought across.";
        if (SessionColoursDropped > 0) yield return $"{SessionColoursDropped} sessions had a colour of their own; a block is now always coloured by its class.";
        if (FloorPlansDropped > 0) yield return $"{FloorPlansDropped} floor plans were left behind: the floor plan is not part of the app.";
    }
}

/// <summary>
/// Reads any old <c>.cccc</c> (schema 1 to 23) with the old app's own loader and migrations, then
/// translates the migrated project into the new model. See docs/spec/06-importing-old-files.md.
/// </summary>
public static class LegacyImport
{
    /// <summary>The newest old format this build reads.</summary>
    public static int NewestSchema => Old.ProjectFile.CurrentSchemaVersion;

    public static (Timetable Timetable, ImportReport Report) FromBytes(byte[] bytes, DateOnly today)
    {
        var schema = PeekSchema(bytes);
        var old = CCCCScheduler.Services.ProjectPackage.LoadFromBytes(bytes);
        return Translate(old, today, schema);
    }

    /// <summary>The file's own format number, read before the loader stamps the current one over it.</summary>
    private static int PeekSchema(byte[] bytes)
    {
        try
        {
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
            var entry = zip.Entries.FirstOrDefault(e => e.FullName.EndsWith("project.json", StringComparison.OrdinalIgnoreCase));
            if (entry is null) return 0;
            using var doc = System.Text.Json.JsonDocument.Parse(entry.Open());
            return doc.RootElement.TryGetProperty("SchemaVersion", out var v) && v.TryGetInt32(out var n) ? n : Old.ProjectFile.OldestSchemaVersion;
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException)
        {
            return 0;
        }
    }

    public static (Timetable Timetable, ImportReport Report) Translate(Old.ProjectFile p, DateOnly today, int schema = 0)
    {
        var b = p.Building;
        int termIndex = 0;
        var semesters = p.Terms.Select(t => new Semester
        {
            Id = Edits.CopyId(Guid.Parse("7e2a0000-0000-4000-8000-000000000000"), DateOnly.FromDayNumber(termIndex++)),
            Name = t.Name,
            StartMonday = Week.MondayOf(t.StartMonday),
            WeekCount = Math.Clamp(t.WeekCount, 0, Semester.MaxWeekCount),
        }).ToList();

        var t = new Timetable
        {
            Settings = new Settings { SchoolName = string.IsNullOrWhiteSpace(b.Name) ? "CCCC" : b.Name, WeeklyScheduledTarget = b.WeeklyScheduledTarget },
            Semesters = EntitySet<Semester>.Of(semesters),
            Closures = EntitySet<Closure>.Of(p.Holidays.Select(h => new Closure
            {
                Id = h.Id, Name = h.Name, From = h.From, To = h.To < h.From ? h.From : h.To,
                Scope = h.Scope == Old.HolidayScope.Groups ? ClosureScope.Groups : ClosureScope.WholeBuilding,
                GroupIds = [.. h.GroupIds.Distinct()],
            })),
            Rooms = EntitySet<Room>.Of(b.Rooms.Select(r => new Room { Id = r.Id, Name = r.Name, Category = r.Category, Colour = r.ColourHex })),
            Programmes = EntitySet<Programme>.Of(b.Strands.Select(s => new Programme { Id = s.Id, Name = s.Name, Colour = s.ColourHex, IsGuest = s.IsGuest })),
            Groups = EntitySet<Group>.Of(b.Groups.Select(g => new Group { Id = g.Id, Name = g.Name, Colour = g.ColourHex, ProgrammeId = g.StrandId })),
            Branches = EntitySet<Branch>.Of(b.Branches.Select(x => new Branch { Id = x.Id, Name = x.Name, Colour = x.ColourHex })),
            Disciplines = EntitySet<Discipline>.Of(b.Disciplines.Select(d => new Discipline { Id = d.Id, Name = d.Name, Colour = d.ColourHex, BranchId = d.BranchId })),
            Students = EntitySet<Student>.Of(b.Students.Select(s => new Student
            {
                Id = s.Id, FirstName = s.FirstName, Surname = s.Surname, Email = s.ContactEmail, GroupId = s.GroupId, DisciplineId = s.DisciplineId,
            })),
            Facilitators = EntitySet<Facilitator>.Of(b.Facilitators.Select(f => new Facilitator
            {
                Id = f.Id, Name = f.Name, Colour = f.ColourHex, Email = f.ContactEmail, IsGuest = f.IsGuest,
                Availability = [.. f.Availability.Select(a => new AvailabilityWindow(a.Day, a.Start, a.End))],
                MaxWeeklyHours = f.MaxWeeklyHours, MaxGap = f.MaxGapBetweenSessions,
            })),
            Areas = EntitySet<Area>.Of(b.Areas.Select(a => new Area { Id = a.Id, Name = a.Name, WeeklyTarget = a.WeeklyTarget })),
            Classes = EntitySet<Class>.Of(b.Classes.Select(c => new Class
            {
                Id = c.Id, Name = c.Name, Colour = c.ColourHex, AreaId = c.AreaId,
                Workload = c.Workload switch { Old.WorkloadKind.DropIn => Workload.DropIn, Old.WorkloadKind.NotCounted => Workload.NotCounted, _ => Workload.Counted },
            })),
            TimeRules = EntitySet<TimeRule>.Of(b.TimeRules.Select(r => new TimeRule
            {
                Id = r.Id, Name = r.Name, Kind = r.Kind == Old.TimeRuleKind.Break ? TimeRuleKind.Break : TimeRuleKind.Preparation,
                ClassIds = [.. r.ClassIds.Distinct()], Percent = r.Percent, Minutes = r.Minutes, Days = [.. r.Days.Distinct()],
                Condition = r.Condition == Old.TimeRuleCondition.TeachesThatDay ? TimeRuleCondition.TeachesThatDay : TimeRuleCondition.TeachesBeforeAndAfter,
            })),
            Absences = EntitySet<Absence>.Of(p.Absences.Select(a => new Absence
            {
                Id = a.Id, Person = a.Person == Old.AbsentPerson.Student ? AbsentPerson.Student : AbsentPerson.Facilitator, PersonId = a.PersonId,
                From = a.From, To = a.To < a.From ? a.From : a.To, Reason = a.Reason,
                Notice = a.Notice is { } n ? (AbsenceNotice)(int)n : null, Cause = a.Cause is { } c ? (AbsenceCause)(int)c : null,
                StartTime = a.StartTime, EndTime = a.EndTime,
            })),
            Events = EntitySet<SchoolEvent>.Of(p.Events.Select(e => new SchoolEvent
            {
                Id = e.Id, Title = e.Title, Category = (EventCategory)(int)e.Category, Date = e.Date, EndDate = e.EndDate, AllDay = e.AllDay,
                Start = e.Start, End = e.End,
                Repeat = e.Repeat is { } r ? new Repeat { Kind = (RepeatKind)(int)r.Kind, Every = r.Every, Days = [.. r.Days], Until = r.Until, Count = r.Count } : null,
                Skipped = [.. e.Skipped], SkipClosures = e.SkipClosures, Audience = (EventAudience)(int)e.Audience, RoomId = e.RoomId,
                Place = e.Place, FacilitatorIds = [.. e.FacilitatorIds], StudentIds = [.. e.StudentIds], Note = e.Note,
            })),
        };

        // Which weeks the standing pattern lands on: every semester week that is not frozen, plus any
        // week a pattern session holds a register or a one-off change for — exactly the weeks the old
        // app draws the pattern on, so nothing the school sees changes.
        var frozen = b.FrozenWeeks.Select(Week.MondayOf).ToHashSet();
        var pattern = b.WeeksAreSeparate ? [] : b.Sessions.Where(s => s.WeekMonday is null).ToList();
        var patternWeeks = new SortedSet<DateOnly>();
        if (pattern.Count > 0)
        {
            foreach (var s in semesters) foreach (var m in s.Mondays()) patternWeeks.Add(m);
            foreach (var s in pattern)
            {
                foreach (var a in s.Attendance) patternWeeks.Add(Week.MondayOf(a.WeekMonday));
                foreach (var x in s.Exceptions) patternWeeks.Add(Week.MondayOf(x.WeekMonday));
            }
            patternWeeks.RemoveWhere(frozen.Contains);
        }

        var sessions = new List<Session>();
        var registers = new List<Register>();
        var used = new HashSet<Guid>();
        int moved = 0, covered = 0, notPlaced = 0, colours = 0;

        void Place(Old.ScheduleEntry e, DateOnly monday, Guid id)
        {
            if (!used.Add(id)) id = Edits.CopyId(id, monday.AddDays(7 * 1000));
            var s = Translate(e, monday, id, t);
            var over = CCCCScheduler.ViewModels.ScheduleResolver.ExceptionFor(e, monday);
            if (over is not null)
            {
                switch (over.Kind)
                {
                    case Old.ExceptionKind.Cancelled:
                        s = s with { Cancelled = true, CancelNote = over.Note };
                        moved++;
                        break;
                    case Old.ExceptionKind.Covered:
                        s = s with { CoverIds = [.. (over.CoverFacilitatorIds ?? []).Distinct()], CoverNote = over.Note };
                        if (s.CoverIds.IsEmpty) s = s with { CoverNote = null };
                        else covered++;
                        break;
                    case Old.ExceptionKind.Rescheduled:
                        var day = over.NewDay ?? e.Day;
                        var start = over.NewStart ?? e.Start;
                        var end = over.NewEnd ?? e.End;
                        if (over.NewStart is null || over.NewEnd is null) { start = e.Start; end = e.End; }
                        s = Domain.Rules.Windows.Shift(s, start - e.Start) with
                        {
                            Date = Week.DateOf(monday, day), Start = start, End = end,
                            MovedFrom = new MovedFrom(s.Date, s.Start, s.End),
                            Notes = string.IsNullOrWhiteSpace(over.Note) ? s.Notes : string.IsNullOrWhiteSpace(s.Notes) ? over.Note : s.Notes + "\n" + over.Note,
                        };
                        moved++;
                        break;
                }
            }
            if (!string.IsNullOrWhiteSpace(e.ColourHex)) colours++;
            sessions.Add(s);
            var record = e.Attendance.FirstOrDefault(a => Week.MondayOf(a.WeekMonday) == monday);
            if (record is not null)
            {
                var roster = record.RosterStudentIds ?? [.. Domain.Rules.Roster.For(t, s)];
                registers.Add(new Register
                {
                    Id = s.Id, RosterIds = [.. roster], PresentIds = [.. record.PresentStudentIds ?? []],
                    ExcusedIds = [.. record.ExcusedStudentIds ?? []], FiledBy = record.FiledBy, FiledAt = record.FiledAt,
                }.Normalised());
            }
        }

        foreach (var e in b.Sessions.Where(s => s.WeekMonday is not null))
        {
            var monday = Week.MondayOf(e.WeekMonday!.Value);
            Place(e, monday, e.Id);
            notPlaced += e.Attendance.Count(a => Week.MondayOf(a.WeekMonday) != monday)
                         + e.Exceptions.Count(x => Week.MondayOf(x.WeekMonday) != monday);
        }
        foreach (var e in pattern)
        {
            foreach (var monday in patternWeeks) Place(e, monday, Edits.CopyId(e.Id, monday));
            notPlaced += e.Attendance.Count(a => frozen.Contains(Week.MondayOf(a.WeekMonday)))
                         + e.Exceptions.Count(x => frozen.Contains(Week.MondayOf(x.WeekMonday)));
        }
        if (b.WeeksAreSeparate) notPlaced += b.Sessions.Count(s => s.WeekMonday is null);

        t = t with { Sessions = EntitySet<Session>.Of(sessions), Registers = EntitySet<Register>.Of(registers) };
        var report = new ImportReport(
            schema == 0 ? p.SchemaVersion : schema,
            t.Rooms.Count, t.Programmes.Count, t.Groups.Count, t.Students.Count, t.Facilitators.Count, t.Classes.Count,
            t.Sessions.Count, pattern.Count, pattern.Count > 0 ? patternWeeks.Count : 0, t.Registers.Count, t.Absences.Count, t.Events.Count,
            moved, covered, notPlaced, colours, b.Floors.Count);
        return (t, report);
    }

    private static Session Translate(Old.ScheduleEntry e, DateOnly monday, Guid id, Timetable t)
    {
        var audience = e.Scope switch
        {
            Old.SessionScope.WholeBuilding => Audience.Everyone,
            Old.SessionScope.Groups => new Audience { Kind = AudienceKind.Groups, GroupIds = [.. e.GroupIds.Distinct()] },
            Old.SessionScope.Programme => new Audience { Kind = AudienceKind.Programme, ProgrammeId = e.ProgrammeId, GroupIds = [.. e.GroupIds.Distinct()] },
            _ => e.GroupId is { } g ? Audience.ForGroup(g) : Audience.Everyone,
        };
        var parts = e.FacilitatorParts.ToDictionary(x => x.FacilitatorId, x => x);
        var s = new Session
        {
            Id = id,
            Date = Week.DateOf(monday, e.Day),
            Start = e.Start,
            End = e.End > e.Start ? e.End : e.Start.AddMinutes(15),
            Title = e.Title,
            ClassId = e.ClassId,
            Audience = audience,
            RoomId = e.RoomId is { } r && t.Rooms.Contains(r) ? r : null,
            Facilitators = [.. e.FacilitatorIds.Distinct().Select(f => parts.TryGetValue(f, out var p) ? new Assignment(f, p.From, p.Until) : new Assignment(f))],
            AddedStudentIds = [.. e.StudentIds.Distinct()],
            LeftOffStudentIds = [.. e.ExcludedStudentIds.Distinct()],
            Notes = e.Notes,
            Provisional = e.Provisional,
        };
        return Domain.Rules.Windows.Normalise(s);
    }
}
