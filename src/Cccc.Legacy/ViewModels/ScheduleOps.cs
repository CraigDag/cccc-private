using CCCCScheduler.Helpers;
using CCCCScheduler.Models;

namespace CCCCScheduler.ViewModels;

/// <summary>Small, UI-free, unit-testable operations over sessions (search match, clone).</summary>
public static class ScheduleOps
{
    /// <summary>
    /// Case-insensitive substring match across a session row's visible fields — <b>and the name of the
    /// class it is linked to</b>, which is the one matched field the row does not display.
    ///
    /// <para>That exception is what a catalogue merge costs. Merging leaves every session's title
    /// exactly as it was on purpose, so afterwards two sessions legitimately show <i>Floor
    /// Acrobatic</i> and <i>Floor Acrobatics</i> while being one activity. Matching on
    /// <see cref="SessionRow.Title"/> alone therefore split them again at the filter — and
    /// <c>ExportFilters.Apply</c> inherits this predicate, so <b>Export filtered report as PDF
    /// silently omitted half a merged class</b>. A merge that quietly breaks an export is worse than
    /// the split it was meant to fix.</para>
    ///
    /// <para>It widens the filter to the class and no further: a row still matches only text that
    /// appears in one of its own fields or in its own class's name.</para>
    /// </summary>
    public static bool Matches(SessionRow r, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        string q = query.Trim();
        bool C(string? s) => s is not null && s.Contains(q, StringComparison.OrdinalIgnoreCase);
        return C(r.RoomName) || C(r.DayName) || C(r.Title) || C(r.ClassName)
               || C(r.GroupName) || C(r.FacilitatorNames) || C(r.Notes);
    }

    /// <summary>
    /// The combined timetable filter: optional "only this room", optional facilitator, optional group,
    /// optional strand, optional student, optional day, a free-text query, and whether to drop the
    /// open sessions. Empty Guid / null day / blank query / <c>false</c> mean "no filter on that
    /// dimension", and the dimensions combine as AND.
    ///
    /// <para><b>The student filter is the facilitator filter's exact mirror</b>, one list across:
    /// membership of the session's roster rather than of its teaching staff. That is what makes an
    /// individual student timetable fall out of the machinery that already produces an individual
    /// facilitator one — including, via <see cref="ExportFilters.Apply"/>, in PDF, ICS and CSV.</para>
    ///
    /// <para><b>The day filter narrows to one day and does nothing else.</b> In the owner's words: "if
    /// you're planning for a day then only the things you add on that day populate that view." It
    /// behaves exactly like the four id filters beside it — including that a day with nothing on it
    /// shows an empty timetable rather than falling back to the whole week, which is the same answer a
    /// facilitator who teaches nothing already gets.</para>
    ///
    /// <para><b>Read on <see cref="SessionRow.Day"/> rather than on <c>Entry.Day</c>, and since schema
    /// 12 those are genuinely two different values.</b> A one-off change can move a class to another
    /// day of one week, so on the resolved basis the row carries the day the occurrence FELL on and
    /// the entry still carries the day it usually runs. Filtering an exported week to "Thursday" must
    /// give the person the class that was moved onto the Thursday and not the one that was moved off
    /// it — this predicate sits between that choice and every filtered PDF, ICS and CSV the school
    /// sends out. This remark used to say the two were the same value; they were, and the sentence
    /// was a reason nobody would re-open, which is why it is written out here instead.</para>
    ///
    /// <para><b><paramref name="hideOpenSessions"/> is the one dimension that is not a value somebody
    /// picked from a list</b> — it drops every session whose class is marked <i>Open to anyone, not
    /// required</i>, which is Open Training and the reason it exists. The owner, 2026-09-13, filtering
    /// his week to BA Students: <i>"OPEN TRAINING can be attended by external people and adding it on
    /// the academic timetable stretches our timetable out by several hours on a Wednesday only. For
    /// the school Admin, seeing all these classes is very useful but for students it is not."</i>
    /// A whole-building session still matches every cohort filter, unchanged and deliberately — see
    /// <see cref="ScheduleEntry.IsFor"/>; this asks a different question, about whether the session is
    /// part of the academic timetable at all, and it is answered by the class rather than by the
    /// audience. <see cref="SessionWorkload.IsOpenToAnyone"/> is that rule, and lunch is not one.</para>
    ///
    /// <para><b>Every dimension is a required parameter.</b> None of them defaults, because a caller
    /// that forgets one would silently widen an export rather than fail to compile — and there is one
    /// caller (<see cref="ExportFilters.Apply"/>) between this predicate and every filtered PDF, ICS
    /// and CSV the school sends out.</para>
    /// </summary>
    public static bool PassesFilters(SessionRow row, Guid roomFilterId,
        Guid facilitatorFilterId, Guid groupFilterId, Guid strandFilterId,
        Guid studentFilterId, DayOfWeek? dayFilter, string? search, bool hideOpenSessions)
    {
        // A room-less row (row.Room null) never matches a specific room filter, and always matches
        // "no filter on this dimension" (roomFilterId == Guid.Empty), same as any other row.
        if (roomFilterId != Guid.Empty && row.Room?.Id != roomFilterId) return false;
        if (facilitatorFilterId != Guid.Empty && !row.Entry.FacilitatorIds.Contains(facilitatorFilterId)) return false;
        if (groupFilterId != Guid.Empty && !row.Entry.IsFor(groupFilterId)) return false;
        if (strandFilterId != Guid.Empty && !row.StrandIds.Contains(strandFilterId)) return false;
        if (studentFilterId != Guid.Empty && !row.RosterIds.Contains(studentFilterId)) return false;
        if (dayFilter is DayOfWeek day && row.Day != day) return false;
        // Last of the seven early returns, and read off the row rather than looked up here: the kind
        // is resolved once per row in MainViewModel.BuildRow through SessionWorkload, so this
        // predicate stays a comparison and the "no class means counted" rule stays in one place.
        if (hideOpenSessions && SessionWorkload.IsOpenToAnyone(row.Workload)) return false;
        return Matches(row, search);
    }

