using BusinessLicensing_Practice.Services;

namespace BusinessLicensingSystem.Tests.AiDocumentValidation;

[Trait("Feature", "AI Document Validation")]
public sealed class AiDocumentValidationTests
{
    private static readonly byte[] Document = [1, 2, 3];
    private static readonly AiDocumentExpectedValues Expected = new(
        "Thandi", "Ndlovu", "Cape Foods (Pty) Ltd", "2024/123456/07",
        "10 Long Street", "", "Central", "Cape Town", "8001");

    [Fact]
    public async Task DisabledValidation_DoesNotCallAiProvider()
    {
        var fake = new FakeExtractor(new("South African Smart ID", "Extracted"));
        var result = await new AiDocumentValidationPolicy(fake).EvaluateAsync(false, "Owner ID Document", "owner.pdf", Document, Expected);
        Assert.Equal(AiDocumentValidationStatus.Disabled, result.Status);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task SmartIdDocument_IsAccepted()
    {
        var result = await Evaluate(new("South African Smart ID", "Extracted"), "Owner ID Document");
        Assert.Equal(AiDocumentValidationStatus.Match, result.Status);
    }

    [Fact]
    public async Task UnsupportedIdentityDocument_IsRejected()
    {
        var result = await Evaluate(new("Other", "Extracted"), "Owner ID Document");
        Assert.Equal(AiDocumentValidationStatus.NoMatch, result.Status);
    }

    [Fact]
    public async Task CompanyRegistrationFormatting_IsNormalised()
    {
        var extraction = new AiDocumentExtraction("Certificate of Incorporation", "Extracted",
            BusinessName: "Cape Foods Proprietary Limited", RegistrationNumber: "2024 123456 07");
        var result = await Evaluate(extraction, "Certificate of Incorporation");
        Assert.Equal(AiDocumentValidationStatus.Match, result.Status);
    }

    [Fact]
    public async Task DifferentProofOfAddress_IsRejected()
    {
        var extraction = new AiDocumentExtraction("Proof of Address", "Extracted", AddressLine1: "99 Short Road",
            City: "Cape Town", PostalCode: "8001", AddressContext: "RelevantPhysicalAddress");
        var result = await Evaluate(extraction, "Proof of Address");
        Assert.Equal(AiDocumentValidationStatus.NoMatch, result.Status);
    }

    private static Task<AiDocumentValidationResult> Evaluate(AiDocumentExtraction extraction, string documentType) =>
        new AiDocumentValidationPolicy(new FakeExtractor(extraction)).EvaluateAsync(true, documentType, "fixture.pdf", Document, Expected);

    private sealed class FakeExtractor(AiDocumentExtraction? result) : IAiDocumentValidationService
    {
        public int Calls { get; private set; }
        public Task<AiDocumentExtraction?> ExtractAsync(AiDocumentKind kind, string fileName, byte[] fileBytes, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }
}
