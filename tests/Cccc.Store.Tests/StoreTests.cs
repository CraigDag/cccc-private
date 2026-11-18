using Cccc.Domain;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Sample;

namespace Cccc.Store.Tests;

public sealed class Scratch : IDisposable
{
    public string Folder { get; } = Path.Combine(Path.GetTempPath(), "cccc-store-tests", Guid.NewGuid().ToString("N"));
    public DateTimeOffset Now = new(2030, 3, 4, 9, 0, 0, TimeSpan.Zero);
    public DateOnly Today = new(2030, 3, 4);

    public Scratch() => ProjectStore.AllowTemp = true;

    public ProjectStore Open() => ProjectStore.Open(Folder, create: true);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}

public class ProjectStoreTests
{
    [Fact]
    public void AMissingFolderIsRefusedUnlessTheFirstStartSaysToCreateIt()
    {
        using var s = new Scratch();
        var e = Assert.Throws<StoreRefused>(() => ProjectStore.Open(s.Folder));
        Assert.Contains("does not exist", e.Message);
        s.Open();
        Assert.True(File.Exists(Path.Combine(s.Folder, "cccc.db")));
    }

    [Fact]
    public void AnEmptyFolderIsNotTakenForAFirstStart()
    {
        using var s = new Scratch();
        Directory.CreateDirectory(s.Folder);   // what Docker leaves when a bind mount's folder has gone
        var e = Assert.Throws<StoreRefused>(() => ProjectStore.Open(s.Folder));
        Assert.Contains("is empty", e.Message);
        Assert.False(File.Exists(Path.Combine(s.Folder, "cccc.db")));
    }

    [Fact]
    public void AFolderWithFilesButNoDatabaseIsNotStartedOver()
    {
        using var s = new Scratch();
        Directory.CreateDirectory(s.Folder);
        File.WriteAllText(Path.Combine(s.Folder, "something.cccc"), "x");
        Assert.Throws<StoreRefused>(() => ProjectStore.Open(s.Folder));
    }

    [Fact]
    public void ATemporaryFolderIsRefused()
    {
        ProjectStore.AllowTemp = false;
        try
        {
            Assert.Throws<StoreRefused>(() => ProjectStore.Open(Path.Combine(Path.GetTempPath(), "x" + Guid.NewGuid()), create: true));
        }
        finally { ProjectStore.AllowTemp = true; }
    }

    [Fact]
    public void EveryChangeSurvivesAReopenAndAReplayEqualsTheLiveState()
    {
        using var s = new Scratch();
        var store = s.Open();
        var sample = SampleSchool.Create(s.Today, s.Today);
        var id = store.Create("Year", sample, "ada", s.Now);
        var live = new LiveProjects(store, () => s.Now, () => s.Today).Get(id);
        var room = new Room { Name = "New studio" };
        live.Commit(new Draft(live.State).Put(Collections.Rooms, room).ToBatch("Added a room"), "ada");
        var first = live.State.Sessions.First(x => x.Date >= s.Today);
        live.Commit(t => Edits.Move(t, first.Id, first.Date, new TimeOnly(15, 0), new TimeOnly(16, 0)), "bo");

        var reopened = ProjectStore.Open(s.Folder);
        var (state, seq) = reopened.Load(id);
        Assert.Equal(2, seq);
        Assert.Equal(ModelJson.Serialise(live.State), ModelJson.Serialise(state));
        Assert.Equal("bo", reopened.Find(id)!.LastChangedBy);
    }

