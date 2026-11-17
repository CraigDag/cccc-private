using Cccc.Domain.Sample;
using Cccc.Store;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Tests;

public class ProjectChoiceTests
{
    [Fact]
    public void BringingInANewTimetableDoesNotMoveTheSchoolOntoIt()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "stores", Guid.NewGuid().ToString("N"));
        try
        {
            var store = ProjectStore.Open(folder, create: true);
            var day = new DateOnly(2030, 3, 6);
            var live = new LiveProjects(store, () => DateTimeOffset.UtcNow, () => day);
            var choice = new ProjectChoice(store, live);
            var school = store.Create("School", SampleSchool.Create(day, day), "a", DateTimeOffset.UtcNow);
            Assert.Equal(school, choice.OpenId());
            store.Create("Imported", SampleSchool.Create(day, day), "a", DateTimeOffset.UtcNow.AddMinutes(1));
            Assert.Equal(school, choice.OpenId());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(folder, true); } catch (IOException) { }
        }
    }
}
