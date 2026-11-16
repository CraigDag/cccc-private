using CCCCScheduler.Services;

namespace CCCCScheduler.Helpers;

/// <summary>
/// <b>What every exported sheet says under its big line, and what every exported file is called.</b>
/// One place, because the alternative is three exporters and five call sites each assembling their
/// own version of the same sentence.
///
/// <para><b>Craig's call, 2026-09-10, in his own words: <i>"Use 'CCCC Scheduler', week first"</i>.</b>
/// He was looking at a calendar view headed "BA Semester 1" — the filter — carrying "BA Semester 1"
/// again in the corner, because that was also what the open project happened to be called. Two
/// lines: the first says WHOSE week this is (the scope, or a person's name), and the second says
/// what the document IS — the product, the week with its dates and its year, and the SHAPE of the
/// sheet.</para>
///
/// <para><b>The project's name is on none of them any more, and that is deliberate rather than
/// incidental.</b> A term can be named after a person — which is why this app already keeps project
/// names out of its logs and out of every URL — and on these sheets it was usually a second, less
/// exact copy of the scope beside it. <see cref="Product"/> is a constant here rather than anything
/// read off an open project, so no document can carry one again by accident.</para>
///
/// <para><b>The year is here for a stated reason</b>: week numbers repeat, the school keeps a folder
/// of these, and "CCCC Week 37 — Whole school.pdf" from two Septembers running are the same file
/// name. See <see cref="WeekLabel.ForFileName"/> for why that year is the ISO one.</para>
/// </summary>
public static class DocumentHeading
{
    /// <summary>The product, as a document names it. Never the project, and never a school's name.</summary>
    public const string Product = "CCCC Scheduler";

    /// <summary>
    /// What separates the clauses of the second line — a middle dot with a space either side,
    /// Craig's own shape. One constant so a test can pin the line rather than a spelling of it.
    /// </summary>
    public const string Separator = " · ";

    /// <summary>What separates the parts of a file name. Spaced, because a file name is read.</summary>
    public const string FilePart = " — ";

    /// <summary>The shape of the sheet <c>QuestPdfTimetableExporter</c> draws — the owner's word for it.</summary>
    public const string CalendarView = "Calendar view";

    /// <summary>
    /// <b>The calendar view printed under <i>Events only</i></b>, which holds no classes and says so
    /// on the sheet and in the file name. Craig, 2026-10-28: the file was called the same as the
    /// whole week's, so the two replaced each other in a downloads folder.
    /// </summary>
    public const string EventsView = "Events";

    /// <summary>
    /// <b>The calendar view printed under <i>Guests only</i></b>, for <see cref="EventsView"/>'s
    /// reason: it holds only the visiting groups' classes, and a file named like the whole week's
    /// would replace the school's week in a downloads folder.
    /// </summary>
    public const string GuestsView = "Guests";

    /// <summary>The shape of the sheet <c>QuestPdfExporter</c> draws: the six-column administrative list.</summary>
    public const string List = "List";

    /// <summary>The shape of the sheet <c>QuestPdfRoomGridExporter</c> draws: one page per day.</summary>
    public const string RoomGrid = "Room grid";

    /// <summary>What the per-student batch is called, on the zip that holds it.</summary>
    public const string StudentCalendars = "Student calendars";

    /// <summary>
    /// <b>The second line of a sheet</b> — "CCCC Scheduler · Week 37, 7–13 September 2026 · Calendar
    /// view", with the scope appended when the big line above it is NOT the scope.
    /// </summary>
    /// <param name="scope">
    /// <c>MainViewModel.ExportScopeLabel</c> on a page whose big line is a person's name, and
    /// <c>null</c> on a page that already leads with the scope — printing it twice reads as two
    /// different claims about the same sheet, which is the duplication this whole change removes.
    /// </param>
    public static string Line(DateOnly weekOf, string shape, string? scope = null)
    {
        string line = Product + Separator + WeekLabel.ForDocuments(weekOf) + Separator + shape;
        return string.IsNullOrWhiteSpace(scope) ? line : line + Separator + scope;
    }

    /// <summary>
    /// <b>What a file of this week is called, before its extension</b> — "CCCC Week 37 2026 — BA
    /// Semester 1 — Calendar view".
    ///
    /// <para><b>Scrubbed here rather than by the caller</b>, through
    /// <see cref="StudentTimetableBatch.SafeName"/> — the fixed, platform-independent set, because
    /// this runs on Linux and the file lands on Windows. A caller that forgot would produce a name
    /// the browser saves and the school's laptop refuses, and the scope in the middle of it is a
    /// group name somebody typed.</para>
    /// </summary>
    public static string FileName(DateOnly weekOf, string scope, string shape)
        => StudentTimetableBatch.SafeName(FileLead(weekOf) + FilePart + scope + FilePart + shape);

    /// <summary>
    /// <b>What every one of these file names starts with</b> — "CCCC Week 37 2026". Separate from
    /// <see cref="FileName"/> because the per-student batch leads each of a school's worth of files
    /// with it and then puts the child's own name after it, which is the whole of "week first".
    /// </summary>
    public static string FileLead(DateOnly weekOf) => "CCCC " + WeekLabel.ForFileName(weekOf);
}
