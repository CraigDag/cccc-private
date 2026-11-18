using System.Collections.Immutable;
using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Web.Features.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Hosting;

namespace Cccc.Web.Tests;

public class ReminderMailTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder().AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))).Build();

    private static readonly DateOnly Monday = new(2026, 11, 16);

    private static Reminder Cleo()
    {
        var f = new Facilitator { Name = "Cleo Test", Email = "cleo@example.invalid" };
        var s = new Session { Date = Monday, Title = "Juggling", Start = new(9, 0), End = new(10, 30) };
        return new Reminder(f, [new Occurrence(s, OccurrenceState.Runs, null, ImmutableList<Guid>.Empty, [f.Id])]);
    }

    [Fact]
    public void RemindersAreOffUnlessAskedAndATypoIsOffAndSaysSo()
    {
        Assert.Equal(ReminderMode.Off, ReminderSettings.Read(Config()).Mode);
        var typo = ReminderSettings.Read(Config(("Reminders:Mode", "Onn")));
        Assert.Equal(ReminderMode.Off, typo.Mode);
        Assert.Equal("Reminders__Mode is \"Onn\", not Off, Preview or On, so reminders are off.", typo.Problem);
        var noPreviewAddress = ReminderSettings.Read(Config(("Reminders:Mode", "Preview")));
        Assert.Equal(ReminderMode.Off, noPreviewAddress.Mode);
        Assert.Equal("Reminders__Mode is Preview and Reminders__PreviewTo is not set, so reminders are off.", noPreviewAddress.Problem);
        var badTime = ReminderSettings.Read(Config(("Reminders:Mode", "On"), ("Reminders:At", "six")));
        Assert.Equal(ReminderMode.Off, badTime.Mode);
        Assert.Equal("Reminders__At is \"six\", not a time like 18:00, so reminders are off.", badTime.Problem);
        var on = ReminderSettings.Read(Config(("Reminders:Mode", "on"), ("Reminders:At", "17:30")));
        Assert.Equal((ReminderMode.On, new TimeOnly(17, 30), (string?)null), (on.Mode, on.At, on.Problem));
    }

    [Fact]
    public void MailIsOnlyConfiguredWithAHostAndAFromAddress()
    {
        Assert.False(MailSettings.Read(Config()).Configured);
        Assert.False(MailSettings.Read(Config(("Smtp:Host", "smtp.example.invalid"))).Configured);
        Assert.False(MailSettings.Read(Config(("Smtp:Host", "smtp.example.invalid"), ("Smtp:From", "a@example.invalid"), ("Smtp:Security", "tls"))).Configured);
        var ok = MailSettings.Read(Config(("Smtp:Host", "smtp.example.invalid"), ("Smtp:From", "a@example.invalid"), ("Smtp:Password", "secret")));
        Assert.True(ok.Configured);
        Assert.DoesNotContain("secret", ok.Describe());
        Assert.DoesNotContain("secret", ok.ToString());   // a record prints every field, so this one is overridden
    }

    [Fact]
    public void APreviewGoesToThePreviewAddressAndSaysWhoItWasFor()
    {
        var s = ReminderSettings.Read(Config(("Reminders:Mode", "Preview"), ("Reminders:PreviewTo", "office@example.invalid")));
        var mail = Assert.Single(ReminderSettings.Compose(s, [Cleo()], Monday));
        Assert.Equal("office@example.invalid", mail.To);
        Assert.StartsWith("[Preview: this would have gone to Cleo Test <cleo@example.invalid>]", mail.Body);
        Assert.Contains("Juggling, 09:00–10:30", mail.Body);
    }

    [Fact]
    public void OnSendsToTheFacilitatorAndTheSummaryCountsOnly()
    {
        var s = ReminderSettings.Read(Config(("Reminders:Mode", "On"), ("Reminders:SummaryTo", "office@example.invalid"), ("Reminders:Link", "https://skema.example.invalid")));
        var mails = ReminderSettings.Compose(s, [Cleo()], Monday);
        Assert.Equal(["cleo@example.invalid", "office@example.invalid"], mails.Select(m => m.To));
        Assert.DoesNotContain("[Preview", mails[0].Body);
        Assert.Contains("https://skema.example.invalid/registers", mails[0].Body);
        Assert.Contains("Cleo Test: 1", mails[1].Body);
        Assert.Empty(ReminderSettings.Compose(s, [], Monday));
    }
}

public sealed class FakeMailer : IMailer
{
    public readonly List<OutgoingMail> Sent = [];
    public int Calls;
    public Task SendAsync(IReadOnlyList<OutgoingMail> mails, CancellationToken ct)
    {
        Calls++;
        lock (Sent) Sent.AddRange(mails);
        return Task.CompletedTask;
    }
}

/// <summary>The real host with reminders and tomorrow's changes in Preview, due from midnight, and mail going to a fake.</summary>
public sealed class ReminderHost : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    public readonly string Folder = Path.Combine(AppContext.BaseDirectory, "stores", Guid.NewGuid().ToString("N"));
    public readonly FakeMailer Mailer = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.UseSetting("Store:Path", Folder);
        builder.UseSetting("Store:Create", "true");
        builder.UseSetting("Reminders:Mode", "Preview");
        builder.UseSetting("Reminders:PreviewTo", "office@example.invalid");
        builder.UseSetting("Reminders:At", "00:00");
        builder.UseSetting("NewsMail:Mode", "Preview");
        builder.UseSetting("NewsMail:PreviewTo", "office@example.invalid");
        builder.UseSetting("NewsMail:At", "00:00");
        builder.UseSetting("Smtp:Host", "smtp.example.invalid");
        builder.UseSetting("Smtp:From", "timetable@example.invalid");
        builder.ConfigureServices(s =>
        {
            var existing = s.Single(d => d.ServiceType == typeof(IMailer));
            s.Remove(existing);
            s.AddSingleton<IMailer>(Mailer);
        });
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}

public class ReminderServiceTests(ReminderHost host) : IClassFixture<ReminderHost>
{
    [Fact]
    public async Task TheReminderRunsOnceADayHoweverOftenItIsAsked()
    {
        _ = host.Services;   // starts the host, and with it the service's first look
        var service = host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<RegisterReminderService>().Single();
        var store = host.Services.GetRequiredService<Cccc.Store.ProjectStore>();
        var clock = host.Services.GetRequiredService<Cccc.Web.Infrastructure.SchoolClock>();
        for (int i = 0; i < 50 && store.GetMeta("reminders_last") is null; i++) await Task.Delay(100);
        Assert.Equal(clock.Today.ToString("yyyy-MM-dd"), store.GetMeta("reminders_sent"));
        Assert.StartsWith(DateTimeOffset.UtcNow.Year.ToString(), store.GetMeta("reminders_last"));
        var first = store.GetMeta("reminders_last");
        await Task.Delay(20);
        await service.RunOnceAsync(CancellationToken.None);
        await service.RunOnceAsync(CancellationToken.None);
        Assert.Equal(first, store.GetMeta("reminders_last"));
        Assert.All(host.Mailer.Sent, m => Assert.Equal("office@example.invalid", m.To));
    }
}
