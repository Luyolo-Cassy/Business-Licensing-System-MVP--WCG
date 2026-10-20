namespace BusinessLicensing_Practice.Services;

/// <summary>
/// Defines post-commit notifications for application submission and review events.
/// </summary>
public interface IApplicationNotificationService
{
    Task NotifySubmissionAsync(int applicationId, CancellationToken cancellationToken = default);
    Task NotifyReviewAsync(int applicationId, string previousStatus, string newStatus,
        string? municipalMessage, CancellationToken cancellationToken = default);
    Task NotifyResubmissionAsync(int applicationId, CancellationToken cancellationToken = default);
}
