using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;

namespace Cccc.Web.Features.Admin;

/// <summary>
/// The rebuilt app's own file, <c>.cccc2</c>: a zip holding <c>project.json</c>. A different name from
/// the old <c>.cccc</c> on purpose, so the old app is never handed a file it would call corrupt.
/// </summary>
public static class ProjectFiles
{
    public const string Extension = ".cccc2";
    /// <summary>
    /// The format this build writes. 2 added the Scratch Week; a file of format 1 has none, which reads as an
    /// empty one. 3 added former classes, spaces and facilitators, which a build of format 2 would read as in
    /// use; a file of format 2 has nothing former.
    /// </summary>
    private const int Format = 3;
    private const string Marker = "cccc2 ";

    public static byte[] Write(Timetable t)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("format.txt").Open(), Encoding.UTF8)) w.Write(Marker + Format);
            using (var w = new StreamWriter(zip.CreateEntry("project.json", CompressionLevel.Optimal).Open(), Encoding.UTF8)) w.Write(ModelJson.Serialise(t));
        }
        return ms.ToArray();
    }

    public static bool LooksLikeOurs(byte[] bytes)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            return zip.GetEntry("format.txt") is not null;
        }
        catch (InvalidDataException) { return false; }
    }

    public static Timetable Read(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using (var r = new StreamReader(zip.GetEntry("format.txt")?.Open() ?? throw new InvalidDataException("This is not a .cccc2 file.")))
        {
            var said = r.ReadToEnd().Trim();
            if (!said.StartsWith(Marker, StringComparison.Ordinal) || !int.TryParse(said[Marker.Length..], out var n) || n < 1)
                throw new InvalidDataException("This is not a .cccc2 file this app understands.");
            if (n > Format) throw new InvalidDataException("This .cccc2 file was saved by a newer version of the app.");
        }
        using var j = new StreamReader(zip.GetEntry("project.json")?.Open() ?? throw new InvalidDataException("The file has no project in it."));
        return ModelJson.Deserialise(j.ReadToEnd());
    }

    /// <summary>
    /// The scrub erasure runs over every stored copy: any object whose id is the person's has its name
    /// and email replaced. The id is kept, so registers and counts still add up.
    /// </summary>
    public static Func<string, string> Eraser(Guid personId)
    {
        var id = personId.ToString();
        return json =>
        {
            if (!json.Contains(id, StringComparison.OrdinalIgnoreCase)) return json;
            var node = JsonNode.Parse(json);
            Walk(node);
            return node?.ToJsonString() ?? json;
        };

        void Walk(JsonNode? n)
        {
            switch (n)
            {
                case JsonObject o:
                    if (o["id"] is JsonValue v && v.TryGetValue<string>(out var s) && string.Equals(s, id, StringComparison.OrdinalIgnoreCase))
                        foreach (var key in new[] { "firstName", "surname", "name", "email" })
                            if (o.ContainsKey(key)) o[key] = key == "email" ? "" : "[erased]";
                    foreach (var kv in o.ToList()) Walk(kv.Value);
                    break;
                case JsonArray a:
                    foreach (var x in a) Walk(x);
                    break;
            }
        }
    }

    /// <summary>
    /// The same erasure for the words of the history: a label ("Changed …") is not a record with an id, so
    /// the person is found there by every name they have had. Labels are written not to name people, and
    /// this is what keeps that promise if one ever does, or did before.
    /// </summary>
    public static Func<string, string> Relabeller(IEnumerable<string> names)
    {
        var all = names.Select(n => n.Trim()).Where(n => n.Length > 1 && !n.Contains("[erased]", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(n => n.Length).ToList();
        if (all.Count == 0) return label => label;
        // A whole name, never part of a word (erasing "Ann" leaves "Annual" alone); the longest first, so
        // a name inside a longer one they also had is not reached first.
        var whole = new System.Text.RegularExpressions.Regex(
            string.Join("|", all.Select(n => @"(?<!\w)" + System.Text.RegularExpressions.Regex.Escape(n) + @"(?!\w)")),
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return label => whole.Replace(label, "[erased]");
    }

    /// <summary>Every name a person has had in a timetable's stored history, and the one they have now.</summary>
    public static IEnumerable<string> NamesOf(Guid personId, IEnumerable<Change> history, Timetable now)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (now.Students[personId] is { } st) names.Add(st.Name);
        if (now.Facilitators[personId] is { } f) names.Add(f.Name);
        foreach (var op in history.SelectMany(c => c.Batch.Ops).Where(o => o.Id == personId))
            foreach (var node in new[] { op.Before, op.After })
            {
                if (node is null) continue;
                if (op.Collection == Collections.Students.Name && node.Deserialize<Student>(ModelJson.Options) is { } s) names.Add(s.Name);
                if (op.Collection == Collections.Facilitators.Name && node.Deserialize<Facilitator>(ModelJson.Options) is { } x) names.Add(x.Name);
            }
        return names;
    }
}
