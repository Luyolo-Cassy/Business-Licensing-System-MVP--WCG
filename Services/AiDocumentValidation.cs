using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BusinessLicensing_Practice.Services;

public enum AiDocumentValidationStatus { Disabled, Match, NoMatch, UnableToVerify, Unavailable }
public enum AiDocumentKind { OwnerIdentity, CertificateOfIncorporation, ProofOfAddress }
public sealed record AiDocumentExpectedValues(string ApplicantFirstName = "", string ApplicantLastName = "", string BusinessName = "", string RegistrationNumber = "", string AddressLine1 = "", string AddressLine2 = "", string Suburb = "", string City = "", string PostalCode = "");
public sealed record AiDocumentValidationResult(AiDocumentValidationStatus Status, string? ReasonCode = null, string? StoredValue = null);
public sealed record AiDocumentExtraction(string DocumentType, string ExtractionStatus, string? FirstName = null, string? LastName = null, string? BusinessName = null, string? RegistrationNumber = null, string? AddressLine1 = null, string? AddressLine2 = null, string? Suburb = null, string? City = null, string? PostalCode = null, string? AddressContext = null);

public interface IAiDocumentValidationService
{
    Task<AiDocumentExtraction?> ExtractAsync(AiDocumentKind documentKind, string fileName, byte[] fileBytes, CancellationToken cancellationToken = default);
}

// Coordinates extraction, deterministic comparison, and versioned persistence
// of document-validation outcomes.
public sealed class AiDocumentValidationPolicy(IAiDocumentValidationService validator)
{
    private const int PayloadVersion = 1;
    private static string ValidatorVersion(AiDocumentKind kind) => kind == AiDocumentKind.OwnerIdentity ? "owner-document-type-v2" : "document-validation-v1";

    public async Task<AiDocumentValidationResult> EvaluateAsync(bool enabled, string documentType, string fileName, byte[] fileBytes, AiDocumentExpectedValues expected, CancellationToken cancellationToken = default)
    {
        if (!TryGetKind(documentType, out var kind) || !enabled)
            return new(AiDocumentValidationStatus.Disabled, StoredValue: TryGetKind(documentType, out kind) ? Serialize(AiDocumentValidationStatus.Disabled, kind, expected, fileBytes) : null);
        var extraction = await validator.ExtractAsync(kind, fileName, fileBytes, cancellationToken);
        var result = extraction == null ? new AiDocumentValidationResult(AiDocumentValidationStatus.UnableToVerify, "TechnicalFailure") : AiDocumentComparisons.Compare(kind, extraction, expected);
        return result with { StoredValue = Serialize(result.Status, kind, expected, fileBytes) };
    }

    // Reuse a stored result only when its policy version, validator, document kind,
    // and application-data fingerprint still match.
    public bool IsCurrent(string? stored, string documentType, AiDocumentExpectedValues expected, out AiDocumentValidationStatus status)
    {
        status = default;
        if (!TryGetKind(documentType, out var kind) || string.IsNullOrWhiteSpace(stored)) return false;
        if (Enum.TryParse(stored, out AiDocumentValidationStatus legacy)) { status = legacy == AiDocumentValidationStatus.Unavailable ? AiDocumentValidationStatus.UnableToVerify : legacy; return false; }
        try
        {
            var p = JsonSerializer.Deserialize<ValidationPayload>(stored);
            if (p == null || p.Version != PayloadVersion || p.Validator != ValidatorVersion(kind) || p.Kind != kind.ToString() || p.InputFingerprint != InputFingerprint(kind, expected) || !Enum.TryParse(p.Outcome, out status)) return false;
            if (status == AiDocumentValidationStatus.Unavailable) status = AiDocumentValidationStatus.UnableToVerify;
            return status is AiDocumentValidationStatus.Match or AiDocumentValidationStatus.NoMatch or AiDocumentValidationStatus.UnableToVerify or AiDocumentValidationStatus.Disabled;
        }
        catch (JsonException) { return false; }
    }

