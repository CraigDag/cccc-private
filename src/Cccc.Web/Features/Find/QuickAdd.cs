using System.Collections.Immutable;
using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Features.Find;

/// <summary>
/// A class typed into Find in one line, the way a person says it: "aerial ba 1 fri 13-14:30 aerial hall dag".
/// The line is read for what it names (a class, groups or a programme, a space, facilitators, a day or a
/// date, a time or a length) and whatever it does not say is taken from the class's most like session
/// (<see cref="Habits.Like"/>). Nothing is saved from here: Enter opens the add form on the draft, to be
/// looked over first, so a misreading is seen twice before it is anything. Nothing here is logged.
/// </summary>
public static class QuickAdd
{
    /// <summary>What a line was read as: the class to add, the session the rest came from, and the words not understood.</summary>
    public sealed record Reading(Session Draft, Session? Like, IReadOnlyList<string> NotUnderstood);

    /// <summary>
    /// The line read as a class to add, or null when it says no class (or title) and no time to add it at: a
    /// time, or a day with a class whose usual time can be taken. <paramref name="shown"/> is the day the plan
    /// is on, so "fri" means that week's Friday if it has not gone, and otherwise the next.
    /// </summary>
    public static Reading? Read(Timetable t, string line, DateOnly today, DateOnly shown)
    {
        var words = TypedLine.Words(line);
        if (words.Count == 0) return null;
        var unclear = new List<string>();

        // 1. Names, longest first, before anything else: "BA 1" is a group before "1" can be an hour.
        var names = TypedLine.Names(t);
        var found = TypedLine.Whole(words, names, unclear);

        bool everyone = false;
        for (int i = 0; i < words.Count; i++)
        {
            if (words[i].Used) continue;
            if (words[i].Is("everyone", "everybody")) { words[i].Used = true; everyone = true; }
            else if (i + 1 < words.Count && !words[i + 1].Used
                     && (words[i].Is("whole") && words[i + 1].Is("school", "building") || words[i].Is("all") && words[i + 1].Is("groups")))
            { words[i].Used = words[i + 1].Used = true; everyone = true; }
        }

        // 2. A time from one to another: "13-14:30" in one word, or "13 to 14:30" in three.
        TimeOnly? start = null, end = null;
        for (int i = 0; i < words.Count && start is null; i++)
        {
            if (words[i].Used) continue;
            if (TypedLine.RangeIn(words[i].Raw) is { } r)
            { (start, end) = r; words[i].Used = true; }
            else if (i + 2 < words.Count && !words[i + 1].Used && !words[i + 2].Used && TypedLine.IsDash(words[i + 1])
                     && TypedLine.Range(words[i].Raw, words[i + 2].Raw) is { } r3)
            { (start, end) = r3; words[i].Used = words[i + 1].Used = words[i + 2].Used = true; }
        }

        // 3. Days, dates, times and lengths.
        DateOnly? date = null;
        TimeSpan? length = null;
        for (int i = 0; i < words.Count; i++)
        {
            var w = words[i];
            if (w.Used) continue;
            var next = TypedLine.Next(words, i);
            if (date is null && TypedLine.DateAt(words, i, today, shown) is { } d) date = d;
            else if (w.Is("at", "@", "from", "kl", "kl.") && next is not null && start is null && TypedLine.Clock(next.Raw, bare: true) is { } at)
            { start = at; w.Used = next.Used = true; }
            else if (w.Is("noon", "midday") && start is null) { start = new TimeOnly(12, 0); w.Used = true; }
            else if (TypedLine.Clock(w.Raw, bare: false) is { } clock && (start is null || end is null && clock > start))
            {
                if (start is null) start = clock; else end = clock;
                w.Used = true;
            }
            else if (length is null && TypedLine.LengthOf(w.Raw) is { } len) { length = len; w.Used = true; }
            else if (length is null && next is not null && TypedLine.IsNumber(w.Raw) && TypedLine.LengthOf(w.Raw + next.Raw) is { } len2)
            { length = len2; w.Used = next.Used = true; }
        }

        // 4. A bare number is the hour only when nothing else said one: "aerial fri 13".
        if (start is null && words.FirstOrDefault(w => !w.Used && TypedLine.IsHour(w.Raw)) is { } bare && TypedLine.Clock(bare.Raw, bare: true) is { } hour)
        { start = hour; bare.Used = true; }

        // 5. The start of one name and of no other: "acro", "lindq".
        found.AddRange(TypedLine.Starts(words, names, unclear));

        var cls = found.FirstOrDefault(n => n.Kind == "class");
        var groups = found.Where(n => n.Kind == "group").Select(n => n.Id).Distinct().ToList();
        var programme = found.FirstOrDefault(n => n.Kind == "programme");
        var room = found.FirstOrDefault(n => n.Kind == "room");
        var people = found.Where(n => n.Kind == "facilitator").Select(n => n.Id).Distinct().ToList();
        var notUnderstood = new List<string>();
        // A second class, programme or space is not what the line is about: said, not quietly used.
        notUnderstood.AddRange(found.Where(n => n.Kind is "class" or "programme" or "room").GroupBy(n => n.Kind).SelectMany(g => g.Skip(1)).Select(n => n.Name));

        // What is left is the title when no class is named ("staff meeting"), from its first word to its last.
        var left = TypedLine.Left(words);
        string title = "";
        if (left.Count > 0)
        {
            var span = string.Join(" ", words.Skip(left[0]).Take(left[^1] - left[0] + 1).Where(w => !w.Used).Select(w => w.Raw));
            if (cls is null) title = char.ToUpper(span[0], NameOrder.Culture) + span[1..];
            else notUnderstood.AddRange(left.Select(i => words[i].Raw));
        }
        notUnderstood.AddRange(unclear);
        if (cls is null && title.Length == 0) return null;

        var day = date ?? shown;
        var classId = cls?.Id;
        var forGroups = groups.Union(programme is null ? [] : t.Groups.Where(g => g.ProgrammeId == programme.Id).Select(g => g.Id)).ToList();
        var like = classId is { } id && (start is not null || date is not null || length is not null)
            ? Habits.Like(t, id, day, start, forGroups, people) : null;
        bool usedLike = false;
        if (start is null)
        {
            if (like is null) return null;
            start = like.Start;
            usedLike = true;
        }
        if (end is null)
        {
            var len = length ?? like?.Length ?? TimeSpan.FromHours(1);
            if (length is null && like is not null) usedLike = true;
            int until = start.Value.Hour * 60 + start.Value.Minute + (int)len.TotalMinutes;
            end = until >= 24 * 60 ? new TimeOnly(23, 45) : new TimeOnly(until / 60, until % 60);
        }
        var s = Edits.Snap(start.Value);
        var e = Edits.Snap(end.Value);

        Audience audience;
        if (everyone) audience = Audience.Everyone;
        else if (groups.Count == 0 && programme is not null) audience = Edits.ForProgramme(t, programme.Id);
        else if (forGroups.Count == 1) audience = Audience.ForGroup(forGroups[0]);
        else if (forGroups.Count > 1) audience = Audience.ForGroups(forGroups);
        else if (like is not null) { audience = like.Audience; usedLike = true; }
        else audience = new Audience { Kind = AudienceKind.Group };

        Guid? roomId = room?.Id;
        if (roomId is null && like?.RoomId is { } likeRoom) { roomId = likeRoom; usedLike = true; }

        ImmutableList<Assignment> who = [.. people.Select(p => new Assignment(p))];
        if (who.IsEmpty && like is { Facilitators.IsEmpty: false })
        {
            // Who was in only part of it keeps that part only at the very same times.
            who = like.Start == s && like.End == e ? like.Facilitators : [.. like.Facilitators.Select(a => new Assignment(a.FacilitatorId))];
            usedLike = true;
        }

        var draft = new Session
        {
            Date = day, Start = s, End = e, Title = cls?.Name ?? title, ClassId = classId,
            Audience = audience, RoomId = roomId, Facilitators = who,
        };
        return new Reading(draft, usedLike ? like : null, notUnderstood);
    }

