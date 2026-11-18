using Cccc.Store;
using Microsoft.AspNetCore.DataProtection;
using Cccc.Web.Components;
using Cccc.Web.Features.Print;
using Cccc.Web.Infrastructure;
using Cccc.Web.Features.SignIn;

// `cccc backup <file>`: a consistent copy of the whole store while the app runs, for the nightly
// off-site backup. Nothing else is a command; nothing takes a passphrase on a command line.
if (args is ["backup", var target, ..])
{
    var config = new ConfigurationBuilder().AddJsonFile("appsettings.json", optional: true)
        .AddEnvironmentVariables().Build();
    var folder = config["Store:Path"] ?? throw new InvalidOperationException("Store__Path is not set.");
    try { ProjectStore.Open(folder).Backup(target); }
    catch (Exception e) when (e is StoreRefused or IOException)
    {
        Console.Error.WriteLine("Backup failed: " + e.Message);
        return 1;
    }
    Console.WriteLine("Backed up.");
    return 0;
}

// `cccc health`: the container's health check (the runtime image has no curl).
if (args is ["health", ..])
{
    try
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
        var ok = await http.GetStringAsync("http://127.0.0.1:8080/healthz") == "ok";
        return ok ? 0 : 1;
    }
    catch (Exception) { return 1; }
}

var builder = WebApplication.CreateBuilder(args);

// The store is required, and the host refuses to start without a good one. There is deliberately no
// "accept it anyway" switch: a store that takes every change and is empty after a reboot looks exactly
// like the app forgetting a term.
var storePath = builder.Configuration["Store:Path"];
ProjectStore store;
try
{
    store = ProjectStore.Open(storePath ?? "", create: builder.Configuration.GetValue<bool>("Store:Create"));
}
catch (StoreRefused e)
{
    // Said plainly and alone, so it is the one line in the log rather than the top of a stack trace.
    Console.Error.WriteLine("The app will not start: " + e.Message);
    return 2;
}

builder.Services.AddSingleton(store);
builder.Services.AddSingleton<SchoolClock>();
builder.Services.AddSingleton(sp => new AccountDirectory(store, () => sp.GetRequiredService<SchoolClock>().Now));
builder.Services.AddSingleton(sp =>
{
    var clock = sp.GetRequiredService<SchoolClock>();
    return new LiveProjects(store, () => clock.Now, () => clock.Today);
});
builder.Services.AddSingleton<ProjectChoice>();
// Following the current app is off unless Follow__File names its file (docs/deployment, step 8). The
// path is never logged: the current app names a timetable's file after the timetable.
var followFile = builder.Configuration["Follow:File"];
var follow = new FollowStatus { On = !string.IsNullOrWhiteSpace(followFile) };
builder.Services.AddSingleton(follow);
if (follow.On)
{
    builder.Services.AddSingleton(sp =>
    {
        var clock = sp.GetRequiredService<SchoolClock>();
        return new Follower(followFile!, store, sp.GetRequiredService<LiveProjects>(), sp.GetRequiredService<ProjectChoice>(),
            follow, () => clock.Now, () => clock.Today, sp.GetRequiredService<ILogger<Follower>>());
    });
    builder.Services.AddHostedService<FollowService>();
}
builder.Services.AddSingleton<PdfGate>();
builder.Services.AddSingleton<PrintRequests>();
builder.Services.AddSingleton<PhoneCalendarFiles>();
builder.Services.AddSingleton<Cccc.Web.Features.Now.SeenNews>();
builder.Services.AddSingleton(sp => new Cccc.Web.Features.RollCall.RollCalls(() => sp.GetRequiredService<SchoolClock>().Now));
builder.Services.AddSingleton<ScreenDirectory>();
builder.Services.AddSingleton<Cccc.Web.Features.Screens.ScreenPairing>();
builder.Services.AddSingleton<Cccc.Web.Features.Screens.ScreenLimiter>();
builder.Services.AddHostedService<Maintenance>();
// Mail is an extra, and the end-of-day reminder is off unless Reminders__Mode says otherwise.
var mailSettings = Cccc.Web.Features.Mail.MailSettings.Read(builder.Configuration);
builder.Services.AddSingleton(mailSettings);
builder.Services.AddSingleton(Cccc.Web.Features.Mail.ReminderSettings.Read(builder.Configuration));
builder.Services.AddSingleton<Cccc.Web.Features.Mail.IMailer>(new Cccc.Web.Features.Mail.SmtpMailer(mailSettings));
builder.Services.AddHostedService<Cccc.Web.Features.Mail.RegisterReminderService>();
// And so is the evening email of what changed in somebody's next day that they have not seen (NewsMail__Mode).
builder.Services.AddSingleton(Cccc.Web.Features.Mail.NewsMailSettings.Read(builder.Configuration));
builder.Services.AddHostedService<Cccc.Web.Features.Mail.NewsMailService>();
builder.Services.AddScoped<Cccc.Web.Features.Shell.Dialogs>();
builder.Services.AddScoped<Cccc.Web.Features.Shell.Toasts>();
builder.Services.AddScoped<Cccc.Web.Features.Week.WeekView>();
builder.Services.AddScoped<Cccc.Web.Features.Plan.PlanView>();
builder.Services.AddScoped<Cccc.Web.Features.Shell.TabTimetable>();
builder.Services.AddScoped<Cccc.Web.Features.Find.Finder>();
builder.Services.AddHttpContextAccessor();

// Sign-in cookies are sealed with keys kept beside the store, so an update or restart does not sign
// everybody out.
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Path.GetDirectoryName(store.DatabasePath)!, "keys")))
    .SetApplicationName("cccc-scheduler");

builder.AddSignIn();
builder.Services.AddAntiforgery(o => o.Cookie.Name = "cccc.af");
builder.Services.AddRazorComponents().AddInteractiveServerComponents(o =>
{
    o.DetailedErrors = builder.Environment.IsDevelopment();
});

// Nothing a person types and no project name is ever logged; the framework's own request logging is
// kept at Warning so a URL with a query never reaches the log either.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;

var app = builder.Build();

var clock = app.Services.GetRequiredService<SchoolClock>();
var accounts = app.Services.GetRequiredService<AccountDirectory>();
if (app.Environment.IsDevelopment() && app.Configuration.GetValue<bool>("Preview:Seed"))
    Preview.Seed(store, accounts, clock, app.Configuration.GetValue<bool>("Preview:Accounts"));
if (accounts.SetupToken() is { } token)
    app.Logger.LogWarning("No accounts yet. To make the first administrator, open /setup?token={Token} on this server.", token);

app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<LiveProjects>().SettleAll());

app.UseForwardedHeaders();
app.UseSecurityHeaders();
if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseMustChangePassphrase();
app.UseAntiforgery();

app.MapGet("/healthz", () => "ok").AllowAnonymous();
app.MapStaticAssets().AllowAnonymous();
app.MapSignInEndpoints();
app.MapDownloads();
app.MapProjectDownload();
Cccc.Web.Features.Screens.ScreenEndpoints.MapScreens(app);
Cccc.Web.Features.Reports.ReportCsv.MapReportCsv(app);
// Our own Content-Security-Policy already says frame-ancestors 'none'; Blazor would add a second header.
app.MapRazorComponents<App>().AddInteractiveServerRenderMode(o => o.ContentSecurityFrameAncestorsPolicy = null);

app.Run();
return 0;

public partial class Program;
