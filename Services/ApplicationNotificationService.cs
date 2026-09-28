using System.Net.Mail;
using System.Text.Encodings.Web;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public sealed class ApplicationNotificationService(
    IServiceScopeFactory scopes,
    IEmailService emails,
    ILogger<ApplicationNotificationService> logger) : IApplicationNotificationService
{
    public Task NotifySubmissionAsync(int applicationId, CancellationToken cancellationToken = default) =>
        SendAsync(applicationId, NotificationKind.Submission, null, null, null, cancellationToken);

    public Task NotifyReviewAsync(int applicationId, string previousStatus, string newStatus,
        string? municipalMessage, CancellationToken cancellationToken = default)
    {
        var message = string.IsNullOrWhiteSpace(municipalMessage) ? null : municipalMessage;
        var statusChanged = !string.Equals(previousStatus, newStatus, StringComparison.Ordinal);
        var kind = (statusChanged, message != null) switch
        {
            (true, true) => NotificationKind.StatusAndMessage,
            (true, false) => NotificationKind.Status,
            (false, true) => NotificationKind.Message,
            _ => NotificationKind.None
        };
        return kind == NotificationKind.None
            ? Task.CompletedTask
            : SendAsync(applicationId, kind, previousStatus, newStatus, message, cancellationToken);
    }

    private async Task SendAsync(int applicationId, NotificationKind kind, string? previousStatus,
        string? newStatus, string? municipalMessage, CancellationToken cancellationToken)
    {
        string applicationNumber = "";
        string municipality = "";
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var application = await db.Applications.AsNoTracking()
                .Include(item => item.Details)
                .Include(item => item.User)
                .SingleOrDefaultAsync(item => item.Id == applicationId, cancellationToken);
            if (application == null)
            {
                logger.LogWarning("Application notification skipped. ApplicationId={ApplicationId} NotificationType={NotificationType} Outcome=Skipped Reason=ApplicationNotFound",
                    applicationId, kind);
                return;
            }

            applicationNumber = application.ApplicationNumber;
            municipality = application.Municipality ?? "Not assigned";
            var recipient = ValidAddress(application.Details?.ApplicantEmail)
                ?? ValidAddress(application.User?.Email);
            if (recipient == null)
            {
                logger.LogWarning("Application notification skipped. ApplicationId={ApplicationId} ApplicationNumber={ApplicationNumber} Municipality={Municipality} NotificationType={NotificationType} Outcome=Skipped Reason=NoValidRecipient",
                    applicationId, applicationNumber, municipality, kind);
                return;
            }

            var name = ApplicationEntry.FullName(application.Details);
            if (string.IsNullOrWhiteSpace(name)) name = application.User?.FullName ?? "Applicant";
            var firstName = application.Details?.ApplicantFirstName?.Trim();
            if (string.IsNullOrWhiteSpace(firstName)) firstName = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Applicant";
            var email = BuildMessage(application, recipient, name, firstName, kind, previousStatus, newStatus, municipalMessage);
            await emails.SendAsync(email, cancellationToken);
            logger.LogInformation("Application notification sent. ApplicationId={ApplicationId} ApplicationNumber={ApplicationNumber} Municipality={Municipality} NotificationType={NotificationType} Outcome=Sent",
                applicationId, applicationNumber, municipality, kind);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Application notification failed. ApplicationId={ApplicationId} ApplicationNumber={ApplicationNumber} Municipality={Municipality} NotificationType={NotificationType} Outcome=Failed ExceptionType={ExceptionType}",
                applicationId, applicationNumber, municipality, kind, exception.GetType().Name);
        }
    }

    private static string? ValidAddress(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return null;
        try
        {
            var parsed = new MailAddress(candidate.Trim());
            return string.Equals(parsed.Address, candidate.Trim(), StringComparison.OrdinalIgnoreCase) ? parsed.Address : null;
        }
        catch (FormatException) { return null; }
    }

    private static EmailMessage BuildMessage(Application application, string recipient, string recipientName,
        string firstName, NotificationKind kind, string? previousStatus, string? newStatus, string? municipalMessage)
    {
        var subject = kind switch
        {
            NotificationKind.Submission => $"Application Submitted Successfully – {application.ApplicationNumber}",
            NotificationKind.Message => $"New Message About Your Application – {application.ApplicationNumber}",
            _ => $"Application Status Update – {application.ApplicationNumber}"
        };
        var intro = kind switch
        {
            NotificationKind.Submission => "Your business licence application has been successfully submitted.",
            NotificationKind.Message => "You have received a new message regarding your business licence application.",
            NotificationKind.StatusAndMessage => "There has been an update to your business licence application.",
            _ => "The status of your business licence application has been updated."
        };
        var details = new List<string>
        {
            $"Application Number: {application.ApplicationNumber}",
            $"Licence Type: {application.LicenceType}",
            $"Responsible Municipality: {application.Municipality ?? "Not assigned"}"
        };
        if (kind == NotificationKind.Submission) details.Add("Status: Submitted");
        else if (kind == NotificationKind.Message) details.Add($"Current Status: {application.Status}");
        else
        {
            details.Add($"Previous Status: {previousStatus}");
            details.Add($"New Status: {newStatus}");
        }

        var messageBlock = municipalMessage == null ? "" : $"\n\nMessage from {application.Municipality ?? "the responsible municipality"}:\n\n{municipalMessage}";
        var action = kind == NotificationKind.Submission
            ? "Please keep your application number for reference. You can track the progress of your application through the Provincial Business Licensing System."
            : "Please log in to the Provincial Business Licensing System to view your application.";
        var plain = $"Dear {firstName},\n\n{intro}\n\n{string.Join("\n", details)}{messageBlock}\n\n{action}\n\nRegards,\nProvincial Business Licensing System\n\nThis is an automated email. Please do not reply.";

        static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "");
        var htmlDetails = string.Join("<br>", details.Select(E));
        var htmlMessage = municipalMessage == null ? "" : $"<p><strong>Message from {E(application.Municipality ?? "the responsible municipality")}:</strong></p><p style=\"white-space:pre-wrap\">{E(municipalMessage)}</p>";
        var html = $"<p>Dear {E(firstName)},</p><p>{E(intro)}</p><p>{htmlDetails}</p>{htmlMessage}<p>{E(action)}</p><p>Regards,<br>Provincial Business Licensing System</p><p><em>This is an automated email. Please do not reply.</em></p>";
        return new EmailMessage(recipient, recipientName, subject, plain, html);
    }

    private enum NotificationKind { None, Submission, Status, StatusAndMessage, Message }
}
