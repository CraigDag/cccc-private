using System.Text;
using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Domain.Sample;
using Cccc.Store;
using Cccc.Web.Features.Print;

namespace Cccc.Web.Tests;

/// <summary>
/// A facilitator's semester for their phone: a file somebody imports over the last one, so what matters
/// is that every class of theirs is in it at their own times, and that anything no longer theirs is
/// published as cancelled rather than left out to linger in their calendar.
/// </summary>
public class PhoneCalendarTests
{
    private static readonly DateOnly Monday = new(2026, 11, 16);
    private static readonly Timetable T = SampleSchool.Create(Monday, Monday);
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static Facilitator Named(Timetable t, string name) => t.Facilitators.Single(f => f.Name == name);

    /// <summary>The file's events, each a property list as written, folded lines put back together.</summary>
    private static List<Dictionary<string, string>> Events(string ics)
    {
        var lines = new List<string>();
        foreach (var raw in ics.Split("\r\n"))
            if (raw.StartsWith(' ')) lines[^1] += raw[1..]; else lines.Add(raw);
        var found = new List<Dictionary<string, string>>();
        Dictionary<string, string>? e = null;
        foreach (var l in lines)
        {
            if (l == "BEGIN:VEVENT") e = [];
            else if (l == "END:VEVENT") { found.Add(e!); e = null; }
            else if (e is not null) { var at = l.IndexOf(':'); e.Add(l[..at], l[(at + 1)..]); }   // Add: a property twice fails
        }
        return found;
    }

    private static string Uid(Session s) => $"{s.Id:N}@cccc-scheduler";

    private static Dictionary<string, string> Of(List<Dictionary<string, string>> events, Session s) =>
        Assert.Single(events, e => e["UID"] == Uid(s));

    /// <summary>A class's entry or an event date's: an event's id carries its number in the repeat.</summary>
    private static bool IsEventDate(Dictionary<string, string> e) => e["UID"][..e["UID"].IndexOf('@')].Contains('-');

    [Fact]
    public void EveryClassOfTheirsIsOnItAtTheirOwnTimesAndCoverMovesWithTheTeaching()
    {
        var dag = Named(T, "Dag Øverlie");
        var bo = Named(T, "Bo Lindqvist");
        var ics = PhoneCalendar.Build(T, dag.Id, Monday, Now);
        var events = Events(ics);
        var autumn = T.Semesters.Single(s => s.Contains(Monday));
        Assert.Contains($"X-WR-CALNAME:Dag Øverlie\\, {autumn.Name}\r\n", ics);
        Assert.All(Events(ics), e => Assert.Equal("20261117T120000Z", e["DTSTAMP"]));

        // Every class Dag teaches or covers in the semester, once each, and no other.
        var his = T.Sessions.Where(s => autumn.Contains(s.Date) && (s.FacilitatorIds.Contains(dag.Id) || s.CoverIds.Contains(dag.Id))).ToList();
        Assert.True(his.Count > 10, $"the sample gives Dag {his.Count} classes");
        Assert.Equal(his.Select(Uid).Order(), events.Where(e => !IsEventDate(e)).Select(e => e["UID"]).Order());

        // Last Monday's acrobatics was Bo's and Ada's, and Bo was away that morning: Dag covered it.
        var covered = T.Sessions.Single(s => s.CoverIds.Contains(dag.Id));
        var mine = Of(events, covered);
        Assert.False(mine.ContainsKey("STATUS"), "the class Dag covered is published as cancelled in his own file");
        Assert.Contains("Covering this class.", mine["DESCRIPTION"]);
        Assert.Contains("With Ada Brightwater", mine["DESCRIPTION"]);
        var bos = Of(Events(PhoneCalendar.Build(T, bo.Id, Monday, Now)), covered);
        Assert.Equal("CANCELLED", bos["STATUS"]);
        Assert.Contains("You are away then.", bos["DESCRIPTION"]);

        // Dag is in Monday's Circus disciplines from 14:00 only: his part is what his phone shows.
        var part = T.Sessions.Single(s => s.Date == Monday && s.Facilitators.Any(a => a.FacilitatorId == dag.Id && a.From is not null));
        var e = Of(events, part);
        Assert.Equal(("20261116T140000", "20261116T150000"), (e["DTSTART"], e["DTEND"]));
        Assert.Contains("The class runs 13:00–15:00.", e["DESCRIPTION"]);
        Assert.Equal("Big Top", e["LOCATION"]);
        Assert.Equal("Circus disciplines", e["SUMMARY"]);
    }

