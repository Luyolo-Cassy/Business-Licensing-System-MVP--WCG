using System.Net;
using System.Text.Encodings.Web;
using BusinessLicensing_Practice.Models;
using BusinessLicensing_Practice.Services.Email;
using Microsoft.AspNetCore.Identity;

namespace BusinessLicensing_Practice.Components.Account;

internal sealed class IdentityEmailSender(IEmailService emails) : IEmailSender<ApplicationUser>
{
    public Task SendConfirmationLinkAsync(ApplicationUser user, string email, string confirmationLink)
    {
        var isEmailChange = !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase);
        var subject = isEmailChange ? "Confirm your email change" : "Confirm your email";
        var action = isEmailChange ? "confirm your new email address" : "confirm your email address";
        return SendAsync(user, email, subject,
            $"Please {action} by opening this link:\n\n{WebUtility.HtmlDecode(confirmationLink)}",
            $"<p>Please {action} by <a href=\"{confirmationLink}\">clicking here</a>.</p>");
    }

    public Task SendPasswordResetLinkAsync(ApplicationUser user, string email, string resetLink) =>
        SendAsync(user, email, "Reset your password",
            $"Please reset your password by opening this link:\n\n{WebUtility.HtmlDecode(resetLink)}",
            $"<p>Please reset your password by <a href=\"{resetLink}\">clicking here</a>.</p>");

    public Task SendPasswordResetCodeAsync(ApplicationUser user, string email, string resetCode)
    {
        var encodedCode = HtmlEncoder.Default.Encode(resetCode);
        return SendAsync(user, email, "Reset your password",
            $"Use this code to reset your password: {resetCode}",
            $"<p>Use this code to reset your password: <strong>{encodedCode}</strong></p>");
    }

    private Task SendAsync(ApplicationUser user, string email, string subject, string plainText, string html) =>
        emails.SendAsync(new EmailMessage(
            email,
            string.IsNullOrWhiteSpace(user.FullName) ? email : user.FullName,
            subject,
            plainText,
            html));
}
