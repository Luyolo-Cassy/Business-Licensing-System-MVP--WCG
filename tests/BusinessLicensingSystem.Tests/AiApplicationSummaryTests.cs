using System.Net;
using BusinessLicensing_Practice.Services;
using Microsoft.Extensions.Configuration;

namespace BusinessLicensingSystem.Tests.AiApplicationSummaries;

[Trait("Feature", "AI Application Summaries")]
public sealed class AiApplicationSummaryTests
{
    [Fact]
    public async Task MissingApiKey_DoesNotSendHttpRequest()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "{}");
        var service = Create(handler, configured: false);
        var result = await service.GenerateAsync(Input());
        Assert.False(result.Success);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task ValidStructuredResponse_ReturnsTrimmedSummary()
    {
        var body = """{"candidates":[{"content":{"parts":[{"text":"{\"summary\":\"  Complete application  \"}"}]}}]}""";
        var result = await Create(new RecordingHandler(HttpStatusCode.OK, body)).GenerateAsync(Input());
        Assert.True(result.Success);
        Assert.Equal("Complete application", result.Summary);
    }

    [Fact]
    public async Task RateLimitedProvider_ReturnsUnavailable()
    {
        var result = await Create(new RecordingHandler(HttpStatusCode.TooManyRequests, "{}")).GenerateAsync(Input());
        Assert.False(result.Success);
    }

    [Fact]
    public async Task MalformedProviderResponse_ReturnsUnavailable()
    {
        var result = await Create(new RecordingHandler(HttpStatusCode.OK, "not-json")).GenerateAsync(Input());
        Assert.False(result.Success);
    }

    [Fact]
    public async Task EmptySummary_IsRejected()
    {
        var body = """{"candidates":[{"content":{"parts":[{"text":"{\"summary\":\"   \"}"}]}}]}""";
        var result = await Create(new RecordingHandler(HttpStatusCode.OK, body)).GenerateAsync(Input());
        Assert.False(result.Success);
    }

    private static GeminiApplicationSummaryService Create(RecordingHandler handler, bool configured = true)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = configured ? "offline-test-key" : null
        }).Build();
        return new(new HttpClient(handler), configuration);
    }

    private static AiApplicationSummaryInput Input() => new("APP-001", "Sale of Meals Licence", "New application",
        "Safe Cafe", "Cafe", "Meals", "Hessequa Municipality", "Riversdale", "Monday: 08:00 - 17:00", "No",
        "Pre-packed", null, [new("Food offered", "Prepared meals")], "Under Review", 1, false, ["Owner ID Document"]);

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
