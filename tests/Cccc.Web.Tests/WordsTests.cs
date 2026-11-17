using Cccc.Web.Features.Data;
using Cccc.Web.Features.Week;

namespace Cccc.Web.Tests;

/// <summary>The words a count and a noun are written in, on lists, buttons, forms and reports.</summary>
public class WordsTests
{
    [Fact]
    public void ACountIsSingularForExactlyOne()
    {
        Assert.Equal("0 groups", Looks.Count(0, "group"));
        Assert.Equal("1 group", Looks.Count(1, "group"));
        Assert.Equal("2 groups", Looks.Count(2, "group"));
        Assert.Equal("1 class ahead", Looks.Count(1, "class ahead", "classes ahead"));
        Assert.Equal("3 classes ahead", Looks.Count(3, "class ahead", "classes ahead"));
    }

    [Fact]
    public void EveryKindOfDataIsAddedWithTheRightArticle()
    {
        var added = Kinds.All.ToDictionary(k => k.Slug, k => "Add " + Looks.A(k.One));
        Assert.Equal("Add a space", added["rooms"]);
        Assert.Equal("Add an area", added["areas"]);
        Assert.Equal("Add an absence", added["absences"]);
        Assert.Equal("Add a time rule", added["time-rules"]);
        Assert.All(Kinds.All, k => Assert.DoesNotMatch("^Add a [aeiouAEIOU]", "Add " + Looks.A(k.One)));
    }
}
