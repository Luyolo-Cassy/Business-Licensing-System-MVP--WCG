namespace BusinessLicensing_Practice.Services.Email;

/// <summary>Delivers a prepared application or Identity email message.</summary>
public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken = default);
}
