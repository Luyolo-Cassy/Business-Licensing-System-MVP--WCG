using System.ComponentModel.DataAnnotations;
using BusinessLicensing_Practice.Data;
using BusinessLicensing_Practice.Models;

namespace BusinessLicensing_Practice.Services;

public sealed class AiDocumentValidationWorkflow(AiSettingsService settings, AiDocumentValidationPolicy policy,
    ProtectedUploadService uploads)
{
    public static AiDocumentExpectedValues Expected(ApplicationDraftPayload p) => new(
        p.ApplicantFirstName, p.ApplicantLastName, p.BusinessName, p.RegistrationNumber,
        p.PlaceOfBusinessAddressLine1, p.PlaceOfBusinessAddressLine2, p.PlaceOfBusinessSuburb,
        p.PlaceOfBusinessCity, p.PlaceOfBusinessPostalCode);

    public async Task EnforceAsync(ApplicationDbContext db, ApplicationDraftPayload payload,
        IReadOnlyCollection<ApplicationDraftDocument> documents, CancellationToken cancellationToken = default)
    {
        var configured = await settings.GetDocumentValidationSettingsAsync();
        var expected = Expected(payload);
        string? mismatch = null;
        foreach (var document in documents.Where(x => AiDocumentValidationPolicy.TryGetKind(x.DocumentType, out _)))
        {
            if (!configured.EnabledFor(document.DocumentType)) continue;
            if (!policy.IsCurrent(document.AiValidationStatus, document.DocumentType, expected, out var status) || status == AiDocumentValidationStatus.Disabled)
            {
                var bytes = await uploads.ReadAsync(document.FilePath, cancellationToken);
                var result = bytes == null
                    ? new AiDocumentValidationResult(AiDocumentValidationStatus.UnableToVerify, "StoredDocumentUnavailable")
                    : await policy.EvaluateAsync(true, document.DocumentType, document.FileName, bytes, expected, cancellationToken);
                status = result.Status;
                if (result.StoredValue != null) document.AiValidationStatus = result.StoredValue;
            }
            if (status == AiDocumentValidationStatus.NoMatch) mismatch ??= MismatchMessage(document.DocumentType);
        }
        await db.SaveChangesAsync(cancellationToken);
        if (mismatch != null) throw new ValidationException(mismatch);
    }

    public static string MismatchMessage(string documentType) => documentType switch
    {
        "Owner ID Document" => "The Owner ID document does not match the applicant name or supported identity-document type. Review the applicant details or replace the document.",
        "Certificate of Incorporation" => "The Certificate of Incorporation does not match the business name or registration number. Review the business details or replace the document.",
        "Proof of Address" => "The Proof of Address does not match the Place of Business / Trading Address. Review the trading address or replace the document.",
        _ => "A supporting document does not match the application information."
    };
}
