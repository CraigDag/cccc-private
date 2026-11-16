using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CCCCScheduler.Helpers;

namespace CCCCScheduler.Models;

/// <summary>The facility: its floors, the rooms drawn on them, and the managed groups.</summary>
public sealed class Building
{
    public string Name { get; set; } = "CCCC";

    /// <summary>Observable so the floor switcher updates live when floors are added/removed.</summary>
    public ObservableCollection<FloorPlan> Floors { get; set; } = new();

    /// <summary>Observable so the floor-plan canvas redraws when rooms are added/removed.</summary>
    public ObservableCollection<Room> Rooms { get; set; } = new();

    /// <summary>The programmes. Groups belong to these; see <see cref="Strand"/>.</summary>
    public List<Strand> Strands { get; set; } = new();

    public List<Group> Groups { get; set; } = new();

    public List<Facilitator> Facilitators { get; set; } = new();

    public List<Student> Students { get; set; } = new();

    /// <summary>
    /// The families the school's specialisms fall into — Acrobatic, Aerial, Manipulation, Other. The
    /// parent level of the second hierarchy; see <see cref="Branch"/> for why it is not a
    /// <see cref="Strand"/>. Empty is the normal state of every project that predates them, and of
    /// every new one.
    /// </summary>
    public List<Branch> Branches { get; set; } = new();

    /// <summary>
    /// The specialisms themselves, each optionally under a <see cref="Branch"/>. Students point here
    /// through <see cref="Student.DisciplineId"/>. Empty is the normal state of every project that
    /// predates them, and of every new one — the school types the list it actually teaches.
    /// </summary>
    public List<Discipline> Disciplines { get; set; } = new();

    public List<ClassEntry> Classes { get; set; } = new();

    /// <summary>
    /// <b>What is added to a facilitator's week beyond teaching</b> — prep time, a break that counts as
    /// work. Schema 18; see <see cref="TimeRule"/>. In the order the list shows them.
    /// </summary>
    public List<TimeRule> TimeRules { get; set; } = new();

    /// <summary>
    /// The curriculum areas a class's hours are summed under, each with its own weekly target. See
    /// <see cref="Area"/>. Empty is the normal state of every project that predates them.
    /// </summary>
    public List<Area> Areas { get; set; } = new();

    /// <summary>
    /// Hours of scheduled activity the week is meant to contain, across every area and the classes in
    /// none — <i>"30 scheduled hours"</i> in the administrator's sheet. Deliberately NOT per-area: it
    /// is the second of their two targets and the one that asks whether the week as a whole is full
    /// enough, so it lives here rather than on <see cref="Area"/>.
    ///
    /// <para>Only classes that count as workload contribute, so lunch occupies the day without
    /// counting towards it — see <see cref="ClassEntry.Workload"/>.</para>
    ///
    /// <para><c>null</c> means no target, and <see cref="TimeSpan.Zero"/> does not; it advises rather
    /// than enforces. Both for the same reasons as <see cref="Area.WeeklyTarget"/>.</para>
    /// </summary>
    public TimeSpan? WeeklyScheduledTarget { get; set; }

    // ---- Sessions ------------------------------------------------------------------------------
    //
    // The one way to ask what is happening in this building. Introduced as a seam over Room.Schedule
    // (Task 2) so 29 production call sites could move onto it before the store itself moved; now
    // (Task 3) these seven bodies are what actually reads and writes Sessions below, and
    // Room.Schedule no longer exists at all.

    /// <summary>
    /// <b>Every session in this building — the timetable itself.</b> A session names its room through
    /// <see cref="ScheduleEntry.RoomId"/>, or names none at all.
    ///
    /// <para><b>It sat on <c>Room.Schedule</c> until schema 10</b>, which made the room a session's
    /// owner rather than one of its attributes. The owner's ruling, 2026-08-31: <i>"classes and
    /// events are the true underlying constant. The timetable exists to display events."</i></para>
    ///
    /// <para>It is on <c>Building</c> rather than on <c>ProjectFile</c> for one reason and it is
    /// pragmatic: 23 Core methods take a <c>Building</c>, and moving it a level up would change all
    /// of their signatures for no functional gain. That move remains available later as a rename.</para>
    /// </summary>
    public List<ScheduleEntry> Sessions { get; set; } = new();

