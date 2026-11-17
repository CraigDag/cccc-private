using System.Net;
using System.Text.RegularExpressions;
using Cccc.Store;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Cccc.Web.Tests;

/// <summary>The real host, in Production, on a store folder of its own. No browser: these are the HTTP facts.</summary>
public sealed class Host : WebApplicationFactory<Program>
{
    public readonly string Folder = Path.Combine(AppContext.BaseDirectory, "stores", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Store:Path", Folder);
        builder.UseSetting("Store:Create", "true");
    }

    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    public const string AdminPassphrase = "a long enough passphrase";

    public Account Admin()
    {
        var accounts = Services.GetRequiredService<AccountDirectory>();
        return accounts.SetupToken() is { } token
            ? accounts.CreateFirstAdministrator(token, "admin", "Test Administrator", AdminPassphrase)
            : accounts.All().First(a => a.Login == "admin");
    }

    public static async Task<HttpResponseMessage> SignIn(HttpClient c, string login, string passphrase)
    {
        var page = await c.GetStringAsync("/signin");
        var token = Regex.Match(page, "name=\"__RequestVerificationToken\" value=\"([^\"]+)\"").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(token), "The sign-in page had no antiforgery token in it.");
        return await c.PostAsync("/signin", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["_handler"] = "signin", ["__RequestVerificationToken"] = token,
            ["Form.Login"] = login, ["Form.Passphrase"] = passphrase, ["Form.Back"] = "",
        }));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}

public class HostTests : IClassFixture<Host>
{
    private readonly Host _host;
    public HostTests(Host host) => _host = host;

    [Fact]
    public async Task EveryPageIsBehindSignInIncludingOnesNobodyListed()
    {
        var c = _host.Client();
        foreach (var path in new[] { "/", "/data", "/admin/system", "/reports", "/a-page-added-later" })
        {
            var r = await c.GetAsync(path);
            Assert.True(r.StatusCode == HttpStatusCode.Redirect, $"{path} answered {(int)r.StatusCode} to nobody");
            Assert.StartsWith("/signin", r.Headers.Location!.OriginalString.Replace("http://localhost", ""));
        }
    }

