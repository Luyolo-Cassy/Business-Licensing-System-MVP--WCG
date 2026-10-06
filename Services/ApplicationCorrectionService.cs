using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace BusinessLicensing_Practice.Services;

public sealed class ApplicationCorrectionService(
    IServiceScopeFactory scopes,
    MunicipalRoutingService routing,
    ApplicationPdfService pdfs,
    ApplicationFileService files,
    ProtectedUploadService uploads,
    IApplicationNotificationService notifications,
    ILogger<ApplicationCorrectionService> logger)
{
    public async Task RequestCorrectionsAsync(ClaimsPrincipal principal, int applicationId, string request)
    {
        request = request.Trim();
        if (request.Length == 0) throw new ValidationException("Enter the information or corrections required from the applicant.");
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var official = await OfficialAccess.GetAsync(db, principal) ?? throw new UnauthorizedAccessException();
        var application = await db.Applications.SingleOrDefaultAsync(a => a.Id == applicationId && a.Municipality == official.Municipality)
            ?? throw new UnauthorizedAccessException();
        if (application.Status is not (ApplicationWorkflow.Submitted or ApplicationWorkflow.UnderReview or
            ApplicationWorkflow.DepartmentAssessment or ApplicationWorkflow.FinalDecision))
            throw new ValidationException("Additional information cannot be requested from the application's current state.");
        if (application.Status == ApplicationWorkflow.AdditionalInformationRequired)
            throw new ValidationException("This application already has an open correction request.");

        var previousStatus = application.Status;
        application.Status = ApplicationWorkflow.AdditionalInformationRequired;
        db.MunicipalMessages.Add(new MunicipalMessage
        {
            ApplicationId = application.Id,
            Content = request,
            MessageType = ApplicationWorkflow.CorrectionRequestMessage,
            SenderId = official.Id,
            SenderName = official.FullName,
            CreatedAtUtc = DateTime.UtcNow
        });
        ApplicationAuditService.Add(db, application, official, "MunicipalOfficial",
            ApplicationAuditEventTypes.CorrectionRequested, "Additional information requested from applicant.",
            previousStatus, application.Status,
            new Dictionary<string, object?> { ["request"] = request });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        try { await notifications.NotifyReviewAsync(application.Id, previousStatus, application.Status, request); }
        catch (Exception error) { logger.LogError(error, "Post-commit correction-request notification failed. ApplicationId={ApplicationId}", application.Id); }
    }

    public async Task<(ApplicationDraft Draft, MunicipalMessage Request)> LoadOrCreateDraftAsync(ClaimsPrincipal principal, int applicationId)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await RequireOwnerAsync(scope.ServiceProvider, principal);
        var application = await RequireEditableApplicationAsync(db, user.Id, applicationId);
        var request = await OpenRequestAsync(db, applicationId) ?? throw new ValidationException("This correction request is no longer open.");
        var draft = await db.ApplicationDrafts.Include(d => d.Documents)
            .SingleOrDefaultAsync(d => d.SourceApplicationId == applicationId && d.UserId == user.Id);
        if (draft != null) return (draft, request);

        var details = application.Details ?? throw new ValidationException("This legacy application cannot be edited online. Please contact the responsible municipality for assistance.");
        var municipality = await db.Municipalities.AsNoTracking().SingleOrDefaultAsync(m => m.Name == application.Municipality);
        var payload = new ApplicationDraftPayload
        {
            LicenceType = application.LicenceType,
            ApplicationType = details.ApplicationType ?? "",
            ApplicantFirstName = details.ApplicantFirstName ?? "",
            ApplicantLastName = details.ApplicantLastName ?? "",
            ApplicantAddressLine1 = details.ApplicantAddressLine1 ?? "",
            ApplicantAddressLine2 = details.ApplicantAddressLine2 ?? "",
            ApplicantSuburb = details.ApplicantSuburb ?? "",
            ApplicantCity = details.ApplicantCity ?? "",
            ApplicantPostalCode = details.ApplicantPostalCode ?? "",
            ApplicantTelephone = details.ApplicantTelephone ?? "",
            ApplicantEmail = details.ApplicantEmail ?? "",
            BusinessName = application.BusinessName,
            RegistrationNumber = application.RegistrationNumber,
            TaxNumber = application.TaxNumber,
            BusinessCategory = application.BusinessCategory,
            ContactPerson = details.ContactPerson ?? "",
            BusinessTelephone = details.BusinessTelephone ?? "",
            BusinessEmail = details.BusinessEmail ?? "",
            PlaceOfBusinessAddressLine1 = application.PlaceOfBusinessAddressLine1,
            PlaceOfBusinessAddressLine2 = application.PlaceOfBusinessAddressLine2,
            PlaceOfBusinessSuburb = application.PlaceOfBusinessSuburb,
            PlaceOfBusinessCity = application.PlaceOfBusinessCity,
            PlaceOfBusinessPostalCode = application.PlaceOfBusinessPostalCode,
            PostalSameAsBusiness = details.PostalAddressSameAsBusiness,
            PostalAddressLine1 = details.PostalAddressLine1 ?? "",
            PostalAddressLine2 = details.PostalAddressLine2 ?? "",
            PostalSuburb = details.PostalSuburb ?? "",
            PostalCity = details.PostalCity ?? "",
            PostalPostalCode = details.PostalPostalCode ?? "",
            TradingDays = ParseTradingDays(details.TradingHours),
            OpenOnPublicHolidays = details.OpenOnPublicHolidays,
            LicenceAnswers = ApplicationPdfService.DeserializeAnswers(details.LicenceSpecificDetailsJson),
            DeclarationAccepted = false,
            PopiaConsentAccepted = false,
            ProvisionalMunicipality = municipality?.RoutingName
        };
        var now = DateTime.UtcNow;
        draft = new ApplicationDraft
        {
            UserId = user.Id,
            SourceApplicationId = application.Id,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            SchemaVersion = ApplicationDraftService.CurrentSchemaVersion,
            CurrentStep = 1,
            CreatedAtUtc = now,
            LastSavedAtUtc = now,
            Documents = application.Documents.Select(document => new ApplicationDraftDocument
            {
                DocumentType = document.DocumentType,
                FileName = document.FileName,
                FilePath = document.FilePath,
                AiValidationStatus = AiDocumentValidationStatus.Disabled.ToString()
            }).ToList()
        };
        db.ApplicationDrafts.Add(draft);
        await db.SaveChangesAsync();
        return (draft, request);
    }

    public async Task<ApplicationDraft> SaveDraftAsync(ClaimsPrincipal principal, int applicationId, ApplicationDraftPayload payload, int currentStep)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var user = await RequireOwnerAsync(scope.ServiceProvider, principal);
        _ = await RequireEditableApplicationAsync(db, user.Id, applicationId);
        if (await OpenRequestAsync(db, applicationId) == null) throw new ValidationException("This correction request is no longer open.");
        var draft = await db.ApplicationDrafts.SingleOrDefaultAsync(d => d.SourceApplicationId == applicationId && d.UserId == user.Id)
            ?? throw new ValidationException("The correction draft is no longer available.");
        if (payload.LicenceType != (await db.Applications.Where(a => a.Id == applicationId).Select(a => a.LicenceType).SingleAsync()))
            throw new ValidationException("The license type cannot be changed while correcting an application.");
        draft.PayloadJson = JsonSerializer.Serialize(payload, JsonOptions);
        draft.CurrentStep = Math.Clamp(currentStep, 1, 7);
        draft.LastSavedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return draft;
    }

    public async Task ResubmitAsync(ClaimsPrincipal principal, int applicationId)
    {
        await using var scope = scopes.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ApplicationDbContext>();
        var user = await RequireOwnerAsync(services, principal);
        await using var transaction = await db.Database.BeginTransactionAsync();
        var application = await RequireEditableApplicationAsync(db, user.Id, applicationId);
        var requests = await db.MunicipalMessages.Where(m => m.ApplicationId == applicationId &&
            m.MessageType == ApplicationWorkflow.CorrectionRequestMessage && m.ResolvedAtUtc == null).ToListAsync();
        if (requests.Count == 0) throw new ValidationException("This correction request is no longer open.");
        var draft = await db.ApplicationDrafts.Include(d => d.Documents)
            .SingleOrDefaultAsync(d => d.SourceApplicationId == applicationId && d.UserId == user.Id)
            ?? throw new ValidationException("The correction draft is no longer available.");
        var payload = ApplicationDraftService.Deserialize(draft.PayloadJson);
        var aiValidationEnabled = await db.AiSettings.AsNoTracking().Where(s => s.Id == AiSettings.SingletonId)
            .Select(s => s.DocumentValidationEnabled).SingleAsync();
        Validate(payload, draft.Documents, application.LicenceType, aiValidationEnabled);

        var address = new TradingAddress(payload.PlaceOfBusinessAddressLine1.Trim(), payload.PlaceOfBusinessAddressLine2.Trim(),
            payload.PlaceOfBusinessSuburb.Trim(), payload.PlaceOfBusinessCity.Trim(), payload.PlaceOfBusinessPostalCode.Trim());
        var route = await routing.ResolveAsync(address);
        if (!route.Success) throw new ValidationException(route.Message);
        var routedMunicipality = await MunicipalityManagementService.RequireActiveRoutingAsync(db, route.Municipality!);
        if (!string.Equals(routedMunicipality.Name, application.Municipality, StringComparison.Ordinal))
            throw new ValidationException("The corrected trading address belongs to a different municipality. Please contact the responsible municipality for assistance before resubmitting.");

        var oldDocumentPaths = application.Documents.Select(d => d.FilePath).Distinct().ToList();
        var oldPdfPath = application.ApplicationFormFilePath;
        Apply(application, payload, address);
        application.Documents.Clear();
        foreach (var document in draft.Documents)
            application.Documents.Add(new ApplicationDocument { DocumentType = document.DocumentType, FileName = document.FileName, FilePath = document.FilePath });
        var now = DateTime.UtcNow;
        application.RevisionNumber = Math.Max(1, application.RevisionNumber) + 1;
        application.LastResubmittedAtUtc = now;
        application.Status = ApplicationWorkflow.Submitted;
        application.DecisionDateUtc = null;
        application.DecisionReason = null;
        foreach (var request in requests) request.ResolvedAtUtc = now;
        ApplicationAuditService.Add(db, application, user, "BusinessOwner",
            ApplicationAuditEventTypes.ApplicationResubmitted,
            $"Applicant resubmitted application as revision {application.RevisionNumber}.",
            ApplicationWorkflow.AdditionalInformationRequired, application.Status,
            new Dictionary<string, object?> { ["revisionNumber"] = application.RevisionNumber });

        string? newPdfPath = null;
        try
        {
            var fileName = $"{application.ApplicationNumber}-Official-Application-r{application.RevisionNumber}.pdf";
            newPdfPath = await files.SaveGeneratedPdfAsync(application.Id, fileName, pdfs.Generate(application));
            application.ApplicationFormFileName = fileName;
            application.ApplicationFormFilePath = newPdfPath;
            db.ApplicationDrafts.Remove(draft);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            if (newPdfPath != null) try { await files.DeleteGeneratedPdfAsync(newPdfPath); } catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Unable to clean up an uncommitted generated PDF. ApplicationId={ApplicationId}", application.Id); }
            throw;
        }

        foreach (var path in oldDocumentPaths)
            try { await uploads.DeleteIfUnreferencedAsync(db, path); }
            catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Unable to clean up a superseded application document. ApplicationId={ApplicationId}", application.Id); }
        if (!string.Equals(oldPdfPath, application.ApplicationFormFilePath, StringComparison.Ordinal))
            try { await files.DeleteGeneratedPdfAsync(oldPdfPath); }
            catch (Exception cleanupError) { logger.LogWarning(cleanupError, "Unable to clean up a superseded generated PDF. ApplicationId={ApplicationId}", application.Id); }
        try { await notifications.NotifyResubmissionAsync(application.Id); }
        catch (Exception error) { logger.LogError(error, "Post-commit resubmission notification failed. ApplicationId={ApplicationId}", application.Id); }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task<ApplicationUser> RequireOwnerAsync(IServiceProvider services, ClaimsPrincipal principal)
    {
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.GetUserAsync(principal);
        var stamp = principal.FindFirstValue("AspNet.Identity.SecurityStamp");
        if (principal.Identity?.IsAuthenticated != true || user == null || !await users.IsInRoleAsync(user, "BusinessOwner") ||
            await users.IsLockedOutAsync(user) || (stamp != null && stamp != user.SecurityStamp)) throw new UnauthorizedAccessException();
        return user;
    }

    private static async Task<Application> RequireEditableApplicationAsync(ApplicationDbContext db, string userId, int id) =>
        await db.Applications.Include(a => a.Details).Include(a => a.Documents)
            .SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId && a.Status == ApplicationWorkflow.AdditionalInformationRequired)
        ?? throw new UnauthorizedAccessException();

    private static Task<MunicipalMessage?> OpenRequestAsync(ApplicationDbContext db, int applicationId) =>
        db.MunicipalMessages.AsNoTracking().Where(m => m.ApplicationId == applicationId &&
            m.MessageType == ApplicationWorkflow.CorrectionRequestMessage && m.ResolvedAtUtc == null)
            .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id).FirstOrDefaultAsync();

    private static List<TradingDay> ParseTradingDays(string? json)
    {
        try { return JsonSerializer.Deserialize<List<TradingDay>>(json ?? "") ?? []; }
        catch (JsonException) { return []; }
    }

    private static void Validate(ApplicationDraftPayload p, IReadOnlyCollection<ApplicationDraftDocument> documents, string licenceType, bool aiValidationEnabled)
    {
        var licence = LicenceApplicationCatalog.Find(licenceType) ?? throw new ValidationException("The license type is no longer available.");
        if (p.LicenceType != licenceType) throw new ValidationException("The license type cannot be changed while correcting an application.");
        var error = ApplicationEntry.ValidateName(p.ApplicantFirstName, p.ApplicantLastName)
            ?? (!ApplicationEntry.ValidApplicationType(p.ApplicationType) ? "Select a valid application type." : null)
            ?? (Blank(p.ApplicantAddressLine1, p.ApplicantSuburb, p.ApplicantCity, p.ApplicantPostalCode, p.ApplicantTelephone, p.ApplicantEmail) ? "Complete all required applicant details." : null)
            ?? (!ApplicationEntry.ValidPostalCode(p.ApplicantPostalCode) ? "Enter a valid applicant postal code." : null)
            ?? (!ApplicationEntry.ValidTelephone(p.ApplicantTelephone) ? "Enter a valid applicant telephone number." : null)
            ?? (!ApplicationEntry.ValidEmail(p.ApplicantEmail) ? "Enter a valid applicant email address." : null)
            ?? (p.ApplicantFirstName.Trim().Length > 100 || p.ApplicantLastName.Trim().Length > 100 ||
                p.ApplicantAddressLine1.Trim().Length > 150 || p.ApplicantAddressLine2.Trim().Length > 150 ||
                p.ApplicantSuburb.Trim().Length > 100 || p.ApplicantCity.Trim().Length > 100 ? "One or more applicant fields exceed the allowed length." : null)
            ?? (Blank(p.BusinessName, p.RegistrationNumber, p.TaxNumber, p.BusinessCategory, p.ContactPerson, p.BusinessTelephone, p.BusinessEmail,
                p.PlaceOfBusinessAddressLine1, p.PlaceOfBusinessSuburb, p.PlaceOfBusinessCity, p.PlaceOfBusinessPostalCode) ? "Complete all required business details." : null)
            ?? (!ApplicationEntry.ValidRegistration(p.RegistrationNumber) ? "Enter a valid company registration number." : null)
            ?? (!ApplicationEntry.ValidTaxNumber(p.TaxNumber) ? "Enter a valid tax number." : null)
            ?? (!ApplicationEntry.ValidTelephone(p.BusinessTelephone) ? "Enter a valid business telephone number." : null)
            ?? (!ApplicationEntry.ValidEmail(p.BusinessEmail) ? "Enter a valid business email address." : null)
            ?? (!ApplicationEntry.ValidPostalCode(p.PlaceOfBusinessPostalCode) ? "Enter a valid trading-address postal code." : null)
            ?? ApplicationEntry.ValidateTradingHours(p.TradingDays)
            ?? ApplicationEntry.ValidatePublicHolidayTrading(p.OpenOnPublicHolidays)
            ?? ApplicationEntry.ValidatePostalAddress(p.PostalSameAsBusiness, p.PostalAddressLine1, p.PostalSuburb, p.PostalCity, p.PostalPostalCode)
            ?? (p.PostalSameAsBusiness == false && !ApplicationEntry.ValidPostalCode(p.PostalPostalCode) ? "Enter a valid postal-address postal code." : null)
            ?? (p.PostalSameAsBusiness == false && (p.PostalAddressLine1.Trim().Length > 150 || p.PostalAddressLine2.Trim().Length > 150 ||
                p.PostalSuburb.Trim().Length > 100 || p.PostalCity.Trim().Length > 100 || p.PostalPostalCode.Trim().Length > 12)
                ? "One or more postal-address fields exceed the allowed length." : null)
            ?? (licence.Questions.Any(q => !ApplicationEntry.ValidLicenceAnswer(p.LicenceAnswers.GetValueOrDefault(q.Key), q.Required, q.Options)) ? "Complete all required license-specific questions." : null)
            ?? (licence.Documents.Any(r => r.Required && !documents.Any(d => d.DocumentType == r.DocumentType)) ? "Upload every required supporting document." : null)
            ?? (documents.Any(d => !licence.Documents.Any(r => r.DocumentType == d.DocumentType) &&
                !ApplicationDocumentTypes.IsAdditional(d.DocumentType)) ? "The correction draft contains an unsupported document type." : null)
            ?? (aiValidationEnabled && documents.FirstOrDefault(d => d.DocumentType == "Owner ID Document") is { AiValidationStatus: null or nameof(AiDocumentValidationStatus.NoMatch) }
                ? "Upload a valid Owner ID or Passport document." : null)
            ?? (!p.DeclarationAccepted || !p.PopiaConsentAccepted ? "Accept the declaration and POPIA consent before resubmitting." : null);
        if (error != null) throw new ValidationException(error);
    }

    private static bool Blank(params string[] values) => values.Any(string.IsNullOrWhiteSpace);

    private static void Apply(Application a, ApplicationDraftPayload p, TradingAddress address)
    {
        a.BusinessName = p.BusinessName.Trim(); a.RegistrationNumber = p.RegistrationNumber.Trim(); a.TaxNumber = p.TaxNumber.Trim();
        a.BusinessCategory = p.BusinessCategory.Trim(); a.PlaceOfBusinessAddress = address.Formatted;
        a.PlaceOfBusinessAddressLine1 = address.Line1; a.PlaceOfBusinessAddressLine2 = address.Line2;
        a.PlaceOfBusinessSuburb = address.Suburb; a.PlaceOfBusinessCity = address.City; a.PlaceOfBusinessPostalCode = address.PostalCode;
        a.PopiaConsentAccepted = true;
        var d = a.Details ??= new ApplicationDetails { ApplicationId = a.Id };
        d.ApplicationType = p.ApplicationType; d.ApplicantFirstName = p.ApplicantFirstName.Trim(); d.ApplicantLastName = p.ApplicantLastName.Trim();
        d.ApplicantAddressLine1 = p.ApplicantAddressLine1.Trim(); d.ApplicantAddressLine2 = p.ApplicantAddressLine2.Trim();
        d.ApplicantSuburb = p.ApplicantSuburb.Trim(); d.ApplicantCity = p.ApplicantCity.Trim(); d.ApplicantPostalCode = p.ApplicantPostalCode.Trim();
        d.ApplicantTelephone = p.ApplicantTelephone.Trim(); d.ApplicantEmail = p.ApplicantEmail.Trim();
        d.PostalAddressSameAsBusiness = p.PostalSameAsBusiness; d.PostalAddressLine1 = p.PostalSameAsBusiness == false ? p.PostalAddressLine1.Trim() : null;
        d.PostalAddressLine2 = p.PostalSameAsBusiness == false ? p.PostalAddressLine2.Trim() : null; d.PostalSuburb = p.PostalSameAsBusiness == false ? p.PostalSuburb.Trim() : null;
        d.PostalCity = p.PostalSameAsBusiness == false ? p.PostalCity.Trim() : null; d.PostalPostalCode = p.PostalSameAsBusiness == false ? p.PostalPostalCode.Trim() : null;
        d.ContactPerson = p.ContactPerson.Trim(); d.BusinessTelephone = p.BusinessTelephone.Trim(); d.BusinessEmail = p.BusinessEmail.Trim();
        d.TradingHours = ApplicationEntry.SerializeTradingHours(p.TradingDays); d.OpenOnPublicHolidays = p.OpenOnPublicHolidays;
        d.LicenceSpecificDetailsJson = JsonSerializer.Serialize(p.LicenceAnswers); d.DeclarationAccepted = true; d.DeclarationAcceptedAt = DateTime.UtcNow;
    }
}