    /// <summary>
    /// Every session in the building, whatever room it is in — or none. <b>The one collection that
    /// never drops a room-less session</b>, which is what makes it the right source for anything keyed
    /// on day and time rather than on room: the week grid, the calendar-view exports, the sessions
    /// list, the conflicts panel. See <see cref="SessionsByRoom"/> for the collection that does drop
    /// one, and why that is also correct for what reads it.
    /// </summary>
    public IEnumerable<ScheduleEntry> AllSessions() => Sessions;

    /// <summary>
    /// <b>Whether each calendar week has a timetable of its own.</b> The owner's call, 2026-09-14, in
    /// his own words: <i>"each week being its own individual set in the scope of a 1 year .cccc
    /// file"</i>.
    ///
    /// <list type="bullet">
    /// <item><c>false</c> — <b>one shared pattern</b>, which is what every project written before
    /// schema 13 is and what this app did from the beginning. <see cref="Sessions"/> is a single
    /// week's worth, every session's <see cref="ScheduleEntry.WeekMonday"/> is null, and every week
    /// you can navigate to draws that same list. Editing week 39 changes week 38 because they are the
    /// same objects.</item>
    /// <item><c>true</c> — <b>separate weeks</b>. <see cref="Sessions"/> holds every week's sessions
    /// at once, each stamped with its own <see cref="ScheduleEntry.WeekMonday"/>, and a week nothing
    /// is stamped for is <b>blank</b>: it has no timetable yet, which is a recorded fact rather than
    /// something inferred from an empty screen. That distinction is the whole reason this is stored
    /// rather than derived — a blank week has to be able to say so, on the calendar and on a printed
    /// sheet, instead of printing a correctly-headed empty page.</item>
    /// </list>
    ///
    /// <para><b>Migrating does not flip it.</b> A schema-12 file arrives <c>false</c> and keeps
    /// behaving exactly as it did; the school converts deliberately, through
    /// <c>MainViewModel.SeparateTheWeeks</c>, which is one undo step and asks which weeks the pattern
    /// fills. A migration cannot make that choice for anybody: the pattern carries no date at all, so
    /// there is no week to put it on that would not be a guess — and a guess here is a term's
    /// timetable appearing in the wrong place with nothing saying so.</para>
    ///
    /// <para>It lives on <see cref="Building"/> rather than on <c>ProjectFile</c> for the same
    /// pragmatic reason <see cref="Sessions"/> does, stated above: it describes what that collection
    /// means, and every method that reads the collection already takes a <see cref="Building"/>.</para>
    /// </summary>
    public bool WeeksAreSeparate { get; set; }

    /// <summary>
    /// <b>The sessions that belong to the week containing <paramref name="anyDateInWeek"/></b> — and
    /// the one place <see cref="WeeksAreSeparate"/> is read. Everything asking "what is on this week"
    /// goes through here: the grid, the resolver, both week bases, the copy, the exports.
    ///
    /// <para>With separate weeks it is the sessions stamped for that week, which may legitimately be
    /// <b>none</b> — a blank week. Without them it is <see cref="AllSessions"/> unchanged, because the
    /// one pattern is what every week shows, which is exactly the behaviour a schema-12 project had.
    /// So a call site moved onto this method keeps its old answer on an old file, by construction.</para>
    ///
    /// <para><b>Not to be confused with <see cref="AllSessions"/></b>, which stays every session in
    /// the building across every week and is what the integrity sweeps want — scrubbing a deleted
    /// student off every roster, re-pointing a merged class, orphaning a deleted room's sessions. Each
    /// of those is wrong if narrowed to one week, and this one is wrong if widened to all of them; the
    /// two questions were the same question until schema 13 and are not any more.</para>
    /// </summary>
    public IEnumerable<ScheduleEntry> SessionsInWeek(DateOnly anyDateInWeek)
    {
        // The fast path, and the state almost every project is in: one pattern, nothing frozen, so
        // the answer is the list itself and no Where is allocated per call. SessionsInWeek is asked
        // once per session per render by the resolver.
        if (!SessionsVaryByWeek) return Sessions;

        var monday = WeekLabel.MondayOf(anyDateInWeek);
        if (WeeksAreSeparate || FrozenWeeks.Contains(monday))
            return Sessions.Where(s => s.WeekMonday == monday);

        // A project with frozen weeks still has a standing pattern, and the pattern is the sessions
        // that belong to no particular week. Note this is NOT "every session" any more: once a week
        // has been frozen, its own copies are in the same list and would otherwise be drawn on top of
        // the pattern in every other week of the year.
        return Sessions.Where(s => s.WeekMonday is null);
    }

