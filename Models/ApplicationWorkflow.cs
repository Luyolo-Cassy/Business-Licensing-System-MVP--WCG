namespace BusinessLicensing_Practice.Models;

public static class ApplicationWorkflow
{
    public const string Submitted = "Submitted";
    public const string UnderReview = "Under Review";
    public const string AdditionalInformationRequired = "Additional Information Required";
    public const string DepartmentAssessment = "Department Assessment";
    public const string FinalDecision = "Final Decision";
    public const string LicenceIssued = "Licence Issued";
    public const string Rejected = "Rejected";
    public const string Withdrawn = "Withdrawn";

    public const string GeneralMessage = "General";
    public const string CorrectionRequestMessage = "CorrectionRequest";

    public static readonly IReadOnlyList<string> Statuses =
    [
        Submitted, UnderReview, AdditionalInformationRequired, DepartmentAssessment,
        FinalDecision, LicenceIssued, Rejected, Withdrawn
    ];

    public static bool IsTerminal(string? status) => status is LicenceIssued or Rejected or Withdrawn;
}
