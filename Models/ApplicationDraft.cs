namespace BusinessLicensing_Practice.Models;

public class ApplicationDraft
{
    public int Id { get; set; }
    public string UserId { get; set; } = "";
    public ApplicationUser? User { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public int SchemaVersion { get; set; } = 1;
    public int CurrentStep { get; set; } = 1;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSavedAtUtc { get; set; }
    public List<ApplicationDraftDocument> Documents { get; set; } = [];
}

public class ApplicationDraftDocument
{
    public int Id { get; set; }
    public int ApplicationDraftId { get; set; }
    public ApplicationDraft? ApplicationDraft { get; set; }
    public string DocumentType { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FilePath { get; set; } = "";
    public string? AiValidationStatus { get; set; }
}