    /// <summary>
    /// The strands <paramref name="entry"/> is on for, resolved through
    /// <see cref="ScheduleEntry.CohortIds"/> and each named cohort's <see cref="Group.StrandId"/> — a
    /// group pointing at a strand the project no longer holds counts as unassigned, exactly as
    /// <see cref="ScheduleEntry.IsFor"/> ignores a stale group. <b>The one implementation of "which
    /// strands is this session on for"</b>: <c>MainViewModel.BuildRow</c> calls it to build
    /// <see cref="SessionRow.StrandIds"/> (the grid's and every export's answer, via
    /// <see cref="PassesFilters"/>), and <c>WeekTally.FilterFor</c> calls it directly (the dock's
    /// answer) — so the two cannot start describing different weeks the way a hand-written second
    /// resolution of the same question once let them.
    ///
    /// <para>A <see cref="SessionScope.WholeBuilding"/> session is on for every live strand, so this
    /// names all of them rather than none — the strand-level mirror of
    /// <see cref="ScheduleEntry.IsFor"/> answering <c>true</c> for every cohort.</para>
    /// </summary>
    public static IReadOnlySet<Guid> StrandsFor(Building building, ScheduleEntry entry)
    {
        var live = building.Strands.Select(s => s.Id).ToHashSet();
        if (entry.Scope == SessionScope.WholeBuilding) return live;

        var strands = new HashSet<Guid>();
        foreach (var groupId in entry.CohortIds)
        {
            var group = building.Groups.FirstOrDefault(g => g.Id == groupId);
            if (group?.StrandId is Guid sid && live.Contains(sid)) strands.Add(sid);
        }
        return strands;
    }

    /// <summary>
    /// Clones every session on <paramref name="from"/> onto <paramref name="to"/>, <b>within the week
    /// containing <paramref name="anyDateInWeek"/></b>; returns the count.
    ///
    /// <para><b>It appends, and does not clear the destination day.</b> That is deliberate and it is
    /// old: this gesture is <i>"and Thursday is like Tuesday as well"</i>, over a day somebody is
    /// looking at. It is also the reason <see cref="CopyWeek"/> — which nobody is looking at the
    /// destination of, and which can be pointed at ten weeks at once — <b>replaces</b> instead: an
    /// appended week doubles every session in it, and a doubled session is only ever noticed if it
    /// collides on a room, a facilitator or a student. A Lunch row has none of the three.</para>
    /// </summary>
    public static int CopyDay(Building building, DayOfWeek from, DayOfWeek to, DateOnly anyDateInWeek)
    {
        if (from == to) return 0;
        // SessionsInWeek, not room-by-room: a room-less source session must be copied too, or this
        // silently drops it — the same class of bug SessionsByRoom would cause on any other
        // day/time-keyed surface. Clone already carries RoomId across (copied, not re-derived), so the
        // result is added straight to Sessions rather than through AddSession, which needs a live Room
        // to stamp one with. .ToList() first: adding to Sessions while enumerating it throws.
        //
        // Copying Tuesday onto Thursday means THIS week's Tuesday onto THIS week's Thursday. Before
        // separate weeks there was one Tuesday and the question could not be asked; there are now as
        // many as the year has, and reading them all would put every Tuesday of the year on one
        // Thursday. The clone keeps the source's own WeekMonday, so the copy lands in the same week.
        var clones = building.SessionsInWeek(anyDateInWeek).Where(s => s.Day == from).Select(s => Clone(s, to)).ToList();
        // Into the list the week lives in: Copy day on the Scratch Week stays in the Scratch Week.
        building.ListForWeek(anyDateInWeek).AddRange(clones);
        return clones.Count;
    }

