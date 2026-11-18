using System.Globalization;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;

namespace Cccc.Domain.Editing;

/// <summary>
/// Start next year from this one: this year's shape with an empty history. The current app's
/// <c>ScheduleOps.StartNextYear</c>, with two differences, each for a reason.
/// <list type="bullet">
/// <item><b>The result is a new timetable beside this one</b> (<see cref="Make"/> returns it and the caller
/// stores it), so this year's registers, absences and history stay where they happened, and nobody has to
/// remember to keep a copy before the rollover writes over the year.</item>
/// <item><b>Every semester starts in the same week number next year</b>, rather than 52 weeks on. A Danish
/// school thinks in week numbers (the autumn break is week 42), and a year that holds an ISO week 53, as
/// 2026 does, makes 52 weeks on a week early: week 34 of 2026 plus 52 weeks is week 33 of 2027. Each
/// semester moves by its own whole number of weeks, so everything inside it keeps its weekday and its
/// place in the semester.</item>
/// </list>
/// What carries over and what is dropped is the current app's: the building and its equipment, the people, the groups
/// (unchanged: nothing here can know who has graduated), the catalogue and the rules carry over; the
/// timetable carries over as its first timetabled week only, onto next year's, because a week ahead is
/// blank until somebody fills it; registers and every other week are dropped, being a record of what
/// happened. Events are dated to the calendar, so none moves: one with a date in the new year is kept
/// (an audition already booked), the rest stay with this year. Absences follow the same rule, so a leave
/// already booked for September is not lost, and so does asking to be away; offers to cover are for this
/// year's classes, so none carries over. Closures are a choice, off by default: week-numbered ones
/// land right, but Christmas and Easter do not, and a wrong closure shuts the building on a day it is open.
/// </summary>
public static class NextYear
{
    public sealed record SemesterMove(Semester From, Semester To)
    {
        public int Days => To.FirstMonday.DayNumber - From.FirstMonday.DayNumber;
    }

    /// <summary>
    /// What starting next year would do, counted off the timetable. The form shows it and <see cref="Make"/>
    /// does it, so the two cannot describe different years.
    /// </summary>
    public sealed record Plan(
        IReadOnlyList<SemesterMove> Semesters,
        DateOnly? FirstWeek,
        DateOnly? FirstWeekTo,
        int Classes,
        int OtherClasses,
        int Registers,
        int AbsencesKept,
        int AbsencesDropped,
        int EventsKept,
        int EventsDropped,
        int Closures,
        int ScratchClasses)
    {
        /// <summary>Next year's first Monday: the earliest of the semesters, moved.</summary>
        public DateOnly Starts => Semesters.Min(m => m.To.FirstMonday);
    }

    /// <summary>The Monday of the same ISO week number next year, or next year's last week when it has no week 53.</summary>
    public static DateOnly SameWeekNextYear(DateOnly anyDate)
    {
        var monday = Week.MondayOf(anyDate).ToDateTime(TimeOnly.MinValue);
        int next = ISOWeek.GetYear(monday) + 1;
        int week = Math.Min(ISOWeek.GetWeekOfYear(monday), ISOWeek.GetWeeksInYear(next));
        return DateOnly.FromDateTime(ISOWeek.ToDateTime(next, week, DayOfWeek.Monday));
    }

    /// <summary>
    /// The week whose classes become next year's first: the first week at or after this year's start that
    /// has any, or failing that the earliest that has any; null when no week has a class. The current app's
    /// <c>FirstTimetabledWeek</c>.
    /// </summary>
    public static DateOnly? FirstTimetabledWeek(Timetable t)
    {
        var weeks = t.Sessions.Select(s => s.WeekMonday).Distinct().Order().ToList();
        if (weeks.Count == 0) return null;
        var start = t.YearStartMonday ?? DateOnly.MinValue;
        return weeks.Where(w => w >= start).Cast<DateOnly?>().FirstOrDefault() ?? weeks[0];
    }

    /// <summary>What starting next year would do, or null when there are no semesters and so no year to roll forward.</summary>
    public static Plan? For(Timetable t)
    {
        if (t.Semesters.Count == 0) return null;
        var moves = t.Semesters.OrderBy(s => s.FirstMonday).ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(s => new SemesterMove(s, s with { StartMonday = SameWeekNextYear(s.FirstMonday) })).ToList();
        var starts = moves.Min(m => m.To.FirstMonday);
        var first = FirstTimetabledWeek(t);
        int classes = first is { } f ? t.SessionsInWeek(f).Count() : 0;
        int keptAbsences = t.Absences.Count(a => Carried(t, a, starts));
        int keptEvents = t.Events.Count(e => EventSchedule.LastDay(e) >= starts);
        return new Plan(moves, first, first is { } w ? w.AddDays(ShiftDays(moves, w)) : null,
            classes, t.Sessions.Count - classes, t.Registers.Count,
            keptAbsences, t.Absences.Count - keptAbsences, keptEvents, t.Events.Count - keptEvents,
            t.Closures.Count, t.Scratch.Count);
    }