    /// <summary>
    /// <b>Whether <see cref="SessionsInWeek"/> can answer two different weeks differently</b> — the
    /// negation of this method's own fast path, named, so that a caller asking "does the week in the
    /// box change what is on screen?" gets the answer from the method that decides it.
    ///
    /// <para><b>It is deliberately not <see cref="WeeksAreSeparate"/>, and that mistake has been made
    /// twice.</b> Separate weeks is one of the two ways a week becomes its own; a frozen week is the
    /// other, and it arrives by itself as weeks end rather than by anybody pressing anything. A
    /// condition written as <c>WeeksAreSeparate</c> is therefore true of a school that converted and
    /// false of every school that simply kept using the app — which is the harder half to notice,
    /// because it is silent until the first week freezes and then silent again about why.</para>
    ///
    /// <para><b>Not <c>WeekIsFrozen(someDate)</c> either.</b> That asks about one week, and the
    /// question a step asks is about the pair: moving OFF a frozen week changes the answer exactly as
    /// moving onto one does.</para>
    ///
    /// <para><b>[JsonIgnore] because it is derived</b>, and System.Text.Json writes a read-only
    /// property into the file unless told not to — see
    /// <c>DerivedPropertiesStayOutOfTheFileTests</c>, which is what catches the next one.</para>
    /// </summary>
    [JsonIgnore]
    public bool SessionsVaryByWeek => WeeksAreSeparate || FrozenWeeks.Count > 0;

    /// <summary>
    /// <b>The standing pattern: the sessions that belong to no particular week.</b> What every week
    /// that is neither separate nor frozen draws, and what <see cref="FreezeWeek"/> copies.
    ///
    /// <para>Empty once <see cref="WeeksAreSeparate"/> is true — such a project has no pattern at
    /// all, which is the whole meaning of the flag.</para>
    /// </summary>
    public IEnumerable<ScheduleEntry> StandingPattern() => Sessions.Where(s => s.WeekMonday is null);

    /// <summary>
    /// <b>The weeks that hold their own record of what ran, while the rest of the year still follows
    /// the standing pattern.</b> Mondays, in order. The owner's rule, 2026-09-14: <i>the future
    /// follows one pattern and is never blank; a week freezes as it ran once it ends.</i> <b>He
    /// reversed the first half on 2026-09-23</b> — the weeks ahead are blank until somebody fills
    /// them — and <c>ScheduleOps.ClearWeeksAfter</c> is how a project gets there, on his press. A
    /// project that has pressed it has separate weeks and freezes nothing; this list is for the
    /// ones that have not yet.
    ///
    /// <para><b>This is not a smaller <see cref="WeeksAreSeparate"/> and the two are not alternatives
    /// to each other.</b> Separate weeks is the deliberate conversion a school presses once, after
    /// which there is no pattern and an unfilled week is <i>blank</i>. Freezing is automatic,
    /// incremental, and leaves the pattern standing: it exists so that changing next term's timetable
    /// does not silently rewrite what October actually ran. A project whose weeks are separate never
    /// freezes anything, because every one of its weeks is already its own.</para>
    ///
    /// <para><b>It has to be stored rather than inferred from the stamped sessions</b>, for exactly
    /// the reason a blank week does: a week that ran with nothing on it is a fact, and a week with no
    /// stamped sessions would otherwise fall back to the pattern and re-acquire a timetable it never
    /// had. The same goes for a frozen week somebody then empties.</para>
    ///
    /// <para><see cref="ViewModels.ScheduleOps.FreezeEndedWeeks"/> is the one writer, and
    /// <c>MainViewModel.PushUndo</c> its one caller — every change to a project freezes whatever has
    /// already ended before it is made. Reading never writes.</para>
    /// </summary>
    public List<DateOnly> FrozenWeeks { get; set; } = new();

    /// <summary>Whether the week containing <paramref name="anyDateInWeek"/> holds its own record.</summary>
    public bool WeekIsFrozen(DateOnly anyDateInWeek)
        => FrozenWeeks.Contains(WeekLabel.MondayOf(anyDateInWeek));