    /// <summary>
    /// <b>Copies one week's whole timetable onto another week, replacing whatever was there.</b> The
    /// owner's call, 2026-09-14: with weeks separate, a week after the current one starts blank and
    /// this is how it stops being blank.
    ///
    /// <para><b>It REPLACES the destination and never merges into it</b>, which is the one rule this
    /// operation cannot ship without. <see cref="CopyDay"/> appends, for its own good reasons stated
    /// there, and inheriting that here would double every session in the destination week —
    /// invisibly, because a duplicate is only reported when it collides on a room, a facilitator or a
    /// student, and the school's own Lunch rows carry none of the three. The caller is expected to
    /// have said how many sessions are about to go: <see cref="SessionCountIn"/> is that number.</para>
    ///
    /// <para><b>Nothing that happened to the source week is carried across.</b>
    /// <see cref="Clone"/> already drops <see cref="ScheduleEntry.Attendance"/> and
    /// <see cref="ScheduleEntry.Exceptions"/> — a register taken in week 38 is not a fact about week
    /// 39, and week 38's cancellation would silently cancel week 39. What a copy inherits is what the
    /// session <i>is</i>; see <see cref="Clone"/> for the full list and the guard that holds it.</para>
    ///
    /// <para>Answers how many sessions the destination week ended up with. Copying a blank week is
    /// legitimate and lands as zero — it is how a week that was filled by mistake is emptied again,
    /// and refusing it would be this app telling somebody their own timetable is wrong.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The project's weeks are not separate, so there are no weeks to copy between — only one shared
    /// pattern. A caller reaching here has skipped <see cref="Building.WeeksAreSeparate"/>, and the
    /// alternative to throwing is stamping a WeekMonday onto a pattern session, which silently makes
    /// the project half-converted.
    /// </exception>
    public static int CopyWeek(Building building, DateOnly fromAnyDateInWeek, DateOnly toAnyDateInWeek)
    {
        ArgumentNullException.ThrowIfNull(building);
        if (!building.WeeksAreSeparate)
            throw new InvalidOperationException(
                "This project has one shared weekly pattern rather than separate weeks, so there is no " +
                "week to copy to. Give it separate weeks first.");

        var from = WeekLabel.MondayOf(fromAnyDateInWeek);
        var to = WeekLabel.MondayOf(toAnyDateInWeek);
        // THE SCRATCH WEEK IS ONE END OR THE OTHER OF TWO OF THE THREE COPIES THIS DOES (schema 24):
        // a real week into it is "Copy this week to Scratch Week", it onto a real week is "Place onto a
        // week…". Each end reads and writes the list that week lives in, and Clone re-stamps every copy
        // with the destination's Monday — which is the whole of what makes a placed copy count again
        // and a copied-in one stop counting. See Building.ScratchSessions.
        var source = building.ListForWeek(from);
        var destination = building.ListForWeek(to);
        if (from == to) return destination.Count(s => s.WeekMonday == to);

        // Read the source BEFORE the destination is emptied. The two lists are disjoint by
        // construction (different WeekMonday), so the order cannot matter today — it is written this
        // way so that it still cannot on the day somebody adds a second stamp to a session.
        var clones = source.Where(s => s.WeekMonday == from)
                           .Select(s => Clone(s, newWeek: to))
                           .ToList();
        destination.RemoveAll(s => s.WeekMonday == to);
        destination.AddRange(clones);
        return clones.Count;
    }

    /// <summary>
    /// How many sessions the week containing <paramref name="anyDateInWeek"/> holds — <b>zero for a
    /// blank week</b>. What a copy's confirmation counts before it replaces anything, and what the
    /// calendar asks before it says a week has no timetable yet.
    /// </summary>
    public static int SessionCountIn(Building building, DateOnly anyDateInWeek)
        => building.SessionsInWeek(anyDateInWeek).Count();

    /// <summary>
    /// <b>How many registers were taken in the week containing <paramref name="anyDateInWeek"/></b> —
    /// one per session per week, so this counts sessions that have a record for that week rather than
    /// students marked on them.
    ///
    /// <para><b>It exists because <see cref="CopyWeek"/> is destructive in a way its own count does
    /// not describe.</b> A copy REPLACES the destination week: the sessions there are removed and
    /// clones put in their place, and <see cref="Clone"/> deliberately does not carry
    /// <see cref="ScheduleEntry.Attendance"/> across — a register taken in week 38 is not a fact
    /// about week 39. So the registers in the destination go with the sessions that held them, and
    /// "4 weeks already have a timetable" is true while saying nothing about the thing that cannot be
    /// typed back in from memory.</para>
    ///
    /// <para><b>Direction has nothing to do with it, and the guard is deliberately not written in
    /// terms of one.</b> Craig asked for the warning when copying BACKWARDS, 2026-09-18, which is
    /// where it will nearly always fire: a week ahead of the one you are standing on has not happened,
    /// so nobody has stood in a room and recorded it. But a week can be behind the source and hold
    /// nothing, and a project can be opened on an early week with registers filed ahead of it — so
    /// the question asked is whether the destination HAS any, which is the fact that matters, rather
    /// than which way the copy is going, which only correlates with it.</para>
    ///
    /// <para><b>Counted over the week's own stamped sessions</b>, so a record whose
    /// <see cref="AttendanceRecord.WeekMonday"/> names another week — which a session belonging to
    /// this week should not carry, and which <c>SeparateTheWeeks</c> files onto the nearest week
    /// precisely so that it cannot be lost — is not counted here. The two checks are the same
    /// question asked of the same list; this one is about what a copy is about to remove.</para>
    /// </summary>
    public static int RegisterCountIn(Building building, DateOnly anyDateInWeek)
    {
        ArgumentNullException.ThrowIfNull(building);
        var monday = WeekLabel.MondayOf(anyDateInWeek);
        return building.SessionsInWeek(anyDateInWeek)
            .Count(s => s.Attendance.Any(a => WeekLabel.MondayOf(a.WeekMonday) == monday));
    }

