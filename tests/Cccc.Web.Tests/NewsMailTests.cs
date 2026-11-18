using System.Collections.Immutable;
using Cccc.Domain.Editing;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Store;
using Cccc.Web.Features.Mail;
using Cccc.Web.Features.Now;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Cccc.Web.Tests;

/// <summary>The evening email of what changed in somebody's next day that they have not seen (<see cref="NewsMailSettings"/>).</summary>
public class NewsMailTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    private static readonly DateOnly Thursday = new(2030, 3, 7);
    private static readonly Facilitator Ada = new() { Name = "Ada Brightwater", Email = "ada@example.invalid" };
    private static readonly Room Hall = new() { Name = "Hall" };
    private static readonly Room Gym = new() { Name = "Gym" };
    private static readonly Timetable School = Timetable.Empty with { Rooms = EntitySet<Room>.Of([Hall, Gym]), Facilitators = EntitySet<Facilitator>.Of([Ada]) };

    private static ClassChange Changed(Session? was, Session? now, params string[] by) =>
        new((was ?? now)!.Id, was, now, by.Length == 0 ? ["admin"] : [.. by], DateTimeOffset.UnixEpoch, 1);

    private static readonly Session Aerial = new()
    {
        Date = Thursday, Start = new(9, 0), End = new(11, 0), Title = "Aerial", RoomId = Hall.Id, Facilitators = [new Assignment(Ada.Id)],
    };

    private static EveningNote Note(params ClassChange[] changes) => new(Ada, Thursday, changes);

    private static string NameOf(string login) => login == "admin" ? "Preview Administrator" : login;

    [Fact]
    public void ItIsOffUnlessAskedAndAWrongSettingIsOffAndSaysWhich()
    {
        Assert.Equal(ReminderMode.Off, NewsMailSettings.Read(Config()).Settings.Mode);
        Assert.Equal(ReminderMode.Off, NewsMailSettings.Read(Config(("Reminders:Mode", "On"))).Settings.Mode);   // the reminders' switch is not this one
        var typo = NewsMailSettings.Read(Config(("NewsMail:Mode", "Onn"))).Settings;
        Assert.Equal((ReminderMode.Off, "NewsMail__Mode is \"Onn\", not Off, Preview or On, so these emails are off."), (typo.Mode, typo.Problem));
        var noPreviewAddress = NewsMailSettings.Read(Config(("NewsMail:Mode", "Preview"))).Settings;
        Assert.Equal((ReminderMode.Off, "NewsMail__Mode is Preview and NewsMail__PreviewTo is not set, so these emails are off."), (noPreviewAddress.Mode, noPreviewAddress.Problem));
        var on = NewsMailSettings.Read(Config(("NewsMail:Mode", "on"), ("NewsMail:At", "17:30"))).Settings;
        Assert.Equal((ReminderMode.On, new TimeOnly(17, 30), (string?)null), (on.Mode, on.At, on.Problem));
        var mail = MailSettings.Read(Config(("Smtp:Host", "smtp.example.invalid"), ("Smtp:From", "a@example.invalid")));
        Assert.Equal("Nobody is emailed. NewsMail__Mode=Preview or On starts them; the runbook has the steps.", NewsMailSettings.Read(Config()).Describe(mail));
        Assert.Equal("On: every evening at 17:30, each facilitator whose next day changed in a way they have not seen is sent one email.",
            NewsMailSettings.Read(Config(("NewsMail:Mode", "on"), ("NewsMail:At", "17:30"))).Describe(mail));
        // The reminders still say what they say.
        Assert.Equal("Nobody is emailed. Reminders__Mode=Preview or On starts them; the runbook has the steps.", ReminderSettings.Read(Config()).Describe(mail));
    }

    [Fact]
    public void APreviewGoesToThePreviewAddressAndSaysWhoItWasForAndWhatChangedInTheirWords()
    {
        var s = NewsMailSettings.Read(Config(("NewsMail:Mode", "Preview"), ("NewsMail:PreviewTo", "office@example.invalid"))).Settings;
        var mail = Assert.Single(NewsMailSettings.Compose(s, School, [Note(Changed(Aerial, Aerial with { RoomId = Gym.Id }, "admin", "bo"))], NameOf));
        Assert.Equal(("office@example.invalid", "Tomorrow, Thu 7 Mar: what changed for you"), (mail.To, mail.Subject));
        Assert.Equal(
            "[Preview: this would have gone to Ada Brightwater <ada@example.invalid>]\n\n"
            + "Hello Ada Brightwater,\n\nA class of yours tomorrow, Thu 7 Mar, has changed, and you may not have seen it yet:\n\n"
            + "  • Aerial, Thu 7 Mar, 09:00–11:00 · Gym\n    Was in Hall (changed by Preview Administrator and bo)\n"
            + "\nNow, in the timetable, has it too, with Got it to say you have seen it.\n"
            + "\nThis is an automatic email; nobody needs a reply.\n", mail.Body);
    }

    [Fact]
    public void OnSendsToTheFacilitatorWithTheLinkAndTheSummaryCountsOnly()
    {
        var s = NewsMailSettings.Read(Config(("NewsMail:Mode", "On"), ("NewsMail:SummaryTo", "office@example.invalid"), ("NewsMail:Link", "https://skema.example.invalid"))).Settings;
        var juggling = Aerial with { Id = Guid.NewGuid(), Title = "Juggling", Start = new(13, 0), End = new(14, 0) };
        var mails = NewsMailSettings.Compose(s, School, [Note(Changed(Aerial, null), Changed(null, juggling, "bo"))], NameOf);
        Assert.Equal(["ada@example.invalid", "office@example.invalid"], mails.Select(m => m.To));
        Assert.DoesNotContain("[Preview", mails[0].Body);
        Assert.Contains("Classes of yours tomorrow, Thu 7 Mar, have changed", mails[0].Body);
        Assert.Contains("  • Aerial, Thu 7 Mar, 09:00–11:00 · Hall\n    Deleted (changed by Preview Administrator)\n", mails[0].Body);
        Assert.Contains("  • Juggling, Thu 7 Mar, 13:00–14:00 · Hall\n    New (changed by bo)\n", mails[0].Body);
        Assert.Contains("say you have seen it: https://skema.example.invalid/\n", mails[0].Body);
        Assert.Equal(("Not seen yet: tomorrow's changes, Thu 7 Mar", "Changes to tomorrow, Thu 7 Mar, that these people had not seen at 18:00, so were emailed:\n\n  Ada Brightwater: 2 classes\n"),
            (mails[1].Subject, mails[1].Body));
        Assert.Empty(NewsMailSettings.Compose(s, School, [], NameOf));
    }
}

