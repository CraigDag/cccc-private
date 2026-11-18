using System.Text;
using Cccc.Domain;
using Cccc.Domain.Model;
using Cccc.Domain.Rules;
using Cccc.Store;
using Cccc.Web.Features.Now;
using Cccc.Web.Infrastructure;

namespace Cccc.Web.Features.Mail;

/// <summary>
/// The evening email of what changed in somebody's next day that they have not seen (New for you, by email), with
/// the register reminder's rules: <b>off unless told otherwise</b>, NewsMail__Mode=Preview sends every one to
/// NewsMail__PreviewTo with who it was meant for at the top, On sends them for real, and a value that does not parse
/// is Off and says so. NewsMail__At (18:00 Copenhagen), NewsMail__SummaryTo (one list of counts to an administrator,
/// for the phone calls) and NewsMail__Link (the app's address) are optional. No student is ever named.
/// </summary>
public sealed record NewsMailSettings(ReminderSettings Settings)
{
    public const string Name = "NewsMail";

    public static NewsMailSettings Read(IConfiguration c) => new(ReminderSettings.Read(c, Name, "these emails"));

    public string Describe(MailSettings mail) => Settings.Describe(mail, Name, "these emails",
        "each facilitator whose next day changed in a way they have not seen is sent one email");

    /// <summary>
    /// One email to each of <paramref name="notes"/> about their next day, and the count to an administrator when
    /// asked for. <paramref name="nameOf"/> turns whoever made a change (a login) into a name.
    /// </summary>
    public static IReadOnlyList<OutgoingMail> Compose(ReminderSettings s, Timetable t, IReadOnlyList<EveningNote> notes, Func<string, string> nameOf)
    {
        var mails = new List<OutgoingMail>();
        foreach (var n in notes)
        {
            string day = Cccc.Domain.Week.ShortDate(n.Day);
            var lines = NewsWords.Lines(n.Changes);
            var body = new StringBuilder();
            if (s.Mode == ReminderMode.Preview) body.Append($"[Preview: this would have gone to {n.Facilitator.Name} <{n.Facilitator.Email}>]\n\n");
            body.Append($"Hello {n.Facilitator.Name},\n\n{(lines.Count == 1 ? "A class" : "Classes")} of yours tomorrow, {day}, "
                        + $"{(lines.Count == 1 ? "has" : "have")} changed, and you may not have seen it yet:\n\n");
            foreach (var line in lines)
                body.Append($"  • {NewsWords.Title(t, line)}, {NewsWords.When(t, line)}\n"
                            + $"    {NewsWords.What(t, line, n.Facilitator.Id)} (changed by {string.Join(" and ", line.By.Select(nameOf))})\n");
            body.Append(s.Link is { } l
                ? $"\nNow, in the timetable, has it too, with Got it to say you have seen it: {l}/\n"
                : "\nNow, in the timetable, has it too, with Got it to say you have seen it.\n");
            body.Append("\nThis is an automatic email; nobody needs a reply.\n");
            mails.Add(new OutgoingMail(s.Mode == ReminderMode.Preview ? s.PreviewTo! : n.Facilitator.Email,
                $"Tomorrow, {day}: what changed for you", body.ToString()));
        }
        if (s.SummaryTo is { } to && notes.Count > 0)
        {
            string day = Cccc.Domain.Week.ShortDate(notes[0].Day);
            var body = new StringBuilder($"Changes to tomorrow, {day}, that these people had not seen at {s.At:HH\\:mm}, so were emailed:\n\n");
            foreach (var n in notes) body.Append($"  {n.Facilitator.Name}: {Cccc.Web.Features.Week.Looks.Count(n.Changes.Count, "class", "classes")}\n");
            mails.Add(new OutgoingMail(to, $"Not seen yet: tomorrow's changes, {day}", body.ToString()));
        }
        return mails;
    }
}

/// <summary>Checks once a minute; sends once a day, for the timetable that is open, after the set time.</summary>
public sealed class NewsMailService(NewsMailSettings settings, MailSettings mail, IMailer mailer, ProjectStore store, ProjectChoice choice,
    SeenNews seen, AccountDirectory accounts, SchoolClock clock, ILogger<NewsMailService> log) : BackgroundService
{
    private const string SentKey = "newsmail_sent";
    private const string LastKey = "newsmail_last";
    private ReminderSettings S => settings.Settings;

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (S.Problem is not null || (S.Mode != ReminderMode.Off && !mail.Configured))
            log.LogWarning("Tomorrow's changes by email: {State}", settings.Describe(mail));
        else log.LogInformation("Tomorrow's changes by email: {State}", settings.Describe(mail));
        if (S.Mode == ReminderMode.Off || S.Problem is not null || !mail.Configured) return;
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try { await RunOnceAsync(stop); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // The kind of failure and nothing else: no address, no name, no project.
                log.LogError("Tomorrow's changes by email failed this evening: {Kind}", e.GetType().Name);
                store.SetMeta(LastKey, $"{clock.Now:O}|failed|{e.GetType().Name}");
                store.SetMeta(SentKey, clock.Today.ToString("yyyy-MM-dd"));   // one try an evening, never a loop
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }

    public async Task RunOnceAsync(CancellationToken ct)
    {
        var now = TimeOnly.FromDateTime(clock.Local(clock.Now).DateTime);
        var today = clock.Today;
        if (now < S.At) return;
        if (store.GetMeta(SentKey) == today.ToString("yyyy-MM-dd")) return;
        store.SetMeta(SentKey, today.ToString("yyyy-MM-dd"));
        IReadOnlyList<OutgoingMail> mails = [];
        int count = 0;
        if (choice.Open() is { } project)
        {
            var t = project.State;
            var changes = project.ChangesMadeSince(clock.StartOf(News.ReadFrom(today)));
            var logins = seen.LoginsOf(t.Facilitators.Select(f => f.Id));
            var notes = EveningNotes.For(t, changes, today, now, id => logins.GetValueOrDefault(id) ?? [], id => seen.Read(project.Id, id));
            mails = NewsMailSettings.Compose(S, t, notes, NameOf);
            count = notes.Count;
        }
        if (mails.Count > 0) await mailer.SendAsync(mails, ct);
        store.SetMeta(LastKey, $"{clock.Now:O}|ok|{count}");
        log.LogInformation("Tomorrow's changes by email: {Count} sent ({Mode}).", count, S.Mode);
    }

    private string NameOf(string by) => by == Follower.By ? Follower.Display : accounts.FindByLogin(by)?.Display ?? by;
}
