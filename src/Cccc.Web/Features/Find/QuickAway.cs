using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Ask;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Features.Find;

/// <summary>
/// Somebody away, typed into Find the way it is said: "ada sick", "bo off fri", "dag away 1/12 to 4/12",
/// "cleo out tomorrow 9-12", "alba absent mon-wed", "sick tomorrow" for yourself. A line is about somebody
/// away only when it says so (away, off, out, absent, sick, ill, leave, holiday, can't come) and names one
/// person, or nobody for whoever is signed in. An administrator records it (<see cref="AbsenceForm"/>); a
/// facilitator asks to be away (<c>CantComeForm</c>), for themselves only. Nothing is saved from here, and
/// nothing typed becomes the reason: words that are not understood are said in Find and kept nowhere, as a
/// line about being ill can carry what nobody should store. Nothing here is logged.
/// </summary>
public static class QuickAway
{
    /// <summary>What a line was read as: the absence, and the words not understood.</summary>
    public sealed record Reading(Absence Draft, IReadOnlyList<string> NotUnderstood);

    private static readonly string[] AwayWords = ["away", "off", "out", "absent", "absence", "appointment"];
    private static readonly string[] Ill = ["sick", "ill", "unwell", "illness", "poorly"];
    private static readonly string[] Leave = ["leave", "holiday", "holidays", "vacation", "annual", "approved"];
    private static readonly string[] Myself = ["me", "i", "i'm", "im", "myself"];
    private static readonly string[] Filler = ["is", "am", "are", "be", "will", "was", "i'll", "day", "days"];
    private static readonly string[] Through = ["through", "thru"];

    /// <summary>The last time an absence's window can run to: the form's quarter hours stop at 23:45.</summary>
    private static readonly TimeOnly EndOfDay = new(23, 45);

    /// <summary>
    /// The line read as somebody away, or null when it does not say anybody is, or names nobody (and
    /// <paramref name="me"/>, the facilitator signed in, is nobody either). From today when it says no day;
    /// whole days when it says no time. <paramref name="shown"/> is the day the plan is on, as for a class.
    /// </summary>
    public static Reading? Read(Timetable t, string line, DateOnly today, DateOnly shown, Guid? me)
    {
        var words = TypedLine.Words(line);
        if (words.Count == 0) return null;
        // An absence is on real days: a day named while the Scratch Week is on screen is this week's.
        if (Cccc.Domain.Week.IsScratch(shown)) shown = today;
        var unclear = new List<string>();

        // 1. People, whole and longest first, before anything else: "Bo Lindqvist", then "bo".
        var people = TypedLine.Names(t, students: true).Where(n => n.Kind is "facilitator" or "student").ToList();
        var found = TypedLine.Whole(words, people, unclear);

        // 2. That somebody is away, and what is said about it.
        bool away = false, self = false;
        AbsenceCause? cause = null;
        AbsenceNotice? notice = null;
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (w.Used) continue;
            var next = TypedLine.Next(words, i);
            if (w.Is(AwayWords)) { away = true; w.Used = true; }
            else if (w.Is(Ill)) { away = true; cause ??= AbsenceCause.Illness; w.Used = true; }
            else if (w.Is(Leave)) { away = true; cause ??= AbsenceCause.ApprovedLeave; w.Used = true; }
            else if (w.Is("personal", "family")) { cause ??= AbsenceCause.Personal; w.Used = true; }
            else if (w.Is("planned")) { notice = AbsenceNotice.Planned; w.Used = true; }
            else if (w.Is("unplanned")) { notice = AbsenceNotice.Unplanned; w.Used = true; }
            else if (w.Is("can't", "cant", "cannot", "won't", "wont") && next is not null && next.Is("come", "make", "be"))
            { away = true; w.Used = next.Used = true; }
            else if (w.Is("not") && next is not null && next.Is("in", "coming", "here"))
            { away = true; w.Used = next.Used = true; }
            else if (w.Is("all", "whole", "full") && next is not null && next.Is("day")) w.Used = next.Used = true;
            else if (w.Is(Myself)) { self = true; w.Used = true; }
            else if (w.Is(Filler)) w.Used = true;
        }
        if (!away) return null;

