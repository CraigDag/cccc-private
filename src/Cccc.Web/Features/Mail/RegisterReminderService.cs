using System.Globalization;
using System.Text;
using Cccc.Domain;
using Cccc.Domain.Rules;
using Cccc.Store;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Features.Mail;

public enum ReminderMode { Off, Preview, On }

/// <summary>
/// The end-of-day register reminder. <b>Off unless told otherwise</b>: Reminders__Mode=Preview sends
/// every reminder to Reminders__PreviewTo instead, with who it was meant for at the top, so a week of
/// real evenings can be read before any facilitator gets one; On sends them for real. A value that
/// does not parse is Off and says so, because a typo must never start emailing staff. Reminders__At
/// (18:00 Copenhagen by default), Reminders__SummaryTo (one list to an administrator) and
/// Reminders__Link (the app's address, written in the mail) are optional. No student is ever named.
/// </summary>
public sealed record ReminderSettings(ReminderMode Mode, TimeOnly At, string? PreviewTo, string? SummaryTo, string? Link, string? Problem)
{
    public static ReminderSettings Read(IConfiguration c)
    {
        var s = c.GetSection("Reminders");
        string? T(string k) => s[k] is { } v && v.Trim().Length > 0 ? v.Trim() : null;
        var off = new ReminderSettings(ReminderMode.Off, new TimeOnly(18, 0), null, null, T("Link"), null);
        var at = off.At;
        if (T("At") is { } a && !TimeOnly.TryParseExact(a, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out at))
            return off with { Problem = $"Reminders__At is \"{a}\", not a time like 18:00, so reminders are off." };
        if (T("Mode") is not { } m) return off with { At = at };
        if (!Enum.TryParse<ReminderMode>(m, true, out var mode) || !Enum.IsDefined(mode))
            return off with { At = at, Problem = $"Reminders__Mode is \"{m}\", not Off, Preview or On, so reminders are off." };
        if (mode == ReminderMode.Preview && T("PreviewTo") is null)
            return off with { At = at, Problem = "Reminders__Mode is Preview and Reminders__PreviewTo is not set, so reminders are off." };
        return new ReminderSettings(mode, at, T("PreviewTo"), T("SummaryTo"), T("Link"), null);
    }

    public string Describe(MailSettings mail) =>
        Problem ?? Mode switch
        {
            ReminderMode.Off => "Nobody is emailed. Reminders__Mode=Preview or On starts them; the runbook has the steps.",
            _ when !mail.Configured => $"Set to {Mode}, but nothing is sent: {mail.Describe()}",
            ReminderMode.Preview => $"Preview: every evening at {At:HH\\:mm}, the reminders go to one preview address instead of the facilitators.",
            _ => $"On: every evening at {At:HH\\:mm}, each facilitator with a register missing from that day is sent one email.",
        };

    public static IReadOnlyList<OutgoingMail> Compose(ReminderSettings s, IReadOnlyList<Reminder> reminders, DateOnly day)
    {
        var mails = new List<OutgoingMail>();
        string Date = Cccc.Domain.Week.ShortDate(day);
        foreach (var r in reminders)
        {
            var body = new StringBuilder();
            if (s.Mode == ReminderMode.Preview) body.Append($"[Preview: this would have gone to {r.Facilitator.Name} <{r.Facilitator.Email}>]\n\n");
            body.Append($"Hello {r.Facilitator.Name},\n\nThe register for {(r.Classes.Count == 1 ? "this class" : "these classes")} today is still to be taken:\n\n");
            foreach (var o in r.Classes) body.Append($"  • {o.Session.DisplayTitle}, {Cccc.Domain.Week.Hm(o.Session.Start)}–{Cccc.Domain.Week.Hm(o.Session.End)}\n");
            body.Append(s.Link is { } l ? $"\nYou can take it in the timetable: {l}/registers\n" : "\nYou can take it in the timetable, under Registers.\n");
            body.Append("\nThis is an automatic reminder; nobody needs a reply.\n");
            mails.Add(new OutgoingMail(s.Mode == ReminderMode.Preview ? s.PreviewTo! : r.Facilitator.Email,
                $"Register{(r.Classes.Count == 1 ? "" : "s")} still to take, {Date}", body.ToString()));
        }
        if (s.SummaryTo is { } to && reminders.Count > 0)
        {
            var body = new StringBuilder($"Registers still missing at {s.At:HH\\:mm} on {Date}:\n\n");
            foreach (var r in reminders) body.Append($"  {r.Facilitator.Name}: {r.Classes.Count}\n");
            mails.Add(new OutgoingMail(to, $"Registers still missing, {Date}", body.ToString()));
        }
        return mails;
    }
}

/// <summary>Checks once a minute; sends once a day, for the timetable that is open, after the set time.</summary>
public sealed class RegisterReminderService(ReminderSettings settings, MailSettings mail, IMailer mailer, ProjectStore store,
    ProjectChoice choice, SchoolClock clock, ILogger<RegisterReminderService> log) : BackgroundService
{
    private const string SentKey = "reminders_sent";

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (settings.Problem is not null || (settings.Mode != ReminderMode.Off && !mail.Configured))
            log.LogWarning("Register reminders: {State}", settings.Describe(mail));
        else log.LogInformation("Register reminders: {State}", settings.Describe(mail));
        if (settings.Mode == ReminderMode.Off || settings.Problem is not null || !mail.Configured) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await RunOnceAsync(stop); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The kind of failure and nothing else: no address, no name, no project.
                log.LogError("Register reminders failed this evening: {Kind}", e.GetType().Name);
                store.SetMeta("reminders_last", $"{clock.Now:O}|failed|{e.GetType().Name}");
                store.SetMeta(SentKey, clock.Today.ToString("yyyy-MM-dd"));   // one try an evening, never a loop
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = clock.Local(clock.Now);
        var today = clock.Today;
        if (TimeOnly.FromDateTime(now.DateTime) < settings.At) return;
        if (store.GetMeta(SentKey) == today.ToString("yyyy-MM-dd")) return;
        store.SetMeta(SentKey, today.ToString("yyyy-MM-dd"));
        var reminders = choice.Open() is { } project ? RegisterReminders.For(project.State, today, TimeOnly.FromDateTime(now.DateTime)) : [];
        var mails = ReminderSettings.Compose(settings, reminders, today);
        if (mails.Count > 0) await mailer.SendAsync(mails, ct);
        store.SetMeta("reminders_last", $"{clock.Now:O}|ok|{reminders.Count}");
        log.LogInformation("Register reminders: {Count} sent ({Mode}).", reminders.Count, settings.Mode);
    }
}
