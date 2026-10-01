using BusinessLicensing_Practice.Services.Storage;

namespace BusinessLicensing_Practice.Services;

public sealed class ApplicationFileService(IPrivateFileStore store)
{
    public async Task<string> SaveGeneratedPdfAsync(int applicationId, string fileName, byte[] contents,
        CancellationToken cancellationToken = default)
    {
        var safeFileName = Path.GetFileName(fileName);
        var relativeKey = PrivateFileKeys.Normalize($"{applicationId}/{safeFileName}");
        await store.SaveAsync(PrivateFileKeys.GeneratedApplication(relativeKey), contents, "application/pdf",
            cancellationToken);
        return relativeKey;
    }

    public Task<PrivateFile?> OpenGeneratedPdfAsync(string? relativeKey,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(relativeKey) || relativeKey.StartsWith('/')
            ? Task.FromResult<PrivateFile?>(null)
            : store.OpenReadAsync(PrivateFileKeys.GeneratedApplication(relativeKey), cancellationToken);

    public Task<bool> GeneratedPdfExistsAsync(string? relativeKey,
        CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(relativeKey) || relativeKey.StartsWith('/')
            ? Task.FromResult(false)
            : store.ExistsAsync(PrivateFileKeys.GeneratedApplication(relativeKey), cancellationToken);

    public Task DeleteGeneratedPdfAsync(string? relativeKey, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(relativeKey) || relativeKey.StartsWith('/')
            ? Task.CompletedTask
            : store.DeleteAsync(PrivateFileKeys.GeneratedApplication(relativeKey), cancellationToken);
}
