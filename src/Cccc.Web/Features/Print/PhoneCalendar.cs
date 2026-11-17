using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Shell;
using Cccc.Web.Features.Week;
using DWeek = Cccc.Domain.Week;

namespace Cccc.Web.Features.Print;

/// <summary>
/// One facilitator's semester as a calendar file (RFC 5545) to open on a phone: the classes they are in,
/// at their own times and as the week runs, so cover moves with the teaching, and the school's events for
/// facilitators. A file, not a subscription: the laptop is not on the internet, so nothing can fetch
/// updates from it, and the file is downloaded again after the timetable changes. It is made to be
/// imported over the last one: every class is named by its own id, every event date by its event and its
/// number in the repeat, and anything that is no longer theirs is published as cancelled rather than left
/// out, so importing the new file takes the old entry away instead of leaving somebody to turn up to it.
/// That covers what the timetable still holds (cancelled, closed, away) and, through what the earlier
/// downloads held (<see cref="Make"/>), what it does not (a class deleted, the cover given to somebody
/// else, an event removed). Times are the building's, written without a time zone, as the school's day
/// is read.
/// </summary>
public static class PhoneCalendar
{
    /// <summary>Said beside the button: a downloaded file does not update itself.</summary>
    public const string Note = "Downloads this semester as a calendar file to open on a phone. It does not update itself: download it again after the timetable changes.";

    /// <summary>What a class taken off somebody's timetable is called on their phone: its title is not kept.</summary>
    public const string Gone = "No longer on your timetable";

    /// <summary>
    /// The events the file carries: those every facilitator may see, never an administrators-only one,
    /// whoever downloads it, so a coach's own file and the one an administrator takes for them agree.
    /// </summary>
    private static readonly EventViewer AFacilitator = new(SeesEverything: false, IsEditor: false, IsStudent: false, IsFacilitator: true, IsStaff: false);

    public sealed record Span(DateOnly From, DateOnly To, string Name);

    /// <summary>
    /// One entry a file carried: its id, the first and last day it covers, and its two time lines as
    /// written, never its title or place, so nothing a person wrote is kept outside the timetable.
    /// </summary>
    public sealed record Held(string Uid, DateOnly From, DateOnly To, string Start, string End);

    /// <summary>A file's text, and every entry it carried, for the next download to compare with.</summary>
    public sealed record Made(string Text, IReadOnlyList<Held> Holds);

    /// <summary>What a file covers: the semester the week is in, or that week alone outside every semester.</summary>
    public static Span SpanOf(Timetable t, DateOnly anyDate)
    {
        var monday = DWeek.MondayOf(anyDate);
        return t.Semesters.Where(s => s.Contains(monday)).OrderBy(s => s.FirstMonday).FirstOrDefault() is { } s
            ? new(s.FirstMonday, s.LastMonday.AddDays(6), s.Name)
            : new(monday, monday.AddDays(6), "Week " + DWeek.IsoNumber(monday));
    }

    /// <summary>
    /// The link to a person's file from the timetable this tab shows. Ids and a date only, so it carries
    /// no name and needs no token, and so it never expires on a page left open; the route asks again who
    /// may have it. Marked <c>download</c> where it is drawn: a file to keep, and Blazor leaves such a
    /// link to the browser, where it would otherwise route it and leave the address bar on the file.
    /// </summary>
    public static string Link(Workspace w, Guid facilitatorId, DateOnly anyDate) =>
        w.Project is { } p ? Link(p.Id, facilitatorId, anyDate) : "/signin";

    public static string Link(Guid projectId, Guid facilitatorId, DateOnly anyDate) =>
        $"/calendar/phone?project={projectId}&person={facilitatorId}&week={anyDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

    /// <summary>"Dag Øverlie, Autumn semester.ics": the person's own file, named for them.</summary>
    public static string FileName(Timetable t, Guid facilitatorId, DateOnly anyDate)
    {
        var name = $"{t.Facilitators[facilitatorId]?.Name ?? "Facilitator"}, {SpanOf(t, anyDate).Name}";
        var bad = Path.GetInvalidFileNameChars().Concat(['"', '\\', '/', ':', '*', '?', '<', '>', '|']).ToHashSet();
        return new string(name.Select(c => bad.Contains(c) || char.IsControl(c) ? '-' : c).ToArray()).Trim() + ".ics";
    }

    /// <summary>The file as bytes: UTF-8 without a byte-order mark, which some calendars read as text.</summary>
    public static byte[] Encode(string text) => new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(text);

    public static string Build(Timetable t, Guid facilitatorId, DateOnly anyDate, DateTimeOffset now) =>
        Make(t, facilitatorId, anyDate, now, []).Text;