    public static bool BlocksProgress(AiDocumentValidationStatus status) => status == AiDocumentValidationStatus.NoMatch;
    public static bool TryGetKind(string type, out AiDocumentKind kind)
    {
        kind = type switch { "Owner ID Document" => AiDocumentKind.OwnerIdentity, "Certificate of Incorporation" => AiDocumentKind.CertificateOfIncorporation, "Proof of Address" => AiDocumentKind.ProofOfAddress, _ => default };
        return type is "Owner ID Document" or "Certificate of Incorporation" or "Proof of Address";
    }
    // Persist comparison metadata and fingerprints rather than extracted document content.
    private static string Serialize(AiDocumentValidationStatus outcome, AiDocumentKind kind, AiDocumentExpectedValues expected, byte[] bytes) => JsonSerializer.Serialize(new ValidationPayload(PayloadVersion, outcome.ToString(), kind.ToString(), ValidatorVersion(kind), InputFingerprint(kind, expected), Convert.ToHexString(SHA256.HashData(bytes))));
    private static string InputFingerprint(AiDocumentKind kind, AiDocumentExpectedValues v)
    {
        var text = kind switch { AiDocumentKind.OwnerIdentity => "supported-identity-document", AiDocumentKind.CertificateOfIncorporation => $"{Normalize(v.BusinessName)}|{LettersAndDigits(v.RegistrationNumber)}", _ => $"{Normalize(v.AddressLine1)}|{Normalize(v.AddressLine2)}|{Normalize(v.Suburb)}|{Normalize(v.City)}|{LettersAndDigits(v.PostalCode)}" };
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }
    internal static string Normalize(string? value) => Regex.Replace((value ?? "").Normalize(NormalizationForm.FormKC).ToUpperInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
    internal static string LettersAndDigits(string? value) => new((value ?? "").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
    private sealed record ValidationPayload(int Version, string Outcome, string Kind, string Validator, string InputFingerprint, string DocumentFingerprint);
}

// Applies deterministic, document-specific rules to the structured fields
// returned by the AI provider.
public static class AiDocumentComparisons
{
    public static AiDocumentValidationResult Compare(AiDocumentKind kind, AiDocumentExtraction x, AiDocumentExpectedValues e)
    {
        return kind switch { AiDocumentKind.OwnerIdentity => Owner(x, e), AiDocumentKind.CertificateOfIncorporation => Certificate(x, e), _ => Address(x, e) };
    }
    private static AiDocumentValidationResult Owner(AiDocumentExtraction x, AiDocumentExpectedValues e)
    {
        if (x.DocumentType == "Other") return No("DocumentTypeMismatch");
        if (!string.Equals(x.ExtractionStatus, "Extracted", StringComparison.OrdinalIgnoreCase)) return Unable("DocumentTypeAmbiguous");
        return x.DocumentType is "South African Smart ID" or "South African ID Book" or "Passport" ? Match() : Unable("DocumentTypeAmbiguous");
    }
    private static AiDocumentValidationResult Certificate(AiDocumentExtraction x, AiDocumentExpectedValues e)
    {
        if (string.Equals(x.DocumentType, "Other", StringComparison.OrdinalIgnoreCase)) return No("DocumentTypeMismatch");
        if (!string.Equals(x.DocumentType, "Certificate of Incorporation", StringComparison.OrdinalIgnoreCase) || !string.Equals(x.ExtractionStatus, "Extracted", StringComparison.OrdinalIgnoreCase)) return Unable("DocumentTypeAmbiguous");
        if (string.IsNullOrWhiteSpace(x.BusinessName) || string.IsNullOrWhiteSpace(x.RegistrationNumber)) return Unable("CertificateFieldsUnreadable");
        if (Company(x.BusinessName) != Company(e.BusinessName)) return No("BusinessNameMismatch");
        return AiDocumentValidationPolicy.LettersAndDigits(x.RegistrationNumber) == AiDocumentValidationPolicy.LettersAndDigits(e.RegistrationNumber) ? Match() : No("RegistrationNumberMismatch");
    }
    private static AiDocumentValidationResult Address(AiDocumentExtraction x, AiDocumentExpectedValues e)
    {
        if (string.Equals(x.DocumentType, "Other", StringComparison.OrdinalIgnoreCase)) return No("DocumentTypeMismatch");
        if (!string.Equals(x.DocumentType, "Proof of Address", StringComparison.OrdinalIgnoreCase) || !string.Equals(x.ExtractionStatus, "Extracted", StringComparison.OrdinalIgnoreCase)) return Unable("DocumentTypeAmbiguous");
        if (!string.Equals(x.AddressContext, "RelevantPhysicalAddress", StringComparison.OrdinalIgnoreCase)) return Unable("AddressAmbiguous");
        var actual = Street($"{x.AddressLine1} {x.AddressLine2}"); var expected = Street($"{e.AddressLine1} {e.AddressLine2}");
        if (actual.Length == 0 || string.IsNullOrWhiteSpace(x.City)) return Unable("AddressIncomplete");
        if (actual != expected || AiDocumentValidationPolicy.Normalize(x.City) != AiDocumentValidationPolicy.Normalize(e.City)) return No("TradingAddressMismatch");
        return !string.IsNullOrWhiteSpace(x.PostalCode) && AiDocumentValidationPolicy.LettersAndDigits(x.PostalCode) != AiDocumentValidationPolicy.LettersAndDigits(e.PostalCode) ? No("TradingAddressMismatch") : Match();
    }
    private static string Company(string? value) { var n = AiDocumentValidationPolicy.Normalize(value); n = Regex.Replace(n, @"\bPROPRIETARY LIMITED\b|\bPTY LTD\b", "PTY LTD"); return n.Replace(" AND ", " ", StringComparison.Ordinal); }
    private static string Street(string? value) { var n = AiDocumentValidationPolicy.Normalize(value); return Regex.Replace(n, @"\bSTREET\b", "ST").Replace(" ROAD", " RD", StringComparison.Ordinal).Replace(" AVENUE", " AVE", StringComparison.Ordinal).Replace(" DRIVE", " DR", StringComparison.Ordinal).Replace(" LANE", " LN", StringComparison.Ordinal).Replace(" BOULEVARD", " BLVD", StringComparison.Ordinal); }
    private static AiDocumentValidationResult Match() => new(AiDocumentValidationStatus.Match);
    private static AiDocumentValidationResult No(string reason) => new(AiDocumentValidationStatus.NoMatch, reason);
    private static AiDocumentValidationResult Unable(string reason) => new(AiDocumentValidationStatus.UnableToVerify, reason);
}

// Requests structured extraction from Gemini. Provider, quota, transport, and
// response-format failures become an inconclusive result for manual review.
public sealed class GeminiDocumentValidationService(HttpClient client, IConfiguration configuration, ILogger<GeminiDocumentValidationService> logger) : IAiDocumentValidationService
{
    private const string Model = "gemini-3.5-flash-lite";
    private const string Endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent";
    // Extract fields only; deterministic policy code decides whether they match
    // the application and avoids logging document content.
    public async Task<AiDocumentExtraction?> ExtractAsync(AiDocumentKind kind, string fileName, byte[] bytes, CancellationToken token = default)
    {
        var key = configuration["Gemini:ApiKey"]; if (string.IsNullOrWhiteSpace(key)) { Log("ConfigurationMissing", null, 0); return null; }
        var mime = Path.GetExtension(fileName).ToLowerInvariant() switch { ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", _ => null }; if (mime == null || bytes.Length == 0) return null;
        var timer = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint); request.Headers.Add("x-goog-api-key", key);
            request.Content = JsonContent.Create(new { contents = new[] { new { parts = new object[] { new { inlineData = new { mimeType = mime, data = Convert.ToBase64String(bytes) } }, new { text = Prompt(kind) } } } }, generationConfig = new { maxOutputTokens = 350, responseMimeType = "application/json", responseJsonSchema = Schema(kind) } });
            using var response = await client.SendAsync(request, token);
            if (!response.IsSuccessStatusCode) { Log(response.StatusCode == HttpStatusCode.TooManyRequests ? "RateOrQuotaLimited" : "HttpError", response.StatusCode, timer.ElapsedMilliseconds); return null; }
            await using var stream = await response.Content.ReadAsStreamAsync(token); using var envelope = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            var text = envelope.RootElement.GetProperty("candidates")[0].GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString();
            var result = JsonSerializer.Deserialize<AiDocumentExtraction>(text ?? "", new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); Log(result == null ? "InvalidResponse" : "None", response.StatusCode, timer.ElapsedMilliseconds, result == null ? null : "Extracted"); return result;
        }
        catch (TaskCanceledException) { Log("Timeout", null, timer.ElapsedMilliseconds); return null; }
        catch (HttpRequestException) { Log("NetworkFailure", null, timer.ElapsedMilliseconds); return null; }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException) { Log("InvalidResponse", null, timer.ElapsedMilliseconds); return null; }
        void Log(string category, HttpStatusCode? status, long ms, string? outcome = null) => logger.LogInformation("Gemini document validation completed. DocumentCategory={DocumentCategory} Outcome={Outcome} DurationMs={DurationMs} HttpStatus={HttpStatus} ErrorCategory={ErrorCategory}", kind, outcome ?? "UnableToVerify", ms, status.HasValue ? (int)status.Value : null, category);
    }
    private static string Prompt(AiDocumentKind kind) => kind switch
    {
        AiDocumentKind.OwnerIdentity => "Classify only the document type as South African Smart ID, South African ID Book, Passport, Other, or Unknown. Do not extract names, identity numbers, photographs, or other personal information. extractionStatus is Extracted when the document type is clear, otherwise Unclear.",
        AiDocumentKind.CertificateOfIncorporation => "Classify as Certificate of Incorporation, Other, or Unknown without relying on one template. Extract only registered businessName and registrationNumber. extractionStatus is Extracted only when both are readable, otherwise Unclear.",
        _ => "Classify as Proof of Address, Other, or Unknown. Extract the relevant customer, tenant, or business physical address, never an issuer office, bank branch, or unrelated mailing address. addressContext is RelevantPhysicalAddress only when confident, otherwise Ambiguous or IssuerAddress. extractionStatus is Extracted only for a usable readable address, otherwise Unclear."
    };
    private static object Schema(AiDocumentKind kind)
    {
        object S() => new { type = "string" };
        object properties = kind switch { AiDocumentKind.OwnerIdentity => new { documentType = S(), extractionStatus = S() }, AiDocumentKind.CertificateOfIncorporation => new { documentType = S(), extractionStatus = S(), businessName = S(), registrationNumber = S() }, _ => new { documentType = S(), extractionStatus = S(), addressLine1 = S(), addressLine2 = S(), suburb = S(), city = S(), postalCode = S(), addressContext = S() } };
        var required = kind switch { AiDocumentKind.OwnerIdentity => new[] { "documentType", "extractionStatus" }, AiDocumentKind.CertificateOfIncorporation => new[] { "documentType", "extractionStatus", "businessName", "registrationNumber" }, _ => new[] { "documentType", "extractionStatus", "addressLine1", "city", "addressContext" } };
        return new { type = "object", properties, required, additionalProperties = false };
    }
}
