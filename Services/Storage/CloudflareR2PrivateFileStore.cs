using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace BusinessLicensing_Practice.Services.Storage;

public sealed class CloudflareR2PrivateFileStore(
    IAmazonS3 client,
    IOptions<CloudflareR2Options> options) : IPrivateFileStore
{
    private readonly string bucketName = options.Value.BucketName;

    public async Task SaveAsync(string objectKey, ReadOnlyMemory<byte> contents, string contentType,
        CancellationToken cancellationToken = default)
    {
        var key = PrivateFileKeys.Normalize(objectKey);
        await using var input = new MemoryStream(contents.ToArray(), writable: false);
        var request = new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            InputStream = input,
            ContentType = contentType,
            DisablePayloadSigning = true,
            DisableDefaultChecksumValidation = true
        };

        await client.PutObjectAsync(request, cancellationToken);
    }

    public async Task<PrivateFile?> OpenReadAsync(string objectKey,
        CancellationToken cancellationToken = default)
    {
        var key = PrivateFileKeys.Normalize(objectKey);
        try
        {
            var response = await client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = bucketName,
                Key = key
            }, cancellationToken);

            return new PrivateFile(
                new ResponseOwningStream(response.ResponseStream, response),
                response.Headers.ContentType,
                response.ContentLength);
        }
        catch (AmazonS3Exception exception) when (IsMissing(exception))
        {
            return null;
        }
    }

    public async Task<byte[]?> ReadAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var file = await OpenReadAsync(objectKey, cancellationToken);
        if (file == null) return null;

        await using var content = file.Content;
        using var buffer = file.Length is > 0 and <= int.MaxValue
            ? new MemoryStream((int)file.Length.Value)
            : new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    public async Task<bool> ExistsAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var key = PrivateFileKeys.Normalize(objectKey);
        try
        {
            await client.GetObjectMetadataAsync(new GetObjectMetadataRequest
            {
                BucketName = bucketName,
                Key = key
            }, cancellationToken);
            return true;
        }
        catch (AmazonS3Exception exception) when (IsMissing(exception))
        {
            return false;
        }
    }

    public async Task DeleteAsync(string objectKey, CancellationToken cancellationToken = default)
    {
        var key = PrivateFileKeys.Normalize(objectKey);
        await client.DeleteObjectAsync(new DeleteObjectRequest
        {
            BucketName = bucketName,
            Key = key
        }, cancellationToken);
    }

    private static bool IsMissing(AmazonS3Exception exception) =>
        exception.StatusCode == HttpStatusCode.NotFound ||
        string.Equals(exception.ErrorCode, "NoSuchKey", StringComparison.Ordinal) ||
        string.Equals(exception.ErrorCode, "NotFound", StringComparison.Ordinal);

    private sealed class ResponseOwningStream(Stream inner, IDisposable owner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,
            CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default) => inner.WriteAsync(buffer, cancellationToken);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                owner.Dispose();
            }
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            owner.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
