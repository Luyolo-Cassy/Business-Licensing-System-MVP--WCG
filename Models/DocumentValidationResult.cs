namespace BusinessLicensing_Practice.Models;

public sealed class DocumentValidationResult
{
    public string Status { get; set; } = "REVIEW";
    public string DocumentType { get; set; } = "";
    public string Summary { get; set; } = "";
    public string ExpiryDate { get; set; } = "";
    public double Confidence { get; set; }
    public List<DocumentValidationIssue> Issues { get; set; } = new();
}

public sealed class DocumentValidationIssue
{
    public string Type { get; set; } = "";
    public string Message { get; set; } = "";
}
