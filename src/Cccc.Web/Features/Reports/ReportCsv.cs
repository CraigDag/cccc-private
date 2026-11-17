using System.Globalization;
using System.Text;
using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Store;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Features.Reports;

using Week = Cccc.Domain.Week;

/// <summary>
/// The reports as spreadsheets: comma-separated, UTF-8 with a byte-order mark so Excel reads Æ Ø Å,
/// hours as decimal numbers so a column can be summed. Every number comes from the same sums the
/// screen shows (<see cref="ReportMath"/>, <see cref="Hours"/>). The address carries only dates.
/// </summary>
public static class ReportCsv
{
    public static string Hours(Timetable t, DateOnly from, DateOnly to)
    {
        var occ = ReportMath.Happening(t, from, to);
        var weeks = ReportMath.Mondays(from, to).ToList();
        var sb = new StringBuilder();
        void Row(params string[] cells) => sb.Append(string.Join(",", cells.Select(Quote))).Append("\r\n");

        Row("Hours", Span(from, to));
        Row("Only classes that happen count: closed and cancelled ones do not.");
        Row("");
        Row("Group", "Hours");
        foreach (var r in ReportMath.ByGroup(t, occ)) Row(r.Name, N(r.Hours));
        Row("A class for several groups counts in full for each, so this column can add up to more than the total.");
        Row("");
        Row("Programme", "Hours");
        foreach (var r in ReportMath.ByProgramme(t, occ)) Row(r.Name, N(r.Hours));
        Row("");
        Row("Area", "Hours", "Weekly target", "Note");
        foreach (var r in ReportMath.ByArea(t, occ, weeks.Count)) Row(r.Name, N(r.Hours), r.Target is { } x ? N(x) : "", r.Note ?? "");
        Row("");

        // Every time rule is its own column whether or not it added anything, so a formula can be built
        // against the sheet; Teaching plus the rule columns is Total on every row.
        var rules = t.TimeRules.OrderBy(r => r.DisplayName, NameOrder.Comparer).ToList();
        var byWeek = weeks.ToDictionary(m => m, m => Cccc.Domain.Rules.Hours.ByFacilitator(t, Occurrences.HappeningInWeek(t, m)));
        Row(new[] { "Facilitator", "Teaching" }.Concat(rules.Select(r => r.DisplayName)).Append("Total").Concat(weeks.Select(m => "Wk " + Week.IsoNumber(m))).ToArray());
        foreach (var f in t.Facilitators.OrderBy(f => f.IsGuest).ThenBy(f => f.Name, NameOrder.Comparer))
        {
            var splits = weeks.Select(m => byWeek[m].GetValueOrDefault(f.Id) ?? HoursSplit.None).ToList();
            Row(new[] { f.Name + (f.IsGuest ? " (guest)" : ""), N(Sum(splits.Select(s => s.Teaching))) }
                .Concat(rules.Select(r => N(Sum(splits.SelectMany(s => s.Rules).Where(x => x.RuleId == r.Id).Select(x => x.Time)))))
                .Append(N(Sum(splits.Select(s => s.Total))))
                .Concat(splits.Select(s => N(s.Total))).ToArray());
        }
        Row("Cover counts for whoever was in the room; a class still waiting for cover counts for nobody.");
        return sb.ToString();
    }

    public static string Attendance(Timetable t, DateOnly from, DateOnly to)
    {
        var (classes, students) = ReportMath.Attendance(t, ReportMath.Happening(t, from, to));
        var sb = new StringBuilder();
        void Row(params string[] cells) => sb.Append(string.Join(",", cells.Select(Quote))).Append("\r\n");
        Row("Attendance", Span(from, to));
        Row("");
        Row("Class", "Registers", "Present", "Excused", "Absent", "Present %");
        foreach (var r in classes) Row(r.Name, r.Registers.ToString(), r.Present.ToString(), r.Excused.ToString(), r.Absent.ToString(), Pct(r.PresentShare));
        Row("");
        Row("Student", "Registers", "Present", "Excused", "Absent", "Present %");
        foreach (var r in students) Row(r.Name, r.Registers.ToString(), r.Present.ToString(), r.Excused.ToString(), r.Absent.ToString(), Pct(r.PresentShare));
        return sb.ToString();
    }

    public static void MapReportCsv(this WebApplication app) =>
        app.MapGet("/reports/csv/{kind}", (string kind, DateOnly from, DateOnly to, HttpContext ctx,
            AccountDirectory accounts, ProjectChoice choice) =>
        {
            var account = AccountClaims.Current(ctx.User, accounts);
            if (account is null) return Results.Unauthorized();
            // Reports are an administrator's, and the attendance sheet names every student.
            if (!new Access(account).MayUseAdminTools) return Results.Forbid();
            if (to < from || to.DayNumber - from.DayNumber > 400) return Results.BadRequest("Choose up to a year of weeks.");
            if (choice.Open() is not { } project) return Results.NotFound();
            var t = project.State;
            var (text, name) = kind switch
            {
                "hours" => (Hours(t, from, to), "Hours"),
                "attendance" => (Attendance(t, from, to), "Attendance"),
                _ => (null, ""),
            };
            if (text is null) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "no-store";
            var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            // The file is named by its dates, never by the timetable's name.
            return Results.File(bytes, "text/csv; charset=utf-8", $"{name} {from:yyyy-MM-dd} to {to:yyyy-MM-dd}.csv");
        });

    private static string Span(DateOnly from, DateOnly to) =>
        $"{Week.ShortDate(Week.MondayOf(from))} to {Week.ShortDate(Week.MondayOf(to).AddDays(6))}";

    private static TimeSpan Sum(IEnumerable<TimeSpan> xs) => xs.Aggregate(TimeSpan.Zero, (a, b) => a + b);

    private static string N(TimeSpan h) => h.TotalHours.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Pct(double? d) => d is { } x ? (x * 100).ToString("0", CultureInfo.InvariantCulture) : "";

    /// <summary>Quoted when needed; a cell starting = + - @ is prefixed so a spreadsheet never runs it as a formula.</summary>
    public static string Quote(string s)
    {
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0])) s = "'" + s;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + s.Replace("\"", "\"\"") + "\"" : s;
    }
}
