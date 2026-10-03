namespace BusinessLicensing_Practice.Models;

public class ApplicationAiSummary
{
    public long Id { get; set; }
    public int ApplicationId { get; set; }
    public Application Application { get; set; } = null!;
    public int RevisionNumber { get; set; }
    public string SummaryText { get; set; } = "";
    public DateTime GeneratedAtUtc { get; set; }
    public string GeneratedByUserId { get; set; } = "";
    public string GeneratedByName { get; set; } = "";
    public int FormatVersion { get; set; }
    public string Model { get; set; } = "";
}
