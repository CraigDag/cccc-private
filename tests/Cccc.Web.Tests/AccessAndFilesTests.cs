using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Sample;
using Cccc.Store;
using Cccc.Web.Features.Admin;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Tests;

public class AccessTests
{
    private static Access As(Role role, bool suspended = false) =>
        new(new Account(Guid.NewGuid(), "x", "X", role, AccountTags.None, null, suspended, false, "s", DateTimeOffset.UnixEpoch, true));

    [Theory]
    [InlineData("registers", true, true, true)]
    [InlineData("requests", true, true, true)]
    [InlineData("offers", true, true, true)]
    [InlineData("spaceAsks", true, true, true)]
    [InlineData("outOfUse", false, false, true)]
    [InlineData("absences", false, false, true)]
    [InlineData("sessions", false, true, true)]
    [InlineData("events", false, true, true)]
    [InlineData("scratch", false, true, true)]
    [InlineData("rooms", false, false, true)]
    [InlineData("students", false, false, true)]
    [InlineData("settings", false, false, true)]
    public void WhoMayChangeWhichCollection(string collection, bool viewer, bool editor, bool administrator)
    {
        Assert.Equal(viewer, As(Role.Viewer).MayChange(collection));
        Assert.Equal(editor, As(Role.Editor).MayChange(collection));
        Assert.Equal(administrator, As(Role.Administrator).MayChange(collection));
    }

    [Theory]
    [InlineData("registers")]
    [InlineData("requests")]
    [InlineData("offers")]
    [InlineData("spaceAsks")]
    [InlineData("sessions")]
    [InlineData("scratch")]
    [InlineData("rooms")]
    public void NobodyAndASuspendedAdministratorMayChangeNothing(string collection)
    {
        Assert.False(Access.Nobody.MayChange(collection));
        Assert.False(As(Role.Administrator, suspended: true).MayChange(collection));
    }

    /// <summary>
    /// Anybody signed in may ask and offer, the edit narrowing it to the facilitator their account is linked
    /// to; only an administrator answers, since a yes records an absence. Following, nobody asks anything.
    /// </summary>
    [Fact]
    public void AnybodyLinkedAsksForThemselvesOnlyAndOnlyAnAdministratorAnswers()
    {
        var ada = Guid.NewGuid();
        Access Linked(Role role, bool suspended = false) => new(new Account(Guid.NewGuid(), "x", "X", role, AccountTags.None, ada, suspended, false, "s", DateTimeOffset.UnixEpoch, true));
        Assert.Equal(new Cccc.Domain.Rules.AskingReader(false, ada), Linked(Role.Viewer).Asking);
        Assert.Equal(new Cccc.Domain.Rules.AskingReader(false, ada), Linked(Role.Editor).Asking);
        Assert.Equal(new Cccc.Domain.Rules.AskingReader(true, ada), Linked(Role.Administrator).Asking);
        Assert.Equal(new Cccc.Domain.Rules.AskingReader(true, null), As(Role.Administrator).Asking);
        Assert.Equal(new Cccc.Domain.Rules.AskingReader(false, null), As(Role.Viewer).Asking);
        Assert.Equal(new Cccc.Domain.Rules.AskingReader(false, null), Access.Nobody.Asking);
        // A suspended account asks for nobody, though its link is still there.
        Assert.Equal(new Cccc.Domain.Rules.AskingReader(false, null), Linked(Role.Administrator, suspended: true).Asking);
        Assert.False(As(Role.Editor).MayAnswerAsking);
        Assert.True(As(Role.Administrator).MayAnswerAsking);
        var following = Linked(Role.Viewer) with { Following = true };
        Assert.False(following.MayChange("requests"));
        Assert.False(following.MayChange("offers"));
        Assert.False(following.MayChange("spaceAsks"));
    }

