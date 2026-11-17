using System.Collections.Immutable;
using Cccc.Domain.Model;

namespace Cccc.Domain.Sample;

/// <summary>
/// A fictional circus school with fictional people, for tests, previews and screenshots. Every name in
/// it is made up. Dates are built around an anchor Monday so a preview always has a current week,
/// while a test passes a fixed anchor and a fixed today and never depends on the calendar.
/// </summary>
public static class SampleSchool
{
    public static Timetable Create(DateOnly anchorAnyDate, DateOnly today)
    {
        var anchor = Week.MondayOf(anchorAnyDate);
        var g = new Ids();

        var autumn = new Semester { Id = g.Next(), Name = "Autumn semester", StartMonday = anchor.AddDays(-7 * 4), WeekCount = 18 };
        var spring = new Semester { Id = g.Next(), Name = "Spring semester", StartMonday = autumn.LastMonday.AddDays(7 * 3), WeekCount = 20 };

        var degree = new Programme { Id = g.Next(), Name = "Degree", Colour = "#4F8CF0" };
        var prep = new Programme { Id = g.Next(), Name = "Prep", Colour = "#4CB782" };
        var visiting = new Programme { Id = g.Next(), Name = "Visiting company", Colour = "#B07CE8", IsGuest = true };

        var ba1 = new Group { Id = g.Next(), Name = "BA 1", Colour = "#4F8CF0", ProgrammeId = degree.Id };
        var ba2 = new Group { Id = g.Next(), Name = "BA 2", Colour = "#3A6FCC", ProgrammeId = degree.Id };
        var prepGroup = new Group { Id = g.Next(), Name = "Prep", Colour = "#4CB782", ProgrammeId = prep.Id };
        var guests = new Group { Id = g.Next(), Name = "Nordlys Troupe", Colour = "#B07CE8", ProgrammeId = visiting.Id };

        var bigTop = new Room { Id = g.Next(), Name = "Big Top", Colour = "#E0A040", Category = "Hall" };
        var studioA = new Room { Id = g.Next(), Name = "Studio A", Colour = "#5FB0C8", Category = "Studio" };
        var studioB = new Room { Id = g.Next(), Name = "Studio B", Colour = "#6FC8A0", Category = "Studio" };
        var aerial = new Room { Id = g.Next(), Name = "Aerial Hall", Colour = "#C88FE0", Category = "Hall" };
        var gym = new Room { Id = g.Next(), Name = "Gym", Colour = "#E08080", Category = "Gym" };
        var canteen = new Room { Id = g.Next(), Name = "Canteen", Colour = "#A0A0A0" };

        var tumbling = new Branch { Id = g.Next(), Name = "Acrobatics", Colour = "#E0A040" };
        var air = new Branch { Id = g.Next(), Name = "Aerial", Colour = "#C88FE0" };
        var objects = new Branch { Id = g.Next(), Name = "Object manipulation", Colour = "#5FB0C8" };
        var disciplines = new[]
        {
            new Discipline { Id = g.Next(), Name = "Hand to hand", BranchId = tumbling.Id, Colour = "#E0A040" },
            new Discipline { Id = g.Next(), Name = "Trapeze", BranchId = air.Id, Colour = "#C88FE0" },
            new Discipline { Id = g.Next(), Name = "Aerial straps", BranchId = air.Id, Colour = "#B070D0" },
            new Discipline { Id = g.Next(), Name = "Juggling", BranchId = objects.Id, Colour = "#5FB0C8" },
            new Discipline { Id = g.Next(), Name = "Cyr wheel", BranchId = objects.Id, Colour = "#40A0B0" },
        };

        Facilitator F(string name, string colour, double? max = null, bool guest = false) =>
            new() { Id = g.Next(), Name = name, Colour = colour, Email = Slug(name) + "@example.invalid", IsGuest = guest,
                    MaxWeeklyHours = max is { } m ? TimeSpan.FromHours(m) : null };
        var ada = F("Ada Brightwater", "#F0A050", 20);
        var bo = F("Bo Lindqvist", "#50A0F0", 22);
        var cleo = F("Cleo Marchetti", "#A070F0", 18);
        var dag = F("Dag Øverlie", "#50C890", 20);
        var esme = F("Esme Halloran", "#F07090", 16) with { MaxGap = TimeSpan.FromHours(3) };
        var finn = F("Finn Aalborg-Reyes", "#C0C050");
        var guest = F("Gwen Stroud", "#A0A0C0", guest: true);

        var areaTechnique = new Area { Id = g.Next(), Name = "Technique", WeeklyTarget = TimeSpan.FromHours(10) };
        var areaCreation = new Area { Id = g.Next(), Name = "Creation", WeeklyTarget = TimeSpan.FromHours(4) };
        var areaBody = new Area { Id = g.Next(), Name = "Body and movement", WeeklyTarget = TimeSpan.FromHours(6) };

        Class C(string name, string colour, Area? area = null, Workload w = Workload.Counted) =>
            new() { Id = g.Next(), Name = name, Colour = colour, AreaId = area?.Id, Workload = w };
        var acro = C("Acrobatics", "#E0A040", areaTechnique);
        var aerialClass = C("Aerial", "#C88FE0", areaTechnique);
        var juggling = C("Juggling", "#5FB0C8", areaTechnique);
        var dance = C("Contemporary dance", "#F07090", areaBody);
        var strength = C("Strength and conditioning", "#E08080", areaBody);
        var creation = C("Creation lab", "#70C8A0", areaCreation);
        var disciplineClass = C("Circus disciplines", "#D0B060", areaTechnique);
        var open = C("Open training", "#7090A0", null, Workload.DropIn);
        var lunch = C("Lunch", "#909090", null, Workload.NotCounted);
        var guestWorkshop = C("Guest workshop", "#B07CE8");

        var prepRule = new TimeRule { Id = g.Next(), Name = "Prep time", Kind = TimeRuleKind.Preparation, Percent = 50, ClassIds = [creation.Id] };
        var lunchRule = new TimeRule { Id = g.Next(), Name = "Paid lunch", Kind = TimeRuleKind.Break, Minutes = 30, ClassIds = [lunch.Id],
                                       Condition = TimeRuleCondition.TeachesBeforeAndAfter };

        string[] firsts = ["Alba", "Benno", "Carys", "Dmitri", "Elin", "Fenna", "Gaspard", "Hedda", "Ines", "Jory", "Kaia", "Lior",
                           "Maren", "Nils", "Orla", "Piet", "Quinn", "Runa", "Soren", "Tilde", "Ulla", "Vito", "Wren", "Ylva", "Åse", "Ægir"];
        string[] lasts = ["Ashcombe", "Birkeland", "Corvino", "Dunmore", "Eskildsen", "Falk", "Grummet", "Hjort", "Isaksen", "Jepsen",
                          "Kestrel", "Lund", "Moberg", "Nygaard", "Ostrander", "Pell", "Quist", "Rask", "Strand", "Tolver", "Ulven",
                          "Vesterby", "Wolde", "Yarrow", "Aagaard", "Ødegaard"];
        var students = new List<Student>();
        var cohorts = new[] { ba1, ba1, ba1, ba1, ba1, ba1, ba1, ba1, ba2, ba2, ba2, ba2, ba2, ba2, ba2, ba2, prepGroup, prepGroup, prepGroup, prepGroup, prepGroup, prepGroup, guests, guests, guests, guests };
        for (int i = 0; i < firsts.Length; i++)
            students.Add(new Student
            {
                Id = g.Next(), FirstName = firsts[i], Surname = lasts[i], Email = Slug(firsts[i] + " " + lasts[i]) + "@example.invalid",
                GroupId = cohorts[i].Id, DisciplineId = cohorts[i] == guests ? null : disciplines[i % disciplines.Length].Id,
            });

        var sessions = new List<Session>();
        Session S(DateOnly date, int sh, int sm, int eh, int em, Class c, Audience aud, Room room, params Assignment[] who) =>
            new()
            {
                Id = g.Next(), Date = date, Start = new TimeOnly(sh, sm), End = new TimeOnly(eh, em), Title = c.Name,
                ClassId = c.Id, Audience = aud, RoomId = room.Id, Facilitators = [.. who],
            };
        Assignment A(Facilitator f, TimeOnly? from = null, TimeOnly? until = null) => new(f.Id, from, until);
        var everyone = Audience.Everyone;
        var oneBa1 = Audience.ForGroup(ba1.Id);
        var oneBa2 = Audience.ForGroup(ba2.Id);
        var onePrep = Audience.ForGroup(prepGroup.Id);
        var bothBa = new Audience { Kind = AudienceKind.Programme, ProgrammeId = degree.Id, GroupIds = [ba1.Id, ba2.Id] };

        // Four built weeks: two behind, this one, and the next. Every later week is blank, as the school's rule says.
        for (int w = -2; w <= 1; w++)
        {
            var m = anchor.AddDays(7 * w);
            var mon = m; var tue = m.AddDays(1); var wed = m.AddDays(2); var thu = m.AddDays(3); var fri = m.AddDays(4);
            foreach (var day in new[] { mon, tue, wed, thu, fri })
                sessions.Add(S(day, 12, 0, 12, 45, lunch, everyone, canteen));
            sessions.Add(S(mon, 9, 0, 10, 30, strength, bothBa, gym, A(esme)));
            sessions.Add(S(mon, 10, 45, 12, 0, acro, oneBa1, bigTop, A(ada), A(bo)));
            sessions.Add(S(mon, 10, 45, 12, 0, juggling, oneBa2, studioA, A(cleo)));
            sessions.Add(S(mon, 13, 0, 15, 0, disciplineClass, bothBa, bigTop, A(ada), A(dag, from: new TimeOnly(14, 0))));
            sessions.Add(S(mon, 9, 30, 11, 30, dance, onePrep, studioB, A(finn)));
            sessions.Add(S(mon, 13, 0, 14, 30, acro, onePrep, studioB, A(bo)));
            sessions.Add(S(tue, 9, 0, 11, 0, aerialClass, oneBa1, aerial, A(cleo)));
            sessions.Add(S(tue, 9, 0, 11, 0, dance, oneBa2, studioA, A(finn)));
            sessions.Add(S(tue, 13, 0, 16, 0, creation, oneBa1, studioB, A(esme)));
            sessions.Add(S(tue, 13, 0, 15, 0, strength, onePrep, gym, A(dag)));
            sessions.Add(S(wed, 9, 0, 10, 30, strength, bothBa, gym, A(esme)));
            sessions.Add(S(wed, 10, 45, 12, 0, juggling, oneBa1, studioA, A(cleo)));
            sessions.Add(S(wed, 10, 45, 12, 0, aerialClass, oneBa2, aerial, A(dag)));
            sessions.Add(S(wed, 13, 0, 17, 0, open, everyone, bigTop, A(bo)));
            sessions.Add(S(thu, 9, 0, 11, 0, acro, oneBa2, bigTop, A(ada)));
            sessions.Add(S(thu, 9, 0, 12, 0, creation, onePrep, studioB, A(finn)));
            sessions.Add(S(thu, 13, 0, 16, 0, creation, oneBa2, studioA, A(esme), A(cleo)));
            sessions.Add(S(fri, 9, 0, 11, 0, dance, bothBa, studioA, A(finn)));
            sessions.Add(S(fri, 11, 0, 12, 0, juggling, onePrep, studioB, A(cleo)));
            sessions.Add(S(fri, 13, 0, 14, 30, aerialClass, oneBa1, aerial, A(dag)));
            if (w == 0)
            {
                sessions.Add(S(thu, 10, 0, 12, 0, guestWorkshop, Audience.ForGroup(guests.Id), aerial, A(guest)));
                sessions.Add(S(fri, 9, 30, 11, 30, guestWorkshop, Audience.ForGroup(guests.Id), bigTop, A(guest), A(bo)));
            }
        }

        // Ada is away on this week's Thursday, so her class needs cover. Bo is away one morning last week, covered by Dag.
        var absences = new List<Absence>
        {
            new() { Id = g.Next(), Person = AbsentPerson.Facilitator, PersonId = ada.Id, From = anchor.AddDays(3), To = anchor.AddDays(3),
                    Notice = AbsenceNotice.Planned, Cause = AbsenceCause.ApprovedLeave },
            new() { Id = g.Next(), Person = AbsentPerson.Facilitator, PersonId = bo.Id, From = anchor.AddDays(-7), To = anchor.AddDays(-7),
                    StartTime = new TimeOnly(8, 0), EndTime = new TimeOnly(12, 0), Notice = AbsenceNotice.Unplanned, Cause = AbsenceCause.Illness },
            new() { Id = g.Next(), Person = AbsentPerson.Student, PersonId = students[2].Id, From = anchor.AddDays(-6), To = anchor.AddDays(-5),
                    Notice = AbsenceNotice.Unplanned, Cause = AbsenceCause.Illness },
        };
        var coveredMonday = sessions.First(s => s.Date == anchor.AddDays(-7) && s.ClassId == acro.Id && s.Audience.Kind == AudienceKind.Group && s.Audience.GroupIds[0] == ba1.Id);
        sessions[sessions.IndexOf(coveredMonday)] = coveredMonday with { CoverIds = [dag.Id], CoverNote = "Dag took the morning" };

        // Registers for most of what has run, leaving a few missing on purpose.
        var t = new Timetable
        {
            Settings = new Settings { Name = "Sample school year", SchoolName = "Northlight Circus School", WeeklyScheduledTarget = TimeSpan.FromHours(15) },
            Semesters = EntitySet<Semester>.Of([autumn, spring]),
            Closures = EntitySet<Closure>.Of(
            [
                new Closure { Id = g.Next(), Name = "Autumn break", From = autumn.FirstMonday.AddDays(7 * 7), To = autumn.FirstMonday.AddDays(7 * 7 + 6) },
                new Closure { Id = g.Next(), Name = "Prep study day", From = anchor.AddDays(4), To = anchor.AddDays(4), Scope = ClosureScope.Groups, GroupIds = [prepGroup.Id] },
            ]),
            Rooms = EntitySet<Room>.Of([bigTop, studioA, studioB, aerial, gym, canteen]),
            Programmes = EntitySet<Programme>.Of([degree, prep, visiting]),
            Groups = EntitySet<Group>.Of([ba1, ba2, prepGroup, guests]),
            Branches = EntitySet<Branch>.Of([tumbling, air, objects]),
            Disciplines = EntitySet<Discipline>.Of(disciplines),
            Students = EntitySet<Student>.Of(students),
            Facilitators = EntitySet<Facilitator>.Of([ada, bo, cleo, dag, esme, finn, guest]),
            Areas = EntitySet<Area>.Of([areaTechnique, areaCreation, areaBody]),
            Classes = EntitySet<Class>.Of([acro, aerialClass, juggling, dance, strength, creation, disciplineClass, open, lunch, guestWorkshop]),
            TimeRules = EntitySet<TimeRule>.Of([prepRule, lunchRule]),
            Sessions = EntitySet<Session>.Of(sessions),
            Absences = EntitySet<Absence>.Of(absences),
            Events = EntitySet<SchoolEvent>.Of(
            [
                new SchoolEvent { Id = g.Next(), Title = "Staff meeting", Category = EventCategory.Meeting, Date = anchor.AddDays(-14).AddDays(0),
                                  Start = new TimeOnly(15, 30), End = new TimeOnly(16, 30), Audience = EventAudience.Staff | EventAudience.Facilitators,
                                  Repeat = new Repeat { Kind = RepeatKind.Weekly, Days = [DayOfWeek.Monday], Count = 10 }, RoomId = studioA.Id },
                new SchoolEvent { Id = g.Next(), Title = "Work-in-progress showing", Category = EventCategory.Performance, Date = anchor.AddDays(4),
                                  Start = new TimeOnly(16, 0), End = new TimeOnly(17, 30), RoomId = bigTop.Id },
                new SchoolEvent { Id = g.Next(), Title = "Budget review", Category = EventCategory.Meeting, Date = anchor.AddDays(2),
                                  Start = new TimeOnly(8, 0), End = new TimeOnly(8, 45), Audience = EventAudience.Administrators },
                new SchoolEvent { Id = g.Next(), Title = "Nordlys Troupe visiting", Category = EventCategory.HostedGroup, Date = anchor.AddDays(3),
                                  EndDate = anchor.AddDays(4), AllDay = true },
            ]),
        };

        var registers = new List<Register>();
        int k = 0;
        foreach (var s in t.Sessions.Where(s => s.Date < today).OrderBy(s => s.Date).ThenBy(s => s.Start).ThenBy(s => s.Id))
        {
            var roster = Rules.Roster.For(t, s);
            if (roster.Count == 0) continue;
            if (k++ % 9 == 4) continue;     // a few left missing
            var excused = Rules.Registers.KnownAway(t, s);
            var present = roster.Where((id, i) => !excused.Contains(id) && (i + k) % 7 != 0);
            registers.Add(Rules.Registers.File(t, s, present, excused, ada.Email, s.Date.ToDateTime(s.End, DateTimeKind.Utc)));
        }
        return t with { Registers = EntitySet<Register>.Of(registers) };
    }

    private static string Slug(string name) =>
        new string(name.ToLowerInvariant().Select(c => char.IsLetter(c) && c < 128 ? c : '.').ToArray()).Trim('.').Replace("..", ".");

    /// <summary>Deterministic ids, so the sample is the same every time it is made.</summary>
    private sealed class Ids
    {
        private int _n;
        public Guid Next() => new($"5a5a0000-0000-4000-8000-{++_n:D12}");
    }
}
