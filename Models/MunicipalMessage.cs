namespace BusinessLicensing_Practice.Models;

// Each applicant-facing note is also an in-app notification until ReadAtUtc is set.
public class MunicipalMessage
{
    public int Id { get; set; }
    public int ApplicationId { get; set; }
    public Application Application { get; set; } = null!;
    public string Content { get; set; } = "";
    public string? SenderId { get; set; }
    public ApplicationUser? Sender { get; set; }
    public string SenderName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
}