        // 3. Days: one, a stretch from one to another ("mon to wed", "1/12-4/12", "until fri"), or a week.
        DateOnly? from = null, to = null;
        var extra = new List<string>();
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (w.Used) continue;
            var next = TypedLine.Next(words, i);
            if (WeekSaid(w, next, today, shown) is { } week)
            {
                if (from is null) { (from, to) = week; w.Used = next!.Used = true; }
                continue;
            }
            if (w.Raw.IndexOfAny(['-', '–', '—']) is var dash and > 0 && dash < w.Raw.Length - 1 && from is null
                && OneWord(w.Raw[..dash], today, shown, null) is { } a && OneWord(w.Raw[(dash + 1)..], today, shown, a) is { } b)
            {
                (from, to) = (a, b);
                w.Used = true;
                continue;
            }
            bool until = TypedLine.IsDash(w) && !w.Is("-", "–", "—", "to") || w.Is(Through);
            bool joins = TypedLine.IsDash(w) || w.Is(Through);
            if (joins && next is not null && (from is not null || until) && to is null
                && TypedLine.DateAt(words, i + 1, today, shown, from ?? today) is { } end)
            {
                to = end;
                w.Used = true;
                continue;
            }
            if (from is null && TypedLine.DateAt(words, i, today, shown) is { } d) from = d;
            // A second day that does not join the first ("mon wed") is said, in the words it took.
            else if (from is not null && TypedLine.DateAt(words, i, today, shown) is not null) extra.Add(next is { Used: true } ? w.Raw + " " + next.Raw : w.Raw);
        }
        var first = from ?? today;
        var last = to ?? first;
        if (last < first)
        {
            extra.Add("to " + Cccc.Domain.Week.ShortDate(last));
            last = first;
        }

        // 4. Times: "9-12", "9 to 12", "morning", "from 14", "until 11". Whole days when none is said.
        (TimeOnly From, TimeOnly Until)? window = null;
        for (int i = 0; i < words.Count && window is null; i++)
        {
            var w = words[i];
            if (w.Used) continue;
            var next = TypedLine.Next(words, i);
            if (TypedLine.RangeIn(w.Raw) is { } r) { window = r; w.Used = true; }
            else if (i + 2 < words.Count && next is not null && !words[i + 2].Used && TypedLine.IsDash(next)
                     && TypedLine.Range(w.Raw, words[i + 2].Raw) is { } r3)
            { window = r3; w.Used = next.Used = words[i + 2].Used = true; }
        }
        for (int i = 0; i < words.Count && window is null; i++)
        {
            var w = words[i];
            if (w.Used) continue;
            var next = TypedLine.Next(words, i);
            if (w.Is("morning")) { window = (TimeOnly.MinValue, new TimeOnly(12, 0)); w.Used = true; }
            else if (w.Is("afternoon")) { window = (new TimeOnly(12, 0), EndOfDay); w.Used = true; }
            else if (w.Is("evening")) { window = (new TimeOnly(17, 0), EndOfDay); w.Used = true; }
            else if (w.Is("from", "after") && next is not null && TypedLine.Clock(next.Raw, bare: true) is { } at && at < EndOfDay)
            { window = (at, EndOfDay); w.Used = next.Used = true; }
            else if (w.Is("until", "till", "til", "before") && next is not null && TypedLine.Clock(next.Raw, bare: true) is { } by && by > TimeOnly.MinValue)
            { window = (TimeOnly.MinValue, by); w.Used = next.Used = true; }
        }

        // 5. The start of one person's name and of no other: "lindq", "øver".
        found.AddRange(TypedLine.Starts(words, people, unclear));

        var named = found.DistinctBy(n => n.Id).ToList();
        TypedLine.Named? person = named.FirstOrDefault();
        var left = TypedLine.Left(words);
        var notUnderstood = new List<string>();
        if (person is null)
        {
            // Yourself, when you say so or say nothing else: a word that may be somebody's name ("ada" said of two
            // people, or of somebody gone) is never quietly read as you.
            if (!self && (left.Count > 0 || unclear.Count > 0)) return null;
            if (me is not { } mine || t.Facilitators[mine] is not { } f || f.IsFormer()) return null;
            person = new TypedLine.Named("facilitator", mine, f.Name, [], 4);
        }
        else if (self && person.Id != me) notUnderstood.Add("me");
        // One person at a time: anybody else named is said, never quietly left out.
        notUnderstood.AddRange(named.Skip(1).Select(n => n.Name));
        notUnderstood.AddRange(extra);
        notUnderstood.AddRange(left.Select(i => words[i].Raw));
        notUnderstood.AddRange(unclear);

        var draft = new Absence
        {
            Person = person.Kind == "student" ? AbsentPerson.Student : AbsentPerson.Facilitator,
            PersonId = person.Id, From = first, To = last, Cause = cause,
            Notice = notice ?? Absence.NoticeWhenSaid(today, first, cause),
            StartTime = window is { } win ? Edits.Snap(win.From) : null,
            EndTime = window is { } wn ? Edits.Snap(wn.Until) : null,
        };
        if (draft is { StartTime: { } s, EndTime: { } e } && e <= s) draft = draft with { StartTime = null, EndTime = null };
        return new Reading(draft, notUnderstood);
    }

    /// <summary>
    /// The line as Find shows it, first in the list: who would be away and when, what was said about it, and what
    /// it would take them off. <paramref name="records"/> is an administrator's, who records it; otherwise it is
    /// a facilitator asking for themselves, which says what an ask says: only the days go, never why.
    /// </summary>
    public static FindHit Hit(Timetable t, Reading r, bool records, DateOnly today)
    {
        var a = r.Draft;
        var name = a.Person == AbsentPerson.Student ? t.Students[a.PersonId]?.Name : t.Facilitators[a.PersonId]?.Name;
        var parts = new List<string>();
        if (records)
        {
            var said = new List<string>();
            if (a.Cause is { } c) said.Add(CauseWords(c));
            if (a.Notice is { } n) said.Add(n == AbsenceNotice.Planned ? "planned" : "unplanned");
            parts.Add(string.Join(", ", said));
        }
        else if (a.From < today) parts.Add("that day has gone: say from today on");
        parts.Add(WhatItTakes(t, a, records));
        if (!records) parts.Add("only the days are sent, never why");
        if (r.NotUnderstood.Count > 0) parts.Add("not understood: " + string.Join(", ", r.NotUnderstood));
        var label = records ? $"Record {name} away · {When(a)}" : $"Ask to be away · {When(a)}";
        var colour = a.Person == AbsentPerson.Facilitator ? t.Facilitators[a.PersonId]?.Colour : null;
        return new FindHit(records ? "quick-away" : "quick-ask", label, string.Join(" · ", parts.Where(p => p.Length > 0)), null, colour, Away: a);
    }

    /// <summary>"Thu 19 Nov", "Mon 23 Nov – Wed 25 Nov", "Thu 19 Nov 09:00–12:00", "Thu 19 Nov until 12:00".</summary>
    public static string When(Absence a)
    {
        var days = a.To == a.From ? Cccc.Domain.Week.ShortDate(a.From) : $"{Cccc.Domain.Week.ShortDate(a.From)} – {Cccc.Domain.Week.ShortDate(a.To)}";
        if (a is not { StartTime: { } s, EndTime: { } e }) return days;
        return days + " " + (s == TimeOnly.MinValue ? "until " + Cccc.Domain.Week.Hm(e)
            : e == EndOfDay ? "from " + Cccc.Domain.Week.Hm(s)
            : Looks.When(s, e));
    }

    /// <summary>The causes as Today's list says them.</summary>
    public static string CauseWords(AbsenceCause c) => c switch
    {
        AbsenceCause.Illness => "illness",
        AbsenceCause.Personal => "personal",
        AbsenceCause.ApprovedLeave => "approved leave",
        _ => "other",
    };

    /// <summary>What recording it would do to their classes, from <see cref="Away.WhatItLeaves"/>, in a few words.</summary>
    private static string WhatItTakes(Timetable t, Absence a, bool records)
    {
        var leaves = Cccc.Domain.Rules.Away.WhatItLeaves(t, a);
        string them = records ? "their" : "your";
        if (leaves.Count == 0) return $"none of {them} classes";
        if (a.Person == AbsentPerson.Student)
            return leaves.Count == 1 ? "misses " + Briefly(t, leaves[0].Now) : $"misses {leaves.Count} classes";
        if (leaves.Count == 1)
        {
            var (now, after) = leaves[0];
            return $"{Briefly(t, now)}: {AskingWords.After(t, a.PersonId, now, after)}";
        }
        int cover = leaves.Count(x => x.After.NeedsCover);
        int covering = leaves.Count(x => x.Now.Session.CoverIds.Contains(a.PersonId));
        var words = $"takes {(records ? "them" : "you")} off {leaves.Count} classes";
        if (cover > 0) words += $", {cover} would need cover";
        if (covering > 0) words += $", {covering} {(records ? "they cover" : "you cover")}";
        return words;
    }

    private static string Briefly(Timetable t, Occurrence o) =>
        $"{Looks.Title(t, o.Session)} {Cccc.Domain.Week.ShortDay(o.Date.DayOfWeek)} {Cccc.Domain.Week.Hm(o.Start)}";

    /// <summary>"this week" from today (or the plan's later week) to its Sunday; "next week", the whole of the one after.</summary>
    private static (DateOnly, DateOnly)? WeekSaid(TypedLine.Word w, TypedLine.Word? next, DateOnly today, DateOnly shown)
    {
        if (next is null || !next.Is("week")) return null;
        var monday = Cccc.Domain.Week.MondayOf(shown) > Cccc.Domain.Week.MondayOf(today) ? Cccc.Domain.Week.MondayOf(shown) : Cccc.Domain.Week.MondayOf(today);
        if (w.Is("this", "all"))
        {
            var from = monday > today ? monday : today;
            return (from, monday.AddDays(6));
        }
        return w.Is("next") ? (monday.AddDays(7), monday.AddDays(13)) : null;
    }

    /// <summary>A date said in one word, for either side of "mon-wed" or "1/12-4/12".</summary>
    private static DateOnly? OneWord(string raw, DateOnly today, DateOnly shown, DateOnly? onOrAfter) =>
        raw.Length == 0 ? null : TypedLine.DateAt([new TypedLine.Word(raw)], 0, today, shown, onOrAfter);
}
