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
    ProjectStore.Open(folder).Backup(target);
    Console.WriteLine("Backed up.");
    return;
}

var builder = WebApplication.CreateBuilder(args);

// The store is required, and the host refuses to start without a good one. There is deliberately no
// "accept it anyway" switch: a store that takes every change and is empty after a reboot looks exactly
// like the app forgetting a term.
var storePath = builder.Configuration["Store:Path"];
var store = ProjectStore.Open(storePath ?? "", create: builder.Configuration.GetValue<bool>("Store:Create"));

builder.Services.AddSingleton(store);
builder.Services.AddSingleton<SchoolClock>();
builder.Services.AddSingleton(sp => new AccountDirectory(store, () => sp.GetRequiredService<SchoolClock>().Now));
builder.Services.AddSingleton(sp =>
{
    var clock = sp.GetRequiredService<SchoolClock>();
    return new LiveProjects(store, () => clock.Now, () => clock.Today);
});
builder.Services.AddSingleton<ProjectChoice>();
builder.Services.AddSingleton<PdfGate>();
builder.Services.AddSingleton<PrintRequests>();
builder.Services.AddHostedService<Maintenance>();
builder.Services.AddScoped<Cccc.Web.Features.Shell.Dialogs>();
builder.Services.AddScoped<Cccc.Web.Features.Shell.Toasts>();
builder.Services.AddScoped<Cccc.Web.Features.Week.WeekView>();
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
// Our own Content-Security-Policy already says frame-ancestors 'none'; Blazor would add a second header.
app.MapRazorComponents<App>().AddInteractiveServerRenderMode(o => o.ContentSecurityFrameAncestorsPolicy = null);

app.Run();

public partial class Program;
