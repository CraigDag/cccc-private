using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace Cccc.Web.Features.Mail;

/// <summary>
/// How mail leaves the building. Mail is an extra: with no <c>Smtp__Host</c> the app runs exactly as
/// without it. The same variables as the current app, so one environment file serves both:
/// Smtp__Host, Smtp__Port (587), Smtp__Security (StartTls, SslOnConnect or None), Smtp__User,
/// Smtp__Password, Smtp__From, Smtp__FromName, Smtp__ReplyTo. The password is never logged or shown.
/// </summary>
public sealed record MailSettings(string? Host, int Port, SecureSocketOptions Security, string? User, string? Password,
    string? From, string? FromName, string? ReplyTo, string? Problem)
{
    public bool Configured => Host is not null && Problem is null;

    public static MailSettings Read(IConfiguration c)
    {
        var s = c.GetSection("Smtp");
        string? T(string k) => s[k] is { } v && v.Trim().Length > 0 ? v.Trim() : null;
        var host = T("Host");
        var none = new MailSettings(null, 587, SecureSocketOptions.StartTls, null, null, null, null, null, null);
        if (host is null) return none;
        int port = 587;
        if (T("Port") is { } p && (!int.TryParse(p, out port) || port is < 1 or > 65535))
            return none with { Host = host, Problem = "Smtp__Port is not a port number, so no mail is sent." };
        var security = T("Security") switch
        {
            null or "StartTls" => SecureSocketOptions.StartTls,
            "SslOnConnect" => SecureSocketOptions.SslOnConnect,
            "None" => SecureSocketOptions.None,
            _ => (SecureSocketOptions?)null,
        };
        if (security is null) return none with { Host = host, Problem = "Smtp__Security is not StartTls, SslOnConnect or None, so no mail is sent." };
        var from = T("From");
        if (from is null || !MailboxAddress.TryParse(from, out _))
            return none with { Host = host, Problem = "Smtp__From is not an address, so no mail is sent." };
        return new MailSettings(host, port, security.Value, T("User") ?? from, T("Password"), from, T("FromName"), T("ReplyTo"), null);
    }

    /// <summary>A record prints every field, the password included; this one prints what Describe says.</summary>
    public override string ToString() => Describe();

    public string Describe() => Problem ?? (Configured ? $"Mail goes out through {Host}:{Port} as {From}." : "No mail is set up.");
}

public sealed record OutgoingMail(string To, string Subject, string Body);

public interface IMailer
{
    Task SendAsync(IReadOnlyList<OutgoingMail> mails, CancellationToken ct);
}

public sealed class SmtpMailer(MailSettings settings) : IMailer
{
    public async Task SendAsync(IReadOnlyList<OutgoingMail> mails, CancellationToken ct)
    {
        if (!settings.Configured) throw new InvalidOperationException(settings.Describe());
        using var client = new SmtpClient();
        await client.ConnectAsync(settings.Host!, settings.Port, settings.Security, ct);
        if (settings.Password is not null) await client.AuthenticateAsync(settings.User!, settings.Password, ct);
        foreach (var m in mails)
        {
            var msg = new MimeMessage();
            msg.From.Add(new MailboxAddress(settings.FromName ?? "CCCC Scheduler", settings.From!));
            if (settings.ReplyTo is { } r && MailboxAddress.TryParse(r, out var reply)) msg.ReplyTo.Add(reply);
            msg.To.Add(MailboxAddress.Parse(m.To));
            msg.Subject = m.Subject;
            msg.Body = new TextPart("plain") { Text = m.Body };
            await client.SendAsync(msg, ct);
        }
        await client.DisconnectAsync(true, ct);
    }
}
