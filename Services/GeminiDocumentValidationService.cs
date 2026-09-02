using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BusinessLicensing_Practice.Models;

namespace BusinessLicensing_Practice.Services;

public sealed class GeminiDocumentValidationService
{
    private const string Model = "gemini-3.7-flash";
    private const string Endpoint =
        "https://generativelanguage.googleapis.com/v1beta/models/" + Model + ":generateContent";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public GeminiDocumentValidationService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task<DocumentValidationResult> ValidateDocumentAsync(
        byte[] fileBytes,
        string mimeType,
        string documentType,
        string requirementDescription,
        string licenceType,
        string businessName,
        string registrationNumber,
        CancellationToken cancellationToken = default)
    {
        var apiKey = _configuration["Gemini:ApiKey"];

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Gemini API key is not configured. Set the Gemini:ApiKey configuration value.");
        }

        var prompt = BuildPrompt(
            documentType,
            requirementDescription,
            licenceType,
            businessName,
            registrationNumber);

        var requestBody = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { text = prompt },
                        new
                        {
                            inlineData = new
                            {
                                mimeType,
                                data = Convert.ToBase64String(fileBytes)
                            }
                        }
                    }
                }
            },
            generationConfig = new
            {
                response_mime_type = "application/json",
                response_schema = new
                {
                    type = "object",
                    properties = new
                    {
                        status = new { type = "string", @enum = new[] { "PASS", "REVIEW", "FAIL" } },
                        documentType = new { type = "string" },
                        summary = new { type = "string" },
                        expiryDate = new { type = "string" },
                        confidence = new { type = "number" },
                        issues = new
                        {
                            type = "array",
                            items = new
                            {
                                type = "object",
                                properties = new
                                {
                                    type = new { type = "string" },
                                    message = new { type = "string" }
                                },
                                required = new[] { "type", "message" }
                            }
                        }
                    },
                    required = new[]
                    {
                        "status", "documentType", "summary",
                        "expiryDate", "confidence", "issues"
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Headers.Add("x-goog-api-key", apiKey);
        request.Content = new StringContent(
            JsonSerializer.Serialize(requestBody),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Gemini returned {(int)response.StatusCode}: {ExtractApiError(responseText)}");
        }

        using var json = JsonDocument.Parse(responseText);

        var modelText = json.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(modelText))
        {
            throw new InvalidOperationException("Gemini returned an empty validation result.");
        }

        var result = JsonSerializer.Deserialize<DocumentValidationResult>(
            modelText,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

        if (result == null)
        {
            throw new InvalidOperationException("Gemini returned an invalid validation result.");
        }

        result.Status = NormalizeStatus(result.Status);

        if (result.Status == "PASS" && result.Issues.Count > 0)
        {
            result.Status = "REVIEW";
        }

        return result;
    }

    private static string BuildPrompt(
        string documentType,
        string requirementDescription,
        string licenceType,
        string businessName,
        string registrationNumber)
    {
        return $"""
You are a document-validation assistant for a South African municipal business licensing system.

Your job is to inspect the uploaded document and determine whether it meets the stated requirement.
Do not invent information. If a value cannot be read confidently, say so and use REVIEW.

APPLICATION CONTEXT
Licence type: {licenceType}
Business name: {businessName}
Registration number: {registrationNumber}

EXPECTED DOCUMENT
Document type: {documentType}
Requirement: {requirementDescription}

VALIDATION RULES
1. Confirm that the uploaded document is actually the expected document type.
2. Check that the document is readable and appears complete.
3. Extract relevant dates. If the document has an expiry/valid-until date, determine whether it is expired as of today.
4. If the document has no expiry date, do not mark it expired merely because no expiry date exists.
5. Where a business name, registration number, address, owner name, certificate number, issue date or other identifying information is visible, compare it with the application context.
6. Flag missing required information, obvious inconsistencies, an expired document, or a wrong document type.
7. Do not claim that a document is legally authentic or genuine. You can only assess what is visible in the supplied document.
8. Use PASS only when the document appears to satisfy the supplied requirement.
9. Use FAIL when there is a clear, material problem such as an expired required certificate or wrong document.
10. Use REVIEW when the document cannot be confidently assessed because information is unclear, unreadable, missing, or ambiguous.
11. Keep feedback concise and understandable to a business owner.
12. Return ONLY JSON matching the requested schema.

STATUS MEANING
PASS = appears valid and meets the supplied requirements.
REVIEW = needs human review or a clearer/new copy.
FAIL = clearly does not meet the supplied requirements.

For expiryDate, use YYYY-MM-DD when a date can be confidently determined; otherwise return an empty string.
Confidence must be between 0 and 1.
""";
    }

    private static string NormalizeStatus(string status) =>
        status.Trim().ToUpperInvariant() switch
        {
            "PASS" => "PASS",
            "FAIL" => "FAIL",
            _ => "REVIEW"
        };

    private static string ExtractApiError(string responseText)
    {
        try
        {
            using var json = JsonDocument.Parse(responseText);
            if (json.RootElement.TryGetProperty("error", out var error) &&
                error.TryGetProperty("message", out var message))
            {
                return message.GetString() ?? responseText;
            }
        }
        catch
        {
            // Fall through and return the raw response.
        }

        return responseText.Length > 500 ? responseText[..500] : responseText;
    }
}
