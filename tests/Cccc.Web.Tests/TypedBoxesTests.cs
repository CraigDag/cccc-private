using System.Text.RegularExpressions;

namespace Cccc.Web.Tests;

/// <summary>
/// No box may draw the server's text AND take every keystroke itself (<c>value="@x"</c> with <c>@oninput</c>):
/// the render that comes back writes the server's idea of the text over a box somebody is still typing in,
/// and what was typed in between is gone. Measured on 17 Nov 2026 through a link holding everything 100 ms
/// each way, a key every 20 ms: that shape lost letters in 6 typings of 6 ("Outreach studio annex" came out
/// "Outx"), while <c>@bind</c> with <c>@bind:event="oninput"</c> lost none in 6, because Blazor notes what
/// the box holds before it draws again, so there is nothing to write back. <c>TypedText</c> is the one place
/// the raw shape is allowed: it never draws the server's text after the first time.
/// </summary>
public class TypedBoxesTests
{
    private static readonly Regex Comments = new(@"@\*.*?\*@", RegexOptions.Singleline);

    // An input or textarea up to the first '>'. A lambda's '=>' can end it early, which only ever cuts off
    // attributes after a handler; the shape looked for is written as value then @oninput.
    private static readonly Regex Box = new(@"<(?:input|textarea)\b[^>]*>", RegexOptions.Singleline);

    [Fact]
    public void NoBoxWritesTheServersTextBackOverTypingButTypedText()
    {
        var root = Root();
        var sep = Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(Path.Combine(root, "src", "Cccc.Web"), "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .ToList();
        int boxes = 0, bound = 0;
        var offenders = new List<string>();
        foreach (var file in files.Where(f => Path.GetFileName(f) != "TypedText.razor"))
        {
            // Comments blanked to their own newlines, so a line number points at the box.
            var markup = Comments.Replace(File.ReadAllText(file), m => new string('\n', m.Value.Count(c => c == '\n')));
            foreach (Match m in Box.Matches(markup))
            {
                boxes++;
                if (m.Value.Contains("@bind", StringComparison.Ordinal)) bound++;
                if (m.Value.Contains("@oninput", StringComparison.Ordinal) && Regex.IsMatch(m.Value, @"\bvalue\s*=\s*""@"))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{markup[..m.Index].Count(c => c == '\n') + 1}");
            }
        }
        // What the scan saw, before what it did not: a pattern that matched nothing would pass with nothing checked.
        Assert.True(boxes >= 60 && bound >= 40, $"only {boxes} boxes ({bound} bound) in {files.Count} files: the scan is not reading the markup");
        Assert.True(offenders.Count == 0,
            "These boxes draw a value and take @oninput, which loses letters typed while the server answers. "
            + "Use @bind with @bind:event=\"oninput\", or TypedText:\n  " + string.Join("\n  ", offenders));
    }

    private static string Root()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
            if (Directory.Exists(Path.Combine(d.FullName, "src", "Cccc.Web"))) return d.FullName;
        throw new InvalidOperationException("src/Cccc.Web was not found above " + AppContext.BaseDirectory);
    }
}