    [Fact]
    public void ALockedWeekRefusesAChangeButTakesARegister()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", SampleSchool.Create(s.Today, s.Today), "ada", s.Now);
        var live = new LiveProjects(store, () => s.Now, () => s.Today).Get(id);
        var past = live.State.Sessions.First(x => x.Date < Week.MondayOf(s.Today) && !x.Audience.IsEveryone);
        Assert.Throws<EditRefused>(() => live.Commit(t => Edits.Move(t, past.Id, past.Date, new TimeOnly(7, 0), new TimeOnly(8, 0)), "ada"));
        live.Commit(t => Edits.FileRegister(t, past.Id, [], [], Domain.Rules.RegisterReader.Administrator, "ada", s.Now), "ada");
        Assert.True(live.State.Registers.Contains(past.Id));
    }

    [Fact]
    public void AskingAnsweredSinceIsNotTakenBackByTheAskersUndoAndTheAnswerIsTheAdministrators()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", SampleSchool.Create(s.Today, s.Today), "ada", s.Now);
        var live = new LiveProjects(store, () => s.Now, () => s.Today).Get(id);
        var bo = live.State.Facilitators.Single(f => f.Name == "Bo Lindqvist");
        var day = s.Today.AddDays(1);
        live.Commit(t => Edits.AskToBeAway(t, day, day, null, null, Domain.Rules.AskingReader.Facilitator(bo.Id), s.Today, s.Now), "bo");
        var asked = live.State.Requests.Single(r => r.FacilitatorId == bo.Id);
        live.Commit(t => Edits.AnswerRequest(t, asked.Id, Answer.Yes, Domain.Rules.AskingReader.Administrator, s.Now), "admin");

        // Bo's Undo cannot take back what has been answered: the absence stays recorded, and he is told why.
        var undone = live.Undo("bo");
        Assert.False(undone.Done);
        Assert.Equal(["asking to be away (changed since by admin)"], undone.Skipped);
        Assert.Equal(Answer.Yes, live.State.Requests[asked.Id]!.Answer);
        Assert.Contains(live.State.Absences, a => a.Id == live.State.Requests[asked.Id]!.AbsenceId);
        // The administrator's takes back the yes, and the absence with it: it waits again, and Bo, whose Undo
        // that step was, takes it back from My week instead.
        Assert.True(live.Undo("admin").Done);
        Assert.Equal(Domain.Rules.RequestState.Waiting, Domain.Rules.Asking.StateOf(live.State.Requests[asked.Id]!, s.Today));
        Assert.DoesNotContain(live.State.Absences, a => a.PersonId == bo.Id && a.From == day);
        Assert.False(live.CanUndo("bo"));
        live.Commit(t => Edits.TakeBackRequest(t, asked.Id, Domain.Rules.AskingReader.Facilitator(bo.Id)), "bo");
        Assert.False(live.State.Requests.Contains(asked.Id));
    }

    [Fact]
    public void UndoIsPerPersonAndSkipsWhatSomebodyElseChangedSince()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", Timetable.Empty, "ada", s.Now);
        var live = new LiveProjects(store, () => s.Now, () => s.Today).Get(id);
        var a = new Room { Name = "A" };
        var b = new Room { Name = "B" };
        live.Commit(new Draft(live.State).Put(Collections.Rooms, a).Put(Collections.Rooms, b).ToBatch("Added two rooms"), "ada");
        live.Commit(new Draft(live.State).Put(Collections.Rooms, a with { Name = "A, renamed by Bo" }).ToBatch("Renamed"), "bo");

        Assert.False(live.CanUndo("cleo"));
        var undone = live.Undo("ada");
        Assert.True(undone.Done);
        Assert.Single(undone.Skipped);
        Assert.Contains("changed since by bo", undone.Skipped[0]);
        Assert.Equal("A, renamed by Bo", live.State.Rooms[a.Id]!.Name);
        Assert.False(live.State.Rooms.Contains(b.Id));

        live.Redo("ada");
        Assert.True(live.State.Rooms.Contains(b.Id));
        live.Undo("bo");
        Assert.Equal("A", live.State.Rooms[a.Id]!.Name);
    }

    [Fact]
    public void PuttingAnEarlierMomentBackIsANewChangeAndHistoryIsKept()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", Timetable.Empty, "ada", s.Now);
        var live = new LiveProjects(store, () => s.Now, () => s.Today).Get(id);
        live.Commit(new Draft(live.State).Put(Collections.Rooms, new Room { Name = "A" }).ToBatch("1"), "ada");
        live.Commit(new Draft(live.State).Put(Collections.Rooms, new Room { Name = "B" }).ToBatch("2"), "ada");
        live.PutBack(1, "ada");
        Assert.Single(live.State.Rooms);
        Assert.Equal(3, store.History(id).Count);
    }

    [Fact]
    public void ChangesMadeSinceATimeAreThoseAtOrAfterItInOrderAndANewChangeIsSeen()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", Timetable.Empty, "ada", s.Now);
        var live = new LiveProjects(store, () => s.Now, () => s.Today).Get(id);
        void Add(string name) => live.Commit(new Draft(live.State).Put(Collections.Rooms, new Room { Name = name }).ToBatch(name), "ada");
        Add("before");
        s.Now = s.Now.AddHours(1);
        var since = s.Now;
        Add("at");
        s.Now = s.Now.AddHours(1);
        Add("after");

        Assert.Equal(["at", "after"], live.ChangesMadeSince(since).Select(c => c.Batch.Label));
        Assert.Equal(["at", "after"], store.ChangesMadeSince(id, since.ToOffset(TimeSpan.FromHours(1))).Select(c => c.Batch.Label));   // the same moment in Copenhagen
        Add("later");
        Assert.Equal(["at", "after", "later"], live.ChangesMadeSince(since).Select(c => c.Batch.Label));   // what was read before is not kept past a new change
    }

    [Fact]
    public void PruneFoldsOldHistoryButKeepsTheState()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", Timetable.Empty, "ada", s.Now);
        var live = new LiveProjects(store, () => s.Now, () => s.Today).Get(id);
        for (int i = 0; i < 5; i++)
            live.Commit(new Draft(live.State).Put(Collections.Rooms, new Room { Name = $"R{i}" }).ToBatch($"{i}"), "ada");
        store.Prune(s.Now.AddDays(31));
        Assert.Empty(store.History(id));
        Assert.Equal(5, store.Load(id).State.Rooms.Count);
    }

    [Fact]
    public void ADeletedProjectComesBackUntilThirtyDaysHavePassed()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", Timetable.Empty, "ada", s.Now);
        store.Delete(id, s.Now);
        Assert.Empty(store.List());
        store.Undelete(id);
        Assert.Single(store.List());
        // What a person's phone calendar carried goes with its timetable, and only with its own.
        var other = store.Create("Next", Timetable.Empty, "ada", s.Now);
        var person = Guid.NewGuid();
        store.SetCalendarHeld(id, person, "[]");
        store.SetCalendarHeld(other, person, "[1]");
        store.SetCalendarHeld(other, person, "[2]");
        Assert.Equal(("[]", "[2]"), (store.CalendarHeld(id, person), store.CalendarHeld(other, person)));
        store.Delete(id, s.Now);
        store.Prune(s.Now.AddDays(31));
        Assert.Null(store.Find(id));
        Assert.Null(store.CalendarHeld(id, person));
        Assert.Equal("[2]", store.CalendarHeld(other, person));
        // Erased, a person's go from every timetable, and nobody else's do.
        var someoneElse = Guid.NewGuid();
        store.SetCalendarHeld(other, someoneElse, "[3]");
        store.ForgetCalendars(person);
        Assert.Equal(((string?)null, "[3]"), (store.CalendarHeld(other, person), store.CalendarHeld(other, someoneElse)));
    }

    [Fact]
    public void ARewriteReachesEveryStoredCopy()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", Timetable.Empty, "ada", s.Now);
        var live = new LiveProjects(store, () => s.Now, () => s.Today).Get(id);
        live.Commit(new Draft(live.State).Put(Collections.Students, new Student { FirstName = "Zebedee", Surname = "Unique" }).ToBatch("Added"), "ada");
        live.Settle();
        int changed = store.Rewrite(id, json => json.Replace("Zebedee", "[erased]"));
        Assert.True(changed >= 2);
        Assert.DoesNotContain("Zebedee", File.ReadAllBytes(store.DatabasePath).Select(b => (char)b).Aggregate(new System.Text.StringBuilder(), (sb, c) => sb.Append(c)).ToString());
        Assert.Equal("[erased]", store.Load(id).State.Students.Single().FirstName);
    }

    [Fact]
    public void ARewriteReachesTheWordsOfALabelToo()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", Timetable.Empty, "ada", s.Now);
        var live = new LiveProjects(store, () => s.Now, () => s.Today).Get(id);
        live.Commit(new Draft(live.State).Put(Collections.Facilitators, new Facilitator { Name = "Zebedee Unique" }).ToBatch("Changed Zebedee Unique"), "ada");
        live.Settle();
        store.Rewrite(id, json => json.Replace("Zebedee Unique", "[erased]"), label => label.Replace("Zebedee Unique", "[erased]"));
        Assert.Equal("Changed [erased]", store.ChangesSince(id, 0).Single().Batch.Label);
        // Nowhere on disk: not in the database file, and not in its write-ahead log either.
        foreach (var file in new[] { store.DatabasePath, store.DatabasePath + "-wal" }.Where(File.Exists))
            Assert.DoesNotContain("Zebedee", File.ReadAllBytes(file).Select(b => (char)b).Aggregate(new System.Text.StringBuilder(), (sb, c) => sb.Append(c)).ToString());
    }

    /// <summary>
    /// Undo checks each operation it puts back on its own, so a merge's re-pointing of an ended week has to
    /// be read beside the class it brings back, or undoing a merge would leave the ended weeks merged.
    /// </summary>
    [Fact]
    public void UndoingAMergePutsEveryWeekBackTheEndedOnesIncluded()
    {
        using var s = new Scratch();
        var store = s.Open();
        var merged = new Class { Name = "Floor Acrobatic" };
        var keep = new Class { Name = "Floor Acrobatics" };
        var ended = new Session { Date = s.Today.AddDays(-7), Title = merged.Name, ClassId = merged.Id };
        var ahead = new Session { Date = s.Today.AddDays(1), Title = merged.Name, ClassId = merged.Id };
        var t = Timetable.Empty with
        {
            Classes = EntitySet<Class>.Of([merged, keep]),
            Sessions = EntitySet<Session>.Of([ended, ahead]),
        };
        var id = store.Create("Year", t, "ada", s.Now);
        var live = new LiveProjects(store, () => s.Now, () => s.Today).Get(id);

        live.Commit(x => Edits.MergeClass(x, merged.Id, keep.Id, s.Today), "ada");
        Assert.Equal((keep.Id, keep.Id), (live.State.Sessions[ended.Id]!.ClassId!.Value, live.State.Sessions[ahead.Id]!.ClassId!.Value));

        var undone = live.Undo("ada");
        Assert.True(undone.Done);
        Assert.Empty(undone.Skipped);
        Assert.Equal((merged.Id, merged.Id), (live.State.Sessions[ended.Id]!.ClassId!.Value, live.State.Sessions[ahead.Id]!.ClassId!.Value));
        Assert.Equal(merged.Name, live.State.Sessions[ahead.Id]!.Title);
        Assert.NotNull(live.State.Classes[merged.Id]);

        live.Redo("ada");
        Assert.Equal(keep.Id, live.State.Sessions[ended.Id]!.ClassId);
        Assert.Null(live.State.Classes[merged.Id]);
    }

    [Fact]
    public void ARewriteThroughTheLiveCopiesLeavesNothingInMemoryToWriteTheOldWordsBack()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", Timetable.Empty, "ada", s.Now);
        var gone = store.Create("Gone", Timetable.Empty, "ada", s.Now);
        var projects = new LiveProjects(store, () => s.Now, () => s.Today);
        var student = new Student { FirstName = "Zebedee", Surname = "Unique" };
        // A tab has this one open and keeps hold of it: an administrator looking at it while another erases.
        var held = projects.Get(id);
        held.Commit(new Draft(held.State).Put(Collections.Students, student).ToBatch("Added"), "ada");
        var g = projects.Get(gone);
        g.Commit(new Draft(g.State).Put(Collections.Students, student).ToBatch("Added"), "ada");
        store.Delete(gone, s.Now);
        projects.Forget(gone);
        Changed? told = null;
        held.ChangedEvent += c => told = c;

        int changed = projects.RewriteEverywhere(json => json.Replace("Zebedee", "[erased]"));

        // The tab's copy is still the one copy, it says what the store says, and the tab was told.
        Assert.Same(held, projects.Get(id));
        Assert.Equal("[erased]", held.State.Students.Single().FirstName);
        Assert.NotNull(told);
        Assert.False(held.CanUndo("ada"));
        // Its next change and snapshot carry none of the old words.
        held.Commit(new Draft(held.State).Put(Collections.Rooms, new Room { Name = "Studio" }).ToBatch("Added a room"), "ada");
        held.Settle();
        Assert.Equal("[erased]", store.Load(id).State.Students.Single().FirstName);
        var stored = string.Concat(store.ChangesSince(id, 0).SelectMany(c => c.Batch.Ops).Select(o => o.Before?.ToJsonString() + o.After?.ToJsonString()));
        Assert.Contains("[erased]", stored);
        Assert.DoesNotContain("Zebedee", stored);
        Assert.True(changed >= 4, $"{changed} stored values changed");
        // The deleted one was rewritten too, and let go after.
        Assert.Null(projects.Loaded(gone));
        Assert.Equal("[erased]", store.Load(gone).State.Students.Single().FirstName);
    }

    [Fact]
    public void AStoreOfTheFirstFormatOpensAndOneFromANewerVersionIsRefused()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", SampleSchool.Create(s.Today, s.Today), "ada", s.Now);
        void SetFormat(string v)
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath}");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $"UPDATE meta SET value='{v}' WHERE key='format'";
            cmd.ExecuteNonQuery();
        }
        string Format()
        {
            using var c = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabasePath}");
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT value FROM meta WHERE key='format'";
            return (string)cmd.ExecuteScalar()!;
        }
        SetFormat("1");
        var older = ProjectStore.Open(s.Folder);
        Assert.Equal(store.Load(id).State.Sessions.Count, older.Load(id).State.Sessions.Count);
        Assert.Equal(ProjectStore.FormatVersion.ToString(), Format());   // and it is this format from now on
        SetFormat((ProjectStore.FormatVersion + 1).ToString());
        Assert.Contains("newer version", Assert.Throws<StoreRefused>(() => ProjectStore.Open(s.Folder)).Message);
    }

    /// <summary>
    /// What each store format brought, and the store's format the newest of them: a collection added without
    /// moving it would be replayed by an older build as one it has never heard of. A collection added to the
    /// timetable has to be decided about here, or this goes red.
    /// </summary>
    [Fact]
    public void EveryCollectionIsGivenTheFormatThatBroughtItAndTheStoreIsTheNewestOfThem()
    {
        (string What, int Format)[] brought =
        [
            ("scratch", 2),
            ("formerSince", 3),          // a field on classes, spaces and facilitators, not a collection
            ("requests", 4), ("offers", 4),
            ("equipment", 5),            // with what a space has and a class needs, fields on rooms and classes
        ];
        string[] first = ["settings", "semesters", "closures", "rooms", "programmes", "groups", "branches", "disciplines",
                          "students", "facilitators", "areas", "classes", "timeRules", "sessions", "registers", "absences", "events"];
        Assert.Equal(first.Concat(brought.Where(b => b.What != "formerSince").Select(b => b.What)).Order(StringComparer.Ordinal),
            Collections.All.Select(c => c.Name).Order(StringComparer.Ordinal));
        Assert.Equal(ProjectStore.FormatVersion, brought.Max(b => b.Format));
    }

    [Fact]
    public void ABackupIsAWholeStoreThatOpens()
    {
        using var s = new Scratch();
        var store = s.Open();
        var id = store.Create("Year", SampleSchool.Create(s.Today, s.Today), "ada", s.Now);
        var copyFolder = Path.Combine(s.Folder, "backup");
        Directory.CreateDirectory(copyFolder);
        store.Backup(Path.Combine(copyFolder, "cccc.db"));
        var copy = ProjectStore.Open(copyFolder);
        Assert.Equal(store.Load(id).State.Sessions.Count, copy.Load(id).State.Sessions.Count);
    }
}