public class NewsMailServiceTests(ReminderHost host) : IClassFixture<ReminderHost>
{
    [Fact]
    public async Task ItRunsOnceADayHoweverOftenItIsAsked()
    {
        _ = host.Services;   // starts the host, and with it the service's first look
        var service = host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<NewsMailService>().Single();
        var store = host.Services.GetRequiredService<Cccc.Store.ProjectStore>();
        var clock = host.Services.GetRequiredService<Cccc.Web.Infrastructure.SchoolClock>();
        for (int i = 0; i < 50 && store.GetMeta("newsmail_last") is null; i++) await Task.Delay(100);
        Assert.Equal(clock.Today.ToString("yyyy-MM-dd"), store.GetMeta("newsmail_sent"));
        var first = store.GetMeta("newsmail_last");
        Assert.EndsWith("|ok|0", first);
        await Task.Delay(20);
        await service.RunOnceAsync(CancellationToken.None);
        await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal(first, store.GetMeta("newsmail_last"));
    }
}

/// <summary>The service with a timetable open: who is written to, about what, and in whose words.</summary>
public class NewsMailWiringTests(ReminderHost host) : IClassFixture<ReminderHost>
{
    private Account Admin()
    {
        var accounts = host.Services.GetRequiredService<AccountDirectory>();
        return accounts.SetupToken() is { } token
            ? accounts.CreateFirstAdministrator(token, "admin", "Test Administrator", Host.AdminPassphrase)
            : accounts.All().First(a => a.Login == "admin");
    }

    [Fact]
    public async Task ItWritesAboutTomorrowLeavingOutWhatTheyDidThemselvesAndWhatTheySaidTheySaw()
    {
        var store = host.Services.GetRequiredService<ProjectStore>();
        var clock = host.Services.GetRequiredService<Cccc.Web.Infrastructure.SchoolClock>();
        var accounts = host.Services.GetRequiredService<AccountDirectory>();
        var seen = host.Services.GetRequiredService<SeenNews>();
        var service = host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<NewsMailService>().Single();
        for (int i = 0; i < 50 && store.GetMeta("newsmail_last") is null; i++) await Task.Delay(100);   // its first look, with nothing open
        var tomorrow = clock.Today.AddDays(1);
        var hall = new Room { Name = "Hall" };
        var gym = new Room { Name = "Gym" };
        var ada = new Facilitator { Name = "Ada Brightwater", Email = "ada@example.invalid" };
        var esme = new Facilitator { Name = "Esme Halloran", Email = "esme@example.invalid" };
        Session On(string title, int hour, Facilitator f) => new()
        {
            Date = tomorrow, Start = new(hour, 0), End = new(hour + 1, 0), Title = title, RoomId = hall.Id, Facilitators = [new Assignment(f.Id)],
        };
        Session aerial = On("Aerial", 9, ada), juggling = On("Juggling", 11, ada), trapeze = On("Trapeze", 13, ada), rope = On("Rope", 15, esme);
        var id = store.Create("Test", Timetable.Empty with
        {
            Rooms = EntitySet<Room>.Of([hall, gym]), Facilitators = EntitySet<Facilitator>.Of([ada, esme]),
            Sessions = EntitySet<Session>.Of([aerial, juggling, trapeze, rope]),
        }, "test", DateTimeOffset.UtcNow);
        var admin = Admin();
        var coach = accounts.Add(admin.Id, "ada", "Ada", Role.Viewer, "a coach's passphrase");
        accounts.Update(admin.Id, coach.Id, coach.Display, Role.Viewer, default, ada.Id, false);
        var live = host.Services.GetRequiredService<LiveProjects>().Get(id);
        void Respace(Session s, string by) => live.Commit(t => Edits.PutSession(t, t.Sessions[s.Id]! with { RoomId = gym.Id }), by);
        Respace(aerial, "admin");
        Respace(juggling, "ada");      // her own
        Respace(trapeze, "admin");     // and then she says she saw it
        var changes = live.ChangesMadeSince(DateTimeOffset.UtcNow.AddDays(-1));
        seen.GotIt(id, ada.Id, News.For(live.State, changes, ada.Id, clock.Today, TimeOnly.MinValue, ["ada"], seen.Read(id, ada.Id)).Where(x => x.Id == trapeze.Id), changes);

        store.SetMeta("newsmail_sent", "");
        await service.RunOnceAsync(CancellationToken.None);

        var mail = Assert.Single(host.Mailer.Sent, m => m.Subject.StartsWith("Tomorrow, ", StringComparison.Ordinal));
        Assert.Equal(("office@example.invalid", $"Tomorrow, {Cccc.Domain.Week.ShortDate(tomorrow)}: what changed for you"), (mail.To, mail.Subject));
        Assert.StartsWith("[Preview: this would have gone to Ada Brightwater <ada@example.invalid>]", mail.Body);
        Assert.Contains("  • Aerial, ", mail.Body);
        Assert.Contains("    Was in Hall (changed by Test Administrator)\n", mail.Body);
        Assert.DoesNotContain("Juggling", mail.Body);
        Assert.DoesNotContain("Trapeze", mail.Body);
        Assert.EndsWith("|ok|1", store.GetMeta("newsmail_last"));
    }
}

