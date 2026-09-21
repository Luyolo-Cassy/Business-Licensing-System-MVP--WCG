using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

const string model = "gemini-3.5-flash-lite";
const string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";
const string prompt = """
    Does this PDF visually appear to contain a South African Smart ID card?

    Classify only the document type from its visual appearance and structure. Do not assess authenticity, verify identity, validate an ID number, extract personal information, or reproduce any names, numbers, dates, signatures, addresses, or other personal details. Words such as "ID" or "Identity Document", a person's name, or an ID number alone are not sufficient. Set documentType to "South African Smart ID" when isMatch is true, otherwise set it to "Other". Keep reason short, generic, and free of personal information.
    """;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .Build();
var apiKey = configuration["Gemini:ApiKey"];

if (string.IsNullOrWhiteSpace(apiKey))
{
    Fail("Gemini API key not configured");
    return;
}

var projectDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
var tests = new[]
{
    new TestCase("SmartId.pdf", true),
    new TestCase("FakeId.pdf", false)
};

if (tests.Any(test => !File.Exists(Path.Combine(projectDirectory, "TestDocuments", test.FileName))))
{
    Fail("one or more test PDFs are missing");
    return;
}

using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
var allPassed = true;
var requestCount = 0;

foreach (var test in tests)
{
    var path = Path.Combine(projectDirectory, "TestDocuments", test.FileName);
    ClassificationResult result;
    try
    {
        result = await ClassifyAsync(client, apiKey, path);
        requestCount++;
    }
    catch (GeminiRequestException error)
    {
        Fail(error.Message);
        return;
    }
    catch (TaskCanceledException)
    {
        Fail("HTTP/API request timed out");
        return;
    }
    catch (HttpRequestException)
    {
        Fail("HTTP/API request failed");
        return;
    }
    catch (JsonException)
    {
        Fail("Gemini returned an invalid structured response");
        return;
    }

    var classification = result.IsMatch ? "MATCH" : "NO_MATCH";
    Console.WriteLine(test.FileName);
    Console.WriteLine($"Classification: {classification}");

    var structurallyValid = !string.IsNullOrWhiteSpace(result.Reason) &&
        result.Reason.Length <= 300 &&
        result.DocumentType == (result.IsMatch ? "South African Smart ID" : "Other");
    if (result.IsMatch != test.ExpectedMatch || !structurallyValid)
    {
        allPassed = false;
        Console.WriteLine("Expected classification: " + (test.ExpectedMatch ? "MATCH" : "NO_MATCH"));
    }
}

Console.WriteLine($"Gemini requests: {requestCount}");
Console.WriteLine($"Document classification: {(allPassed ? "PASS" : "FAIL")}");
if (!allPassed) Environment.ExitCode = 1;

static async Task<ClassificationResult> ClassifyAsync(HttpClient client, string apiKey, string path)
{
    var pdfBase64 = Convert.ToBase64String(await File.ReadAllBytesAsync(path));
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
    request.Headers.Add("x-goog-api-key", apiKey);
    request.Content = JsonContent.Create(new
    {
        contents = new[]
        {
            new
            {
                parts = new object[]
                {
                    new { inlineData = new { mimeType = "application/pdf", data = pdfBase64 } },
                    new { text = prompt }
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
                    documentType = new { type = "string", @enum = new[] { "South African Smart ID", "Other" } },
                    reason = new { type = "string" }
                },
                required = new[] { "isMatch", "documentType", "reason" },
                additionalProperties = false
            }
        }
    });

    using var response = await client.SendAsync(request);
    if (!response.IsSuccessStatusCode)
    {
        throw new GeminiRequestException(response.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "authentication failed",
            HttpStatusCode.TooManyRequests => "quota/rate limit reached",
            HttpStatusCode.NotFound => "model unavailable",
            _ => $"HTTP/API request failed ({(int)response.StatusCode})"
        });
    }

    await using var responseStream = await response.Content.ReadAsStreamAsync();
    using var json = await JsonDocument.ParseAsync(responseStream);
    var text = json.RootElement
        .GetProperty("candidates")[0]
        .GetProperty("content")
        .GetProperty("parts")[0]
        .GetProperty("text")
        .GetString()
        ?.Trim();
    return JsonSerializer.Deserialize<ClassificationResult>(text ?? "", new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    }) ?? throw new JsonException();
}

static void Fail(string message)
{
    Console.Error.WriteLine($"Gemini document classification: FAIL - {message}");
    Environment.ExitCode = 1;
}

sealed record TestCase(string FileName, bool ExpectedMatch);
sealed record ClassificationResult(
    [property: JsonPropertyName("isMatch")] bool IsMatch,
    [property: JsonPropertyName("documentType")] string DocumentType,
    [property: JsonPropertyName("reason")] string Reason);
sealed class GeminiRequestException(string message) : Exception(message);