    /// <summary>
    /// <b>Turns one shared weekly pattern into separate weeks</b>, writing the pattern onto each of
    /// <paramref name="weeks"/> and leaving every other week of the year blank. The one door onto
    /// <see cref="Building.WeeksAreSeparate"/>, and the thing the schema-13 migration deliberately
    /// does not do for anybody — see <c>ProjectFile.CurrentSchemaVersion</c> for why a migration
    /// cannot choose these weeks.
    ///
    /// <para><b>What already happened to a particular week goes to that week.</b> A pattern session's
    /// <see cref="ScheduleEntry.Exceptions"/> and <see cref="ScheduleEntry.Attendance"/> are both
    /// already keyed by the Monday they belong to, so each is handed to the copy for its own week and
    /// to no other. Copying them wholesale would cancel a class in every week it was ever cancelled in
    /// and file one register under fifty-two; dropping them would throw away every register the school
    /// has taken. Neither is acceptable, and the keys mean neither is necessary — which is the one
    /// respect in which this is not <see cref="Clone"/>, and the reason it is not written in terms of
    /// it.</para>
    ///
    /// <para><b>A record belonging to a week outside <paramref name="weeks"/> is kept, on the closest
    /// week that IS in the list.</b> Registers are the school's own evidence that somebody stood in a
    /// room, and the alternative — dropping a register because the week it was taken in was not one
    /// the person converting happened to tick — is silent destruction of exactly the data this whole
    /// change exists to preserve. Answers how many sessions the project ended up with.</para>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The project already has separate weeks. Running this over one would replace every week that is
    /// in the list and silently leave every week that is not, which is not a conversion but a partial
    /// overwrite nobody asked for.
    /// </exception>
    public static int SeparateTheWeeks(Building building, IEnumerable<DateOnly> weeks)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(weeks);
        if (building.WeeksAreSeparate)
            throw new InvalidOperationException("This project already has a timetable for each week.");

        var mondays = weeks.Select(WeekLabel.MondayOf).Distinct().OrderBy(d => d).ToList();

        // THE PATTERN, NOT THE SESSION LIST. Since schema 14 the list can also hold the copies of
        // weeks that have already been frozen (Building.FrozenWeeks), and those are what those weeks
        // actually ran. Converting must neither spread them over the year nor throw them away, so the
        // pattern is the sessions belonging to no week, the frozen weeks are carried through as they
        // are, and a week that is both frozen and named keeps its own record rather than being
        // overwritten by a pattern that post-dates it.
        var pattern = building.StandingPattern().ToList();
        var alreadyFrozen = building.FrozenWeeks.ToHashSet();
        var frozenSessions = building.Sessions.Where(e => e.WeekMonday is DateOnly f && alreadyFrozen.Contains(f)).ToList();

        building.WeeksAreSeparate = true;
        // Every week is its own now, so there is nothing left for the record to distinguish.
        building.FrozenWeeks.Clear();

        if (mondays.Count == 0)
        {
            // Separate weeks and no week named: every week blank, and the pattern is gone. It is a
            // legitimate thing to ask for (a year to be typed from scratch) and the caller has the
            // undo step, so it is not refused — but nothing is invented to fill it either. What was
            // already frozen is kept all the same: it is a record of weeks that ran, not a pattern.
            building.Sessions.Clear();
            building.Sessions.AddRange(frozenSessions);
            return frozenSessions.Count;
        }

        var rebuilt = new List<ScheduleEntry>(pattern.Count * mondays.Count + frozenSessions.Count);
        rebuilt.AddRange(frozenSessions);
        foreach (var monday in mondays)
        {
            if (alreadyFrozen.Contains(monday)) continue;   // that week's own record outranks the pattern
            foreach (var source in pattern)
            {
                var copy = Clone(source, newWeek: monday);
                copy.Exceptions.AddRange(source.Exceptions.Where(x => NearestOf(mondays, x.WeekMonday) == monday));
                copy.Attendance.AddRange(source.Attendance.Where(a => NearestOf(mondays, a.WeekMonday) == monday));
                rebuilt.Add(copy);
            }
        }

