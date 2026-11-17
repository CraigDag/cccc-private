using System.IO.Compression;
using System.Text;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Store;
using Cccc.Web.Features.Shell;
using Cccc.Web.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Old = CCCCScheduler.Models;

namespace Cccc.Web.Tests;

/// <summary>
/// Following the current app: its file, written the way it writes it, is brought across as one change
/// per save, nothing when nothing changed, and never anything half-read; and the timetable that follows
/// it is changed by nobody here.
/// </summary>
public sealed class FollowingTests : IDisposable
{
    private static readonly DateOnly Monday = new(2030, 3, 4);
    private readonly string _root = Path.Combine(AppContext.BaseDirectory, "stores", Guid.NewGuid().ToString("N"));
    private readonly string _file;
    private readonly ProjectStore _store;
    private readonly LiveProjects _live;
    private readonly ProjectChoice _choice;
    private readonly FollowStatus _status = new() { On = true };
    private DateTimeOffset _now = new(2030, 3, 6, 9, 0, 0, TimeSpan.Zero);
    private DateOnly _today = new(2030, 3, 6);
    private int _saves;

    public FollowingTests()
    {
        _file = Path.Combine(_root, "current", "School.cccc");
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        _store = ProjectStore.Open(Path.Combine(_root, "store"), create: true);
        _live = new LiveProjects(_store, () => _now, () => _today);
        _choice = new ProjectChoice(_store, _live);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    private readonly Said _said = new();

    private Follower Make() => new(_file, _store, _live, _choice, _status, () => _now, () => _today, _said);

    /// <summary>Every line the follower logs, as it would be written.</summary>
    private sealed class Said : ILogger<Follower>
    {
        public List<string> Lines { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? e, Func<TState, Exception?, string> format) =>
            Lines.Add(format(state, e));
    }

    private Guid Followed() => Follower.FollowedId(_store) ?? throw new InvalidOperationException("Nothing is followed yet.");

    /// <summary>A save over there: the whole file written aside, then renamed over the old one, a second after the last.</summary>
    private void Save(Old.ProjectFile p) => Write(CCCCScheduler.Services.ProjectPackage.SaveToBytes(p, savedBy: "test"));

    private void Write(byte[] bytes)
    {
        var part = _file + ".part";
        File.WriteAllBytes(part, bytes);
        File.Move(part, _file, overwrite: true);
        File.SetLastWriteTimeUtc(_file, new DateTime(2030, 3, 6, 8, 0, 0, DateTimeKind.Utc).AddSeconds(++_saves));
    }

    private static byte[] Package(string json)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry("project.json").Open(), new UTF8Encoding(false)))
            w.Write(json);
        return ms.ToArray();
    }

    /// <summary>A made-up school in the current app's own model: a week of its own, a class with a register taken.</summary>
    private static (Old.ProjectFile File, Old.ScheduleEntry Acro, Old.Student Alba) School()
    {
        var group = new Old.Group { Name = "BA 1" };
        var alba = new Old.Student { FirstName = "Alba", Surname = "Test", GroupId = group.Id };
        var ada = new Old.Facilitator { Name = "Ada Brightwater" };
        var room = new Old.Room { Name = "Studio A" };
        var acro = new Old.ScheduleEntry
        {
            Title = "Acro", Day = DayOfWeek.Wednesday, WeekMonday = Monday, Start = new(9, 0), End = new(11, 0),
            Scope = Old.SessionScope.Group, GroupId = group.Id, RoomId = room.Id, FacilitatorIds = [ada.Id],
        };
        acro.Attendance.Add(new Old.AttendanceRecord { WeekMonday = Monday, PresentStudentIds = [alba.Id] });
        var building = new Old.Building { Groups = [group], Students = [alba], Facilitators = [ada], Sessions = [acro], WeeksAreSeparate = true };
        building.Rooms.Add(room);
        var file = new Old.ProjectFile
        {
            SchemaVersion = Old.ProjectFile.CurrentSchemaVersion,
            Terms = [new Old.TermInfo { Name = "Spring", StartMonday = Monday, WeekCount = 4 }],
            Building = building,
        };
        return (file, acro, alba);
    }

    [Fact]
    public void TheFirstLookBringsTheCurrentAppsTimetableInAndOpensItForEverybody()
    {
        var ours = _store.Create("Ours", Timetable.Empty, "admin", _now);
        _choice.Choose(ours);
        var (p, acro, alba) = School();
        Save(p);

        Make().Check();

        var id = Followed();
        Assert.NotEqual(ours, id);
        Assert.Equal(id, _choice.OpenId());
        var t = _live.Get(id).State;
        Assert.Equal(new TimeOnly(9, 0), t.Sessions[acro.Id]!.Start);
        Assert.Equal(Mark.Present, t.Registers[acro.Id]!.MarkFor(alba.Id));
        Assert.Equal(Follower.By, _store.Find(id)!.LastChangedBy);
        // Brought in whole as the timetable's first state, so nothing about it is a change.
        var extra = _store.ChangesSince(id, 0).SelectMany(c => c.Batch.Ops).Select(o => o.Collection + " " + o.Before + " -> " + o.After).ToList();
        Assert.True(extra.Count == 0, string.Join("\n", extra));
        Assert.Null(_status.Problem);
        Assert.Equal(_now, _status.Checked);
    }

    [Fact]
    public void ASaveOverThereComesAcrossAsOneChangeOfWhatChangedAndNothingWhenNothingDid()
    {
        var (p, acro, _) = School();
        Save(p);
        var follower = Make();
        follower.Check();
        var project = _live.Get(Followed());
        long seq = project.Sequence;
        int told = 0;
        project.ChangedEvent += _ => told++;

        // Saved again with nothing changed, and looked at afresh after a restart: nothing is written, nobody told.
        Save(p);
        _now += TimeSpan.FromSeconds(30);
        follower.Check();
        Make().Check();
        Assert.Equal(seq, project.Sequence);
        Assert.Equal(0, told);

        // A class moved and another added over there.
        acro.Start = new(10, 0);
        var dance = new Old.ScheduleEntry { Title = "Dance", Day = DayOfWeek.Thursday, WeekMonday = Monday, Start = new(13, 0), End = new(14, 0), Scope = Old.SessionScope.WholeBuilding };
        p.Building.Sessions.Add(dance);
        Save(p);
        _now += TimeSpan.FromSeconds(30);
        follower.Check();

        Assert.Equal(seq + 1, project.Sequence);
        Assert.Equal(1, told);
        var change = Assert.Single(_store.ChangesSince(project.Id, seq));
        Assert.Equal(Follower.By, change.By);
        Assert.Equal(new[] { acro.Id, dance.Id }.Order(), change.Batch.Ops.Select(o => o.Id).Order());
        Assert.Equal(new TimeOnly(10, 0), project.State.Sessions[acro.Id]!.Start);
        Assert.Equal(_now, _status.BroughtIn);
        Assert.Equal(2, _status.LastChanges);
        Assert.False(project.CanUndo(Follower.By));

        // The current app keeps its own week lock: a week that has ended here still follows what it says.
        _today = Monday.AddDays(14);
        acro.End = new(11, 30);
        Save(p);
        follower.Check();
        Assert.Null(_status.Problem);
        Assert.Equal(new TimeOnly(11, 30), project.State.Sessions[acro.Id]!.End);

        // A class copied into the current app's Scratch Week arrives in this one's, and counts for nothing.
        var planned = CCCCScheduler.ViewModels.ScratchWeek.CopyIn(p.Building, acro);
        Save(p);
        follower.Check();
        Assert.Null(_status.Problem);
        Assert.Equal(Cccc.Domain.Week.DateOf(Cccc.Domain.Editing.ScratchWeek.Monday, DayOfWeek.Wednesday), project.State.Scratch[planned.Id]!.Date);
        Assert.Equal(2, project.State.Sessions.Count);
    }

    [Fact]
    public void AFileThatCannotBeBroughtInLeavesTheTimetableAsItWasAndSaysWhy()
    {
        var (p, acro, _) = School();
        Save(p);
        var follower = Make();
        follower.Check();
        var project = _live.Get(Followed());
        long seq = project.Sequence;
        var before = ModelJson.Serialise(project.State);
        var good = _now;

        File.Delete(_file);
        _now += TimeSpan.FromSeconds(30);
        follower.Check();
        Assert.Contains("not where Follow__File says", _status.Problem);
        var since = _now;
        Assert.Equal(since, _status.ProblemSince);

        // A newer version's file is refused in the old loader's own words, and the trouble is the same stretch of it.
        Write(Package($$"""{ "SchemaVersion": {{Old.ProjectFile.CurrentSchemaVersion + 1}}, "Building": {} }"""));
        _now += TimeSpan.FromSeconds(30);
        follower.Check();
        Assert.Contains("newer version", _status.Problem);
        Assert.Equal(since, _status.ProblemSince);
        // The log is told the kind of failure and never the loader's words, which are made from the file.
        Assert.Contains(_said.Lines, l => l.Contains("could not be brought in (InvalidDataException)"));
        Assert.DoesNotContain(_said.Lines, l => l.Contains("newer version"));

        // Something that is not a timetable at all.
        Write("not a timetable"u8.ToArray());
        _now += TimeSpan.FromSeconds(30);
        follower.Check();
        Assert.StartsWith("The current app's file could not be brought in", _status.Problem);

        Assert.Equal(seq, project.Sequence);
        Assert.Equal(before, ModelJson.Serialise(project.State));
        Assert.Equal(good, _status.Checked);

        // Put right over there, and it follows again.
        acro.Start = new(10, 0);
        Save(p);
        _now += TimeSpan.FromSeconds(30);
        follower.Check();
        Assert.Null(_status.Problem);
        Assert.Null(_status.ProblemSince);
        Assert.Equal(new TimeOnly(10, 0), project.State.Sessions[acro.Id]!.Start);
    }

    [Fact]
    public void TheLaptopsCopierIsAskedFirstSoAStaleCopyIsNeverCalledUpToDate()
    {
        var note = Path.Combine(Path.GetDirectoryName(_file)!, Follower.CopierNote);
        void Note(string state, DateTimeOffset at) => File.WriteAllText(note, $"{state} {at.ToUnixTimeSeconds()}\n");
        var (p, acro, _) = School();
        Save(p);
        var follower = Make();

        // Following a file directly there is no note, and nothing is asked of one.
        follower.Check();
        Assert.Null(_status.Problem);
        var project = _live.Get(Followed());

        Note("ok", _now);
        _now += TimeSpan.FromSeconds(30);
        follower.Check();
        Assert.Null(_status.Problem);
        var good = _status.Checked;

        // The copier cannot find the file: its last copy is still there and reads perfectly well, but the
        // timetable is not called up to date, and a change in that copy waits until the copier is right.
        acro.Start = new(10, 0);
        Save(p);
        Note("missing", _now);
        _now += TimeSpan.FromSeconds(30);
        follower.Check();
        Assert.Contains("cannot find the current app's file", _status.Problem);
        Assert.Equal(good, _status.Checked);
        Assert.NotEqual(new TimeOnly(10, 0), project.State.Sessions[acro.Id]!.Start);

        Note("failed", _now);
        follower.Check();
        Assert.Contains("could not copy", _status.Problem);

        // A copier that has stopped writes no note at all: an "ok" more than three minutes old says so.
        Note("ok", _now - Follower.CopierQuiet - TimeSpan.FromSeconds(1));
        follower.Check();
        Assert.Contains("has not looked", _status.Problem);

        Note("nonsense", _now);
        follower.Check();
        Assert.Contains("not one this app understands", _status.Problem);

        // Put right, and what waited comes across.
        Note("ok", _now);
        _now += TimeSpan.FromSeconds(30);
        follower.Check();
        Assert.Null(_status.Problem);
        Assert.Equal(new TimeOnly(10, 0), project.State.Sessions[acro.Id]!.Start);
    }

    [Theory]
    [InlineData(Role.Viewer)]
    [InlineData(Role.Editor)]
    [InlineData(Role.Administrator)]
    public void FollowingRefusesEveryChangeAndNarrowsNothingThatIsSeen(Role role)
    {
        var plain = new Access(new Account(Guid.NewGuid(), "x", "X", role, AccountTags.Staff, Guid.NewGuid(), false, false, "s", DateTimeOffset.UnixEpoch, true));
        var following = plain with { Following = true };

        Assert.Contains(Collections.All, c => plain.MayChange(c.Name));
        Assert.All(Collections.All, c => Assert.False(following.MayChange(c.Name), c.Name));
        Assert.False(following.MayEdit);
        Assert.False(following.MayEditSomething);
        // What they see, and whose registers are counted as still to take, is what it was.
        Assert.Equal(plain.Events, following.Events);
        Assert.Equal(plain.Registers, following.Registers);
        Assert.Equal(plain.MayUseAdminTools, following.MayUseAdminTools);
    }

    [Fact]
    public void TheOneDoorRefusesAChangeToAFollowedTimetableInWords()
    {
        var project = _live.Get(_store.Create("Followed", Timetable.Empty, Follower.By, _now));
        var admin = new Account(Guid.NewGuid(), "admin", "Admin", Role.Administrator, AccountTags.None, null, false, false, "s", DateTimeOffset.UnixEpoch, true);
        var clock = new SchoolClock(new ConfigurationBuilder().Build(), new HostingEnvironment { EnvironmentName = "Production" }, NullLogger<SchoolClock>.Instance);
        var toasts = new Toasts();
        var w = new Workspace(new Access(admin) { Following = true }, project, clock, toasts, new AccountDirectory(_store, () => _now));
        long seq = project.Sequence;

        Assert.False(w.Commit(t => new Draft(t).Put(Collections.Rooms, new Room { Name = "Studio Z" }).ToBatch("Added the space Studio Z")));

        Assert.Equal(seq, project.Sequence);
        Assert.Equal(FollowWords.Refusal, Assert.Single(toasts.Shown).Text);
        Assert.Equal(Follower.Display, w.NameOf(Follower.By));
    }
}