    [Fact]
    public void AClassThatIsNoLongerTheirsIsPublishedCancelledSoAReimportClearsIt()
    {
        // Ada is away this Thursday, so her acrobatics needs cover: not hers that day.
        var ada = Named(T, "Ada Brightwater");
        var thursday = T.Sessions.Single(s => s.Date == Monday.AddDays(3) && s.FacilitatorIds.Contains(ada.Id));
        var away = Of(Events(PhoneCalendar.Build(T, ada.Id, Monday, Now)), thursday);
        Assert.Equal("CANCELLED", away["STATUS"]);
        Assert.Contains("You are away then.", away["DESCRIPTION"]);

        // Friday is the Prep study day: Prep's juggling is closed, and says why.
        var cleo = Named(T, "Cleo Marchetti");
        var closed = Occurrences.ForWeek(T, Monday).Single(o => o.State == OccurrenceState.Closed && o.Session.FacilitatorIds.Contains(cleo.Id)).Session;
        var shut = Of(Events(PhoneCalendar.Build(T, cleo.Id, Monday, Now)), closed);
        Assert.Equal("CANCELLED", shut["STATUS"]);
        Assert.Contains("Closed: Prep study day.", shut["DESCRIPTION"]);

        // A class called off, with the reason it was given.
        var next = T.Sessions.Single(s => s.Date == Monday.AddDays(7) && s.FacilitatorIds.Contains(cleo.Id));
        var pencilled = T.Sessions.Single(s => s.Date == Monday.AddDays(9) && s.FacilitatorIds.Contains(cleo.Id));
        var t = T with { Sessions = T.Sessions.Put(next with { Cancelled = true, CancelNote = "Rigging check" }).Put(pencilled with { Provisional = true }) };
        Assert.Equal("Provisional: Juggling", Of(Events(PhoneCalendar.Build(t, cleo.Id, Monday, Now)), pencilled)["SUMMARY"]);
        var c = Of(Events(PhoneCalendar.Build(t, cleo.Id, Monday, Now)), next);
        Assert.Equal("CANCELLED", c["STATUS"]);
        Assert.Contains("Cancelled: Rigging check.", c["DESCRIPTION"]);
        // And what still runs is not cancelled: the file is not cancelling everything.
        Assert.False(Of(Events(PhoneCalendar.Build(T, cleo.Id, Monday, Now)), next).ContainsKey("STATUS"));
    }