    /// <summary>
    /// <b>Every week this building has a session stamped for, in order.</b> For a project with one
    /// shared pattern that is its <see cref="FrozenWeeks"/> and nothing else — the pattern itself
    /// belongs to no week, and answering with "every week in the year" would invent a fact the file
    /// does not hold.
    /// </summary>
    public IEnumerable<DateOnly> WeeksWithSessions()
        => Sessions.Where(s => s.WeekMonday is not null)
                      .Select(s => s.WeekMonday!.Value)
                      .Distinct()
                      .OrderBy(d => d);

    /// <summary>The sessions in one room.</summary>
    public IEnumerable<ScheduleEntry> SessionsIn(Room room)
        => Sessions.Where(s => s.RoomId == room.Id);

    /// <summary>
    /// Every session paired with the room it is in — what a <b>room-keyed</b> surface needs: the room
    /// grid, the floor plan. <b>Deliberately does not yield a session whose <see cref="ScheduleEntry.RoomId"/>
    /// is null or names no live room</b>, because there is no room to pair it with — no column on the
    /// grid, no shape on the plan. A day/time-keyed surface must read <see cref="AllSessions"/>
    /// instead, or a room-less session vanishes from it with nothing anywhere saying so.
    /// </summary>
    public IEnumerable<(Room Room, ScheduleEntry Entry)> SessionsByRoom()
        => Rooms.SelectMany(r => SessionsIn(r).Select(e => (r, e)));

    /// <summary>
    /// Puts a session in a room, and stamps it with that room — or with no room at all when
    /// <paramref name="room"/> is null, which is what lets <c>SessionEditor.NoRoomOption</c> commit an
    /// Add without one.
    /// </summary>
    public void AddSession(ScheduleEntry entry, Room? room)
    {
        entry.RoomId = room?.Id;
        Sessions.Add(entry);
    }

    /// <summary>Takes a session out of the building. False when it was not in it.</summary>
    public bool RemoveSession(ScheduleEntry entry) => Sessions.Remove(entry);

    /// <summary>
    /// Moves a session to another room — or to no room at all when <paramref name="destination"/> is
    /// null — by re-pointing its <see cref="ScheduleEntry.RoomId"/>.
    /// </summary>
    public void MoveSession(ScheduleEntry entry, Room? destination)
        => entry.RoomId = destination?.Id;

    /// <summary>
    /// <b>Drops every session matching <paramref name="predicate"/>, and answers how many went.</b>
    /// The bulk counterpart to <see cref="RemoveSession"/>, added in Task 3 because
    /// <c>ProjectStore</c>'s load-time repair does <c>RemoveAll(s => s.End &lt;= s.Start)</c> and
    /// <see cref="SessionsIn"/> answers an <c>IEnumerable</c> that cannot be removed from. It was
    /// reachable through <c>Room.Schedule</c> until that property was deleted, which is exactly the
    /// kind of hole a seam leaves when it is written from the call sites that were easy.
    ///
    /// <para><b>Genuine deletion only</b> — a session's own data is corrupt or it would not exist at
    /// all, which is <c>ProjectStore</c>'s one caller (<c>s.End &lt;= s.Start</c>). A room going away
    /// is a different question with a different answer since 2026-08-31: see
    /// <see cref="OrphanSessions"/>, its sibling and the one every room/floor delete cascade calls.</para>
    /// </summary>
    public int RemoveSessions(Func<ScheduleEntry, bool> predicate)
    {
        int before = Sessions.Count;
        Sessions.RemoveAll(new Predicate<ScheduleEntry>(predicate));
        return before - Sessions.Count;
    }

    /// <summary>
    /// <b>Clears the room on every session matching <paramref name="predicate"/>, and answers how many
    /// were touched.</b> The orphan counterpart to <see cref="RemoveSessions"/>, added in Task 4 for
    /// the owner's ruling, 2026-08-31: <i>"orphan the sessions when a room is deleted… it should still
    /// appear on the calendar, just without a room assigned."</i>
    ///
    /// <para>Every room and floor delete cascade calls this now rather than <see cref="RemoveSessions"/>:
    /// the session keeps its class, its people and its times, and stays on <see cref="AllSessions"/>
    /// for every day/time-keyed surface — it simply drops off <see cref="SessionsByRoom"/>, exactly as
    /// a hand-edited or restored session naming a room this building no longer has already does.
    /// Deleting a room no longer deletes a term's teaching.</para>
    /// </summary>
    public int OrphanSessions(Func<ScheduleEntry, bool> predicate)
    {
        int count = 0;
        foreach (var s in Sessions)
            if (predicate(s)) { s.RoomId = null; count++; }
        return count;
    }
}