    [Fact]
    public async Task TheHealthProbeAndTheSignInPageAnswerWithoutSignIn()
    {
        var c = _host.Client();
        Assert.Equal("ok", await c.GetStringAsync("/healthz"));
        var r = await c.GetAsync("/signin");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Contains("default-src 'self'", r.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
    }

    [Fact]
    public async Task AWrongPassphraseIsRefusedAndTheRightOneSignsIn()
    {
        _host.Admin();
        var c = _host.Client();
        var wrong = await Host.SignIn(c, "admin", "not the passphrase at all");
        Assert.Equal(HttpStatusCode.OK, wrong.StatusCode);
        Assert.Contains("do not match", await wrong.Content.ReadAsStringAsync());

        var right = await Host.SignIn(c, "admin", Host.AdminPassphrase);
        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        var cookie = right.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("cccc.signin="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase);
        // Not Secure over plain http: the school's laptop serves the app without a certificate, and a
        // Secure cookie there would lock everybody out with the right passphrase.
        Assert.DoesNotContain("secure", cookie.Split(';').Select(p => p.Trim().ToLowerInvariant()));
        Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task BehindAProxyThatSaysHttpsTheSignInCookieIsSecure()
    {
        // nginx in front (the runbook's step 9) ends https itself and says so in X-Forwarded-Proto. The app
        // believes it, so from there on the cookie that carries a sign-in is never sent over plain http.
        _host.Admin();
        var c = _host.Client();
        c.DefaultRequestHeaders.Add("X-Forwarded-Proto", "https");
        var right = await Host.SignIn(c, "admin", Host.AdminPassphrase);
        Assert.Equal(HttpStatusCode.Redirect, right.StatusCode);
        var cookie = right.Headers.GetValues("Set-Cookie").First(h => h.StartsWith("cccc.signin="));
        Assert.Contains("secure", cookie.Split(';').Select(p => p.Trim().ToLowerInvariant()));
    }

    [Fact]
    public async Task AViewerCannotDownloadTheWholeFile()
    {
        var admin = _host.Admin();
        var accounts = _host.Services.GetRequiredService<AccountDirectory>();
        var viewer = accounts.All().FirstOrDefault(a => a.Login == "viewer")
                     ?? accounts.Add(admin.Id, "viewer", "Test Viewer", Role.Viewer, "another long passphrase");
        accounts.ClearMustChange(viewer.Id);
        var store = _host.Services.GetRequiredService<ProjectStore>();
        var id = store.List().FirstOrDefault()?.Id
                 ?? store.Create("Test", Cccc.Domain.Sample.SampleSchool.Create(new DateOnly(2030, 3, 6), new DateOnly(2030, 3, 6)), "test", DateTimeOffset.UtcNow);
        var c = _host.Client();
        Assert.Equal(HttpStatusCode.Redirect, (await Host.SignIn(c, "viewer", "another long passphrase")).StatusCode);
        var r = await c.GetAsync($"/download/project/{id}");
        Assert.NotEqual(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task APrintLinkWorksOnlyForTheAccountItWasMadeFor()
    {
        var admin = _host.Admin();
        var store = _host.Services.GetRequiredService<ProjectStore>();
        if (store.List().Count == 0)
            store.Create("Test", Cccc.Domain.Sample.SampleSchool.Create(new DateOnly(2030, 3, 6), new DateOnly(2030, 3, 6)), "test", DateTimeOffset.UtcNow);
        var prints = _host.Services.GetRequiredService<Cccc.Web.Features.Print.PrintRequests>();
        var view = new Cccc.Web.Features.Week.WeekView();
        view.Start(new DateOnly(2030, 3, 4), null);
        var someoneElses = prints.Link("week", view, new Cccc.Web.Infrastructure.Access(admin with { Id = Guid.NewGuid() }));
        var mine = prints.Link("week", view, new Cccc.Web.Infrastructure.Access(admin));

        var c = _host.Client();
        await Host.SignIn(c, "admin", Host.AdminPassphrase);
        Assert.Equal(HttpStatusCode.Gone, (await c.GetAsync("/print/" + Guid.NewGuid().ToString("N"))).StatusCode);
        Assert.Equal(HttpStatusCode.Gone, (await c.GetAsync(someoneElses)).StatusCode);
        var r = await c.GetAsync(mine);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/pdf", r.Content.Headers.ContentType?.MediaType);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString((await r.Content.ReadAsByteArrayAsync())[..4]));
    }
}

public class ReportCsvTests : IClassFixture<Host>
{
    private readonly Host _host;
    public ReportCsvTests(Host host) => _host = host;

    [Fact]
    public async Task TheSpreadsheetsAreAnAdministratorsAndCarryOnlyDatesInTheirAddress()
    {
        var admin = _host.Admin();
        var accounts = _host.Services.GetRequiredService<AccountDirectory>();
        var editor = accounts.All().FirstOrDefault(a => a.Login == "csv-editor")
                     ?? accounts.Add(admin.Id, "csv-editor", "Test Editor", Role.Editor, "an editor passphrase");
        accounts.ClearMustChange(editor.Id);
        var store = _host.Services.GetRequiredService<ProjectStore>();
        var day = new DateOnly(2030, 3, 6);
        if (store.List().Count == 0) store.Create("Test", Cccc.Domain.Sample.SampleSchool.Create(day, day), "test", DateTimeOffset.UtcNow);
        const string url = "/reports/csv/hours?from=2030-02-04&to=2030-03-10";

        var e = _host.Client();
        await Host.SignIn(e, "csv-editor", "an editor passphrase");
        Assert.NotEqual(HttpStatusCode.OK, (await e.GetAsync(url)).StatusCode);

        var a = _host.Client();
        await Host.SignIn(a, "admin", Host.AdminPassphrase);
        var r = await a.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var bytes = await r.Content.ReadAsByteArrayAsync();
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);   // so Excel reads Æ Ø Å
        var text = System.Text.Encoding.UTF8.GetString(bytes[3..]);
        Assert.Contains("Facilitator,Teaching", text);
        Assert.DoesNotContain("Test", r.Content.Headers.ContentDisposition?.FileName ?? "");
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("Dag, Øverlie", "\"Dag, Øverlie\"")]
    [InlineData("Plain", "Plain")]
    public void ACellIsQuotedAndNeverRunAsAFormula(string cell, string written) =>
        Assert.Equal(written, Cccc.Web.Features.Reports.ReportCsv.Quote(cell));
}