    /// <summary>
    /// The Scratch Week is editors' and administrators' only, one of its four safeguards: a coach is never
    /// shown it. Following the current app hides nothing from an editor (its plan comes across to look at)
    /// and changes nothing (the one door refuses, as for every collection).
    /// </summary>
    [Fact]
    public void OnlyEditorsAndAdministratorsSeeTheScratchWeekAndFollowingChangesNothingInIt()
    {
        Assert.False(Access.Nobody.MaySeeScratch);
        Assert.False(As(Role.Viewer).MaySeeScratch);
        Assert.True(As(Role.Editor).MaySeeScratch);
        Assert.True(As(Role.Administrator).MaySeeScratch);
        var following = As(Role.Editor) with { Following = true };
        Assert.True(following.MaySeeScratch);
        Assert.False(following.MayChange("scratch"));
    }
}

public class RollCallAccessTests
{
    private static Access With(Role role, AccountTags tags, Guid? linked = null, bool suspended = false) =>
        new(new Account(Guid.NewGuid(), "x", "X", role, tags, linked, suspended, false, "s", DateTimeOffset.UnixEpoch, true));

    /// <summary>
    /// The roll call lists every student in the building by name, so it is staff's: editors and administrators,
    /// anybody linked to a facilitator and anybody tagged Staff; never a student, nor a viewer who is none of
    /// those. It changes no timetable, so following the current app does not narrow it.
    /// </summary>
    [Fact]
    public void StaffCallTheRollAndAStudentNever()
    {
        Assert.False(Access.Nobody.MayCallTheRoll);
        Assert.False(With(Role.Viewer, AccountTags.None).MayCallTheRoll);
        Assert.False(With(Role.Viewer, AccountTags.Student).MayCallTheRoll);
        Assert.True(With(Role.Viewer, AccountTags.Staff).MayCallTheRoll);
        Assert.True(With(Role.Viewer, AccountTags.Student | AccountTags.Staff).MayCallTheRoll);
        Assert.True(With(Role.Viewer, AccountTags.None, Guid.NewGuid()).MayCallTheRoll);
        Assert.True(With(Role.Editor, AccountTags.None).MayCallTheRoll);
        Assert.True(With(Role.Administrator, AccountTags.None).MayCallTheRoll);
        // A suspended account calls nothing, whatever it is linked to or tagged.
        Assert.False(With(Role.Administrator, AccountTags.Staff, Guid.NewGuid(), suspended: true).MayCallTheRoll);
        Assert.False(With(Role.Viewer, AccountTags.Staff, Guid.NewGuid(), suspended: true).MayCallTheRoll);
        Assert.True((With(Role.Viewer, AccountTags.None, Guid.NewGuid()) with { Following = true }).MayCallTheRoll);
        Assert.True((With(Role.Editor, AccountTags.None) with { Following = true }).MayCallTheRoll);
    }
}

public class ProjectFileTests
{
    private static readonly DateOnly Day = new(2030, 3, 6);

    [Fact]
    public void AProjectComesBackFromItsFileUnchanged()
    {
        var t = SampleSchool.Create(Day, Day);
        Assert.NotEmpty(t.Sessions);
        Assert.NotEmpty(t.Students);
        var back = ProjectFiles.Read(ProjectFiles.Write(t));
        Assert.Equal(ModelJson.Serialise(t), ModelJson.Serialise(back));
    }

