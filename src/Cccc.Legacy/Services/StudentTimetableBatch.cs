using System.Text;

namespace CCCCScheduler.Services;

/// <summary>One student's page and the file it is written to.</summary>
/// <param name="Name">Their name as the page shows it — unsanitised, because it is read not stored.</param>
/// <param name="FileName">The file it goes in, safe on every platform and unique in its folder.</param>
public sealed record TimetableFile(Guid StudentId, string Name, string FileName);

/// <summary>
/// Naming a school's worth of timetables at once.
///
/// <para>The bulk export is a loop; what earns a function is the naming, because two of the ways it
/// goes wrong are silent. A file name that collides with another student's <b>overwrites</b> it —
/// one family gets a timetable that is not theirs, the other gets nothing, and the folder looks
/// complete either way. And a name carrying a character the file system refuses throws part-way
/// through, leaving a folder that also looks finished.</para>
/// </summary>
public static class StudentTimetableBatch
{
    /// <summary>What a student with no usable name is filed under.</summary>
    public const string Unnamed = "Unnamed student";

    /// <summary>
    /// <b>Which end of the file name the week tag goes on.</b> Craig's call, 2026-09-10 — <i>"Use
    /// 'CCCC Scheduler', week first"</i> — applies to the web's batch; the two hibernating desktops
    /// keep what they had, which is why this is a choice rather than a change.
    /// </summary>
    public enum WeekTagPlace
    {
        /// <summary>"Alice Jensen — Week 37.pdf". What every caller did until 2026-09-10.</summary>
        Trailing,

        /// <summary>
        /// "CCCC Week 37 2026 — Alice Jensen.pdf". A folder of these sorts by week rather than by
        /// whichever child comes first alphabetically, which is how a school keeps them.
        /// </summary>
        Leading,
    }

    /// <summary>
    /// One file per student, in the order given, each named for them and unique within the run.
    /// </summary>
    /// <param name="weekTag">
    /// A short week label joined to each name — "Week 37" trailing, or the whole lead
    /// "CCCC Week 37 2026" leading. It stops a second run for a different week silently replacing
    /// the first, and tells the person opening the attachment which week they are looking at. Empty
    /// leaves the name bare either way.
    /// </param>
    /// <param name="place">Which end it goes on — see <see cref="WeekTagPlace"/>.</param>
    public static IReadOnlyList<TimetableFile> Plan(
        IEnumerable<(Guid Id, string Name)> students, string weekTag,
        WeekTagPlace place = WeekTagPlace.Trailing)
    {
        ArgumentNullException.ThrowIfNull(students);

        string tag = SafeName(weekTag);
        string joined = tag.Length == 0 ? "" : " — ";
        string prefix = place == WeekTagPlace.Leading ? tag + joined : "";
        string suffix = place == WeekTagPlace.Leading ? "" : joined + tag;

        // Uniqueness is decided on the student's own part of the name and nothing else, whichever
        // end the tag is on: the tag is the same string for every file in a run, so two children
        // called the same thing collide identically either way and the second is still "(2)".
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<TimetableFile>();

        foreach (var (id, name) in students)
        {
            string who = SafeName(name);
            if (who.Length == 0) who = Unnamed;
            if (IsReservedOnWindows(who)) who = "_" + who;

            files.Add(new TimetableFile(id, name, prefix + Unique(who, taken) + suffix + ".pdf"));
        }

        return files;
    }

    /// <summary>
    /// The one file <b>this</b> student is filed under, or <c>null</c> when the roster does not carry
    /// them.
    ///
    /// <para><b>Answered by planning the whole school and taking their row</b>, not by naming them in
    /// isolation — which is the entire point. Two students really can be called the same thing, and a
    /// single save that named them from their own name alone would offer the second one the first
    /// one's file: the picker asks "replace?", the user says yes to a name that looks right, and one
    /// family's week is gone while another family receives a classmate's. Going through
    /// <see cref="Plan"/> also means a timetable saved on its own and the same student's file in a
    /// whole-school run carry the <em>same</em> name, so neither can quietly overwrite the other.</para>
    ///
    /// <para>Stable for a given roster and order, which is what makes it usable as a suggestion: the
    /// second Sam Lee is offered "Sam Lee (2) — Week 37.pdf" every time, not a name that moves.</para>
    /// </summary>
    public static string? FileNameFor(
        Guid studentId, IEnumerable<(Guid Id, string Name)> students, string weekTag)
    {
        foreach (var file in Plan(students, weekTag))
            if (file.StudentId == studentId) return file.FileName;
        return null;
    }

    /// <summary>
    /// <paramref name="name"/> with everything a file system refuses turned into a space, and the
    /// result trimmed.
    ///
    /// <para><b>A fixed set, deliberately, rather than <see cref="Path.GetInvalidFileNameChars"/>.</b>
    /// That method answers differently on each platform — macOS objects only to the separator and the
    /// null — so using it would give the same student two different file names depending on which
    /// machine ran the export, and the macOS one would not open on Windows. The whole premise of this
    /// document is that both platforms produce the same thing.</para>
    /// </summary>
    public static string SafeName(string? name)
    {
        var sb = new StringBuilder();
        foreach (char c in name ?? "")
            sb.Append(c < ' ' || Refused.Contains(c) ? ' ' : c);

        // A trailing dot or space is legal to ask for and impossible to create on Windows.
        return sb.ToString().Trim().TrimEnd('.', ' ');
    }

    private const string Refused = "<>:\"/\\|?*";

    /// <summary>
    /// The names Windows reserves for devices. A student called Aux is not impossible, and the
    /// failure is an exception part-way through a whole school's export rather than one bad file.
    /// </summary>
    private static readonly HashSet<string> Devices = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private static bool IsReservedOnWindows(string name) => Devices.Contains(name);

    private static string Unique(string who, HashSet<string> taken)
    {
        if (taken.Add(who)) return who;
        for (int n = 2; ; n++)
        {
            string candidate = $"{who} ({n})";
            if (taken.Add(candidate)) return candidate;
        }
    }
}