    /// <summary>The line as Find shows it, first in the list: what would be added, and where the rest came from.</summary>
    public static FindHit Hit(Timetable t, Reading r)
    {
        var s = r.Draft;
        var when = Cccc.Domain.Week.IsScratch(s.Date) ? "Scratch Week " + Cccc.Domain.Week.ShortDay(s.Date.DayOfWeek) : Cccc.Domain.Week.ShortDate(s.Date);
        var parts = new List<string>
        {
            s.Audience.Kind == AudienceKind.Group && s.Audience.GroupIds.Count == 0 ? "no group yet" : Looks.Audience(t, s),
            t.Rooms.Find(s.RoomId)?.Name ?? "no space",
            s.Facilitators.Count == 0 ? "nobody teaching yet" : Looks.Names(t, s.FacilitatorIds),
        };
        if (r.Like is { } like) parts.Add("the rest as on " + Cccc.Domain.Week.ShortDate(like.Date));
        if (r.NotUnderstood.Count > 0) parts.Add("not understood: " + string.Join(", ", r.NotUnderstood));
        return new FindHit("quick-add", $"Add {Looks.Title(t, s)} · {when} {Looks.When(s)}", string.Join(" · ", parts), null, Looks.ColourOf(t, s), s);
    }

    /// <summary>
    /// The same line read for a coach who adds no classes: asking for its space then, for what it names. Enter opens
    /// the ask on it (<c>SpaceAskForm</c>), to be looked over and sent; whoever it names to teach, the ask is theirs.
    /// </summary>
    public static FindHit AskHit(Timetable t, Reading r)
    {
        var s = r.Draft;
        var parts = new List<string>
        {
            Looks.Title(t, s),
            s.Audience.Kind == AudienceKind.Group && s.Audience.GroupIds.Count == 0 ? "no group yet" : Looks.Audience(t, s),
        };
        if (r.Like is { } like) parts.Add("the rest as on " + Cccc.Domain.Week.ShortDate(like.Date));
        if (r.NotUnderstood.Count > 0) parts.Add("not understood: " + string.Join(", ", r.NotUnderstood));
        return new FindHit("quick-space", $"Ask for {t.Rooms.Find(s.RoomId)?.Name ?? "a space"} · {Cccc.Domain.Week.ShortDate(s.Date)} {Looks.When(s)}",
            string.Join(" · ", parts), null, Looks.ColourOf(t, s), s);
    }
}
