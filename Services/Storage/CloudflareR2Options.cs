using System.ComponentModel.DataAnnotations;

namespace BusinessLicensing_Practice.Services.Storage;

public sealed class CloudflareR2Options : IValidatableObject
{
    public const string SectionName = "Storage:R2";

    [Required]
    public string Endpoint { get; set; } = string.Empty;

    [Required]
    public string BucketName { get; set; } = string.Empty;

    [Required]
    public string AccessKeyId { get; set; } = string.Empty;

    [Required]
    public string SecretAccessKey { get; set; } = string.Empty;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!string.IsNullOrWhiteSpace(Endpoint) &&
            (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var endpoint) ||
             endpoint.Scheme != Uri.UriSchemeHttps ||
             !endpoint.Host.EndsWith(".r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase) ||
             !string.IsNullOrEmpty(endpoint.UserInfo) ||
             endpoint.AbsolutePath != "/" ||
             !string.IsNullOrEmpty(endpoint.Query) ||
             !string.IsNullOrEmpty(endpoint.Fragment)))
        {
            yield return new ValidationResult(
                $"{SectionName}:Endpoint must be a root HTTPS r2.cloudflarestorage.com S3 endpoint without credentials, query, or fragment.",
                [nameof(Endpoint)]);
        }

        if (!string.IsNullOrWhiteSpace(BucketName) &&
            (BucketName.Contains('/') || BucketName.Contains('\\')))
        {
            yield return new ValidationResult(
                $"{SectionName}:BucketName must be a bucket name, not a path.",
                [nameof(BucketName)]);
        }
    }
}
