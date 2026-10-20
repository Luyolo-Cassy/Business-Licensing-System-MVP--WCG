namespace BusinessLicensing_Practice.Services.Storage;

// Defines private storage namespaces and rejects rooted, nested, or parent-directory
// filename representations before a provider resolves them.
public static class PrivateFileKeys
{
    public const string ProtectedUploadsPrefix = "protected-uploads";
    public const string GeneratedApplicationsPrefix = "generated-applications";
    public const string UploadReferencePrefix = "/uploads/";

    public static string ProtectedUpload(string fileName) =>
        Combine(ProtectedUploadsPrefix, RequireSingleSegment(fileName));

    public static string GeneratedApplication(string relativeKey) =>
        Combine(GeneratedApplicationsPrefix, Normalize(relativeKey));

    public static string FromUploadReference(string reference)
    {
        if (!reference.StartsWith(UploadReferencePrefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The protected-upload reference is invalid.");

        return ProtectedUpload(reference[UploadReferencePrefix.Length..]);
    }

    public static string ToUploadReference(string objectKey)
    {
        var normalized = Normalize(objectKey);
        var prefix = ProtectedUploadsPrefix + "/";
        if (!normalized.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidOperationException("The protected-upload object key is invalid.");

        return UploadReferencePrefix + RequireSingleSegment(normalized[prefix.Length..]);
    }

    // Canonicalize separators and reject paths that could escape the intended
    // logical storage namespace.
    public static string Normalize(string objectKey)
    {
        if (string.IsNullOrWhiteSpace(objectKey) || objectKey.StartsWith('/') || objectKey.EndsWith('/') ||
            objectKey.Contains('\\') || objectKey.Any(char.IsControl))
            throw new InvalidOperationException("The private-file object key is invalid.");

        var segments = objectKey.Split('/');
        if (segments.Any(segment => segment.Length == 0 || segment is "." or ".." ||
            segment.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0))
            throw new InvalidOperationException("The private-file object key is invalid.");

        return string.Join('/', segments);
    }

    private static string Combine(string prefix, string suffix) => Normalize($"{prefix}/{suffix}");

    private static string RequireSingleSegment(string value)
    {
        var normalized = Normalize(value);
        if (normalized.Contains('/'))
            throw new InvalidOperationException("The private-file name must be a single path segment.");
        return normalized;
    }
}