    [Fact]
    public void WhatAnEarlierDownloadHadAndThisOneDoesNotIsCancelledAtTheTimesItHad()
    {
        var dag = Named(T, "Dag Øverlie");
        var cleo = Named(T, "Cleo Marchetti");
        // An inspection everybody could see this Friday, which later moves to the spring for administrators alone.
        var inspection = new SchoolEvent { Id = Guid.NewGuid(), Title = "Rigging inspection", Date = Monday.AddDays(4), Start = new(8, 0), End = new(9, 0) };
        // And next Monday's juggling is Cleo's, with Dag down to cover it.
        var lent = T.Sessions.Single(s => s.Date == Monday.AddDays(7) && s.FacilitatorIds.Contains(cleo.Id));
        Assert.DoesNotContain(dag.Id, lent.FacilitatorIds);
        var first = PhoneCalendar.Make(T with { Sessions = T.Sessions.Put(lent with { CoverIds = [dag.Id] }), Events = T.Events.Put(inspection) }, dag.Id, Monday, Now, []);
        Assert.Contains(first.Holds, h => h.Uid == Uid(lent));
        Assert.Equal(Events(first.Text).Select(e => e["UID"]).Order(), first.Holds.Select(h => h.Uid).Order());
        Assert.Contains(first.Holds, h => h.Uid == $"{inspection.Id:N}-1@cccc-scheduler");

        // Next Tuesday's strength class is deleted, the cover Dag gave last Monday goes to Cleo, Wednesday's
        // aerial moves an hour, and the showing is taken out of the year.
        var deleted = T.Sessions.Single(s => s.Date == Monday.AddDays(8) && s.FacilitatorIds.Contains(dag.Id));
        var covered = T.Sessions.Single(s => s.CoverIds.Contains(dag.Id));
        var moved = T.Sessions.Single(s => s.Date == Monday.AddDays(2) && s.FacilitatorIds.Contains(dag.Id));
        var showing = T.Events.Single(e => e.Title == "Work-in-progress showing");
        // And a Friday class of his moves into the spring: still his, on a date this file does not cover;
        // so does the visiting company's stay. Next Wednesday's moves there too, and goes to Cleo: his nowhere.
        var away = T.Sessions.Single(s => s.Date == Monday.AddDays(11) && s.FacilitatorIds.Contains(dag.Id));
        var given = T.Sessions.Single(s => s.Date == Monday.AddDays(9) && s.FacilitatorIds.Contains(dag.Id));
        var visit = T.Events.Single(e => e.Title == "Nordlys Troupe visiting");
        var spring = T.Semesters.Single(s => !s.Contains(Monday)).FirstMonday.AddDays(1);
        var t = T with
        {
            Sessions = T.Sessions.Remove(deleted.Id).Put(covered with { CoverIds = [cleo.Id] })
                                 .Put(moved with { Start = moved.Start.AddHours(1), End = moved.End.AddHours(1) })
                                 .Put(away with { Date = spring.AddDays(3) })
                                 .Put(given with { Date = spring.AddDays(1), Facilitators = [new Assignment(cleo.Id, null, null)] })
                                 .Put(lent with { Date = spring.AddDays(7), CoverIds = [dag.Id] }),
            Events = T.Events.Remove(showing.Id).Put(visit with { Date = spring.AddDays(2), EndDate = spring.AddDays(3) })
                             .Put(inspection with { Date = spring.AddDays(4), Audience = EventAudience.Administrators }),
        };
        // And an earlier file carried a spring class this one says nothing about, and a line nobody wrote.
        var springUid = $"{Guid.NewGuid():N}@cccc-scheduler";
        var before = first.Holds.Append(new PhoneCalendar.Held(springUid, spring, spring, "DTSTART:" + spring.ToString("yyyyMMdd") + "T090000", "DTEND:" + spring.ToString("yyyyMMdd") + "T100000"))
                                .Append(new PhoneCalendar.Held($"{Guid.NewGuid():N}@cccc-scheduler", Monday, Monday, "DTSTART:20261116T090000\r\nX-INJECTED:1", "DTEND:20261116T100000"))
                                .Append(first.Holds.Single(h => h.Uid == Uid(deleted)))   // carried twice, written once
                                .ToList();

        var second = PhoneCalendar.Make(t, dag.Id, Monday, Now.AddHours(1), before);
        var events = Events(second.Text);
        Assert.Equal(events.Count, events.Select(e => e["UID"]).Distinct().Count());

        var gone = Of(events, deleted);
        var old = Of(Events(first.Text), deleted);
        Assert.Equal(("CANCELLED", PhoneCalendar.Gone), (gone["STATUS"], gone["SUMMARY"]));
        Assert.Equal((old["DTSTART"], old["DTEND"]), (gone["DTSTART"], gone["DTEND"]));
        Assert.False(gone.ContainsKey("LOCATION"), "a class taken off keeps no place or title outside the timetable");
        Assert.Equal("CANCELLED", Of(events, covered)["STATUS"]);
        Assert.Equal(PhoneCalendar.Gone, Of(events, covered)["SUMMARY"]);
        var shown = Assert.Single(events, e => e["UID"] == $"{showing.Id:N}-1@cccc-scheduler");
        Assert.Equal(("CANCELLED", "20261120T160000"), (shown["STATUS"], shown["DTSTART"]));

        // A class that moved is the same entry at its new time, not one cancelled and one added.
        var now = Of(events, moved);
        Assert.Equal(("20261118T114500", false), (now["DTSTART"], now.ContainsKey("STATUS")));

        // The class moved into the spring is not cancelled here, where the spring file would be undone by
        // this one opened after it; it is carried, at the date the phone still has, for that file to move.
        Assert.DoesNotContain(Uid(away), second.Text);
        Assert.Contains(second.Holds, h => h.Uid == Uid(away) && h.From == Monday.AddDays(11));
        Assert.DoesNotContain(Uid(lent), second.Text);   // and one he is covering there
        Assert.Contains(second.Holds, h => h.Uid == Uid(lent));
        Assert.DoesNotContain($"{visit.Id:N}-1@cccc-scheduler", second.Text);
        Assert.Contains(second.Holds, h => h.Uid == $"{visit.Id:N}-1@cccc-scheduler" && h.From == Monday.AddDays(3));
        Assert.Equal(PhoneCalendar.Gone, Of(events, given)["SUMMARY"]);   // moved out and given away: not his anywhere
        var hidden = Assert.Single(events, e => e["UID"] == $"{inspection.Id:N}-1@cccc-scheduler");   // moved out, and no longer his to see
        Assert.Equal((PhoneCalendar.Gone, "20261120T080000"), (hidden["SUMMARY"], hidden["DTSTART"]));
        // The spring class is carried for the spring file, and not cancelled by this one; the line nobody wrote goes nowhere.
        Assert.DoesNotContain(springUid, second.Text);
        Assert.Contains(second.Holds, h => h.Uid == springUid);
        Assert.DoesNotContain("X-INJECTED", second.Text);
        Assert.DoesNotContain(second.Holds, h => h.Start.Contains("X-INJECTED"));

        // A phone that missed the second file is told again by the third, and a file taken half a minute
        // after the last still supersedes it: a calendar may pass over an update whose sequence has not moved.
        var third = Events(PhoneCalendar.Make(t, dag.Id, Monday, Now.AddHours(1).AddSeconds(30), second.Holds).Text);
        Assert.Equal("CANCELLED", Of(third, deleted)["STATUS"]);
        Assert.True(long.Parse(Of(third, deleted)["SEQUENCE"]) > long.Parse(gone["SEQUENCE"]), $"{Of(third, deleted)["SEQUENCE"]} after {gone["SEQUENCE"]}");
    }

