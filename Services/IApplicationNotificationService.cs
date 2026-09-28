namespace BusinessLicensing_Practice.Services;

public interface IApplicationNotificationService
{
    Task NotifySubmissionAsync(int applicationId, CancellationToken cancellationToken = default);
    Task NotifyReviewAsync(int applicationId, string previousStatus, string newStatus,
        string? municipalMessage, CancellationToken cancellationToken = default);
}
