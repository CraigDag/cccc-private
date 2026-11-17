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
    [InlineData("sessions", false, true, true)]
    [InlineData("events", false, true, true)]
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
    [InlineData("sessions")]
    [InlineData("rooms")]
    public void NobodyAndASuspendedAdministratorMayChangeNothing(string collection)
    {
        Assert.False(Access.Nobody.MayChange(collection));
        Assert.False(As(Role.Administrator, suspended: true).MayChange(collection));
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
            using (var w = new StreamWriter(zip.CreateEntry("format.txt").Open())) w.Write("cccc2 2");
            using (var w = new StreamWriter(zip.CreateEntry("project.json").Open())) w.Write("{}");
        }
        var e = Assert.Throws<InvalidDataException>(() => ProjectFiles.Read(ms.ToArray()));
        Assert.Contains("newer version", e.Message);
    }

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
    /// be able to reach them; somebody still on it, or already erased, is not "taken off".
    /// </summary>
    [Fact]
    public void ThePeopleTakenOffButStillNamedInTheHistoryAreFoundForErasure()
    {
        var t = SampleSchool.Create(Day, Day);
        var students = t.Students.OrderBy(s => s.Surname, Cccc.Domain.NameOrder.Comparer).ToList();
        var (gone, erased) = (students[0], students[1]);
        var coach = t.Facilitators.OrderBy(f => f.Name, Cccc.Domain.NameOrder.Comparer).First();
        var history = new List<Change>();
        var now = t;
        foreach (var build in new Func<Timetable, Batch>[]
        {
            x => Edits.RemoveStudent(x, gone.Id, Day),
            x => Edits.RemoveFacilitator(x, coach.Id, Day),
            x => Edits.RemoveStudent(x, erased.Id, Day),
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

        Assert.Equal(new[] { (true, gone.Id, gone.Name), (false, coach.Id, coach.Name) }.OrderBy(x => x.Item3, Cccc.Domain.NameOrder.Comparer),
            found.Select(f => (f.Student, f.Id, f.Name)));
        // Put back on the timetable, they are listed with everybody on it instead.
        Assert.DoesNotContain(FormerPeople.In(history, now with { Students = now.Students.Put(gone) }), f => f.Id == gone.Id);
    }

    [Fact]
    public void TheEraserLeavesAFileWithoutThePersonByteForByte()
    {
        const string json = "{\"students\":[{\"id\":\"00000000-0000-0000-0000-000000000001\",\"firstName\":\"Kim\"}]}";
        Assert.Same(json, ProjectFiles.Eraser(Guid.NewGuid())(json));
    }
}