    /// <summary>
    /// The file, given what earlier downloads of it held. An earlier entry on a day inside this file's
    /// span that this file no longer carries is published as cancelled at the times it had; one wholly
    /// outside the span is left alone, since this file says nothing about those dates, and carried on.
    /// </summary>
    public static Made Make(Timetable t, Guid facilitatorId, DateOnly anyDate, DateTimeOffset now, IEnumerable<Held> before)
    {
        var span = SpanOf(t, anyDate);
        var sb = new StringBuilder();
        var holds = new List<Held>();
        void Line(string s) => sb.Append(Fold(s)).Append("\r\n");
        var utc = now.UtcDateTime;
        string stamp = utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        // Every download supersedes the last: nothing counts revisions of one class, so the export's own
        // second does, and a calendar that passes over an update whose SEQUENCE has not moved takes the new
        // times even from a download moments after the last. Seconds since 2020 fit the standard's integer
        // until 2088.
        long sequence = (long)(utc - new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        void Open(string uid, DateOnly first, DateOnly last, string start, string end)
        {
            Line("BEGIN:VEVENT");
            Line("UID:" + uid);
            Line("DTSTAMP:" + stamp);
            Line("SEQUENCE:" + sequence.ToString(CultureInfo.InvariantCulture));
            Line(start);
            Line(end);
            holds.Add(new Held(uid, first, last, start, end));
        }

        Line("BEGIN:VCALENDAR");
        Line("VERSION:2.0");
        Line("PRODID:-//CCCC//Scheduler//EN");
        Line("CALSCALE:GREGORIAN");
        // No METHOD: PUBLISH asks for an organiser and makes some calendars treat the file as an invitation.
        Line("X-WR-CALNAME:" + Escape($"{t.Facilitators[facilitatorId]?.Name ?? "Facilitator"}, {span.Name}"));

        foreach (var o in Occurrences.Between(t, span.From, span.To)
                     .Where(o => o.Session.FacilitatorIds.Contains(facilitatorId) || o.Session.CoverIds.Contains(facilitatorId)))
        {
            var s = o.Session;
            bool there = o.Happens && o.InTheRoom.Contains(facilitatorId);
            var (from, until) = there ? TimeWindows.InRoom(o, facilitatorId)
                : s.FacilitatorIds.Contains(facilitatorId) ? TimeWindows.Assigned(s, facilitatorId) : (s.Start, s.End);
            Open($"{s.Id:N}@cccc-scheduler", s.Date, s.Date, "DTSTART:" + At(s.Date, from), "DTEND:" + At(s.Date, until));
            Line("SUMMARY:" + Escape((s.Provisional ? "Provisional: " : "") + Looks.Title(t, s)));
            if (!there) Line("STATUS:CANCELLED");
            if (Looks.Room(t, s) is { Length: > 0 } room) Line("LOCATION:" + Escape(room));
            Line("DESCRIPTION:" + Escape(string.Join("\n", Describe(t, o, facilitatorId, there, from, until))));
            Line("END:VEVENT");
        }

        foreach (var e in t.Events.Where(AFacilitator.Sees).OrderBy(e => e.Date).ThenBy(e => e.Id))
            foreach (var (o, on) in EventSchedule.EveryDate(t, e))
            {
                if (o.Date > span.To) break;
                if (o.LastDay < span.From) continue;
                var uid = $"{e.Id:N}-{o.Number.ToString(CultureInfo.InvariantCulture)}@cccc-scheduler";
                if (o.IsAllDayStrip)
                    // A day or a run of days, as the week draws it: the end is the day after the last.
                    Open(uid, o.Date, o.LastDay, "DTSTART;VALUE=DATE:" + Day(o.Date), "DTEND;VALUE=DATE:" + Day(o.LastDay.AddDays(1)));
                else
                    Open(uid, o.Date, o.Date, "DTSTART:" + At(o.Date, e.Start), "DTEND:" + At(o.Date, e.End > e.Start ? e.End : e.Start));
                Line("SUMMARY:" + Escape(e.Title.Trim() is { Length: > 0 } title ? title : "Event"));
                if (!on) Line("STATUS:CANCELLED");
                var place = t.Rooms.Find(e.RoomId)?.Name ?? e.Place.Trim();
                if (place.Length > 0) Line("LOCATION:" + Escape(place));
                var said = new List<string>();
                if (!e.AllDay && o.IsMultiDay) said.Add($"{DWeek.Hm(e.Start)}–{DWeek.Hm(e.End)}");
                if (!on) said.Add("Not on this date.");
                if (e.Note.Trim() is { Length: > 0 } note) said.Add(note);
                if (said.Count > 0) Line("DESCRIPTION:" + Escape(string.Join("\n", said)));
                Line("END:VEVENT");
            }

        // What an earlier download carried and this one does not. Only the entries this file speaks for,
        // those on a day inside its span, as the classes and events above were chosen; the others are kept
        // as they were for the file that does. So is one still theirs on a date outside the span, a class
        // moved into the next semester: cancelling it here would undo that semester's file if this one
        // were opened after it, and that file moves it.
        var written = holds.Select(h => h.Uid).ToHashSet(StringComparer.Ordinal);
        var elsewhere = t.Sessions
            .Where(s => (s.Date < span.From || s.Date > span.To) && (s.FacilitatorIds.Contains(facilitatorId) || s.CoverIds.Contains(facilitatorId)))
            .Select(s => $"{s.Id:N}@cccc-scheduler")
            .Concat(t.Events.Where(AFacilitator.Sees).SelectMany(e => EventSchedule.EveryDate(t, e)
                .Where(x => x.Occurrence.LastDay < span.From || x.Occurrence.Date > span.To)
                .Select(x => $"{e.Id:N}-{x.Occurrence.Number.ToString(CultureInfo.InvariantCulture)}@cccc-scheduler")))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var h in before.Where(IsWellFormed).DistinctBy(h => h.Uid).Where(h => !written.Contains(h.Uid))
                     .OrderBy(h => h.From).ThenBy(h => h.Uid, StringComparer.Ordinal).ToList())
        {
            if (h.To < span.From || h.From > span.To || elsewhere.Contains(h.Uid)) { holds.Add(h); continue; }
            Open(h.Uid, h.From, h.To, h.Start, h.End);
            Line("SUMMARY:" + Gone);
            Line("STATUS:CANCELLED");
            Line("DESCRIPTION:" + Escape("An earlier download of this calendar had this, and it is no longer yours on this date."));
            Line("END:VEVENT");
        }

        Line("END:VCALENDAR");
        return new Made(sb.ToString(), holds);
    }

