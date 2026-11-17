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

    [Fact]
    public void TheEraserLeavesAFileWithoutThePersonByteForByte()
    {
        const string json = "{\"students\":[{\"id\":\"00000000-0000-0000-0000-000000000001\",\"firstName\":\"Kim\"}]}";
        Assert.Same(json, ProjectFiles.Eraser(Guid.NewGuid())(json));
    }
}