        building.Sessions.Clear();
        building.Sessions.AddRange(rebuilt);
        return rebuilt.Count;
    }

    /// <summary>
    /// The week in <paramref name="mondays"/> closest to <paramref name="week"/> — where
    /// <see cref="SeparateTheWeeks"/> files a register or a cancellation whose own week was not one of
    /// the weeks being filled. Ties go to the earlier week, which is arbitrary and only reachable for
    /// a record landing exactly between two filled weeks; what matters is that it is deterministic, so
    /// converting the same file twice cannot produce two different projects.
    /// </summary>
    /// <summary>
    /// <b>What the weeks after <paramref name="anyDateInWeek"/> show today</b> — how many of them have
    /// anything on, how many sessions that is between them, and how many of those sessions carry a
    /// register for the week they are on. What <see cref="ClearWeeksAfter"/> would take off the
    /// screen, counted over the same list the screen is drawn from
    /// (<see cref="Building.SessionsInWeek"/>), so the sentence a person reads before pressing and
    /// the weeks they then find blank cannot describe different things.
    ///
    /// <para><b>Counted the same way whichever kind of project it is.</b> With one shared pattern,
    /// every week ahead shows the pattern, so each counts; with separate weeks, each shows its own
    /// copy. The weeks asked about are the year's and every week anything is stamped for, so a
    /// session stamped beyond the year is counted as well as removed.</para>
    /// </summary>
    public static WeeksAhead WhatIsAfter(Building building, IEnumerable<DateOnly> weeksOfTheYear,
                                         DateOnly anyDateInWeek)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(weeksOfTheYear);
        var anchor = WeekLabel.MondayOf(anyDateInWeek);

        int weeks = 0, sessions = 0, registers = 0;
        foreach (var monday in weeksOfTheYear.Select(WeekLabel.MondayOf)
                                             .Concat(building.WeeksWithSessions())
                                             .Where(m => m > anchor)
                                             .Distinct())
        {
            var on = building.SessionsInWeek(monday).ToList();
            if (on.Count == 0) continue;
            weeks++;
            sessions += on.Count;
            registers += on.Count(s => s.Attendance.Any(a => WeekLabel.MondayOf(a.WeekMonday) == monday));
        }
        return new WeeksAhead(weeks, sessions, registers);
    }

    /// <summary>
    /// <b>Leaves every week after <paramref name="anyDateInWeek"/> blank</b>, so that a week ahead has
    /// a timetable only because somebody copied one onto it or built it. Craig, 2026-09-23:
    /// <i>"Subsequent weeks AFTER the current week should be blank and contain no information, this
    /// means that a week has to be copied to the following week or written from scratch. This will
    /// ensure we can't be on a wrong week by accident just because it looks like the right week from
    /// classes alone."</i> It reverses his 2026-09-14 rule that the future follows one pattern and is
    /// never blank, and he chose that it happens on a press rather than by itself.
    ///
    /// <para><b>A project with one shared pattern is converted on the way</b>, because a pattern is
    /// by definition what every week ahead shows, and so cannot coexist with a blank one. The pattern
    /// is written onto the anchor week only, and every week already frozen keeps the record of what
    /// it ran — <see cref="SeparateTheWeeks"/> is that conversion and this is it with one week named.
    /// A register or one-off change a pattern session held for a week ahead is filed on the anchor's
    /// copy under its own week, as that conversion always files a record whose week is not being
    /// filled, which keeps it without drawing it anywhere.</para>
    ///
    /// <para><b>A project with separate weeks loses the sessions stamped after the anchor</b>,
    /// registers and all. <see cref="WhatIsAfter"/> is what the caller shows first; undo is the way
    /// back.</para>
    /// </summary>
    public static void ClearWeeksAfter(Building building, DateOnly anyDateInWeek)
    {
        ArgumentNullException.ThrowIfNull(building);
        var anchor = WeekLabel.MondayOf(anyDateInWeek);

        if (!building.WeeksAreSeparate)
            SeparateTheWeeks(building, new[] { anchor });

        building.RemoveSessions(s => s.WeekMonday is DateOnly w && WeekLabel.MondayOf(w) > anchor);
    }

    private static DateOnly NearestOf(IReadOnlyList<DateOnly> mondays, DateOnly week)
    {
        var target = WeekLabel.MondayOf(week);
        var best = mondays[0];
        int bestGap = Math.Abs(best.DayNumber - target.DayNumber);
        foreach (var m in mondays)
        {
            int gap = Math.Abs(m.DayNumber - target.DayNumber);
            if (gap < bestGap) { best = m; bestGap = gap; }
        }
        return best;
    }

    /// <summary>
    /// Deep-clones a session (new Id, copied facilitator list and roster), optionally onto another day.
    ///
    /// <para><b>A hand-written field list, and that is the hazard.</b> It is reached by Copy a day —
    /// one gesture that creates a whole day of sessions at once — so a field added to
    /// <see cref="ScheduleEntry"/> and not added here is silently absent from every copy, with the
    /// grid looking perfect. <c>ScheduleEntryCloneCoverageTests</c> is what actually holds this list to
    /// the type: it walks <see cref="ScheduleEntry"/>'s properties by reflection and fails unless each
    /// one is either proved to arrive on a clone or named, with a reason, in the not-copied list below.
    /// A new property fails the build until its author has picked a side — which is the decision that
    /// was skipped, silently, when the roster was added and Copy a day stopped carrying it.</para>
    ///
    /// <para>A clone inherits what the session <i>is</i>, not what has happened to it.
    /// <list type="bullet">
    /// <item><see cref="ScheduleEntry.ClassId"/> IS copied, and <b>verbatim</b> — never re-derived
    /// from the title. After a catalogue merge a session's title and its class's name legitimately
    /// differ, and re-deriving would quietly undo the merge. Without it a copied day would arrive
    /// titled correctly and linked to nothing, and a whole day's hours would vanish from every
    /// per-activity tally.</item>
    /// <item><see cref="ScheduleEntry.StudentIds"/> IS copied — the roster is part of what the
    /// session is, and re-entering it by hand on every copied day was pure friction.</item>
    /// <item><see cref="ScheduleEntry.WeekMonday"/> IS copied, and is the one property a caller can
    /// redirect: <c>newWeek</c> is what makes <see cref="CopyWeek"/> a copy forward rather than a
    /// second copy of week 38 inside week 38. Null on both sides is the ordinary state of a project
    /// whose weeks are not separate.</item>
    /// <item><see cref="ScheduleEntry.Attendance"/> is not — it is a record of past weeks, and
    /// could not mean anything on a session that has not happened yet.</item>
    /// <item><see cref="ScheduleEntry.Exceptions"/> are not — inheriting Monday's cancellation
    /// would silently cancel the Friday you copied it to.</item>
    /// </list></para>
    /// </summary>
    public static ScheduleEntry Clone(ScheduleEntry e, DayOfWeek? newDay = null, DateOnly? newWeek = null) => new()
    {
        Id = Guid.NewGuid(),
        Day = newDay ?? e.Day,
        // The source's own week unless the caller names another, which is exactly the shape newDay
        // already has and for the same reason: a clone stays where its source was until something
        // moves it. Copy a day moves the day and keeps the week; Copy a week moves the week and keeps
        // the day. Normalised here rather than trusted, because every comparison this value takes part
        // in is an equality against a Monday.
        WeekMonday = newWeek is DateOnly w ? WeekLabel.MondayOf(w) : e.WeekMonday,
        Start = e.Start,
        End = e.End,
        Title = e.Title,
        ClassId = e.ClassId,
        GroupId = e.GroupId,
        Scope = e.Scope,
        GroupIds = new List<Guid>(e.GroupIds),
        ProgrammeId = e.ProgrammeId,
        RoomId = e.RoomId,
        FacilitatorIds = new List<Guid>(e.FacilitatorIds),
        FacilitatorParts = e.FacilitatorParts
            .Select(p => new FacilitatorPart { FacilitatorId = p.FacilitatorId, From = p.From, Until = p.Until })
            .ToList(),
        StudentIds = new List<Guid>(e.StudentIds),
        ExcludedStudentIds = new List<Guid>(e.ExcludedStudentIds),
        Notes = e.Notes,
        ColourHex = e.ColourHex,
        Provisional = e.Provisional,
    };

    // ---- Programmes (schema 17) --------------------------------------------------------------------

    /// <summary>
    /// <b>Brings every whole-programme session's list of groups up to date</b> with the programme's
    /// groups as they are now, and answers how many sessions changed. See
    /// <see cref="SessionScope.Programme"/> for why the list is kept rather than looked up.
    ///
    /// <list type="bullet">
    /// <item><b>A week that has run is left as it ran.</b> A frozen week's copy keeps the groups it had,
    /// so a Year Two created in the summer does not appear on last September's classes.</item>
    /// <item><b>A programme that no longer exists</b> leaves its sessions on the groups they already
    /// named, as named groups — the same people on the same classes, rather than nobody.</item>
    /// </list>
    ///
    /// <para>Called by <c>ProjectStore.Validate</c> on every load and by every change to the groups or
    /// the programmes, which are the only two ways the answer can move.</para>
    /// </summary>
    public static int RefreshProgrammeSessions(Building building)
    {
        ArgumentNullException.ThrowIfNull(building);

        var programmes = building.Strands.Select(s => s.Id).ToHashSet();
        int changed = 0;
        foreach (var s in building.AllSessions())
        {
            if (s.Scope != SessionScope.Programme) continue;
            if (s.WeekMonday is { } monday && building.WeekIsFrozen(monday)) continue;

            if (s.ProgrammeId is not { } pid || !programmes.Contains(pid))
            {
                s.Scope = SessionScope.Groups;
                changed++;
                continue;
            }

            var groups = building.Groups.Where(g => g.StrandId == pid).Select(g => g.Id).ToList();
            if (groups.SequenceEqual(s.GroupIds)) continue;
            s.GroupIds = groups;
            changed++;
        }
        return changed;
    }

    // ---- Freezing a week that has ended (schema 14) ---------------------------------------------

    /// <summary>
    /// <b>Writes the standing pattern onto one week and records that week as frozen</b>, answering
    /// how many sessions were written. Zero for a week already frozen, and for a project whose weeks
    /// are separate — such a project has no pattern, and every one of its weeks is already its own.
    ///
    /// <para><b>What already happened to that week goes with it</b>, exactly as in
    /// <see cref="SeparateTheWeeks"/> and for the same reason: a pattern session's
    /// <see cref="ScheduleEntry.Exceptions"/> and <see cref="ScheduleEntry.Attendance"/> are keyed by
    /// the Monday they belong to, so this week's are <i>moved</i> onto this week's copy. Copying them
    /// would cancel the class in every week it was ever cancelled in; leaving them on the pattern
    /// would leave a register filed against a week the pattern is no longer drawn for.</para>
    ///
    /// <para><b><paramref name="movedTo"/> is how a caller that is already holding one of these
    /// sessions finds it again.</b> Freezing replaces, for the frozen week, every object a screen
    /// resolved a moment ago: the pattern entry stays the pattern, and the week the caller is looking
    /// at is now a copy the caller has never seen. Filled in, this maps
    /// <c>(pattern session id, the frozen Monday)</c> to the copy that week now draws, so
    /// <c>MainViewModel.OnItsOwnWeek</c> can re-point a write that names a week. Null when the caller
    /// does not need it, which is every caller that is not about to write.</para>
    /// </summary>
    public static int FreezeWeek(Building building, DateOnly anyDateInWeek,
                                 IDictionary<(Guid PatternId, DateOnly Monday), ScheduleEntry>? movedTo = null)
    {
        ArgumentNullException.ThrowIfNull(building);
        if (building.WeeksAreSeparate) return 0;

        var monday = WeekLabel.MondayOf(anyDateInWeek);
        if (building.FrozenWeeks.Contains(monday)) return 0;

        var copies = new List<ScheduleEntry>();
        foreach (var source in building.StandingPattern().ToList())
        {
            var copy = Clone(source, newWeek: monday);

            // THE ID THE PATTERN AND THE WEEK IMPLY, not a fresh one: two copies of one project that
            // freeze the same week must agree about which session is which, or a register written
            // into the file from one tab cannot be found in the other. See RegisterFiling.FrozenCopyId.
            copy.Id = RegisterFiling.FrozenCopyId(source.Id, monday);
            if (movedTo is not null) movedTo[(source.Id, monday)] = copy;
            copy.Exceptions.AddRange(source.Exceptions.Where(x => WeekLabel.MondayOf(x.WeekMonday) == monday));
            copy.Attendance.AddRange(source.Attendance.Where(a => WeekLabel.MondayOf(a.WeekMonday) == monday));
            source.Exceptions.RemoveAll(x => WeekLabel.MondayOf(x.WeekMonday) == monday);
            source.Attendance.RemoveAll(a => WeekLabel.MondayOf(a.WeekMonday) == monday);
            copies.Add(copy);
        }

        building.Sessions.AddRange(copies);
        building.FrozenWeeks.Add(monday);
        building.FrozenWeeks.Sort();
        return copies.Count;
    }

    /// <summary>
    /// <b>Freezes every week of <paramref name="weeksOfTheYear"/> that has already ended</b>, and
    /// answers how many weeks were frozen. The owner's rule in one method: <i>the future follows one
    /// pattern and is never blank; a week freezes as it ran once it ends.</i>
    ///
    /// <para><b>A week has ended when its Sunday is behind <paramref name="today"/>.</b> The week
    /// containing today is still running and still follows the pattern, which is what makes a
    /// correction made on the Thursday of a live week reach the whole of that week rather than half
    /// of it.</para>
    ///
    /// <para><b>Nothing here reads the clock and nothing here decides which weeks the year has.</b>
    /// Both arrive from the caller — <c>MainViewModel.Today</c>, which the web sets from the browser
    /// because the container's clock is not Copenhagen's, and
    /// <see cref="Helpers.AcademicYear.WeeksFor"/>, which is the one answer to what year this file
    /// covers. A second reading of either here is how the freeze and the calendar would come to
    /// disagree about which week is which.</para>
    ///
    /// <para><b>It is idempotent and it is cheap once run</b>: a week already in
    /// <see cref="Building.FrozenWeeks"/> costs a lookup. That is what lets the one caller be
    /// <c>PushUndo</c> — every change to a project, whatever it changes.</para>
    /// </summary>
    public static int FreezeEndedWeeks(Building building, IEnumerable<DateOnly> weeksOfTheYear, DateOnly today,
                                       IDictionary<(Guid PatternId, DateOnly Monday), ScheduleEntry>? movedTo = null)
    {
        ArgumentNullException.ThrowIfNull(building);
        ArgumentNullException.ThrowIfNull(weeksOfTheYear);
        if (building.WeeksAreSeparate) return 0;

        int frozen = 0;
        foreach (var week in weeksOfTheYear)
        {
            var monday = WeekLabel.MondayOf(week);
            if (monday.AddDays(6) >= today) continue;             // still running, or still to come
            if (building.FrozenWeeks.Contains(monday)) continue;
            FreezeWeek(building, monday, movedTo);
            frozen++;
        }
        return frozen;
    }

    /// <summary>
    /// <b>The week a project with separate weeks opens next year with</b>: the first week at or
    /// after <paramref name="yearStarts"/> that has anything on it, or failing that the earliest week
    /// that has — or null when no week has anything. <see cref="StartNextYear"/> keeps this week and
    /// no other, and <c>NextYearForm</c> counts it, so the form and the rollover cannot come to
    /// describe different weeks.
    /// </summary>
    public static DateOnly? FirstTimetabledWeek(Building building, DateOnly yearStarts)
    {
        ArgumentNullException.ThrowIfNull(building);
        var stamped = building.WeeksWithSessions().ToList();
        return stamped.Where(w => w >= yearStarts).Cast<DateOnly?>().FirstOrDefault()
               ?? stamped.Cast<DateOnly?>().FirstOrDefault();
    }

    /// <summary>
    /// <b>How far next year sits from this one: the length of this year, in whole weeks.</b>
    ///
    /// <para><b>It is 52, and it is derived anyway.</b> That is worth saying because the obvious
    /// reason for deriving it is wrong: a calendar year is 365 or 366 days, which is 52 weeks and one
    /// or two days over, and <see cref="AcademicYear.LastSunday"/> rounds back to a Sunday — so the
    /// spare days are absorbed and a leap year is 52 weeks exactly like every other. Nothing here
    /// ever answers 53. The reason to derive it is the other one: the distance a rollover moves by
    /// and the length of a year are the same fact, and writing 52 here would be a second copy of it
    /// that no test relates to the first. It also answers correctly for the degenerate spans a
    /// hand-edited file can hold, which <c>LastSunday</c> floors at one week.</para>
    /// </summary>
    public static int WeeksInTheYear(TermInfo firstTerm)
    {
        ArgumentNullException.ThrowIfNull(firstTerm);
        var first = AcademicYear.FirstMonday(firstTerm);
        var lastMonday = AcademicYear.LastSunday(firstTerm).AddDays(-6);
        return (lastMonday.DayNumber - first.DayNumber) / 7 + 1;
    }

    /// <summary>
    /// <b><c>Start next year from this one</c>.</b> Rolls the whole project on by the length of its
    /// own year: every term moves forward, every week-stamped timetable moves with them, and
    /// everything that is a record of what actually happened is dropped.
    ///
    /// <para><b>What carries over</b> — the building and its floors, the rooms, the areas, the
    /// people, the groups, the classes, the disciplines, and the timetable itself. That is the point
    /// of the door: next year is this year's shape with an empty history, not a blank file.</para>
    ///
    /// <para><b>What is dropped, and why each one</b>. <b>Registers</b> and <b>absences</b> are a
    /// record of who was in a room on a date that has passed; carrying them forward would assert that
    /// next year's students were marked present before term started. <b>One-off exceptions</b> — a
    /// class moved for one week, a cancellation — are answers to something that happened in a
    /// particular week of the year that is ending, and shifting them forward would state that the
    /// same interruptions are already known about. <b>Frozen weeks</b> go because nothing in the new
    /// year has run yet; a frozen week is the record that one has.</para>
    ///
    /// <para><b>Closures are a CHOICE, and the default is to drop them</b>
    /// (<paramref name="carryClosuresForward"/>). Danish school closures are mostly week-numbered —
    /// efterårsferie is week 42 every year — so shifting them by whole weeks lands most of them
    /// right, which is exactly what makes the ones it does not land right dangerous: Easter moves
    /// against the week numbers, so a shifted Easter closure is a confident, wrong statement that the
    /// building is shut. Dropped, the new year simply has no closures yet and the person enters them
    /// from the calendar they were going to check anyway. Carried, they are a head start that has to
    /// be checked. Both are defensible and the door says which it did; nothing here guesses on the
    /// school's behalf.</para>
    ///
    /// <para><b>Groups are carried over unchanged, deliberately.</b> A cohort that has finished its
    /// programme and a new intake arriving are facts about the school that no rule here can derive —
    /// promoting "BA Semester 1" to "BA Semester 2" is a guess about how this school names its years,
    /// and removing a cohort would throw away its students. So the roster arrives in the new year
    /// exactly as it left the old one, and the door says so, because the alternative is software
    /// quietly deciding who has graduated.</para>
    ///
    /// <para><b>The shift is applied to week stamps, not to days of the week.</b> A standing-pattern
    /// session carries no date at all — it is a day and a time — so it needs no moving and gets none.
    /// Only a week-stamped session moves, and it moves by whole weeks, so a Tuesday class is still on
    /// a Tuesday.</para>
    ///
    /// <para>Answers the number of weeks everything moved by, or <b>0 when there is no term to move
    /// from</b> — a project with no year has no next one, and a caller must not report a rollover
    /// that did not happen.</para>
    /// </summary>
    public static int StartNextYear(ProjectFile project, bool carryClosuresForward = false)
    {
        ArgumentNullException.ThrowIfNull(project);
        if (project.FirstTerm is not { } firstTerm) return 0;

        int weeks = WeeksInTheYear(firstTerm);
        int days = weeks * 7;

        foreach (var term in project.Terms)
            term.StartMonday = term.StartMonday.AddDays(days);

        if (carryClosuresForward)
        {
            foreach (var holiday in project.Holidays)
            {
                holiday.From = holiday.From.AddDays(days);
                holiday.To = holiday.To.AddDays(days);
            }
        }
        else
        {
            project.Holidays.Clear();
        }

        // Dated to the year that is ending, every one of them, and none of them a plan.
        project.Absences.Clear();

        // EVENTS ARE DATED TO THE CALENDAR, NOT TO THE TERM, so none of them moves. An event whose
        // dates reach into the new year is carried — a September audition already booked for next
        // year belongs to that year — and one whose every date has passed stays with the old year's
        // file, where it happened. The terms have already moved, so FirstMonday is the NEW year's.
        var newYear = AcademicYear.FirstMonday(firstTerm);
        project.Events.RemoveAll(e =>
            EventSchedule.RuleDates(e).LastOrDefault().AddDays(e.SpanDays - 1) < newYear);

        var building = project.Building;

        // <b>A week-stamped session means two different things, and the rollover treats them
        // differently.</b> When the weeks are the project's own timetables, every session is stamped,
        // and next year opens with ONE of them: this year's first timetabled week, moved onto next
        // year's, and every other week blank. Craig, 2026-09-23: a week ahead has a timetable only
        // because somebody copied one onto it or built it, so that nobody can be on the wrong week
        // "just because it looks like the right week from classes alone". Moving the whole year
        // forward, which this did until then, would open next year with fifty-two weeks that all look
        // right. When the weeks are not separate, the only stamped sessions are the frozen copies of
        // weeks that have already run, and those are deleted rather than moved -- moving them would
        // leave a session stamped with a week that nothing marks as frozen, which
        // <see cref="Building.SessionsInWeek"/> answers by ignoring it. That is not a tidy
        // representation of last year; it is a session no screen in this app can ever show and no
        // form can ever reach, which is the orphan shape this codebase has been bitten by before.
        if (building.WeeksAreSeparate)
        {
            // The terms have already moved, so the year that is ending starts a year before the one
            // FirstMonday now answers. A week stamped before that year is not this year's first week.
            var kept = FirstTimetabledWeek(building, AcademicYear.FirstMonday(firstTerm).AddDays(-days));

            building.RemoveSessions(s => s.WeekMonday is DateOnly w && WeekLabel.MondayOf(w) != kept);
            foreach (var session in building.AllSessions())
                if (session.WeekMonday is DateOnly stamped)
                    session.WeekMonday = WeekLabel.MondayOf(stamped).AddDays(days);
        }
        else
        {
            foreach (var frozen in building.AllSessions().Where(s => s.WeekMonday is not null).ToList())
                building.RemoveSession(frozen);
        }

        foreach (var session in building.AllSessions())
        {
            session.Exceptions.Clear();
            session.Attendance.Clear();
        }

        // Nothing in the new year has run yet, and a frozen week is the record that one has.
        building.FrozenWeeks.Clear();

        return weeks;
    }
}

/// <summary>
/// What the weeks after one week show: how many have anything on, how many sessions that is between
/// them, and how many of those carry a register for their week. <see cref="ScheduleOps.WhatIsAfter"/>.
/// </summary>
public readonly record struct WeeksAhead(int Weeks, int Sessions, int Registers);