/// <summary>A host that could send mail, to a fake, with what each test class asks for besides.</summary>
public abstract class MailHost : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    public readonly string Folder = Path.Combine(AppContext.BaseDirectory, "stores", Guid.NewGuid().ToString("N"));
    public readonly FakeMailer Mailer = new();
    protected abstract string Environment { get; }
    protected abstract (string Key, string Value)[] Settings { get; }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environment);
        builder.UseSetting("Store:Path", Folder);
        builder.UseSetting("Store:Create", "true");
        builder.UseSetting("Smtp:Host", "smtp.example.invalid");
        builder.UseSetting("Smtp:From", "timetable@example.invalid");
        foreach (var (key, value) in Settings) builder.UseSetting(key, value);
        builder.ConfigureServices(s =>
        {
            s.Remove(s.Single(d => d.ServiceType == typeof(IMailer)));
            s.AddSingleton<IMailer>(Mailer);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}

/// <summary>Mail could go, and nothing is said about these emails: so they are off.</summary>
public sealed class MailButNoNewsHost : MailHost
{
    protected override string Environment => "Production";
    protected override (string Key, string Value)[] Settings => [];
}

/// <summary>In Preview from 18:00, on a school clock that says 10:00 (a Development host can be asked the time).</summary>
public sealed class BeforeTheHourHost : MailHost
{
    protected override string Environment => "Development";
    protected override (string Key, string Value)[] Settings =>
        [("Clock:Time", "10:00"), ("NewsMail:Mode", "Preview"), ("NewsMail:PreviewTo", "office@example.invalid"), ("NewsMail:At", "18:00")];
}

public class NewsMailOffTests(MailButNoNewsHost host) : IClassFixture<MailButNoNewsHost>
{
    [Fact]
    public async Task OffUnlessSwitchedOnItLooksOnceAndStopsAndSendsNothing()
    {
        var service = host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<NewsMailService>().Single();
        var store = host.Services.GetRequiredService<Cccc.Store.ProjectStore>();
        for (int i = 0; i < 50 && service.ExecuteTask is not { IsCompleted: true }; i++) await Task.Delay(100);
        Assert.True(service.ExecuteTask is { IsCompleted: true }, "Off, the service looks once and stops; this one is still running.");
        Assert.Equal(((string?)null, (string?)null), (store.GetMeta("newsmail_sent"), store.GetMeta("newsmail_last")));
        Assert.Empty(host.Mailer.Sent);
    }
}

public class NewsMailBeforeTheHourTests(BeforeTheHourHost host) : IClassFixture<BeforeTheHourHost>
{
    [Fact]
    public async Task BeforeItsTimeItSendsNothingAndKeepsTheEveningForLater()
    {
        var service = host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<NewsMailService>().Single();
        var store = host.Services.GetRequiredService<Cccc.Store.ProjectStore>();
        var clock = host.Services.GetRequiredService<Cccc.Web.Infrastructure.SchoolClock>();
        Assert.Equal(10, clock.Local(clock.Now).Hour);   // the time asked for, so the test does not depend on when it runs
        await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal(((string?)null, (string?)null), (store.GetMeta("newsmail_sent"), store.GetMeta("newsmail_last")));
    }
}
