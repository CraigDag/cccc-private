using Cccc.Store;
using Cccc.Web.Features.Admin;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Features.Print;

public static class ProjectDownload
{
    /// <summary>A whole project as a .cccc2: data about every student, so an administrator's only.</summary>
    public static void MapProjectDownload(this WebApplication app)
    {
        app.MapGet("/download/project/{id:guid}", (Guid id, HttpContext ctx, AccountDirectory accounts, ProjectStore store, LiveProjects live) =>
        {
            var account = AccountClaims.Current(ctx.User, accounts);
            if (account is null) return Results.Unauthorized();
            if (!new Access(account).MayManageProjects) return Results.Forbid();
            if (store.Find(id) is not { Deleted: null }) return Results.NotFound();
            ctx.Response.Headers.CacheControl = "no-store";
            // The file is named by date, never by the project's name, which can be a person's.
            return Results.File(ProjectFiles.Write(live.Get(id).State), "application/octet-stream",
                $"timetable-{DateTime.UtcNow:yyyy-MM-dd}{ProjectFiles.Extension}");
        });
    }
}