public class AccountTests
{
    [Fact]
    public void TheFirstAdministratorComesThroughAOneTimeLinkThatThenCloses()
    {
        using var s = new Scratch();
        var accounts = new AccountDirectory(s.Open(), () => s.Now);
        var token = accounts.SetupToken()!;
        Assert.Throws<AccountRefused>(() => accounts.CreateFirstAdministrator("wrong", "ada", "Ada", "a long passphrase"));
        var ada = accounts.CreateFirstAdministrator(token, "ada", "Ada", "a long passphrase");
        Assert.Equal(Role.Administrator, ada.Role);
        Assert.Null(accounts.SetupToken());
        Assert.NotNull(accounts.Verify("ADA", "a long passphrase"));
        Assert.Null(accounts.Verify("ada", "wrong passphrase"));
    }

    [Fact]
    public void TheLastAdministratorCannotBeLostAndNobodyDemotesThemself()
    {
        using var s = new Scratch();
        var accounts = new AccountDirectory(s.Open(), () => s.Now);
        var ada = accounts.CreateFirstAdministrator(accounts.SetupToken()!, "ada", "Ada", "a long passphrase");
        var bo = accounts.Add(ada.Id, "bo", "Bo", Role.Viewer, "another long one");
        Assert.True(bo.MustChange);
        Assert.Throws<AccountRefused>(() => accounts.Update(ada.Id, ada.Id, "Ada", Role.Editor, AccountTags.None, null, false));
        Assert.Throws<AccountRefused>(() => accounts.Remove(ada.Id, ada.Id));
        Assert.Throws<AccountRefused>(() => accounts.Add(bo.Id, "x", "X", Role.Viewer, "a long passphrase"));
        accounts.Update(ada.Id, bo.Id, "Bo", Role.Administrator, AccountTags.Staff, null, false);
        accounts.Update(bo.Id, ada.Id, "Ada", Role.Viewer, AccountTags.None, null, false);
        Assert.Throws<AccountRefused>(() => accounts.Update(bo.Id, bo.Id, "Bo", Role.Administrator, AccountTags.None, null, suspended: true));
    }

    [Fact]
    public void ASuspendedAccountCannotSignInAndChangingYourOwnMovesTheStamp()
    {
        using var s = new Scratch();
        var accounts = new AccountDirectory(s.Open(), () => s.Now);
        var ada = accounts.CreateFirstAdministrator(accounts.SetupToken()!, "ada", "Ada", "a long passphrase");
        var bo = accounts.Add(ada.Id, "bo", "Bo", Role.Editor, "another long one");
        var stamp = accounts.ChangeOwn(bo.Id, "another long one", "bo's own long one");
        Assert.NotEqual(bo.Stamp, stamp);
        Assert.False(accounts.Find(bo.Id)!.MustChange);
        accounts.Update(ada.Id, bo.Id, "Bo", Role.Editor, AccountTags.None, null, suspended: true);
        Assert.Null(accounts.Verify("bo", "bo's own long one"));
    }
}
