using Cccc.Domain;
using Cccc.Store;
using Cccc.Web.Features.Week;
using Cccc.Web.Infrastructure;
using DWeek = Cccc.Domain.Week;

namespace Cccc.Web.Features.Print;

public static class Downloads
{
    public static void MapDownloads(this WebApplication app)
    {
        // A facilitator's semester for their phone (PhoneCalendar.Link): named by ids, so no token. Who may
        // have it is asked here, not only by the button that is not drawn, and the timetable is the one the
        // link names, asked again now.
        app.MapGet("/calendar/phone", (Guid project, Guid person, DateOnly week, HttpContext ctx, ProjectChoice choice,
            LiveProjects live, AccountDirectory accounts, SchoolClock clock, PhoneCalendarFiles phones) =>
        {
            var account = AccountClaims.Current(ctx.User, accounts);
            if (account is null) return Results.Unauthorized();
            var access = new Access(account);
            if (!choice.MaySee(project, access)) return Results.NotFound();
            var t = live.Get(project).State;
            if (t.Facilitators[person] is null || !access.MayHaveTheWeekOf(person)) return Results.Forbid();
            ctx.Response.Headers.CacheControl = "no-store";
            var (file, fileName) = phones.Make(project, t, person, week, clock.Now, clock.Today);
            return Results.File(file, "text/calendar; charset=utf-8", fileName);
        });

        app.MapGet("/print/{token}", (string token, HttpContext ctx, PrintRequests prints, ProjectChoice choice,
            LiveProjects live, AccountDirectory accounts, PdfGate gate, SchoolClock clock) =>
        {
            var account = AccountClaims.Current(ctx.User, accounts);
            if (account is null) return Results.Unauthorized();
            var access = new Access(account);
            var r = prints.Find(token, account.Id);
            if (r is null) return Results.Text("This link has expired. Go back to the page it was on and press the button again.", statusCode: 410);
            // The timetable the link was made in, asked again now: another may have been opened for
            // everybody since, or the one looked at deleted, and a viewer only ever sees the open one.
            if (!choice.MaySee(r.ProjectId, access))
                return Results.Text("This print link is for a timetable that is no longer open. Press Print again on the week.", statusCode: 410);
            var t = live.Get(r.ProjectId).State;
            var view = r.ToView();
            ctx.Response.Headers.CacheControl = "no-store";

            QuestPDF.Infrastructure.IDocument doc;
            string name;
            switch (r.Kind)
            {
                case "mine" when r.Person is { } fid:
                    // The PDF refuses too, not only the button.
                    if (!access.MayHaveTheWeekOf(fid)) return Results.Forbid();
                    var mv = new WeekView();
                    mv.Start(r.Monday, null);
                    mv.ShowGuests = true;
                    var mm = WeekModel.Build(t, mv, access, clock.Today, s => s.FacilitatorIds.Contains(fid) || s.CoverIds.Contains(fid), fid);
                    doc = Sheets.WeekCalendar(t, mm, t.Facilitators[fid]?.Name ?? "", clock.Local(clock.Now), access.Events, "My week");
                    name = $"My week {DWeek.IsoNumber(r.Monday)}";
                    break;
                case "list":
                    doc = Sheets.WeekList(t, WeekModel.Build(t, view, access, clock.Today), view.Describe(t), clock.Local(clock.Now), access.Events);
                    name = $"Week {DWeek.IsoNumber(r.Monday)} list";
                    break;
                case "rooms":
                    doc = Sheets.RoomGrid(t, WeekModel.Build(t, view, access, clock.Today), view.Describe(t), clock.Local(clock.Now), access.Events, view.Rooms);
                    name = $"Week {DWeek.IsoNumber(r.Monday)} spaces";
                    break;
                case "students":
                    // Data about every student is an administrator's only.
                    if (!access.MayUseAdminTools) return Results.Forbid();
                    var all = WeekModel.Build(t, view, access, clock.Today);
                    var students = view.Students.Count > 0 ? t.Students.Where(s => view.Students.Contains(s.Id))
                        : view.Groups.Count > 0 ? t.Students.Where(s => s.GroupId is { } g && view.Groups.Contains(g))
                        : t.Students;
                    doc = Sheets.StudentCalendars(t, all, students, clock.Local(clock.Now), access.Events);
                    name = $"Week {DWeek.IsoNumber(r.Monday)} students";
                    break;
                default:
                    doc = Sheets.WeekCalendar(t, WeekModel.Build(t, view, access, clock.Today), view.Describe(t), clock.Local(clock.Now), access.Events);
                    name = $"Week {DWeek.IsoNumber(r.Monday)}";
                    break;
            }
            var bytes = gate.Generate(doc);
            ctx.Response.Headers.ContentDisposition = $"inline; filename=\"{name}.pdf\"";
            return Results.File(bytes, "application/pdf");
        });
    }
}
