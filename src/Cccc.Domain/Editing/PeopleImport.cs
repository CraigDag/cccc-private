using Cccc.Domain.Model;

namespace Cccc.Domain.Editing;

/// <summary>One pasted line, as the preview shows it: who it would add, or why it is left out.</summary>
public sealed record ImportLine(int LineNumber, string Text, Student? Student, Facilitator? Facilitator, string? Note, bool Skipped);

/// <summary>
/// Adding people by pasting rows from a spreadsheet (Excel copies tab-separated; a CSV pastes
/// comma-separated; either is read). Nothing is changed until the preview is accepted, and then it is
/// one change, so one Undo takes the whole import back. A row naming somebody already on the list is
/// left out rather than duplicated; a group the timetable does not have is said, never created.
/// </summary>
public static class PeopleImport
{
    public const string StudentColumns = "First name, Surname, Email (optional), Group (optional)";
    public const string FacilitatorColumns = "Name, Email (optional)";

    public static IReadOnlyList<ImportLine> Students(Timetable t, string pasted)
    {
        var known = t.Students.Select(s => Key(s.FirstName + " " + s.Surname)).ToHashSet();
        var lines = new List<ImportLine>();
        foreach (var (n, text, cells) in Rows(pasted))
        {
            if (n == 1 && LooksLikeHeader(cells, "first")) continue;
            var first = Cell(cells, 0);
            var surname = Cell(cells, 1);
            if (first.Length == 0 && surname.Length == 0) continue;
            if (first.Length == 0 || surname.Length == 0)
            {
                lines.Add(new(n, text, null, null, "Needs a first name and a surname.", true));
                continue;
            }
            if (!known.Add(Key(first + " " + surname)))
            {
                lines.Add(new(n, text, null, null, "Already on the list, or twice in what was pasted.", true));
                continue;
            }
            var groupName = Cell(cells, 3);
            var group = groupName.Length == 0 ? null
                : t.Groups.FirstOrDefault(g => string.Equals(g.Name.Trim(), groupName, StringComparison.CurrentCultureIgnoreCase));
            var note = groupName.Length > 0 && group is null ? $"No group called \"{groupName}\": added with no group." : null;
            lines.Add(new(n, text, new Student { FirstName = first, Surname = surname, Email = Cell(cells, 2), GroupId = group?.Id }, null, note, false));
        }
        return lines;
    }

    public static IReadOnlyList<ImportLine> Facilitators(Timetable t, string pasted)
    {
        var known = t.Facilitators.Select(f => Key(f.Name)).ToHashSet();
        var lines = new List<ImportLine>();
        foreach (var (n, text, cells) in Rows(pasted))
        {
            if (n == 1 && LooksLikeHeader(cells, "name")) continue;
            var name = Cell(cells, 0);
            if (name.Length == 0) continue;
            if (!known.Add(Key(name)))
            {
                lines.Add(new(n, text, null, null, "Already on the list, or twice in what was pasted.", true));
                continue;
            }
            lines.Add(new(n, text, null, new Facilitator { Name = name, Email = Cell(cells, 1) }, null, false));
        }
        return lines;
    }

    public static Batch Apply(Timetable t, IReadOnlyList<ImportLine> lines)
    {
        var d = new Draft(t);
        int n = 0;
        foreach (var l in lines.Where(l => !l.Skipped))
        {
            if (l.Student is { } s) { d.Put(Collections.Students, s); n++; }
            if (l.Facilitator is { } f) { d.Put(Collections.Facilitators, f); n++; }
        }
        return d.ToBatch(n == 1 ? "Imported 1 person" : $"Imported {n} people");
    }

    private static IEnumerable<(int Number, string Text, string[] Cells)> Rows(string pasted)
    {
        var raw = (pasted ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (int i = 0; i < raw.Length; i++)
        {
            var text = raw[i].Trim();
            if (text.Length == 0) continue;
            yield return (i + 1, text, Split(text));
        }
    }

    /// <summary>Tabs if there are any (Excel), otherwise commas or semicolons, with "quoted, cells" kept whole.</summary>
    private static string[] Split(string line)
    {
        char sep = line.Contains('\t') ? '\t' : line.Contains(';') && !line.Contains(',') ? ';' : ',';
        var cells = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"') { cur.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (c == sep && !quoted) { cells.Add(cur.ToString()); cur.Clear(); }
            else cur.Append(c);
        }
        cells.Add(cur.ToString());
        return [.. cells.Select(x => x.Trim())];
    }

    private static string Cell(string[] cells, int i) => i < cells.Length ? cells[i] : "";

    /// <summary>A heading row is recognised only by its exact words, so a student called First is never taken for one.</summary>
    private static bool LooksLikeHeader(string[] cells, string kind) =>
        cells.Length > 0 && (kind == "first"
            ? cells[0] is var c && (c.Equals("First name", StringComparison.OrdinalIgnoreCase) || c.Equals("Fornavn", StringComparison.OrdinalIgnoreCase) || c.Equals("First", StringComparison.OrdinalIgnoreCase) && Cell(cells, 1).Equals("Surname", StringComparison.OrdinalIgnoreCase))
            : cells[0].Equals("Name", StringComparison.OrdinalIgnoreCase) || cells[0].Equals("Navn", StringComparison.OrdinalIgnoreCase));

    private static string Key(string name) => string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToUpperInvariant();
}