    /// <summary>
    /// Next year, as a new timetable called <paramref name="name"/>. Refuses a timetable with no semesters,
    /// which the form says rather than offering the button.
    /// </summary>
    public static Timetable Make(Timetable t, string name, bool keepClosures)
    {
        var plan = For(t) ?? throw new EditRefused("This timetable has no semesters, so it has no year to roll forward. Add one in People & places ▸ Semesters first.");
        var starts = plan.Starts;
        var sessions = new List<Session>();
        if (plan.FirstWeek is { } from && plan.FirstWeekTo is { } to)
            foreach (var s in t.SessionsInWeek(from))
                sessions.Add(Edits.CopyOf(t, s, s.Date.AddDays(to.DayNumber - from.DayNumber), Edits.CopyId(s.Id, to)));
        var closures = keepClosures
            ? t.Closures.Select(c =>
            {
                int d = ShiftDays(plan.Semesters, c.From);
                return c with { From = c.From.AddDays(d), To = c.To.AddDays(d) };
            })
            : [];
        var next = t with
        {
            Settings = t.Settings with { Name = name.Trim() },
            Semesters = EntitySet<Semester>.Of(plan.Semesters.Select(m => m.To)),
            Closures = EntitySet<Closure>.Of(closures),
            Sessions = EntitySet<Session>.Of(sessions),
            Registers = EntitySet<Register>.Empty,
            Absences = EntitySet<Absence>.Of(t.Absences.Where(a => Carried(t, a, starts))),
            Events = EntitySet<SchoolEvent>.Of(t.Events.Where(e => EventSchedule.LastDay(e) >= starts)),
            // Asking to be away goes with its days, as an absence does, so one asked for September is not lost;
            // an offer was for one of this year's classes, and next year's are new ones. Asking for a space goes
            // with its day while it waits for an answer; answered, it was this year's, a yes one of this year's classes.
            Requests = EntitySet<TimeOffRequest>.Of(t.Requests.Where(r => r.To >= starts && t.Facilitators[r.FacilitatorId] is { FormerSince: null })),
            Offers = EntitySet<CoverOffer>.Empty,
            SpaceAsks = EntitySet<SpaceAsk>.Of(t.SpaceAsks.Where(a => a.Answer is null && a.Date >= starts && t.Facilitators[a.FacilitatorId] is { FormerSince: null })),
        };
        return t.Classes.Any(c => c.IsFormer()) || t.Rooms.Any(r => r.IsFormer()) || t.Facilitators.Any(f => f.IsFormer())
            ? LeaveFormerBehind(t, next) : next;
    }

    /// <summary>
    /// What is former stays with this year: next year never offers it, so nothing there names it, and its
    /// classes were copied without it already (<see cref="Edits.CopyOf"/>).
    /// </summary>
    private static Timetable LeaveFormerBehind(Timetable t, Timetable next)
    {
        bool FormerPerson(Guid id) => t.Facilitators[id] is { FormerSince: not null };
        bool FormerClass(Guid id) => t.Classes[id] is { FormerSince: not null };
        return next with
        {
            Events = EntitySet<SchoolEvent>.Of(next.Events.Select(e => e with
            {
                RoomId = t.Rooms.Find(e.RoomId) is { FormerSince: not null } ? null : e.RoomId,
                FacilitatorIds = [.. e.FacilitatorIds.Where(f => !FormerPerson(f))],
            })),
            Scratch = EntitySet<Session>.Of(next.Scratch.Select(s => Former.Strip(t, s))),
            TimeRules = EntitySet<TimeRule>.Of(next.TimeRules.Select(r => r with { ClassIds = [.. r.ClassIds.Where(c => !FormerClass(c))] })),
            Classes = EntitySet<Class>.Of(next.Classes.Current()),
            Rooms = EntitySet<Room>.Of(next.Rooms.Current()),
            Facilitators = EntitySet<Facilitator>.Of(next.Facilitators.Current()),
        };
    }

    /// <summary>An absence with a date in next year goes with it, unless it is a former facilitator's.</summary>
    private static bool Carried(Timetable t, Absence a, DateOnly starts) =>
        a.To >= starts && !(a.Person == AbsentPerson.Facilitator && t.Facilitators[a.PersonId] is { FormerSince: not null });

    /// <summary>A name for next year's timetable: this one's with its years moved on (2026 becomes 2027, 2026/27 becomes 2027/28), or "…, next year".</summary>
    public static string SuggestName(string name)
    {
        var trimmed = name.Trim();
        var moved = System.Text.RegularExpressions.Regex.Replace(trimmed, @"(?<![0-9])(20[0-9]{2})(?:([/–-])([0-9]{2}|20[0-9]{2}))?(?![0-9])", m =>
        {
            int first = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) + 1;
            if (!m.Groups[3].Success) return first.ToString(CultureInfo.InvariantCulture);
            var second = m.Groups[3].Value;
            int next = int.Parse(second, CultureInfo.InvariantCulture) + 1;
            return $"{first}{m.Groups[2].Value}{(second.Length == 2 ? (next % 100).ToString("00", CultureInfo.InvariantCulture) : next.ToString(CultureInfo.InvariantCulture))}";
        });
        return moved != trimmed ? moved : (trimmed.Length == 0 ? "Next year" : trimmed + ", next year");
    }

    /// <summary>How far a date moves: with the semester it is in, or failing that the nearest one.</summary>
    private static int ShiftDays(IReadOnlyList<SemesterMove> moves, DateOnly date)
    {
        var inside = moves.FirstOrDefault(m => m.From.Contains(date));
        if (inside is not null) return inside.Days;
        var m = Week.MondayOf(date);
        return moves.MinBy(x => m < x.From.FirstMonday ? x.From.FirstMonday.DayNumber - m.DayNumber : m.DayNumber - x.From.LastMonday.DayNumber)!.Days;
    }

}