    [Fact]
    public void AFileFromANewerVersionIsRefusedRatherThanHalfRead()
    {
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("format.txt").Open())) w.Write("cccc2 8");   // one past this build's
            using (var w = new StreamWriter(zip.CreateEntry("project.json").Open())) w.Write("{}");
        }
        var e = Assert.Throws<InvalidDataException>(() => ProjectFiles.Read(ms.ToArray()));
        Assert.Contains("newer version", e.Message);
    }

    [Fact]
    public void AFileOfTheFirstFormatStillReadsWithAnEmptyScratchWeekAndNothingAsked()
    {
        var t = SampleSchool.Create(Day, Day);
        Assert.NotEmpty(t.Requests);
        Assert.NotEmpty(t.Offers);
        using var ms = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("format.txt").Open())) w.Write("cccc2 1");
            var json = System.Text.Json.Nodes.JsonNode.Parse(ModelJson.Serialise(t))!.AsObject();
            foreach (var later in new[] { "scratch", "requests", "offers", "spaceAsks", "outOfUse" }) Assert.True(json.Remove(later), later);
            using (var w = new StreamWriter(zip.CreateEntry("project.json").Open())) w.Write(json.ToJsonString());
        }
        var back = ProjectFiles.Read(ms.ToArray());
        Assert.Equal(t.Sessions.Count, back.Sessions.Count);
        Assert.Empty(back.Scratch);
        Assert.Empty(back.Requests);
        Assert.Empty(back.Offers);
        Assert.Empty(back.SpaceAsks);
        Assert.Empty(back.OutOfUse);
        Assert.DoesNotContain(back.Classes, c => c.FormerSince is not null);
        // And what this build writes says the format it is, so an older build refuses it rather than drop
        // the plan or what was asked, or offer a former class ahead again.
        var gone = t.Classes.First();
        var asked = new SpaceAsk { FacilitatorId = t.Facilitators.First().Id, Date = Day, Start = new(16, 0), End = new(17, 0), RoomId = t.Rooms.First().Id, Title = "Extra practice" };
        var broken = new OutOfUse { RoomId = t.Rooms.First(r => r.Has.Count > 0).Id, EquipmentId = t.Rooms.First(r => r.Has.Count > 0).Has[0], From = Day, Until = Day.AddDays(2) };
        var planned = t with { Scratch = EntitySet<Session>.Of([new Session { Date = ScratchWeek.Monday }]),
                               Classes = t.Classes.Put(gone with { FormerSince = Day }), SpaceAsks = EntitySet<SpaceAsk>.Of([asked]),
                               OutOfUse = EntitySet<OutOfUse>.Of([broken]) };
        var again = ProjectFiles.Read(ProjectFiles.Write(planned));
        Assert.Single(again.Scratch);
        Assert.Equal(Day, again.Classes[gone.Id]!.FormerSince);
        Assert.Equal(t.Requests.Single(), again.Requests.Single());
        Assert.Equal(t.Offers.Single(), again.Offers.Single());
        Assert.Equal(asked, again.SpaceAsks.Single());
        Assert.Equal(broken, again.OutOfUse.Single());
        using var written = new System.IO.Compression.ZipArchive(new MemoryStream(ProjectFiles.Write(planned)));
        using var r = new StreamReader(written.GetEntry("format.txt")!.Open());
        Assert.Equal("cccc2 7", r.ReadToEnd());
    }

    /// <summary>
    /// The file and the store are the same timetable written down, so what one format brings the other does: equipment
    /// moved the store to 5 and left files at 4, where a build before equipment would have dropped it without a word.
    /// </summary>
    [Fact]
    public void TheFileFormatMovesWithTheStoresFormat() =>
        Assert.Equal(Cccc.Store.ProjectStore.FormatVersion, ProjectFiles.Format);

    [Fact]
    public void ErasingAStudentBlanksTheirNameEverywhereAndLeavesEverybodyElse()
    {
        var t = SampleSchool.Create(Day, Day);
        var gone = t.Students.OrderBy(s => s.Surname).First();
        var kept = t.Students.First(s => s.Id != gone.Id);
        var json = Cccc.Web.Features.Admin.ProjectFiles.Eraser(gone.Id)(ModelJson.Serialise(t));
        Assert.DoesNotContain(gone.Email!, json);
        Assert.Contains(kept.Email!, json);
        var after = ModelJson.Deserialise(json);
        Assert.Equal("[erased]", after.Students[gone.Id]!.FirstName);
        Assert.Equal(t.Students.Count, after.Students.Count);   // the id stays, so counts still add up
    }

    /// <summary>
    /// Somebody taken off the timetable is still named in the stored changes for 30 days, so erasure has to
    /// be able to reach them; somebody still on it, or already erased, is not "taken off". A facilitator who
    /// taught before today is not taken off at all: they stay, former, and are erased from the list of
    /// facilitators like anybody on it.
    /// </summary>
    [Fact]
    public void ThePeopleTakenOffButStillNamedInTheHistoryAreFoundForErasure()
    {
        var visitor = new Facilitator { Name = "Wren Example" };   // never taught, so removing them takes them off
        var sample = SampleSchool.Create(Day, Day);
        var t = sample with { Facilitators = sample.Facilitators.Put(visitor) };
        var students = t.Students.OrderBy(s => s.Surname, Cccc.Domain.NameOrder.Comparer).ToList();
        var (gone, erased) = (students[0], students[1]);
        var coach = t.Facilitators.Where(f => t.Sessions.Any(s => s.Date < Day && s.FacilitatorIds.Contains(f.Id)))
            .OrderBy(f => f.Name, Cccc.Domain.NameOrder.Comparer).First();
        var history = new List<Change>();
        var now = t;
        foreach (var build in new Func<Timetable, Batch>[]
        {
            x => Edits.RemoveStudent(x, gone.Id, Day),
            x => Edits.RemoveFacilitator(x, visitor.Id, Day),
            x => Edits.RemoveStudent(x, erased.Id, Day),
            x => Edits.RemoveFacilitator(x, coach.Id, Day),
        })
        {
            var batch = build(now);
            history.Add(new Change(history.Count + 1, "admin", DateTimeOffset.UnixEpoch, batch));
            now = Apply.Batch(now, batch);
        }
        // The third change as it is stored after erasing that student.
        var scrub = ProjectFiles.Eraser(erased.Id);
        history[2] = history[2] with
        {
            Batch = history[2].Batch with { Ops = [.. history[2].Batch.Ops.Select(o => o with { Before = o.Before is null ? null : System.Text.Json.Nodes.JsonNode.Parse(scrub(o.Before.ToJsonString())) })] },
        };

        var found = FormerPeople.In(history, now);

        Assert.Equal(new[] { (true, gone.Id, gone.Name), (false, visitor.Id, visitor.Name) }.OrderBy(x => x.Item3, Cccc.Domain.NameOrder.Comparer),
            found.Select(f => (f.Student, f.Id, f.Name)));
        Assert.Equal(Day, now.Facilitators[coach.Id]?.FormerSince);
        // Put back on the timetable, they are listed with everybody on it instead.
        Assert.DoesNotContain(FormerPeople.In(history, now with { Students = now.Students.Put(gone) }), f => f.Id == gone.Id);
    }

    /// <summary>
    /// A label is words, not a record with an id: erasure finds the person there by every name they have
    /// had in the history, longest first so a full name goes before a part of it.
    /// </summary>
    [Fact]
    public void ErasureFindsEveryNameAPersonHasHadAndTakesItOutOfTheLabels()
    {
        var ada = new Facilitator { Name = "Ada Lind" };
        var t = Timetable.Empty with { Facilitators = EntitySet<Facilitator>.Of([ada]) };
        var renamed = new Draft(t).Put(Collections.Facilitators, ada with { Name = "Ada Lind-Marr" }).ToBatch("Changed Ada Lind");
        var now = Apply.Batch(t, renamed);
        var names = ProjectFiles.NamesOf(ada.Id, [new Change(1, "admin", DateTimeOffset.UnixEpoch, renamed)], now).ToList();
        Assert.Equal(["Ada Lind", "Ada Lind-Marr"], names.Order(StringComparer.Ordinal));
        var relabel = ProjectFiles.Relabeller(names);
        Assert.Equal("Changed [erased], then [erased]", relabel("Changed ada lind, then Ada Lind-Marr"));
        Assert.Same("Copied a week", relabel("Copied a week"));
        // A whole name only: somebody else whose name begins the same way keeps theirs.
        Assert.Equal("Moved [erased]'s class, not Ada Linden's", relabel("Moved Ada Lind's class, not Ada Linden's"));
    }

    [Fact]
    public void TheEraserLeavesAFileWithoutThePersonByteForByte()
    {
        const string json = "{\"students\":[{\"id\":\"00000000-0000-0000-0000-000000000001\",\"firstName\":\"Kim\"}]}";
        Assert.Same(json, ProjectFiles.Eraser(Guid.NewGuid())(json));
    }
}
