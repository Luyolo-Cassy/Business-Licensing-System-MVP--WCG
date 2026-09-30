using System.Security.Claims;
using System.Text.Json;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public sealed class ApplicationDraftService(IServiceScopeFactory scopes, ProtectedUploadService uploads)
{
    public const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ApplicationDraft?> GetAsync(ClaimsPrincipal principal)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (db, user) = await AuthorizeOwnerAsync(scope.ServiceProvider, principal);
        return await db.ApplicationDrafts.AsNoTracking().Include(draft => draft.Documents)
            .SingleOrDefaultAsync(draft => draft.UserId == user.Id && draft.SourceApplicationId == null);
    }

    public async Task<ApplicationDraftSummary?> GetSummaryAsync(ClaimsPrincipal principal)
    {
        var draft = await GetAsync(principal);
        if (draft == null) return null;
        var payload = Deserialize(draft.PayloadJson);
        return new(draft.Id, payload.LicenceType, payload.BusinessName, draft.CurrentStep, draft.LastSavedAtUtc);
    }

    public async Task<ApplicationDraft> SaveAsync(ClaimsPrincipal principal, ApplicationDraftPayload payload, int currentStep)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (db, user) = await AuthorizeOwnerAsync(scope.ServiceProvider, principal);
        var now = DateTime.UtcNow;
        var draft = await db.ApplicationDrafts.SingleOrDefaultAsync(item => item.UserId == user.Id && item.SourceApplicationId == null);
        if (draft == null)
        {
            draft = new ApplicationDraft { UserId = user.Id, CreatedAtUtc = now };
            db.ApplicationDrafts.Add(draft);
        }
        draft.PayloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        draft.SchemaVersion = CurrentSchemaVersion;
        draft.CurrentStep = Math.Clamp(currentStep, 1, 7);
        draft.LastSavedAtUtc = now;
        await db.SaveChangesAsync();
        return draft;
    }

    public async Task<ApplicationDraftDocument> ReplaceDocumentAsync(ClaimsPrincipal principal, string documentType,
        string fileName, string filePath, AiDocumentValidationStatus? validationStatus, int? sourceApplicationId = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (db, user) = await AuthorizeOwnerAsync(scope.ServiceProvider, principal);
        await RequireCorrectionAccessAsync(db, user.Id, sourceApplicationId);
        await RequireAllowedDocumentTypeAsync(db, sourceApplicationId, documentType);
        var draft = await db.ApplicationDrafts.Include(item => item.Documents)
            .SingleOrDefaultAsync(item => item.UserId == user.Id && item.SourceApplicationId == sourceApplicationId)
            ?? throw new InvalidOperationException("Save the application before uploading documents.");
        var existing = draft.Documents.SingleOrDefault(item => item.DocumentType == documentType);
        var oldPath = existing?.FilePath;
        if (existing == null)
        {
            existing = new ApplicationDraftDocument { DocumentType = documentType };
            draft.Documents.Add(existing);
        }
        existing.FileName = Path.GetFileName(fileName);
        existing.FilePath = filePath;
        existing.AiValidationStatus = validationStatus?.ToString();
        draft.LastSavedAtUtc = DateTime.UtcNow;
        try { await db.SaveChangesAsync(); }
        catch
        {
            await uploads.DeleteIfUnreferencedAsync(db, filePath);
            throw;
        }
        if (!string.IsNullOrWhiteSpace(oldPath) && oldPath != filePath)
            await uploads.DeleteIfUnreferencedAsync(db, oldPath);
        return existing;
    }

    public async Task RemoveDocumentAsync(ClaimsPrincipal principal, string documentType, int? sourceApplicationId = null)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (db, user) = await AuthorizeOwnerAsync(scope.ServiceProvider, principal);
        await RequireCorrectionAccessAsync(db, user.Id, sourceApplicationId);
        await RequireAllowedDocumentTypeAsync(db, sourceApplicationId, documentType);
        var document = await db.ApplicationDraftDocuments
            .SingleOrDefaultAsync(item => item.DocumentType == documentType && item.ApplicationDraft!.UserId == user.Id
                && item.ApplicationDraft.SourceApplicationId == sourceApplicationId);
        if (document == null) return;
        var path = document.FilePath;
        db.ApplicationDraftDocuments.Remove(document);
        await db.SaveChangesAsync();
        await uploads.DeleteIfUnreferencedAsync(db, path);
    }

    public async Task DeleteAsync(ClaimsPrincipal principal)
    {
        await using var scope = scopes.CreateAsyncScope();
        var (db, user) = await AuthorizeOwnerAsync(scope.ServiceProvider, principal);
        var draft = await db.ApplicationDrafts.Include(item => item.Documents)
            .SingleOrDefaultAsync(item => item.UserId == user.Id && item.SourceApplicationId == null);
        if (draft == null) return;
        var paths = draft.Documents.Select(item => item.FilePath).Distinct().ToList();
        db.ApplicationDrafts.Remove(draft);
        await db.SaveChangesAsync();
        foreach (var path in paths) await uploads.DeleteIfUnreferencedAsync(db, path);
    }

    public static ApplicationDraftPayload Deserialize(string json) =>
        JsonSerializer.Deserialize<ApplicationDraftPayload>(json, JsonOptions) ?? new();

    private static async Task<(ApplicationDbContext Db, ApplicationUser User)> AuthorizeOwnerAsync(
        IServiceProvider services, ClaimsPrincipal principal)
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.GetUserAsync(principal);
        var stamp = principal.FindFirstValue("AspNet.Identity.SecurityStamp");
        if (principal.Identity?.IsAuthenticated != true || user == null ||
            !await users.IsInRoleAsync(user, "BusinessOwner") || await users.IsLockedOutAsync(user) ||
            (stamp != null && stamp != user.SecurityStamp))
            throw new UnauthorizedAccessException();
        return (db, user);
    }

    private static async Task RequireCorrectionAccessAsync(ApplicationDbContext db, string userId, int? sourceApplicationId)
    {
        if (sourceApplicationId == null) return;
        var allowed = await db.Applications.AnyAsync(a => a.Id == sourceApplicationId && a.UserId == userId &&
            a.Status == ApplicationWorkflow.AdditionalInformationRequired) &&
            await db.MunicipalMessages.AnyAsync(m => m.ApplicationId == sourceApplicationId &&
                m.MessageType == ApplicationWorkflow.CorrectionRequestMessage && m.ResolvedAtUtc == null);
        if (!allowed) throw new UnauthorizedAccessException();
    }

    private static async Task RequireAllowedDocumentTypeAsync(ApplicationDbContext db, int? sourceApplicationId, string documentType)
    {
        if (sourceApplicationId == null) return;
        var licenceType = await db.Applications.Where(a => a.Id == sourceApplicationId).Select(a => a.LicenceType).SingleAsync();
        if (LicenceApplicationCatalog.Find(licenceType)?.Documents.Any(d => d.DocumentType == documentType) != true &&
            !ApplicationDocumentTypes.IsAdditional(documentType))
            throw new System.ComponentModel.DataAnnotations.ValidationException("Select a valid supporting-document type.");
    }
}