    [Fact]
    public void TheSchoolsEventsForFacilitatorsAreOnItAndAnAdministratorsOneNever()
    {
        var finn = Named(T, "Finn Aalborg-Reyes");
        var meeting = T.Events.Single(e => e.Title == "Staff meeting");
        var skipped = meeting.Date.AddDays(7 * 3);
        var t = T with { Events = T.Events.Put(meeting with { Skipped = [skipped] })
                                           .Put(new SchoolEvent { Id = Guid.NewGuid(), Title = "Open day for families", Date = Monday.AddDays(2), Audience = EventAudience.Students }) };
        var events = Events(PhoneCalendar.Build(t, finn.Id, Monday, Now));
        string Summary(Dictionary<string, string> e) => e["SUMMARY"];
        Assert.DoesNotContain(events, e => Summary(e) == "Budget review");          // administrators only
        Assert.DoesNotContain(events, e => Summary(e) == "Open day for families");  // students only
        Assert.Contains(events, e => Summary(e) == "Work-in-progress showing");    // everybody

        // The Monday meeting, every week of the semester it falls in, named by its number in the repeat:
        // the fourth is skipped, so it is published as cancelled and the fifth keeps its number.
        var mondays = events.Where(e => Summary(e) == "Staff meeting").ToList();
        Assert.Equal(10, mondays.Count);
        var fourth = Assert.Single(mondays, e => e["UID"] == $"{meeting.Id:N}-4@cccc-scheduler");
        Assert.Equal(("CANCELLED", skipped.ToString("yyyyMMdd") + "T153000", skipped.ToString("yyyyMMdd") + "T163000"),
                     (fourth["STATUS"], fourth["DTSTART"], fourth["DTEND"]));
        Assert.Contains("Not on this date.", fourth["DESCRIPTION"]);
        Assert.False(Assert.Single(mondays, e => e["UID"] == $"{meeting.Id:N}-5@cccc-scheduler").ContainsKey("STATUS"));
        Assert.Equal("Studio A", fourth["LOCATION"]);

        // Two whole days of a visiting company: dates without times, ending the day after the last.
        var visit = Assert.Single(events, e => Summary(e) == "Nordlys Troupe visiting");
        Assert.Equal(("20261119", "20261121"), (visit["DTSTART;VALUE=DATE"], visit["DTEND;VALUE=DATE"]));
    }

