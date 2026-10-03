namespace BusinessLicensing_Practice.Models;

public class ApplicationAuditLog
{
    public long Id { get; set; }
    public int ApplicationId { get; set; }
    public string ApplicationNumber { get; set; } = "";
    public string ActorUserId { get; set; } = "";
    public string ActorDisplayName { get; set; } = "";
    public string ActorRole { get; set; } = "";
    public string Municipality { get; set; } = "";
    public string EventType { get; set; } = "";
    public DateTime OccurredAtUtc { get; set; }
    public string? PreviousStatus { get; set; }
    public string? NewStatus { get; set; }
    public string Summary { get; set; } = "";
    public string? MetadataJson { get; set; }
}

public static class ApplicationAuditEventTypes
{
    public const string StatusChanged = "StatusChanged";
    public const string ReviewMessageSent = "ReviewMessageSent";
    public const string CorrectionRequested = "CorrectionRequested";
    public const string ApplicationApproved = "ApplicationApproved";
    public const string ApplicationRejected = "ApplicationRejected";
    public const string ApplicationResubmitted = "ApplicationResubmitted";
    public const string AiSummaryGenerated = "AiSummaryGenerated";

    public static readonly IReadOnlyDictionary<string, string> Labels = new Dictionary<string, string>
    {
        [StatusChanged] = "Status changed",
        [ReviewMessageSent] = "Review message sent",
        [CorrectionRequested] = "Additional information requested",
        [ApplicationApproved] = "Application approved",
        [ApplicationRejected] = "Application rejected",
        [ApplicationResubmitted] = "Application resubmitted",
        [AiSummaryGenerated] = "AI summary generated"
    };
}