public sealed record ApplicationDraftSummary(int Id, string LicenceType, string BusinessName, int CurrentStep, DateTime LastSavedAtUtc);

public sealed class ApplicationDraftPayload
{
    public string LicenceType { get; set; } = "";
    public string ApplicationType { get; set; } = "";
    public string ApplicantFirstName { get; set; } = "";
    public string ApplicantLastName { get; set; } = "";
    public string ApplicantAddressLine1 { get; set; } = "";
    public string ApplicantAddressLine2 { get; set; } = "";
    public string ApplicantSuburb { get; set; } = "";
    public string ApplicantCity { get; set; } = "";
    public string ApplicantPostalCode { get; set; } = "";
    public string ApplicantTelephone { get; set; } = "";
    public string ApplicantEmail { get; set; } = "";
    public string BusinessName { get; set; } = "";
    public string RegistrationNumber { get; set; } = "";
    public string TaxNumber { get; set; } = "";
    public string BusinessCategory { get; set; } = "";
    public string ContactPerson { get; set; } = "";
    public string BusinessTelephone { get; set; } = "";
    public string BusinessEmail { get; set; } = "";
    public string PlaceOfBusinessAddressLine1 { get; set; } = "";
    public string PlaceOfBusinessAddressLine2 { get; set; } = "";
    public string PlaceOfBusinessSuburb { get; set; } = "";
    public string PlaceOfBusinessCity { get; set; } = "";
    public string PlaceOfBusinessPostalCode { get; set; } = "";
    public bool? PostalSameAsBusiness { get; set; }
    public string PostalAddressLine1 { get; set; } = "";
    public string PostalAddressLine2 { get; set; } = "";
    public string PostalSuburb { get; set; } = "";
    public string PostalCity { get; set; } = "";
    public string PostalPostalCode { get; set; } = "";
    public List<TradingDay> TradingDays { get; set; } = [];
    public bool? OpenOnPublicHolidays { get; set; }
    public Dictionary<string, string> LicenceAnswers { get; set; } = [];
    public bool DeclarationAccepted { get; set; }
    public bool PopiaConsentAccepted { get; set; }
    public string? ProvisionalMunicipality { get; set; }
}