    [Fact]
    public void TheFileIsWellFormedWhateverANameHoldsAndOutsideEverySemesterItIsThatWeek()
    {
        var dag = Named(T, "Dag Øverlie");
        var s = T.Sessions.Single(x => x.FacilitatorIds.Contains(dag.Id) && x.Date == Monday);
        var odd = "Acro; hand, to hand \\ with a very long name that will certainly need folding Ønskeø 🎪\u0007";
        var t = T with { Sessions = T.Sessions.Put(s with { Title = odd, Notes = "Line one\nLine two" }), Semesters = EntitySet<Semester>.Empty,
                         Facilitators = T.Facilitators.Put(dag with { Name = "Dag Øverlie/Ash" }) };
        var bytes = PhoneCalendar.Encode(PhoneCalendar.Build(t, dag.Id, Monday, Now));
        Assert.NotEqual(0xEF, bytes[0]);   // no byte-order mark
        var ics = Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", ics);
        Assert.EndsWith("END:VCALENDAR\r\n", ics);
        Assert.DoesNotContain("\r\n\r\n", ics);
        Assert.DoesNotContain("\n", ics.Replace("\r\n", ""));   // every line ends CRLF, none bare
        var lines = ics.Split("\r\n");
        Assert.Contains(lines, l => l.StartsWith(' '));       // something was long enough to fold
        Assert.All(lines, l => Assert.True(Encoding.UTF8.GetByteCount(l) <= 75, $"{Encoding.UTF8.GetByteCount(l)} octets: {l}"));
        // Ordinal: a culture's comparison passes over a control character, and would find one everywhere.
        Assert.DoesNotContain("\u0007", ics, StringComparison.Ordinal);
        Assert.DoesNotContain("\uFFFD", ics, StringComparison.Ordinal);   // a fold never splits a character
        var e = Of(Events(ics), s);
        Assert.Equal("Acro\\; hand\\, to hand \\\\ with a very long name that will certainly need folding Ønskeø 🎪", e["SUMMARY"]);
        Assert.Contains("Line one\\nLine two", e["DESCRIPTION"]);

        // No semesters: the week alone, and the file named for it, with nothing a file name cannot hold.
        var starts = Events(ics).Select(x => x.TryGetValue("DTSTART", out var d) ? d : x["DTSTART;VALUE=DATE"]).ToList();
        Assert.NotEmpty(starts);
        Assert.All(starts, d => Assert.InRange(d[..8], "20261116", "20261122"));
        Assert.Equal("Dag Øverlie-Ash, Week 47.ics", PhoneCalendar.FileName(t, dag.Id, Monday));
    }

    [Fact]
    public void TheDownloadRemembersWhatItCarriedForEachPersonAndForgetsWhatHasPassed()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "stores", Guid.NewGuid().ToString("N"));
        var store = ProjectStore.Open(folder, create: true);
        try
        {
            var project = store.Create("School", T, "test", Now);
            var files = new PhoneCalendarFiles(store);
            var dag = Named(T, "Dag Øverlie");
            var ada = Named(T, "Ada Brightwater");
            var (bytes, name) = files.Make(project, T, dag.Id, Monday, Now, today: Monday.AddDays(1));
            Assert.Equal("Dag Øverlie, Autumn semester.ics", name);
            var carried = Events(Encoding.UTF8.GetString(bytes)).Select(e => e["UID"]).ToHashSet();
            var held = files.Read(project, dag.Id);
            Assert.NotEmpty(held);
            Assert.All(held, h => Assert.Contains(h.Uid, carried));
            // Nothing that has passed is kept, as an old entry cannot send anybody anywhere; what is ahead is.
            Assert.All(held, h => Assert.True(h.To >= Monday.AddDays(1), $"{h.To} kept"));
            Assert.DoesNotContain(held, h => h.Uid == Uid(T.Sessions.Single(s => s.CoverIds.Contains(dag.Id))));
            Assert.Contains(held, h => h.Uid == Uid(T.Sessions.Single(s => s.Date == Monday.AddDays(2) && s.FacilitatorIds.Contains(dag.Id))));
            // The visiting company's two days are kept until the last of them has passed.
            Assert.Contains(held, h => h.From == Monday.AddDays(3) && h.To == Monday.AddDays(4));

            // Dag's Wednesday class is deleted: his next file cancels it, and Ada's, which never had it, does not mention it.
            var wednesday = T.Sessions.Single(s => s.Date == Monday.AddDays(2) && s.FacilitatorIds.Contains(dag.Id));
            var t = T with { Sessions = T.Sessions.Remove(wednesday.Id) };
            var adas = Encoding.UTF8.GetString(files.Make(project, t, ada.Id, Monday, Now, Monday.AddDays(1)).Bytes);
            Assert.DoesNotContain(Uid(wednesday), adas);
            var dags = Events(Encoding.UTF8.GetString(files.Make(project, t, dag.Id, Monday, Now.AddHours(1), Monday.AddDays(1)).Bytes));
            Assert.Equal("CANCELLED", Of(dags, wednesday)["STATUS"]);

            // Once its day has passed it is forgotten; the visiting company's last day is today, so it is kept.
            files.Make(project, t, dag.Id, Monday, Now.AddDays(3), Monday.AddDays(4));
            Assert.DoesNotContain(files.Read(project, dag.Id), h => h.Uid == Uid(wednesday));
            Assert.Contains(files.Read(project, dag.Id), h => h.From == Monday.AddDays(3) && h.To == Monday.AddDays(4));
            // And a record nobody can read is no record.
            store.SetCalendarHeld(project, dag.Id, "{not a list");
            Assert.Empty(files.Read(project, dag.Id));
            Assert.Contains("BEGIN:VEVENT", Encoding.UTF8.GetString(files.Make(project, t, dag.Id, Monday, Now, Monday.AddDays(3)).Bytes));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}
