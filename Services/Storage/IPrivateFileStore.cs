namespace BusinessLicensing_Practice.Services.Storage;

public interface IPrivateFileStore
{
    Task SaveAsync(string objectKey, ReadOnlyMemory<byte> contents, string contentType,
        CancellationToken cancellationToken = default);

    Task<PrivateFile?> OpenReadAsync(string objectKey, CancellationToken cancellationToken = default);

    Task<byte[]?> ReadAsync(string objectKey, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default);
}
