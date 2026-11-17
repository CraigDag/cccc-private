using System.IO.Compression;
using System.Text;
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
    private const string Marker = "cccc2 1";

    public static byte[] Write(Timetable t)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry("format.txt").Open(), Encoding.UTF8)) w.Write(Marker);
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
            if (r.ReadToEnd().Trim() != Marker) throw new InvalidDataException("This .cccc2 file was saved by a newer version of the app.");
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
}