    private static readonly Regex UidShape = new(@"^[0-9a-f]{32}(-[0-9]{1,6})?@cccc-scheduler$", RegexOptions.CultureInvariant);
    private static readonly Regex StartShape = new(@"^DTSTART(:[0-9]{8}T[0-9]{6}|;VALUE=DATE:[0-9]{8})$", RegexOptions.CultureInvariant);
    private static readonly Regex EndShape = new(@"^DTEND(:[0-9]{8}T[0-9]{6}|;VALUE=DATE:[0-9]{8})$", RegexOptions.CultureInvariant);

    /// <summary>An earlier entry is written back only in the shapes this file writes, so a stored line cannot break the file.</summary>
    private static bool IsWellFormed(Held h) =>
        h is { Uid: not null, Start: not null, End: not null } && h.To >= h.From
        && UidShape.IsMatch(h.Uid) && StartShape.IsMatch(h.Start) && EndShape.IsMatch(h.End);

    /// <summary>Who it is for, who else is there, and why it is not theirs when it is not.</summary>
    private static IEnumerable<string> Describe(Timetable t, Occurrence o, Guid me, bool there, TimeOnly from, TimeOnly until)
    {
        var s = o.Session;
        yield return "For " + Looks.Audience(t, s);
        if (!there)
        {
            yield return o.State switch
            {
                OccurrenceState.Closed => "Closed" + (o.ClosedBy is { Name.Length: > 0 } c ? ": " + c.Name.Trim() : "") + ".",
                OccurrenceState.Cancelled => "Cancelled" + (s.CancelNote?.Trim() is { Length: > 0 } why ? ": " + why : "") + ".",
                _ when o.AwayIds.Contains(me) => "You are away then.",
                _ => "You are not in the room for this one.",
            };
            yield break;
        }
        if (s.CoverIds.Contains(me) && !s.FacilitatorIds.Contains(me)) yield return "Covering this class.";
        var others = o.InTheRoom.Where(id => id != me).Select(id => t.Facilitators[id]?.Name).OfType<string>().ToList();
        if (others.Count > 0) yield return "With " + string.Join(", ", others);
        if ((from, until) != (s.Start, s.End)) yield return $"The class runs {DWeek.Hm(s.Start)}–{DWeek.Hm(s.End)}.";
        if (s.Notes?.Trim() is { Length: > 0 } notes) yield return notes;
    }

    private static string At(DateOnly d, TimeOnly time) =>
        Day(d) + "T" + time.ToString("HHmmss", CultureInfo.InvariantCulture);

    private static string Day(DateOnly d) => d.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>
    /// A TEXT value (RFC 5545 §3.3.11): backslash, semicolon, comma and newline escaped, and control
    /// characters other than tab and newline dropped, which would make the whole file invalid.
    /// </summary>
    internal static string Escape(string v)
    {
        var clean = new StringBuilder(v.Length);
        foreach (var ch in v)
            if (!char.IsControl(ch) || ch is '\t' or '\r' or '\n') clean.Append(ch);
        return clean.ToString().Replace("\\", "\\\\").Replace(";", "\\;").Replace(",", "\\,")
            .Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\\n");
    }

    /// <summary>
    /// A line folded at 75 octets (§3.1), each continuation starting with a space, never inside a
    /// character: a name like Øverlie is two octets a letter, and an emoji four, carried whole.
    /// </summary>
    internal static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= 75) return line;
        var sb = new StringBuilder();
        int col = 0;
        bool first = true;
        Span<char> pair = stackalloc char[2];
        foreach (var rune in line.EnumerateRunes())
        {
            int w = rune.Utf8SequenceLength;
            if (col + w > (first ? 75 : 74))
            {
                sb.Append("\r\n ");
                col = 1;
                first = false;
            }
            sb.Append(pair[..rune.EncodeToUtf16(pair)]);
            col += w;
        }
        return sb.ToString();
    }
}
