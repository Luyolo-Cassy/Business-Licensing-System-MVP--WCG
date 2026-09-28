using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BusinessLicensing_Practice.Services.Email;

public sealed class SmtpEmailService(IOptions<EmailOptions> configuredOptions) : IEmailService
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
    {
        var options = configuredOptions.Value;
        Validate(options);

        var fromAddress = string.IsNullOrWhiteSpace(options.FromAddress)
            ? options.SmtpUsername
            : options.FromAddress;
        var mimeMessage = new MimeMessage();
        mimeMessage.From.Add(new MailboxAddress(options.FromName, fromAddress));
        mimeMessage.To.Add(new MailboxAddress(message.RecipientName, message.RecipientAddress));
        mimeMessage.Subject = message.Subject;
        mimeMessage.Body = new BodyBuilder
        {
            TextBody = message.PlainTextBody,
            HtmlBody = message.HtmlBody
        }.ToMessageBody();

        using var client = new SmtpClient { Timeout = checked(options.TimeoutSeconds * 1000) };
        try
        {
            await client.ConnectAsync(options.SmtpHost, options.SmtpPort, Security(options.SmtpSecurity), cancellationToken);
            await client.AuthenticateAsync(options.SmtpUsername, options.SmtpAppPassword, cancellationToken);
            await client.SendAsync(mimeMessage, cancellationToken);
        }
        finally
        {
            if (client.IsConnected)
            {
                try { await client.DisconnectAsync(true, CancellationToken.None); }
                catch { /* Delivery outcome must not be replaced by a disconnect failure. */ }
            }
        }
    }

    private static SecureSocketOptions Security(string value) => value.Trim().ToLowerInvariant() switch
    {
        "starttls" => SecureSocketOptions.StartTls,
        "sslonconnect" => SecureSocketOptions.SslOnConnect,
        _ => throw new InvalidOperationException("Email SMTP security must be StartTls or SslOnConnect.")
    };

    private static void Validate(EmailOptions options)
    {
        if (!options.Enabled) throw new InvalidOperationException("Application email delivery is disabled.");
        if (string.IsNullOrWhiteSpace(options.SmtpHost)) throw new InvalidOperationException("Email SMTP host is not configured.");
        if (options.SmtpPort is < 1 or > 65535) throw new InvalidOperationException("Email SMTP port is invalid.");
        if (string.IsNullOrWhiteSpace(options.SmtpUsername) || string.IsNullOrWhiteSpace(options.SmtpAppPassword))
            throw new InvalidOperationException("Email SMTP credentials are not configured.");
        if (options.TimeoutSeconds is < 1 or > 120) throw new InvalidOperationException("Email SMTP timeout is invalid.");
        _ = Security(options.SmtpSecurity);
    }
}
