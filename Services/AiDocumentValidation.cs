using System.Net.Http.Json;
using System.Text.Json;

namespace BusinessLicensing_Practice.Services;

public enum AiDocumentValidationStatus
{
    Disabled,
    Match,
    NoMatch,
    Unavailable
}

public enum AiDocumentKind
{
    OwnerIdentity
}

public sealed record AiDocumentValidationResult(AiDocumentValidationStatus Status);

public interface IAiDocumentValidationService
{
    Task<AiDocumentValidationResult> ValidateAsync(
        AiDocumentKind documentKind,
        string fileName,
        byte[] fileBytes,
        CancellationToken cancellationToken = default);
}

public class AiDocumentValidationPolicy(IAiDocumentValidationService validator)
{
    public Task<AiDocumentValidationResult> EvaluateAsync(
        bool enabled,
        string documentType,
        string fileName,
        byte[] fileBytes,
        CancellationToken cancellationToken = default)
    {
        if (!enabled || documentType != "Owner ID Document")
            return Task.FromResult(new AiDocumentValidationResult(AiDocumentValidationStatus.Disabled));

        return validator.ValidateAsync(AiDocumentKind.OwnerIdentity, fileName, fileBytes, cancellationToken);
    }

    public static bool BlocksProgress(AiDocumentValidationStatus status) =>
        status == AiDocumentValidationStatus.NoMatch;
}

public class GeminiDocumentValidationService(HttpClient client, IConfiguration configuration) : IAiDocumentValidationService
{
    private const string Model = "gemini-3.5-flash-lite";
    private const string Endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Model}:generateContent";
    private const string Prompt = """
        Does this uploaded document visually appear to contain an acceptable identity document: a South African Smart ID card, an ID book identification/details page, or a passport biographical/identification page?

        Classify only the document type from visual appearance and structure. Do not assess authenticity, verify identity, validate any number, extract personal information, or reproduce names, numbers, dates, signatures, addresses, or other personal details. Plain text claiming to be an ID, including words such as "ID" or "Identity Document", a person's name, or an ID number, is not sufficient. Return only the requested structured result.
        """;

    public async Task<AiDocumentValidationResult> ValidateAsync(
        AiDocumentKind documentKind,
        string fileName,
        byte[] fileBytes,
        CancellationToken cancellationToken = default)
    {
        if (documentKind != AiDocumentKind.OwnerIdentity || fileBytes.Length == 0)
            return new(AiDocumentValidationStatus.NoMatch);

        var apiKey = configuration["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return new(AiDocumentValidationStatus.Unavailable);

        var mimeType = Path.GetExtension(fileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            _ => null
        };
        if (mimeType == null) return new(AiDocumentValidationStatus.NoMatch);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
            request.Headers.Add("x-goog-api-key", apiKey);
            request.Content = JsonContent.Create(new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new object[]
                        {
                            new { inlineData = new { mimeType, data = Convert.ToBase64String(fileBytes) } },
                            new { text = Prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = 200,
                    responseMimeType = "application/json",
                    responseJsonSchema = new
                    {
                        type = "object",
                        properties = new
                        {
                            isMatch = new { type = "boolean" },
                            documentType = new { type = "string", @enum = new[] { "Owner ID or Passport", "Other" } }
                        },
                        required = new[] { "isMatch", "documentType" },
                        additionalProperties = false
                    }
                }
            });

            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode) return new(AiDocumentValidationStatus.Unavailable);

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var envelope = await JsonDocument.ParseAsync(responseStream, cancellationToken: cancellationToken);
            var text = envelope.RootElement.GetProperty("candidates")[0].GetProperty("content")
                .GetProperty("parts")[0].GetProperty("text").GetString();
            var result = JsonSerializer.Deserialize<GeminiClassification>(text ?? "",
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (result == null || result.DocumentType != (result.IsMatch ? "Owner ID or Passport" : "Other"))
                return new(AiDocumentValidationStatus.Unavailable);
            return new(result.IsMatch ? AiDocumentValidationStatus.Match : AiDocumentValidationStatus.NoMatch);
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new(AiDocumentValidationStatus.Unavailable);
        }
    }

    private sealed record GeminiClassification(bool IsMatch, string DocumentType);
}
